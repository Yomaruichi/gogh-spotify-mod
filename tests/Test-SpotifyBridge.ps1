[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$helperPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'GoghSpotifyMod\SpotifySessionBridge.ps1'

$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

function Start-TestHelper {
    param([string] $ExtraArguments)

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()

    $startInfo.FileName = $powershellPath

    $startInfo.Arguments = '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $helperPath + '" ' + $ExtraArguments

    $startInfo.UseShellExecute = $false

    $startInfo.CreateNoWindow = $true

    $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

    $startInfo.RedirectStandardInput = $true

    $startInfo.RedirectStandardOutput = $true

    $startInfo.RedirectStandardError = $true

    $startInfo.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)

    $startInfo.StandardErrorEncoding = [System.Text.UTF8Encoding]::new($false)

    return [System.Diagnostics.Process]::Start($startInfo)
}

# Read one real snapshot without starting or controlling Spotify.
$helper = Start-TestHelper '-Once'

$helperOutput = $helper.StandardOutput.ReadToEndAsync()

$helperErrors = $helper.StandardError.ReadToEndAsync()

try {
    if (-not $helper.WaitForExit(15000)) {
        throw 'One-shot helper did not finish within 15 seconds.'
    }

    $snapshotText = $helperOutput.GetAwaiter().GetResult()

    $diagnostics = $helperErrors.GetAwaiter().GetResult()

    if ($helper.ExitCode -ne 0) {
        throw "One-shot helper failed: $diagnostics"
    }

    $snapshot = $snapshotText | ConvertFrom-Json

    if ($snapshot.SchemaVersion -ne 1 -or $snapshot.Status -notin @('Ready', 'WaitingForSpotify')) {
        throw 'Expected a valid ready or waiting snapshot from the local media API.'
    }

    if ($snapshot.Status -eq 'Ready' -and [string]::IsNullOrWhiteSpace($snapshot.PlaybackStatus)) {
        throw 'A ready snapshot must include a playback state.'
    }

    if ($snapshot.Status -eq 'Ready') {
        foreach ($propertyName in 'CanPrevious', 'CanToggle', 'CanNext', 'TimelineAvailable') {
            if ($snapshot.$propertyName -isnot [bool]) {
                throw "Expected a Boolean capability for $propertyName."
            }
        }

        if ($snapshot.TimelineAvailable) {
            $position = [double] $snapshot.PositionSeconds

            $duration = [double] $snapshot.DurationSeconds

            if ([double]::IsNaN($position) -or [double]::IsInfinity($position) -or
                [double]::IsNaN($duration) -or [double]::IsInfinity($duration) -or
                $duration -le 0 -or $position -lt 0 -or $position -gt $duration) {
                throw 'A valid timeline must contain finite position and duration within track bounds.'
            }
        }

        if (-not [string]::IsNullOrEmpty($snapshot.ArtworkBase64)) {
            $artworkBytes = [Convert]::FromBase64String($snapshot.ArtworkBase64)

            if ($artworkBytes.Length -eq 0 -or $artworkBytes.Length -gt 1048576) {
                throw 'Artwork payload exceeded the bounded image size.'
            }
        }
    }

    # INFO after a real Windows query succeeds; state avoids exposing listening history in test output.
    Write-Host "[INFO] Test.OneShotReturnsValidMediaSnapshot passed; state=$($snapshot.Status)"
} finally {
    if (-not $helper.HasExited) {
        $helper.Kill()
    }

    $helper.Dispose()
}

# An invalid parent must fail visibly instead of leaving a helper running.
$helper = Start-TestHelper '-ParentProcessId 2147483647 -Once'

$helperOutput = $helper.StandardOutput.ReadToEndAsync()

$helperErrors = $helper.StandardError.ReadToEndAsync()

try {
    if (-not $helper.WaitForExit(15000)) {
        throw 'Invalid-parent helper did not finish within 15 seconds.'
    }

    $diagnostics = $helperErrors.GetAwaiter().GetResult()

    if ($helper.ExitCode -eq 0 -or -not $diagnostics.Contains('[ERROR] Spotify.Helper startup failed')) {
        throw 'Expected a nonzero exit and an explicit setup failure for an invalid parent.'
    }

    # INFO after exit status and diagnostics verify the expected failure path.
    Write-Host '[INFO] Test.InvalidParentFailsVisibly passed'
} finally {
    if (-not $helper.HasExited) {
        $helper.Kill()
    }

    $helper.Dispose()
}

# A short-lived stand-in game checks shutdown without closing the real game.
$parentStartInfo = [System.Diagnostics.ProcessStartInfo]::new()

$parentStartInfo.FileName = $powershellPath

$parentStartInfo.Arguments = '-NoLogo -NoProfile -NonInteractive -Command "Start-Sleep -Seconds 5"'

$parentStartInfo.UseShellExecute = $false

$parentStartInfo.CreateNoWindow = $true

$parentStartInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden

$parent = [System.Diagnostics.Process]::Start($parentStartInfo)

$helper = Start-TestHelper ("-ParentProcessId " + $parent.Id)

# Unknown commands must be rejected without changing Spotify playback.
$helper.StandardInput.WriteLine("invalid-command")
$helper.StandardInput.Flush()

$helperOutput = $helper.StandardOutput.ReadToEndAsync()

$helperErrors = $helper.StandardError.ReadToEndAsync()

try {
    if (-not $helper.WaitForExit(15000)) {
        throw 'Helper remained alive after its parent exited.'
    }

    $snapshots = $helperOutput.GetAwaiter().GetResult()

    $diagnostics = $helperErrors.GetAwaiter().GetResult()

    if ($helper.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($snapshots)) {
        throw "Helper did not produce snapshots and exit normally: $diagnostics"
    }

    if (-not $parent.HasExited) {
        throw 'Helper stopped before its parent exited.'
    }

    if (-not $diagnostics.Contains('[WARN] Spotify.Control rejected')) {
        throw 'Expected an unknown transport command to be rejected explicitly.'
    }

    # INFO after the command was rejected without a playback API call.
    Write-Host '[INFO] Test.UnknownCommandIsRejected passed'

    # INFO after the helper emits data and stops following parent exit.
    Write-Host '[INFO] Test.HelperStopsAfterParentExit passed'
} finally {
    foreach ($ownedProcess in @($helper, $parent)) {
        if (-not $ownedProcess.HasExited) {
            $ownedProcess.Kill()
        }

        $ownedProcess.Dispose()
    }
}
