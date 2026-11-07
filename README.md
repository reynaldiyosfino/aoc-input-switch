# AOC Input Switch

Windows tray app that switches monitor inputs over DDC/CI. The window title
shows the detected brand and model. Input buttons come from DDC VCP `0x60`
(this AOC lists DP1, DP2, HDMI1, HDMI2). Keyboard stays on Windows, so
hotkeys still work while the monitor shows another machine.

## Run

```powershell
dotnet publish -c Release -o .\publish
.\publish\AocInputSwitch.exe
```

Default hotkeys follow input order: `Ctrl+Alt+1` … `Ctrl+Alt+N`. Change names
and shortcuts from the tray menu **Inputs**. Close (X) hides the window to
the tray; use **Exit** to quit. Right-click a port with no cable to dim it.

Turn **DDC/CI** on in the monitor OSD. On NVIDIA GPUs the app talks to the
monitor through NVAPI I2C when the Windows DDC API is silent.
