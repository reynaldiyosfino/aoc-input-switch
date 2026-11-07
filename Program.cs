using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AocInputSwitch;

internal static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (HasArg(args, "--probe"))
            return Probe();
        if (HasArg(args, "--cycle"))
            return Cycle();
        if (TryGetSetCode(args, out var setCode))
            return SetOnce(setCode);

        _mutex = new Mutex(true, @"Local\AocInputSwitch", out var created);
        if (!created)
        {
            MessageBox.Show("AOC Input is already running in the system tray.", "AOC Input", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.ApplicationExit += (_, _) =>
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        };

        Application.Run(new TrayApplication());
        return 0;
    }

    private static int Probe()
    {
        var log = new StringBuilder();
        try
        {
            var config = AppConfig.Load();
            if (!MonitorSession.TryOpen(out var session, out var report, out var error) || session is null)
            {
                log.AppendLine(error);
                log.AppendLine(report);
                return Finish(log, 1);
            }

            using (session)
            {
                log.AppendLine(report);
                log.AppendLine("Monitor: " + session.Description);
                log.AppendLine("Brand: " + session.Brand);
                log.AppendLine("Adapter: " + session.Adapter);
                log.AppendLine("Device: " + session.DeviceId);
                var code = session.ReadInputCode(out var readError);
                log.AppendLine("Brightness: " + session.BrightnessStatus());
                log.AppendLine("NVAPI:");
                log.AppendLine(NvidiaDdc.Probe(session.Adapter));
                log.AppendLine(code is uint current
                    ? $"Active input: 0x{current:X2} ({MonitorSession.Describe(current)})"
                    : "Active input: failed. " + readError);
                var live = session.ReadLiveInputs();
                var caps = live.Reported.Count > 0 ? live.Reported : session.CapabilityInputCodes;
                log.AppendLine(caps.Count == 0
                    ? "Capabilities 0x60: (unread, using standard codes)"
                    : "Capabilities 0x60: " + string.Join(" ", caps.Select(item => item.ToString("X2"))));
                log.AppendLine(live.CapsKnown
                    ? "Live inputs: " + string.Join(" ", live.Reported.Select(item => item.ToString("X2")))
                    : "Live inputs: unknown (monitor did not list a subset)");
                var slots = config.Resolve(caps, out var warning);
                foreach (var slot in slots)
                {
                    var available = !live.CapsKnown || live.Reported.Contains(slot.Vcp) || live.Current == slot.Vcp;
                    log.AppendLine($"{slot.Label} = 0x{slot.Vcp:X2}  {slot.Hotkey.Format()}  {(available ? "available" : "unplugged")}");
                }
                if (warning is not null)
                    log.AppendLine(warning);

                var window = new HotkeyWindow();
                window.Open();
                for (var i = 0; i < slots.Count; i++)
                {
                    var registered = window.Register(i + 1, slots[i].Hotkey);
                    log.AppendLine(registered
                        ? $"Hotkey registered: {slots[i].Label} {slots[i].Hotkey.Format()}"
                        : $"Hotkey conflict: {slots[i].Label} {slots[i].Hotkey.Format()} (Win32 {Marshal.GetLastWin32Error()})");
                }

                for (var i = 1; i <= slots.Count; i++)
                    window.Unregister(i);
                window.CloseWindow();
                if (code is null)
                    return Finish(log, 1);
            }

            log.AppendLine("OK");
            return Finish(log, 0);
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            return Finish(log, 1);
        }
    }

    private static int Cycle()
    {
        var log = new StringBuilder();
        try
        {
            var config = AppConfig.Load();
            if (!MonitorSession.TryOpen(out var session, out var report, out var error) || session is null)
            {
                log.AppendLine(error);
                log.AppendLine(report);
                return Finish(log, 1);
            }

            using (session)
            {
                var original = session.ReadInputCode(out var readError);
                if (original is null)
                {
                    log.AppendLine("Could not read the active input. " + readError);
                    return Finish(log, 1);
                }

                var slots = config.Resolve(session.CapabilityInputCodes, out _);
                var target = slots.FirstOrDefault(slot => slot.Vcp != original.Value) ?? slots[0];
                log.AppendLine($"From 0x{original.Value:X2} ({MonitorSession.Describe(original.Value)}) to {target.Label} (0x{target.Vcp:X2})");
                if (!session.TrySetInput(target.Vcp, out var setError))
                {
                    log.AppendLine(setError);
                    Restore(session, original.Value, log);
                    return Finish(log, 1);
                }

                Thread.Sleep(1500);
                var mid = session.ReadInputCode(out _);
                log.AppendLine(mid is uint seen
                    ? $"Read: 0x{seen:X2} ({MonitorSession.Describe(seen)})"
                    : "Read: failed");

                if (!Restore(session, original.Value, log))
                {
                    log.AppendLine("Could not restore the original input. Use the monitor buttons if the picture did not return.");
                    return Finish(log, 1);
                }

                Thread.Sleep(800);
                var back = session.ReadInputCode(out _);
                log.AppendLine(back is uint restored
                    ? $"Restored read: 0x{restored:X2} ({MonitorSession.Describe(restored)})"
                    : "Restored read: failed");
            }

            log.AppendLine("OK");
            return Finish(log, 0);
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            return Finish(log, 1);
        }
    }

    private static bool Restore(MonitorSession session, uint code, StringBuilder log)
    {
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            if (session.TrySetInput(code, out var error))
            {
                log.AppendLine($"Restored 0x{code:X2} ({MonitorSession.Describe(code)})");
                return true;
            }

            log.AppendLine($"Restore attempt {attempt}: {error}");
            Thread.Sleep(500);
        }

        return false;
    }

    private static int Finish(StringBuilder log, int code)
    {
        var text = log.ToString();
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "last-run.txt"), text);
        }
        catch (IOException)
        {
        }

        WriteConsole(text);
        return code;
    }

    private static void WriteConsole(string text)
    {
        if (!AttachConsole(-1))
            return;

        var handle = CreateFileW("CONOUT$", 0x40000000, 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return;

        using var stream = new FileStream(new SafeFileHandle(handle, ownsHandle: true), FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        writer.Write(text);
    }

    private static int SetOnce(uint code)
    {
        var log = new StringBuilder();
        try
        {
            if (!MonitorSession.TryOpen(out var session, out var report, out var error) || session is null)
            {
                log.AppendLine(error);
                log.AppendLine(report);
                return Finish(log, 1);
            }

            using (session)
            {
                var original = session.ReadInputCode(out var readError);
                log.AppendLine(original is uint before
                    ? $"Before: 0x{before:X2} ({MonitorSession.Describe(before)})"
                    : "Before: failed. " + readError);
                if (!session.TrySetInput(code, out var setError))
                {
                    log.AppendLine(setError);
                    if (original is uint restore)
                        Restore(session, restore, log);
                    return Finish(log, 1);
                }

                Thread.Sleep(1500);
                var after = session.ReadInputCode(out _);
                log.AppendLine(after is uint seen
                    ? $"After: 0x{seen:X2} ({MonitorSession.Describe(seen)})"
                    : "After: failed");
                if (original is uint start && start != code)
                    Restore(session, start, log);
            }

            log.AppendLine("OK");
            return Finish(log, 0);
        }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            return Finish(log, 1);
        }
    }

    private static bool TryGetSetCode(string[] args, out uint code)
    {
        code = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--set", StringComparison.OrdinalIgnoreCase))
                continue;
            var token = args[i + 1];
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                token = token[2..];
            return uint.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out code);
        }

        return false;
    }

    private static bool HasArg(string[] args, string name)
    {
        return args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

}
