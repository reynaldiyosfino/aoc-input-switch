using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace AocInputSwitch;

internal sealed class FoundMonitor
{
    public IntPtr Physical { get; set; }
    public bool HasHandle { get; set; }
    public string Adapter { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceString { get; set; } = "";
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "Monitor";
    public string Model { get; set; } = "";
    public bool IsAoc { get; set; }
}

internal sealed class MonitorSession : IDisposable
{
    private const byte InputSourceCode = 0x60;
    private static readonly object Gate = new();
    private static readonly Regex InputCodePattern = new(
        @"(?:^|[^0-9A-Fa-f])60\s*\(\s*([0-9A-Fa-f\s]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DdcModelPattern = new(
        @"model\s*\(\s*([^)]+?)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private IntPtr _handle;
    private bool _hasHandle;
    private string _deviceId = "";
    private string _description = "";
    private string _adapter = "";
    private string _brand = "Monitor";
    private string _model = "";
    private string _capabilities = "";
    private IReadOnlyList<uint> _codes = [];

    private MonitorSession(FoundMonitor monitor)
    {
        _handle = monitor.Physical;
        _hasHandle = monitor.HasHandle;
        _deviceId = monitor.DeviceId;
        _description = monitor.Description;
        _adapter = monitor.Adapter;
        _brand = string.IsNullOrWhiteSpace(monitor.Brand) ? "Monitor" : monitor.Brand;
        _model = monitor.Model?.Trim() ?? "";
    }

    public string DeviceId => _deviceId;
    public string Description => _description;
    public string Adapter => _adapter;
    public string Brand => _brand;
    public string Model => _model;
    public string Title => string.IsNullOrWhiteSpace(_model) ? _brand : $"{_brand}: {_model}";
    public string Capabilities => _capabilities;
    public IReadOnlyList<uint> CapabilityInputCodes => _codes;

    public LiveInputs ReadLiveInputs()
    {
        lock (Gate)
        {
            uint? current = null;
            if (TryRead(out var code, out _))
                current = code;

            IReadOnlyList<uint> reported = _codes;
            var capsKnown = false;
            if (NvidiaDdc.TryGetCapabilities(_adapter, out var text, out _) && text.Length > 0)
            {
                _capabilities = text;
                _codes = ParseInputCodes(text);
                ApplyModelFromCapabilities();
                reported = _codes;
                capsKnown = _codes.Count > 0;
            }
            else if (_codes.Count == 0)
            {
                LoadCapabilities();
                reported = _codes;
                capsKnown = _codes.Count > 0;
            }
            else
                capsKnown = _codes.Count > 0;

            return new LiveInputs(current, reported, capsKnown);
        }
    }

    public static bool TryOpen(out MonitorSession? session, out string report, out string error)
    {
        lock (Gate)
            return TryOpenCore(out session, out report, out error);
    }

    public uint? ReadInputCode(out string error)
    {
        lock (Gate)
        {
            if (!TryRead(out var current, out error))
                return null;
            return current;
        }
    }

    public bool TrySetInput(uint code, out string error)
    {
        lock (Gate)
        {
            if (TrySet(code, out error))
                return true;

            var firstError = error;
            if (TryRediscover() && TrySet(code, out error))
                return true;

            error = Explain(firstError);
            return false;
        }
    }

    public void Dispose()
    {
        lock (Gate)
            DestroyHandle();
    }

    public static string Describe(uint code)
    {
        return code switch
        {
            0x0F => "DP1",
            0x10 => "DP2",
            0x11 => "HDMI1",
            0x12 => "HDMI2",
            _ => $"0x{code:X2}",
        };
    }

    private bool TryRead(out uint current, out string error)
    {
        current = 0;
        error = "";
        if (!_hasHandle && !TryRediscover())
        {
            error = "AOC monitor was not found.";
            return false;
        }

        if (TryGetInput(out current))
            return true;

        var firstError = Win32("GetVCPFeature");
        if (TryRediscover() && TryGetInput(out current))
            return true;
        if (NvidiaDdc.TryGetInput(_adapter, out current, out var nvError))
            return true;

        error = Explain(firstError + " " + nvError);
        return false;
    }

    private bool TrySet(uint code, out string error)
    {
        error = "";
        if (!_hasHandle)
        {
            error = "AOC monitor was not found.";
            return false;
        }

        if (NvidiaDdc.TrySetInput(_adapter, code, out error))
            return true;

        var nvError = error;
        if (TrySetInputCode(code))
            return true;

        error = Win32("SetVCPFeature") + " " + nvError;
        return false;
    }

    private bool TryRediscover()
    {
        var previous = _handle;
        var hadHandle = _hasHandle;
        _hasHandle = false;
        if (!TryOpenCore(out var fresh, out _, out _))
        {
            _handle = previous;
            _hasHandle = hadHandle;
            return hadHandle;
        }

        if (hadHandle && previous != fresh!._handle)
            NativeMethods.DestroyPhysicalMonitor(previous);

        _handle = fresh!._handle;
        _hasHandle = fresh._hasHandle;
        _deviceId = fresh._deviceId;
        _description = fresh._description;
        _adapter = fresh._adapter;
        _brand = fresh._brand;
        _model = fresh._model;
        _capabilities = fresh._capabilities;
        _codes = fresh._codes;
        fresh._hasHandle = false;
        return _hasHandle;
    }

    private void DestroyHandle()
    {
        if (!_hasHandle)
            return;
        NativeMethods.DestroyPhysicalMonitor(_handle);
        _hasHandle = false;
        _handle = IntPtr.Zero;
    }

    private void LoadCapabilities()
    {
        _capabilities = "";
        _codes = [];
        if (!_hasHandle)
            return;
        uint length = 0;
        var lengthOk = false;
        for (var attempt = 0; attempt < 6 && !lengthOk; attempt++)
        {
            lengthOk = NativeMethods.GetCapabilitiesStringLength(_handle, out length) && length is > 0 and <= 65536;
            if (!lengthOk)
                Thread.Sleep(80);
        }

        if (!lengthOk)
            return;

        var buffer = new StringBuilder((int)length);
        if (!NativeMethods.CapabilitiesRequestAndCapabilitiesReply(_handle, buffer, length))
            return;

        _capabilities = buffer.ToString();
        _codes = ParseInputCodes(_capabilities);
        ApplyModelFromCapabilities();
    }

    private static bool TryOpenCore(out MonitorSession? session, out string report, out string error)
    {
        var found = Enumerate();
        var lines = new StringBuilder();
        foreach (var monitor in found)
        {
            lines.Append(monitor.Adapter);
            lines.Append(" | ");
            lines.Append(monitor.DeviceString);
            lines.Append(" | ");
            lines.Append(monitor.DeviceId);
            lines.Append(" | ");
            lines.Append(monitor.Description);
            lines.Append(" | ");
            lines.Append(monitor.Brand);
            if (!string.IsNullOrWhiteSpace(monitor.Model))
            {
                lines.Append(" ");
                lines.Append(monitor.Model);
            }
            lines.Append(monitor.IsAoc ? " | AOC" : "");
            lines.AppendLine();
        }

        report = lines.ToString().TrimEnd();
        var chosen = found.FirstOrDefault(monitor =>
                monitor.IsAoc &&
                monitor.HasHandle &&
                monitor.DeviceId.Contains("AOCB306", StringComparison.OrdinalIgnoreCase))
            ?? found.FirstOrDefault(monitor => monitor.IsAoc && monitor.HasHandle);

        foreach (var monitor in found)
        {
            if (monitor.HasHandle && !ReferenceEquals(monitor, chosen))
                NativeMethods.DestroyPhysicalMonitor(monitor.Physical);
        }

        if (chosen is null)
        {
            session = null;
            error = "AOC monitor was not found.";
            return false;
        }

        session = new MonitorSession(chosen);
        session.LoadCapabilities();
        error = "";
        return true;
    }

    private static List<FoundMonitor> Enumerate()
    {
        var list = new List<FoundMonitor>();
        NativeMethods.MonitorEnumProc callback = (hMonitor, _, _, _) =>
        {
            Collect(hMonitor, list);
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return list;
    }

    private static void Collect(IntPtr hMonitor, List<FoundMonitor> list)
    {
        var info = new NativeMethods.MonitorInfoEx { cbSize = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
            return;

        var (deviceId, deviceString) = LookupMonitorDevice(info.szDevice);
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
        {
            var missing = DetectIdentity(deviceId, deviceString, "");
            list.Add(new FoundMonitor
            {
                Adapter = info.szDevice ?? "",
                DeviceId = deviceId,
                DeviceString = deviceString,
                Description = $"(DDC unavailable, Win32 {Marshal.GetLastWin32Error()})",
                Brand = missing.Brand,
                Model = missing.Model,
                IsAoc = IsAoc(deviceId, deviceString, ""),
            });
            return;
        }

        if (!TryReadPhysicalMonitors(hMonitor, count, out var physical, out var readError))
        {
            var failed = DetectIdentity(deviceId, deviceString, "");
            list.Add(new FoundMonitor
            {
                Adapter = info.szDevice ?? "",
                DeviceId = deviceId,
                DeviceString = deviceString,
                Description = readError,
                Brand = failed.Brand,
                Model = failed.Model,
                IsAoc = IsAoc(deviceId, deviceString, ""),
            });
            return;
        }

        foreach (var monitor in physical)
        {
            var identity = DetectIdentity(deviceId, deviceString, monitor.Description);
            list.Add(new FoundMonitor
            {
                Physical = monitor.Handle,
                HasHandle = true,
                Adapter = info.szDevice ?? "",
                DeviceId = deviceId,
                DeviceString = deviceString,
                Description = monitor.Description,
                Brand = identity.Brand,
                Model = identity.Model,
                IsAoc = IsAoc(deviceId, deviceString, monitor.Description),
            });
        }
    }

    private static bool TryReadPhysicalMonitors(
        IntPtr hMonitor,
        uint count,
        out List<(IntPtr Handle, string Description)> monitors,
        out string error)
    {
        monitors = [];
        var stride = IntPtr.Size + (128 * 2);
        var buffer = Marshal.AllocHGlobal(stride * (int)count);
        try
        {
            for (var i = 0; i < stride * (int)count; i++)
                Marshal.WriteByte(buffer, i, 0);

            if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, buffer))
            {
                error = $"(GetPhysicalMonitors failed, Win32 {Marshal.GetLastWin32Error()})";
                return false;
            }

            for (var i = 0; i < count; i++)
            {
                var item = buffer + (i * stride);
                var handle = Marshal.ReadIntPtr(item);
                var description = Marshal.PtrToStringUni(item + IntPtr.Size) ?? "";
                var terminator = description.IndexOf('\0');
                if (terminator >= 0)
                    description = description[..terminator];
                monitors.Add((handle, description));
            }

            error = "";
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (string Id, string Name) LookupMonitorDevice(string? adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return ("", "");

        var monitor = new NativeMethods.DisplayDevice { cb = Marshal.SizeOf<NativeMethods.DisplayDevice>() };
        if (!NativeMethods.EnumDisplayDevices(adapterName, 0, ref monitor, 0))
            return ("", "");

        return (monitor.DeviceID ?? "", monitor.DeviceString ?? "");
    }

    private static bool IsAoc(string deviceId, string deviceString, string description)
    {
        var blob = $"{deviceId} {deviceString} {description}".ToUpperInvariant();
        return blob.Contains("AOCB306", StringComparison.Ordinal) ||
               blob.Contains("U34G3G3", StringComparison.Ordinal) ||
               blob.Contains(@"MONITOR\AOC", StringComparison.Ordinal);
    }

    internal static (string Brand, string Model) DetectIdentity(string deviceId, string deviceString, string description)
    {
        var (wmiBrand, wmiModel) = TryWmiIdentity(deviceId);
        var brand = wmiBrand;
        if (string.IsNullOrWhiteSpace(brand))
        {
            var match = Regex.Match(deviceId ?? "", @"(?:MONITOR\\|DISPLAY#)([A-Za-z]{3})", RegexOptions.IgnoreCase);
            if (match.Success)
                brand = match.Groups[1].Value.ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(brand))
        {
            foreach (var token in new[] { deviceString, description })
            {
                if (string.IsNullOrWhiteSpace(token))
                    continue;
                var word = token.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                if (word.Length is >= 2 and <= 8 && word.All(char.IsLetter))
                {
                    brand = word.ToUpperInvariant();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(brand))
            brand = "Monitor";

        var model = wmiModel;
        if (string.IsNullOrWhiteSpace(model) &&
            !string.IsNullOrWhiteSpace(deviceString) &&
            !deviceString.Equals("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
            model = deviceString.Trim();

        return (brand, model);
    }

    private static (string Brand, string Model) TryWmiIdentity(string deviceId)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                @"root\wmi",
                "SELECT ManufacturerName, UserFriendlyName, InstanceName FROM WmiMonitorID");
            foreach (var item in searcher.Get())
            {
                using (item)
                {
                    var instance = item["InstanceName"] as string ?? "";
                    var name = DecodeWmiName(item["ManufacturerName"]);
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(deviceId))
                        continue;
                    if (MatchesPnp(deviceId, instance) ||
                        deviceId.Contains(@"MONITOR\" + name, StringComparison.OrdinalIgnoreCase) ||
                        deviceId.Contains("DISPLAY#" + name, StringComparison.OrdinalIgnoreCase))
                        return (name.ToUpperInvariant(), DecodeWmiName(item["UserFriendlyName"]));
                }
            }
        }
        catch (System.Management.ManagementException)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }

        return ("", "");
    }

    private void ApplyModelFromCapabilities()
    {
        if (!string.IsNullOrWhiteSpace(_model))
            return;
        var parsed = ParseDdcModel(_capabilities);
        if (!string.IsNullOrWhiteSpace(parsed))
            _model = parsed;
    }

    internal static string ParseDdcModel(string capabilities)
    {
        var match = DdcModelPattern.Match(capabilities ?? "");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static bool MatchesPnp(string deviceId, string instanceName)
    {
        var device = Regex.Match(deviceId, @"MONITOR\\([^\\]+)", RegexOptions.IgnoreCase);
        if (!device.Success)
            return false;
        return instanceName.Contains(device.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string DecodeWmiName(object? value)
    {
        IEnumerable<int> codes = value switch
        {
            ushort[] unsigned => unsigned.Select(code => (int)code),
            short[] signed => signed.Select(code => (int)code),
            int[] integers => integers,
            byte[] bytes => bytes.Select(code => (int)code),
            string text => text.Select(code => (int)code),
            _ => [],
        };
        var chars = codes.TakeWhile(code => code != 0).Select(code => (char)code).ToArray();
        return new string(chars).Trim();
    }

    internal static IReadOnlyList<uint> ParseInputCodes(string capabilities)
    {
        var match = InputCodePattern.Match(capabilities);
        if (!match.Success)
            return [];

        var codes = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var token in match.Groups[1].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                continue;
            if (seen.Add(code))
                codes.Add(code);
        }

        return codes;
    }

    private bool TryGetInput(out uint current)
    {
        current = 0;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(_handle, InputSourceCode, out _, out current, out _))
                return true;
            if (attempt == 0)
                NativeMethods.GetMonitorBrightness(_handle, out _, out _, out _);
            Thread.Sleep(80);
        }

        return false;
    }

    public string BrightnessStatus()
    {
        lock (Gate)
        {
            if (!_hasHandle)
                return "no handle";
            if (!NativeMethods.GetMonitorBrightness(_handle, out var minimum, out var current, out var maximum))
                return "failed Win32 " + Marshal.GetLastWin32Error();
            return $"{current} ({minimum}-{maximum})";
        }
    }

    private bool TrySetInputCode(uint code)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (NativeMethods.SetVCPFeature(_handle, InputSourceCode, code))
                return true;
            Thread.Sleep(80);
        }

        return false;
    }

    private static string Explain(string error)
    {
        if (error.Contains("DDC/CI", StringComparison.Ordinal))
            return error;
        return error + " Turn on DDC/CI in the AOC on-screen menu.";
    }

    private static string Win32(string api)
    {
        var code = Marshal.GetLastWin32Error();
        if (code == unchecked((int)0xC0262582))
            return $"{api} failed. The AOC did not answer DDC/CI. Open Extra on the monitor menu and set DDC/CI to On.";
        return $"{api} failed (Win32 {code}).";
    }
}
