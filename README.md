# gogh Spotify Mod

An unofficial Windows mod for **gogh: Focus with Your Avatar**, built with BepInEx 6 IL2CPP.

Version **0.5.0** adds a local Spotify player with album art, playback controls, appearance settings, and a progress bar. Spotify continues handling audio in its desktop app. The mod does not require a Spotify developer account, API credentials, or OAuth sign-in.

## Features

- Song title, artist, album art, and playing/paused state.
- Previous, play/pause, and next controls.
- A progress bar with elapsed and total time.
- A draggable player with a saved position.
- Live customization for player size, background opacity, and text size.
- A position lock that prevents dragging while leaving playback buttons usable.
- **F9** to hide or show the player.

The player starts near the bottom-right, above gogh's bottom controls. Existing saved positions are preserved when updating.

## Multiplayer status

**Multiplayer use has not been confirmed as permitted or ban-safe.** This project has not obtained developer confirmation for using its BepInEx-based player in multiplayer rooms. Use in solo is recommended until that is clarified.

The plugin adds a local overlay and controls the local Spotify session. It does not implement room synchronization, transmit songs to other participants, or modify multiplayer messages. That limited scope does not establish official permission.

**F9 only hides the player. It does not unload the plugin or BepInEx.** Disabling the console window also does not disable the mod.

## Requirements

To run:

- Windows 10 version 1809 or later, or Windows 11.
- gogh for Windows x64.
- BepInEx 6 **Unity IL2CPP, Windows x64**, with interop generation completed.
- Spotify desktop exposing a Windows media session.
- Windows PowerShell 5.1, included with Windows.

To build:

- .NET 6 SDK.
- Windows 10/11 SDK, used to compile the artwork adapter.
- The game's local BepInEx and generated interop libraries.

The Windows SDK is a build-time requirement; it is not needed to run a built copy of the mod. No extra NuGet packages are used.

## Build and install

1. Install the correct BepInEx IL2CPP build beside `gogh.exe` and launch the game once to generate its interop libraries.
2. Copy `Local.props.example` to `Local.props`.
3. Set `GameRoot` to your game installation directory, including the final slash.
4. Close gogh before deploying.

Build from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

Build and install into the configured game directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Deploy
```

Deployment copies and verifies these three files in `BepInEx/plugins/GoghSpotifyMod/`:

```text
GoghSpotifyMod.dll
SpotifySessionBridge.ps1
SpotifyArtworkReader.dll
```

The PowerShell policy override applies only to that process; it does not change machine settings.

To update an existing installation, close gogh and deploy the new build over the previous plugin files. Your game-side configuration remains in place.

## Using the player

1. Open Spotify desktop and start a song.
2. Launch gogh normally through Steam.
3. Use the player's previous, play/pause, and next buttons.
4. Drag the top Spotify/status strip to move it.
5. Click **Edit** to customize its appearance or lock its position.
6. Press **F9** to hide or show it.

Playback buttons are disabled when Spotify has no session or does not expose the corresponding capability.

The progress bar shows elapsed and total time. It advances between samples while playing, freezes when paused, and stops at the reported duration. Long recordings use hours when needed. Missing or stale timing shows `--:--` and an empty bar. **Seeking by clicking the bar is not implemented.**

Missing album art uses a neutral placeholder. The helper caches artwork for the current track and limits each image to 1 MiB.

## Appearance settings

Changes made through **Edit** apply live and save automatically.

| Setting | Range | Default |
| --- | --- | --- |
| Player size | 75–150% | 100% |
| Background opacity | 35–100% | 95% |
| Text size | 85–125% | 100% |
| Position lock | Locked / Unlocked | Unlocked |

**Reset appearance** restores these defaults without changing the saved position. Background opacity affects the player's background; text and playback controls remain readable. The appearance editor retains its own readable background.

Settings are stored in the game installation:

```text
BepInEx/config/jd.gogh.spotify.cfg
```

The `[Panel]` section contains:

```ini
Visible = true
RightMargin = 24
BottomMargin = 88
Scale = 1
BackgroundOpacity = 0.95
TextScale = 1
PositionLocked = false
```

Margins use a 1280×720 reference canvas. The player is 336×168 reference pixels before scaling. Dragging updates the margins, and viewport bounds keep the player on-screen.

## Logs and troubleshooting

Logs remain in the game directory:

```text
BepInEx/LogOutput.log
```

Useful messages include `Spotify.Panel created`, `Spotify.NowPlaying changed`, `Spotify.Control accepted`, and `Spotify.Appearance changed`.

Track information is written to this local log. Read failures and rejected commands are also logged. Logs are excluded from this repository.

To disable the extra BepInEx console window, edit `BepInEx/config/BepInEx.cfg` while gogh is closed:

```ini
[Logging.Console]
Enabled = false
```

Keep `[Logging.Disk] Enabled = true` if you want to retain file logs for troubleshooting. Restart gogh after changing BepInEx's console setting.

If the player is missing, check that `Visible` is enabled, press F9 while the game is focused, and inspect the log for startup or panel errors.

## Verification

Check the real Windows media bridge without changing playback:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-SpotifyBridge.ps1
```

These checks cover valid media snapshots, capability and timeline fields, bounded artwork data, visible failure for an invalid parent, rejection of an unknown command, and helper shutdown after its parent exits.

Check timeline calculations and formatting:

```powershell
dotnet run --project .\tests\TimelineTests\TimelineTests.csproj -c Release
```

The timeline checks cover playing and paused states, playback rate, track limits, invalid values, new-track samples, and minute/hour formatting.

For a standalone metadata snapshot:

```powershell
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File .\GoghSpotifyMod\SpotifySessionBridge.ps1 -Once
```

The standalone snapshot includes track metadata and encoded artwork; avoid posting its raw output publicly.

### Tested status

- BepInEx 6.0.0-be.793 initialized on the tested gogh installation using Unity 6000.3.27f2 and IL2CPP metadata v39.
- The original F8 diagnostic plugin loaded and ran more than 79,000 Unity callbacks.
- The rounded player, Japanese track text, album art, all three playback controls, and saved drag positions were verified in-game for version 0.3.0.
- Version 0.5.0 built with zero warnings/errors and deployed successfully on November 22, 2026.
- All 13 timeline checks and four bridge checks passed, including a real Spotify timeline.
- The latest appearance editor and progress-row rendering still need a completed in-game verification pass.
- Multiplayer approval and ban safety remain unconfirmed.

### Unity library workaround

On the tested installation, BepInEx's default download for Unity 6000.3.27 returned 404. The following setting in the game's `BepInEx/config/BepInEx.cfg` allowed interop generation using the previous patch's libraries:

```ini
[IL2CPP]
UnityBaseLibrariesSource = https://unity.bepinex.dev/libraries/6000.3.26.zip
```

This adjacent-version workaround has only been exercised on the tested paths; it is not a general compatibility guarantee. A `Class::Init` signature fallback warning remains in the tested runtime.

## How it works

A hidden Windows PowerShell helper reads Spotify's local Windows media session. The plugin receives UTF-8 JSON snapshots through stdout and sends only the whitelisted `previous`, `toggle`, and `next` commands through stdin.

Metadata is polled every two seconds. Between media calls, incoming commands are checked at 100 ms intervals. Each playback action is attempted once to avoid duplicate skips. The helper exits when its parent game process closes.

Windows supplies the playback position, duration, update timestamp, and rate. The helper normalizes the timeline, and Unity advances it between samples using a monotonic clock. Unity UI changes stay on the game thread.

## Repository layout

- `GoghSpotifyMod/` — main plugin, native player UI, appearance editor, timeline calculations, media helper, and artwork adapter source.
- `GoghStartupTest/` — optional F8 diagnostic plugin used during initial compatibility checks.
- `tests/Test-SpotifyBridge.ps1` — Windows media bridge integration checks.
- `tests/TimelineTests/` — standalone timeline calculation checks.
- `build.ps1` — build and optional deployment.
- `Local.props.example` — template for the local game installation path.

All source, build scripts, and project documentation live here. Game files, downloaded libraries, installed plugin copies, and runtime logs remain in the game installation.

Do not commit game files, runtime libraries, generated interop assemblies, build output, logs, local settings, or credentials. The ignore rules cover those artifacts and local `.env` files.

## Removing the mod

Close gogh and remove only `BepInEx/plugins/GoghSpotifyMod/` to remove this integration. The separate `GoghStartupTest` diagnostic plugin, if installed, can be removed independently.

Removing this plugin does not remove BepInEx itself.

## References

- [BepInEx IL2CPP installation](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)
- [Windows media-session manager](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager)
- [Windows media properties](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmediaproperties)
- [Windows playback timeline](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessiontimelineproperties)
- [gogh developer newsletter discussing mods](https://gogh.gg/news/zh-sc/20260310)
- [gogh multiplayer help](https://gogh.zendesk.com/hc/ja/articles/55524515648025-Multiplayer)
