using System;

using System.Runtime.InteropServices;

using BepInEx;

using BepInEx.Logging;

using BepInEx.Unity.IL2CPP;

using UnityEngine;

namespace GoghStartupTest;

[BepInPlugin("jd.gogh.startuptest", "Gogh Startup Test", "0.1.0")]
[BepInProcess("gogh.exe")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        // INFO when BepInEx invokes this plugin; version identifies the test build.
        Log.LogInfo("[INFO] StartupTest.Load entered; version=0.1.0");

        try
        {
            TestBehaviour.TestLog = Log;

            AddComponent<TestBehaviour>();

            // INFO after registration succeeds; the Unity version verifies a live engine call.
            Log.LogInfo($"[INFO] StartupTest.Component registered; unity={Application.unityVersion}; press F8 while focused");
        }
        catch (Exception exception)
        {
            // ERROR if registration fails; exception describes the failure and the test stops here.
            Log.LogError($"[ERROR] StartupTest.Load failed; nextAction=stop test; exception={exception}");

            throw;
        }
    }
}

public sealed class TestBehaviour : MonoBehaviour
{
    internal static ManualLogSource TestLog;

    private int updateCount;

    private bool f8WasDown;

    private bool testFailed;

    public TestBehaviour(IntPtr pointer) : base(pointer)
    {
    }

    // Use Windows input so the test works independently of Unity's selected input backend.
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    public void Update()
    {
        if (testFailed)
        {
            return;
        }

        try
        {
            updateCount++;

            if (updateCount == 1 || updateCount == 600)
            {
                // INFO on the first and 600th callback; count confirms Unity keeps calling managed code.
                TestLog?.LogInfo($"[INFO] StartupTest.Update alive; callbackCount={updateCount}");
            }

            bool f8IsDown = (GetAsyncKeyState(0x77) & 0x8000) != 0;

            if (Application.isFocused && f8IsDown && !f8WasDown)
            {
                // INFO on a focused F8 press; count and object name verify input and live Unity access.
                TestLog?.LogInfo($"[INFO] StartupTest.F8 PASS; callbackCount={updateCount}; object={gameObject.name}");
            }

            f8WasDown = f8IsDown;
        }
        catch (Exception exception)
        {
            testFailed = true;

            // ERROR once if the runtime test fails; further checks stop to avoid per-frame log spam.
            TestLog?.LogError($"[ERROR] StartupTest.Update failed; nextAction=stop test checks; exception={exception}");
        }
    }
}
