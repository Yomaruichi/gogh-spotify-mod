using System;

using System.Diagnostics;

using System.IO;

using System.Text;

using System.Text.Json;

using BepInEx;

using BepInEx.Unity.IL2CPP;

namespace GoghSpotifyMod;

[BepInPlugin("jd.gogh.spotify", "Gogh Spotify Mod", "0.5.0")]
[BepInProcess("gogh.exe")]
public sealed class Plugin : BasePlugin
{
    private Process bridgeProcess;

    private string lastState;

    private int invalidMessages;

    private NowPlayingPanel panelComponent;

    public override void Load()
    {
        // INFO on plugin entry; version identifies this now-playing panel build.
        Log.LogInfo("[INFO] Spotify.Load starting; version=0.5.0");

        try
        {
            NowPlayingPanel.PanelLog = Log;

            NowPlayingPanel.VisibleSetting = Config.Bind(
                "Panel",
                "Visible",
                true,
                "Show the Spotify now-playing panel. Press F9 in gogh to toggle."
            );

            try
            {
                NowPlayingPanel.RightMargin = Config.Bind("Panel", "RightMargin", 24f, "Distance from the right edge in reference pixels; dragging updates this.");

                NowPlayingPanel.BottomMargin = Config.Bind("Panel", "BottomMargin", 88f, "Distance above the bottom edge; leaves room for gogh controls.");

                NowPlayingPanel.PanelScale = Config.Bind(
                    "Panel",
                    "Scale",
                    1f,
                    new BepInEx.Configuration.ConfigDescription(
                        "Overall player size. Change this live in Edit.",
                        new BepInEx.Configuration.AcceptableValueRange<float>(0.75f, 1.5f)
                    )
                );

                NowPlayingPanel.BackgroundOpacity = Config.Bind(
                    "Panel",
                    "BackgroundOpacity",
                    0.95f,
                    new BepInEx.Configuration.ConfigDescription(
                        "Background opacity; text and controls stay readable.",
                        new BepInEx.Configuration.AcceptableValueRange<float>(0.35f, 1f)
                    )
                );

                NowPlayingPanel.TextScale = Config.Bind(
                    "Panel",
                    "TextScale",
                    1f,
                    new BepInEx.Configuration.ConfigDescription(
                        "Text size relative to the player.",
                        new BepInEx.Configuration.AcceptableValueRange<float>(0.85f, 1.25f)
                    )
                );

                NowPlayingPanel.PositionLocked = Config.Bind(
                    "Panel",
                    "PositionLocked",
                    false,
                    "Prevent dragging the player while keeping playback buttons usable."
                );

                NowPlayingPanel.SendCommand = SendCommand;

                panelComponent = AddComponent<NowPlayingPanel>();
            }
            catch (Exception exception)
            {
                // ERROR if UI component registration fails; the independent metadata bridge still starts.
                Log.LogError($"[ERROR] Spotify.Panel registration failed; nextAction=continue metadata logging; exception={exception}");
            }

            string pluginDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!;

            string helperPath = Path.Combine(pluginDirectory, "SpotifySessionBridge.ps1");

            if (!File.Exists(helperPath))
            {
                throw new FileNotFoundException("SpotifySessionBridge.ps1 is missing beside the plugin DLL.");
            }

            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

            string powershellPath = Path.Combine(
                windowsDirectory,
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"
            );

            var startInfo = new ProcessStartInfo(powershellPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = pluginDirectory
            };

            foreach (string argument in new[]
            {
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                helperPath,
                "-ParentProcessId",
                Environment.ProcessId.ToString()
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            bridgeProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            bridgeProcess.OutputDataReceived += HandleSnapshot;

            bridgeProcess.ErrorDataReceived += HandleDiagnostic;

            bridgeProcess.Exited += HandleExit;

            if (!bridgeProcess.Start())
            {
                throw new InvalidOperationException("Windows did not start the Spotify helper.");
            }

            // INFO only after process creation succeeds; pid identifies our owned helper.
            Log.LogInfo($"[INFO] Spotify.Bridge started; pid={bridgeProcess.Id}; pollingSeconds=2");

            bridgeProcess.BeginOutputReadLine();

            bridgeProcess.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            StopBridge();

            // ERROR if setup fails; exception gives the cause and metadata reading remains disabled.
            Log.LogError($"[ERROR] Spotify.Load failed; nextAction=disable bridge; exception={exception}");
        }
    }

    private void HandleSnapshot(object sender, DataReceivedEventArgs arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.Data))
        {
            return;
        }

        try
        {
            SpotifySnapshot snapshot = JsonSerializer.Deserialize<SpotifySnapshot>(arguments.Data)
                ?? throw new JsonException("Empty snapshot.");

            if (snapshot.SchemaVersion != 1)
            {
                throw new JsonException("Unsupported snapshot version.");
            }

            NowPlayingPanel.Publish(
                snapshot.Status,
                snapshot.Title,
                snapshot.Artist,
                snapshot.PlaybackStatus,
                snapshot.ArtworkBase64,
                snapshot.CanPrevious,
                snapshot.CanToggle,
                snapshot.CanNext,
                snapshot.TimelineAvailable,
                snapshot.PositionSeconds,
                snapshot.DurationSeconds,
                snapshot.PlaybackRate
            );

            string state = JsonSerializer.Serialize(new
            {
                snapshot.Status,
                snapshot.Title,
                snapshot.Artist,
                snapshot.Album,
                snapshot.PlaybackStatus
            });

            if (state == lastState)
            {
                return;
            }

            lastState = state;

            if (snapshot.Status == "Ready")
            {
                // INFO on track/playback changes; title, artist, album and playback come from Spotify's local session.
                Log.LogInfo(
                    $"[INFO] Spotify.NowPlaying changed; playback={SafeText(snapshot.PlaybackStatus)}; " +
                    $"title={SafeText(snapshot.Title)}; artist={SafeText(snapshot.Artist)}; album={SafeText(snapshot.Album)}"
                );
            }
            else if (snapshot.Status == "WaitingForSpotify")
            {
                // INFO when no Spotify session exists; nextAction describes the normal waiting state.
                Log.LogInfo("[INFO] Spotify.Session unavailable; nextAction=open Spotify desktop and play a track");
            }
            else
            {
                // WARN when the bridge reports a failed read; stale track data is not reported as current.
                Log.LogWarning("[WARN] Spotify.Session read failed; nextAction=helper retries in 2 seconds");
            }
        }
        catch (Exception exception)
        {
            invalidMessages++;

            if (invalidMessages <= 3)
            {
                // WARN for the first three invalid messages; count limits diagnostics and no raw helper output is logged.
                Log.LogWarning(
                    $"[WARN] Spotify.Snapshot invalid; count={invalidMessages}; " +
                    $"error={exception.GetType().Name}; nextAction=skip message"
                );
            }
        }
    }

    private void HandleDiagnostic(object sender, DataReceivedEventArgs arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.Data))
        {
            return;
        }

        string diagnostic = SafeText(arguments.Data);

        if (diagnostic.StartsWith("[ERROR]") || diagnostic.StartsWith("[CRITICAL]"))
        {
            // ERROR forwards helper failures; text contains a step, safe error code and recovery action.
            Log.LogError(diagnostic);
        }
        else if (diagnostic.StartsWith("[WARN]"))
        {
            // WARN forwards recoverable helper failures and the planned retry.
            Log.LogWarning(diagnostic);
        }
        else
        {
            // INFO forwards helper lifecycle milestones, excluding song metadata.
            Log.LogInfo(diagnostic);
        }
    }

    private void HandleExit(object sender, EventArgs arguments)
    {
        NowPlayingPanel.Publish("Error", "", "", "");

        // WARN on unexpected helper exit; the user can inspect diagnostics before restarting gogh.
        Log.LogWarning("[WARN] Spotify.Bridge exited; nextAction=inspect prior helper logs and restart gogh");
    }

    private static string SafeText(string value)
    {
        string text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');

        return text.Length > 500 ? text.Substring(0, 500) : text;
    }

    private void SendCommand(string command)
    {
        if (command != "previous" && command != "toggle" && command != "next")
        {
            return;
        }

        try
        {
            if (bridgeProcess == null || bridgeProcess.HasExited)
            {
                throw new InvalidOperationException("Spotify helper is not running.");
            }

            bridgeProcess.StandardInput.WriteLine(command);

            bridgeProcess.StandardInput.Flush();

            // INFO after a whitelisted command enters the pipe; acceptance is logged by the helper separately.
            Log.LogInfo($"[INFO] Spotify.Control queued; action={command}");
        }
        catch (Exception exception)
        {
            // WARN if IPC fails; no automatic retry avoids duplicate transport actions.
            Log.LogWarning($"[WARN] Spotify.Control queue failed; action={command}; error={exception.GetType().Name}; nextAction=skip");
        }
    }

    public override bool Unload()
    {
        StopBridge();

        if (panelComponent != null)
        {
            UnityEngine.Object.Destroy(panelComponent);
        }

        NowPlayingPanel.Remove();

        return true;
    }

    private void StopBridge()
    {
        Process process = bridgeProcess;

        bridgeProcess = null;

        if (process == null)
        {
            return;
        }

        process.Exited -= HandleExit;

        try
        {
            if (!process.HasExited)
            {
                process.Kill();

                // INFO after termination is requested; the only target is the helper created by this plugin.
                Log.LogInfo("[INFO] Spotify.Bridge stopping; reason=plugin unload or setup failure");
            }
        }
        catch (InvalidOperationException)
        {
            // A process that never started or already exited needs no termination.
        }
        finally
        {
            process.Dispose();
        }
    }

    private sealed class SpotifySnapshot
    {
        public int SchemaVersion { get; set; }

        public string Status { get; set; } = "";

        public string Title { get; set; } = "";

        public string Artist { get; set; } = "";

        public string Album { get; set; } = "";

        public string PlaybackStatus { get; set; } = "";

        public string ArtworkBase64 { get; set; } = "";

        public bool CanPrevious { get; set; }

        public bool CanToggle { get; set; }

        public bool CanNext { get; set; }

        public bool TimelineAvailable { get; set; }

        public double PositionSeconds { get; set; }

        public double DurationSeconds { get; set; }

        public double PlaybackRate { get; set; } = 1.0;
    }
}
