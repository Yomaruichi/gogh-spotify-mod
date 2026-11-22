[CmdletBinding()]
param(
    [int] $ParentProcessId = 0,

    [switch] $Once
)

$ErrorActionPreference = 'Stop'

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

function Write-BridgeLog {
    param([string] $Message)

    [Console]::Error.WriteLine($Message)
}

function Wait-WinRtOperation {
    param(
        $Operation,

        [Type] $ResultType
    )

    $closedMethod = $script:asTaskMethod.MakeGenericMethod($ResultType)

    $task = $closedMethod.Invoke($null, @($Operation))

    if (-not $task.Wait(5000)) {
        $Operation.Cancel()

        throw [System.TimeoutException]::new('Windows media request exceeded five seconds.')
    }

    return $task.GetAwaiter().GetResult()
}

function Write-Snapshot {
    param(
        [string] $Status,

        [string] $Title = '',

        [string] $Artist = '',

        [string] $Album = '',

        [string] $PlaybackStatus = '',
        [string] $ArtworkBase64 = '',
        [bool] $CanPrevious = $false,
        [bool] $CanToggle = $false,
        [bool] $CanNext = $false,

        [bool] $TimelineAvailable = $false,

        [double] $PositionSeconds = 0,

        [double] $DurationSeconds = 0,

        [double] $PlaybackRate = 1
    )

    $snapshot = [ordered]@{
        SchemaVersion = 1
        Status = $Status
        Title = $Title
        Artist = $Artist
        Album = $Album
        PlaybackStatus = $PlaybackStatus
        ArtworkBase64 = $ArtworkBase64
        CanPrevious = $CanPrevious
        CanToggle = $CanToggle
        CanNext = $CanNext
        TimelineAvailable = $TimelineAvailable
        PositionSeconds = $PositionSeconds
        DurationSeconds = $DurationSeconds
        PlaybackRate = $PlaybackRate
        UpdatedAtUtc = [DateTime]::UtcNow.ToString('o')
    }

    [Console]::Out.WriteLine(($snapshot | ConvertTo-Json -Compress))
}

function Get-SpotifyArtwork {
    param($Properties)

    if ($null -eq $Properties.Thumbnail -or $null -eq ('SpotifyArtworkReader' -as [type])) {
        return ''
    }

    $stream = $null

    $reader = $null

    try {
        $streamType = [Windows.Storage.Streams.IRandomAccessStreamWithContentType, Windows.Storage.Streams, ContentType=WindowsRuntime]

        $stream = Wait-WinRtOperation -Operation ($Properties.Thumbnail.OpenReadAsync()) -ResultType $streamType

        if ($stream.Size -eq 0 -or $stream.Size -gt 1048576) {
            return ''
        }

        return [Convert]::ToBase64String([SpotifyArtworkReader]::Read($stream))
    } catch {
        # WARN if optional artwork fails; safe code explains why the text-only fallback is used.
        Write-BridgeLog ("[WARN] Spotify.Artwork unavailable; code=" + $_.Exception.HResult + "; reason=" + $_.Exception.Message + "; nextAction=use placeholder")

        return ''
    } finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }

        if ($null -ne $stream) {
            [System.Runtime.InteropServices.Marshal]::ReleaseComObject($stream) | Out-Null
        }
    }
}

function Get-SpotifyTimeline {
    param($Session, $PlaybackInfo)

    try {
        $timeline = $Session.GetTimelineProperties()

        $duration = ($timeline.EndTime - $timeline.StartTime).TotalSeconds

        $position = ($timeline.Position - $timeline.StartTime).TotalSeconds

        $rate = 1.0

        if ($null -ne $PlaybackInfo.PlaybackRate -and $PlaybackInfo.PlaybackRate -gt 0) {
            $rate = [double] $PlaybackInfo.PlaybackRate
        }

        if ([double]::IsNaN($rate) -or [double]::IsInfinity($rate) -or $rate -le 0) {
            $rate = 1.0
        }

        $available = $duration -gt 0 -and
            -not [double]::IsNaN($duration) -and
            -not [double]::IsInfinity($duration) -and
            -not [double]::IsNaN($position) -and
            -not [double]::IsInfinity($position)

        if (-not $available) {
            $duration = 0.0

            $position = 0.0
        }

        $script:lastTimelineError = ''

        if ($available -and $PlaybackInfo.PlaybackStatus.ToString() -eq 'Playing') {
            $updatedAt = [DateTimeOffset] $timeline.LastUpdatedTime

            $age = [Math]::Max(0, ([DateTimeOffset]::UtcNow - $updatedAt).TotalSeconds)

            $position += $age * $rate
        }

        return [pscustomobject]@{
            Available = $available
            PositionSeconds = [Math]::Max(0, [Math]::Min($duration, $position))
            DurationSeconds = [Math]::Max(0, $duration)
            PlaybackRate = $rate
        }
    } catch {
        $errorCode = $_.Exception.GetType().Name + ':' + $_.Exception.HResult

        if ($errorCode -ne $script:lastTimelineError) {
            # WARN only for a new optional timeline failure; metadata/controls continue with unknown time.
            Write-BridgeLog "[WARN] Spotify.Timeline unavailable; code=$errorCode; nextAction=show unknown time"
        }

        $script:lastTimelineError = $errorCode

        return [pscustomobject]@{
            Available = $false
            PositionSeconds = 0.0
            DurationSeconds = 0.0
            PlaybackRate = 1.0
        }
    }
}
function Invoke-SpotifyCommand {
    param($Session, [string] $Command)

    if ($Command -notin @('previous', 'toggle', 'next')) {
        # WARN for rejected input; command contents are excluded and no media action occurs.
        Write-BridgeLog '[WARN] Spotify.Control rejected; reason=unknown command; nextAction=skip'

        return
    }

    if ($null -eq $Session) {
        # WARN when no matching session exists; the action is skipped rather than targeting another app.
        Write-BridgeLog "[WARN] Spotify.Control unavailable; action=$Command; nextAction=open Spotify"

        return
    }

    # INFO before calling the local transport API; action identifies the requested operation.
    Write-BridgeLog "[INFO] Spotify.Control requested; action=$Command"

    try {
        $operation = switch ($Command) {
            'previous' { $Session.TrySkipPreviousAsync() }
            'toggle' { $Session.TryTogglePlayPauseAsync() }
            'next' { $Session.TrySkipNextAsync() }
        }

        $accepted = Wait-WinRtOperation -Operation $operation -ResultType ([bool])

        if ($accepted) {
            # INFO only after Windows reports acceptance; playback metadata confirms the resulting state separately.
            Write-BridgeLog "[INFO] Spotify.Control accepted; action=$Command"
        } else {
            # WARN if Spotify declines an action; the current snapshot remains authoritative.
            Write-BridgeLog "[WARN] Spotify.Control declined; action=$Command; nextAction=wait for playback state"
        }
    } catch {
        # WARN if the local transport call fails; no automatic retry avoids duplicate skips.
        Write-BridgeLog ("[WARN] Spotify.Control failed; action=$Command; code=" + $_.Exception.HResult + "; nextAction=skip action")
    }
}
try {
    # INFO on helper entry; parentPid identifies the game lifetime this helper follows.
    Write-BridgeLog "[INFO] Spotify.Helper starting; parentPid=$ParentProcessId"

    Add-Type -AssemblyName System.Runtime.WindowsRuntime

    $adapterPath = Join-Path $PSScriptRoot 'SpotifyArtworkReader.dll'

    if (-not (Test-Path -LiteralPath $adapterPath)) {
        $adapterPath = Join-Path $PSScriptRoot 'bin\Release\net6.0\SpotifyArtworkReader.dll'
    }

    if (Test-Path -LiteralPath $adapterPath) {
        [System.Reflection.Assembly]::LoadFrom($adapterPath) | Out-Null
    } else {
        # WARN when the optional adapter is missing; metadata and playback remain usable.
        Write-BridgeLog '[WARN] Spotify.Artwork adapter missing; nextAction=use placeholder and rebuild'
    }
    $managerType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime]

    $propertiesType = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties, Windows.Media.Control, ContentType=WindowsRuntime]

    $script:asTaskMethod = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object {
            $_.Name -eq 'AsTask' -and
            $_.IsGenericMethod -and
            $_.GetGenericArguments().Count -eq 1 -and
            $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation' + [char]96 + '1'
        } |
        Select-Object -First 1

    if ($null -eq $script:asTaskMethod) {
        throw [System.MissingMethodException]::new('WinRT AsTask adapter was not found.')
    }

    $parentStartedAt = $null

    if ($ParentProcessId -gt 0) {
        $parentProcess = [System.Diagnostics.Process]::GetProcessById($ParentProcessId)

        $parentStartedAt = $parentProcess.StartTime

        $parentProcess.Dispose()
    }

    $manager = Wait-WinRtOperation -Operation ($managerType::RequestAsync()) -ResultType $managerType

    # INFO only after Windows grants a session manager; intervalSeconds describes the polling cadence.
    Write-BridgeLog '[INFO] Spotify.Helper manager ready; intervalSeconds=2'

    if (-not $Once) {
        Add-Type -TypeDefinition @"
using System;
using System.Collections.Concurrent;
using System.Threading;

public static class SpotifyCommandInput
{
    public static readonly ConcurrentQueue<string> Commands = new ConcurrentQueue<string>();

    public static void Start()
    {
        var reader = new Thread(() =>
        {
            string line;

            while ((line = Console.ReadLine()) != null)
            {
                if (line.Length <= 32)
                {
                    Commands.Enqueue(line);
                }
            }
        });

        reader.IsBackground = true;
        reader.Start();
    }
}
"@

        [SpotifyCommandInput]::Start()
    }

    $artworkKey = ''

    $artworkBase64 = ''

    $nextPollAt = [DateTime]::MinValue

    $spotifySession = $null

    $previousError = ''

    do {
        if ($ParentProcessId -gt 0) {
            try {
                $parentProcess = [System.Diagnostics.Process]::GetProcessById($ParentProcessId)

                $parentStillMatches = $parentProcess.StartTime -eq $parentStartedAt

                $parentProcess.Dispose()

                if (-not $parentStillMatches) {
                    break
                }
            } catch [System.ArgumentException] {
                break
            }
        }

        if (-not $Once) {
            $command = $null

            while ([SpotifyCommandInput]::Commands.TryDequeue([ref] $command)) {
                Invoke-SpotifyCommand -Session $spotifySession -Command $command

                $nextPollAt = [DateTime]::MinValue
            }

            if ([DateTime]::UtcNow -lt $nextPollAt) {
                Start-Sleep -Milliseconds 100

                continue
            }
        }

        $nextPollAt = [DateTime]::UtcNow.AddSeconds(2)

        try {
            $spotifySessions = @(
                $manager.GetSessions() |
                    Where-Object { $_.SourceAppUserModelId -like '*Spotify*' }
            )

            $spotifySession = $spotifySessions |
                Where-Object { $_.GetPlaybackInfo().PlaybackStatus.ToString() -eq 'Playing' } |
                Select-Object -First 1

            if ($null -eq $spotifySession) {
                $spotifySession = $spotifySessions | Select-Object -First 1
            }

            if ($null -eq $spotifySession) {
                Write-Snapshot -Status 'WaitingForSpotify'
            } else {
                $properties = Wait-WinRtOperation -Operation ($spotifySession.TryGetMediaPropertiesAsync()) -ResultType $propertiesType

                $playbackStatus = $spotifySession.GetPlaybackInfo().PlaybackStatus.ToString()

                $newArtworkKey = $properties.AlbumTitle + '|' + $properties.Title + '|' + $properties.Artist

                if ($newArtworkKey -ne $artworkKey) {
                    $artworkBase64 = Get-SpotifyArtwork -Properties $properties

                    $artworkKey = $newArtworkKey
                }

                $playbackInfo = $spotifySession.GetPlaybackInfo()

                $controls = $playbackInfo.Controls

                $timeline = Get-SpotifyTimeline -Session $spotifySession -PlaybackInfo $playbackInfo

                $playbackStatus = $playbackInfo.PlaybackStatus.ToString()

                Write-Snapshot -Status 'Ready' -Title $properties.Title -Artist $properties.Artist -Album $properties.AlbumTitle -PlaybackStatus $playbackStatus -ArtworkBase64 $artworkBase64 -CanPrevious $controls.IsPreviousEnabled -CanToggle $controls.IsPlayPauseToggleEnabled -CanNext $controls.IsNextEnabled -TimelineAvailable $timeline.Available -PositionSeconds $timeline.PositionSeconds -DurationSeconds $timeline.DurationSeconds -PlaybackRate $timeline.PlaybackRate
            }

            if ($previousError -ne '') {
                # INFO after a failed read recovers; stale error state is cleared only on a successful poll.
                Write-BridgeLog '[INFO] Spotify.Helper read recovered'
            }

            $previousError = ''
        } catch {
            $errorCode = $_.Exception.GetType().Name + ':' + $_.Exception.HResult + ':' + $_.Exception.Message

            if ($errorCode -ne $previousError) {
                # WARN on a new read error; code is safe diagnostic metadata and the helper retries.
                Write-BridgeLog "[WARN] Spotify.Helper read failed; code=$errorCode; nextAction=retry in 2 seconds"
            }

            $previousError = $errorCode

            Write-Snapshot -Status 'Error'

            if ($Once) {
                exit 1
            }
        }

        if (-not $Once) {
            Start-Sleep -Milliseconds 100
        }
    } while (-not $Once)

    # INFO after one-shot completion or game exit; no additional background work remains.
    Write-BridgeLog '[INFO] Spotify.Helper stopped; reason=one-shot completed or parent exited'
} catch {
    $errorCode = $_.Exception.GetType().Name + ':' + $_.Exception.HResult + ':' + $_.Exception.Message

    # ERROR if setup cannot complete; code identifies the failure without dumping user media data.
    Write-BridgeLog "[ERROR] Spotify.Helper startup failed; code=$errorCode; nextAction=stop helper"

    exit 1
}
