# Gogh Startup Test

Diagnostic plugin for the installed BepInEx 6 IL2CPP runtime. It checks plugin loading, injected MonoBehaviour callbacks, Unity API calls, and a focused F8 press. Results appear in `BepInEx/LogOutput.log`; there is no on-screen panel.

Build from the source repository root after configuring `Local.props` as described in the root README:

```powershell
dotnet build .\GoghStartupTest\GoghStartupTest.csproj -c Release
```

Copy only `bin/Release/net6.0/GoghStartupTest.dll` into `BepInEx/plugins/GoghStartupTest/` while gogh is closed.

Launch gogh, wait about 30 seconds, click the game and press F8 once. Exit normally, then check the log for `StartupTest.F8 PASS` and `StartupTest.Update alive`. The update milestone at callback 600 depends on the game's frame rate.

To remove the test, delete only `BepInEx/plugins/GoghStartupTest/GoghStartupTest.dll` while gogh is closed.
