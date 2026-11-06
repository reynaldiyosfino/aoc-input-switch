# AOC Input Switch

Windows tray app that switches the AOC U34G3G3R3 (and similar AOC) input
between DP1, DP2, HDMI1, and HDMI2 over DDC/CI. Keyboard stays on Windows,
so hotkeys still work while the AOC shows another machine.

## Run

```powershell
dotnet publish -c Release -o .\publish
.\publish\AocInputSwitch.exe
```

Default hotkeys: `Ctrl+Alt+1` DP1, `Ctrl+Alt+2` DP2, `Ctrl+Alt+3` HDMI1,
`Ctrl+Alt+4` HDMI2. Change them from the tray menu **Set hotkeys**.

Turn **DDC/CI** on in the monitor OSD. On NVIDIA GPUs the app talks to the
monitor through NVAPI I2C when the Windows DDC API is silent.
