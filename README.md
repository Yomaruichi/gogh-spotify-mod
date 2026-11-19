# gogh Spotify integration mod

A Windows mod for gogh built with BepInEx 6 IL2CPP. Version 0.3.0 adds a compact Spotify player with local album art, track and artist, playback state, previous/play-pause/next controls, and a draggable saved position.

The player starts near the bottom-right, above gogh's bottom controls. Its rounded dark surface follows the game's controls. Drag the top Spotify/status strip to move it; the position is saved. F9 hides or shows it. Spotify handles audio in its own app. This implementation uses no Spotify developer account, OAuth tokens, web API calls, or extra NuGet packages.

## Repository layout

- GoghSpotifyMod/: integration plugin and Windows media-session helper.
- GoghStartupTest/: the earlier F8 diagnostic plugin.
- build.ps1: build the integration plugin and optionally deploy it.
- tests/Test-SpotifyBridge.ps1: real media query, failure-path, and helper lifetime checks.

All mod source, build scripts, setup instructions, and project documentation live here. Game libraries, installed plugin copies, and runtime logs stay in the game installation.

## Requirements

- Windows 10 version 1809 or later, or Windows 11.
- .NET 6 SDK and Windows 10/11 SDK for building. The SDK is used to compile a small Framework artwork adapter; runtime users do not need the SDK.
- Spotify desktop app exposing a Windows media session.
- BepInEx 6 Unity IL2CPP installed in gogh, with interop generation completed.

The helper runs in Windows PowerShell 5.1 included with Windows. A separate helper avoids loading WinRT adapters into Unity's runtime. It polls metadata every two seconds, handles incoming controls at 100 ms intervals between media calls, checks only sessions whose application identifier contains Spotify, and exits when gogh closes. Commands travel through the helper's redirected stdin pipe and are limited to previous, toggle, and next. Each is attempted once; Spotify acceptance or rejection is logged. Its stdout carries UTF-8 JSON to the plugin; stderr carries lifecycle and failure diagnostics.

## Build and deploy

Copy Local.props.example to Local.props and set GameRoot to the game installation folder, including its final slash. The local settings file is ignored by Git.

From this repository root:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

To build and install, close gogh first:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Deploy

This installs GoghSpotifyMod.dll, SpotifySessionBridge.ps1, and SpotifyArtworkReader.dll in BepInEx/plugins/GoghSpotifyMod/. Deployment verifies all three file hashes. The commands use a process-only PowerShell execution-policy override because the local signing policy blocks unsigned scripts; they do not change machine settings. The F8 diagnostic plugin can remain installed.

## Try it

1. Open Spotify desktop and start a song.
2. Launch gogh normally through Steam.
3. Pause, resume, or change the song in Spotify.
4. Inspect BepInEx/LogOutput.log for Spotify.NowPlaying changed messages.

Track information is written to this local log. If Spotify has no media session, the plugin logs that it is waiting. Read failures produce retry messages; helper startup failures include safe error codes. Album art is read from the local media session and cached for the current track, with a maximum encoded image size of 1 MiB. Missing art gets a neutral placeholder. Playback buttons are disabled when the session or the corresponding capability is unavailable.

To remove the integration, close gogh and remove only its BepInEx/plugins/GoghSpotifyMod directory.

## Verify the local bridge

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-SpotifyBridge.ps1

The checks read current media data without changing playback. They cover a valid ready/waiting snapshot, visible failure for an invalid parent process, helper shutdown after its parent exits, and rejection of an invalid playback command. Ready snapshots also validate Boolean capabilities and bounded artwork data.

For a standalone snapshot:

    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File .\GoghSpotifyMod\SpotifySessionBridge.ps1 -Once

## Compatibility evidence

On November 19, 2026:

- BepInEx 6.0.0-be.793 completed startup on the installed gogh build, Unity 6000.3.27f2 and metadata v39.
- The default Unity library URL returned 404. Configuring the game's BepInEx/config/BepInEx.cfg with UnityBaseLibrariesSource = https://unity.bepinex.dev/libraries/6000.3.26.zip allowed generation of 193 interop DLLs. Compatibility of this adjacent-version workaround is established only for the tested paths.
- The F8 diagnostic plugin loaded, passed more than 79,000 Unity callbacks, and recorded two F8 passes. The Class::Init fallback warning remains.
- The Windows helper read Spotify metadata containing Japanese text and a paused playback state.
- The integration compiled with zero warnings and errors and was deployed with matching source/destination hashes. Three bridge checks passed: real snapshot, invalid-parent failure, and parent-exit shutdown. Plugin JSON deserialization preserved a Japanese title. In-game load and track-change logging still need verification after deployment.

## References

- [Windows media-session manager](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [Media properties](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmediaproperties)
- [BepInEx IL2CPP installation](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)

Do not commit game files, runtime libraries, generated interop DLLs, local logs, or build output.


## Player configuration

The generated game-side BepInEx/config/jd.gogh.spotify.cfg stores Visible, RightMargin, and BottomMargin under Panel. Margins use the 1280x720 reference canvas. Dragging updates the two margins and viewport bounds keep the player on-screen. The default lower-right position is RightMargin=24 and BottomMargin=88. Text uses loaded game fonts, including the existing Japanese fallback setup.

The native canvas intercepts pointer input over the player so its buttons and drag handle work. The rest of the screen keeps normal game interaction. The old F8 diagnostic plugin remains independent.