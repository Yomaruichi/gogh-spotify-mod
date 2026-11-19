[CmdletBinding()]
param(
    [switch] $Deploy
)

$ErrorActionPreference = 'Stop'

$localSettingsPath = Join-Path $PSScriptRoot 'Local.props'

if (-not (Test-Path -LiteralPath $localSettingsPath)) {
    throw 'Copy Local.props.example to Local.props and set GameRoot before building.'
}

[xml] $localSettings = Get-Content -LiteralPath $localSettingsPath -Raw

$gameRoot = [System.IO.Path]::GetFullPath([string] $localSettings.Project.PropertyGroup.GameRoot)

if ($Deploy -and (Get-Process -Name gogh -ErrorAction SilentlyContinue)) {
    throw 'Close gogh before deploying the plugin.'
}

$projectPath = Join-Path $PSScriptRoot 'GoghSpotifyMod\GoghSpotifyMod.csproj'

# INFO before compilation; configuration identifies the build being attempted.
Write-Host '[INFO] Build.Compile starting; configuration=Release'

dotnet build $projectPath -c Release --nologo

if ($LASTEXITCODE -ne 0) {
    # ERROR after a compiler failure; deployment is aborted without replacing installed files.
    Write-Host '[ERROR] Build.Compile failed; nextAction=abort deployment'

    throw 'Plugin build failed.'
}

$frameworkRoot = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'

$metadata = Get-ChildItem -Path (Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits\10\UnionMetadata\*\Windows.winmd') |
    Where-Object { $_.Directory.Name -ne 'Facade' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $metadata) {
    throw 'Install a Windows 10/11 SDK to build the artwork adapter.'
}

$adapterOutput = Join-Path $PSScriptRoot 'GoghSpotifyMod\bin\Release\net6.0\SpotifyArtworkReader.dll'

$adapterSource = Join-Path $PSScriptRoot 'GoghSpotifyMod\SpotifyArtworkReader.cs'

$runtimeReference = Join-Path $env:SystemRoot 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll'

# INFO before compiling the Framework adapter; SDK metadata is a build-time dependency only.
Write-Host '[INFO] Build.ArtworkAdapter starting'

& (Join-Path $frameworkRoot 'csc.exe') /nologo /target:library ("/out:" + $adapterOutput) ("/reference:" + $metadata.FullName) ("/reference:" + (Join-Path $frameworkRoot 'System.Runtime.WindowsRuntime.dll')) ("/reference:" + $runtimeReference) $adapterSource

if ($LASTEXITCODE -ne 0) {
    throw 'Artwork adapter compilation failed; deployment aborted.'
}

# INFO after successful compilation; runtime users do not need the Windows SDK.
Write-Host '[INFO] Build.ArtworkAdapter complete'
# INFO only after the compiler succeeds; no plugin files have been deployed yet.
Write-Host '[INFO] Build.Compile complete'

if (-not $Deploy) {
    # INFO for an intentional build-only run; the game installation remains untouched.
    Write-Host '[INFO] Build.Deploy skipped; reason=Deploy switch not provided'

    return
}

if (Get-Process -Name gogh -ErrorAction SilentlyContinue) {
    throw 'gogh started during compilation; close it before deploying.'
}

$outputDirectory = Join-Path $PSScriptRoot 'GoghSpotifyMod\bin\Release\net6.0'

$pluginDirectory = Join-Path $gameRoot 'BepInEx\plugins\GoghSpotifyMod'

if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.Unity.IL2CPP.dll'))) {
    throw 'GameRoot does not contain the expected BepInEx IL2CPP installation.'
}

New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null

foreach ($fileName in 'GoghSpotifyMod.dll', 'SpotifySessionBridge.ps1', 'SpotifyArtworkReader.dll') {
    $sourcePath = Join-Path $outputDirectory $fileName

    $destinationPath = Join-Path $pluginDirectory $fileName

    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force

    if ((Get-FileHash -LiteralPath $sourcePath).Hash -ne (Get-FileHash -LiteralPath $destinationPath).Hash) {
        throw "Deployment verification failed for $fileName."
    }
}

# INFO after both deployed files match their source hashes; count identifies the installed artifacts.
Write-Host '[INFO] Build.Deploy complete; verifiedFiles=3'
