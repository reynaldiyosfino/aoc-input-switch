using System.Runtime.InteropServices;
using System.Text;

namespace AocInputSwitch;

internal static class NvidiaDdc
{
    private const uint IdInitialize = 0x0150E828;
    private const uint IdGetErrorMessage = 0x6C2D048C;
    private const uint IdEnumDisplay = 0x9ABDD40D;
    private const uint IdDisplayName = 0x22A78B05;
    private const uint IdGpusFromDisplay = 0x34EF9506;
    private const uint IdOutputId = 0xD995937E;
    private const uint IdI2CWrite = 0xE812EB07;
    private const uint IdI2CRead = 0x2FDE12C5;
    private const int NvOk = 0;
    private const int NvEnd = -7;
    private const int MaxGpus = 64;
    private const byte HostAddress = 0x51;
    private const byte WriteAddress = 0x6E;
    private const byte ReadAddress = 0x6F;
    private const uint I2cSpeedDeprecated = 0xFFFF;
    private static readonly uint[] InfoVersions = [0x00030040, 0x00020038];

    private static readonly object Gate = new();
    private static bool _loaded;
    private static bool _ready;
    private static InitializeDelegate? _initialize;
    private static ErrorMessageDelegate? _errorMessage;
    private static EnumDisplayDelegate? _enumDisplay;
    private static DisplayNameDelegate? _displayName;
    private static GpusFromDisplayDelegate? _gpusFromDisplay;
    private static OutputIdDelegate? _outputId;
    private static I2CDelegate? _i2cWrite;
    private static I2CDelegate? _i2cRead;

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int ErrorMessageDelegate(int status, StringBuilder message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumDisplayDelegate(int index, out IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate int DisplayNameDelegate(IntPtr display, StringBuilder name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GpusFromDisplayDelegate(IntPtr display, [Out] IntPtr[] gpus, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OutputIdDelegate(IntPtr display, out uint outputId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int I2CDelegate(IntPtr gpu, IntPtr info);

    public static string Probe(string adapter)
    {
        lock (Gate)
        {
            if (!EnsureReady(out var error))
                return error;

            var lines = new StringBuilder();
            foreach (var display in ListDisplays())
            {
                lines.Append(display.Name);
                lines.Append(" output=0x");
                lines.Append(display.OutputId.ToString("X"));
                if (!TryGetVcp(display, 0x60, out var value, out var readError))
                    lines.Append(" 0x60 failed: ").Append(readError);
                else
                    lines.Append(" 0x60=0x").Append(value.ToString("X2")).Append(" (").Append(MonitorSession.Describe(value)).Append(')');
                if (NamesMatch(display.Name, adapter))
                    lines.Append(" [target]");
                lines.AppendLine();
            }

            return lines.Length == 0 ? "NVAPI did not see a display." : lines.ToString().TrimEnd();
        }
    }

    public static bool TrySetInput(string adapter, uint code, out string error)
    {
        lock (Gate)
        {
            if (!EnsureReady(out error))
                return false;

            var displays = ListDisplays();
            var chosen = displays.Where(display => NamesMatch(display.Name, adapter)).ToList();
            if (chosen.Count == 0)
                chosen = displays;

            string? last = "NVAPI did not see a display.";
            foreach (var display in chosen)
            {
                if (TrySetVcp(display, 0x60, code, out error))
                    return true;
                last = $"{display.Name}: {error}";
            }

            error = last ?? "NVAPI I2C failed.";
            return false;
        }
    }

    public static bool TryGetInput(string adapter, out uint code, out string error)
    {
        lock (Gate)
        {
            code = 0;
            if (!EnsureReady(out error))
                return false;

            foreach (var display in ListDisplays())
            {
                if (!NamesMatch(display.Name, adapter))
                    continue;
                return TryGetVcp(display, 0x60, out code, out error);
            }

            error = "NVIDIA display for the AOC was not found.";
            return false;
        }
    }

    private static bool EnsureReady(out string error)
    {
        error = "";
        if (_ready)
            return true;
        if (_loaded)
        {
            error = "NVAPI is not ready.";
            return false;
        }

        _loaded = true;
        try
        {
            _initialize = Get<InitializeDelegate>(IdInitialize);
            _errorMessage = Get<ErrorMessageDelegate>(IdGetErrorMessage);
            _enumDisplay = Get<EnumDisplayDelegate>(IdEnumDisplay);
            _displayName = Get<DisplayNameDelegate>(IdDisplayName);
            _gpusFromDisplay = Get<GpusFromDisplayDelegate>(IdGpusFromDisplay);
            _outputId = Get<OutputIdDelegate>(IdOutputId);
            _i2cWrite = Get<I2CDelegate>(IdI2CWrite);
            _i2cRead = Get<I2CDelegate>(IdI2CRead);
        }
        catch (DllNotFoundException)
        {
            error = "nvapi64.dll is missing.";
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            error = "nvapi_QueryInterface is missing.";
            return false;
        }

        if (_initialize is null || _enumDisplay is null || _i2cWrite is null)
        {
            error = "NVAPI I2C is unavailable.";
            return false;
        }

        var status = _initialize();
        if (status != NvOk)
        {
            error = Describe(status);
            return false;
        }

        _ready = true;
        return true;
    }

    private static T? Get<T>(uint id) where T : class
    {
        var pointer = QueryInterface(id);
        return pointer == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }

    private static List<NvDisplay> ListDisplays()
    {
        var list = new List<NvDisplay>();
        if (_enumDisplay is null)
            return list;

        for (var i = 0; i < 16; i++)
        {
            var status = _enumDisplay(i, out var handle);
            if (status == NvEnd)
                break;
            if (status != NvOk)
                continue;

            var name = new StringBuilder(64);
            _displayName?.Invoke(handle, name);
            var gpus = new IntPtr[MaxGpus];
            uint gpuCount = 0;
            uint outputId = 0;
            var gpuStatus = _gpusFromDisplay?.Invoke(handle, gpus, out gpuCount) ?? -1;
            var outputStatus = _outputId?.Invoke(handle, out outputId) ?? -1;
            if (gpuStatus != NvOk || outputStatus != NvOk || gpuCount == 0 || gpus[0] == IntPtr.Zero)
                continue;

            list.Add(new NvDisplay(handle, name.ToString(), gpus[0], outputId));
        }

        return list;
    }

    private static bool TrySetVcp(NvDisplay display, byte feature, uint value, out string error)
    {
        byte[] payload = [0x84, 0x03, feature, (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), 0];
        if (!TryI2c(display, write: true, payload, out error))
            return false;
        Thread.Sleep(50);
        payload = [0x84, 0x03, feature, (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), 0];
        TryI2c(display, write: true, payload, out _);
        Thread.Sleep(80);
        error = "";
        return true;
    }

    private static bool TryGetVcp(NvDisplay display, byte feature, out uint value, out string error)
    {
        value = 0;
        byte[] query = [0x82, 0x01, feature, 0];
        if (!TryI2c(display, write: true, query, out error))
            return false;

        Thread.Sleep(80);
        var reply = new byte[11];
        if (!TryI2c(display, write: false, reply, out error))
            return false;

        if (reply[3] != 0 || reply[4] != feature)
        {
            error = "Monitor rejected VCP " + feature.ToString("X2") + ".";
            return false;
        }

        value = (uint)((reply[8] << 8) | reply[9]);
        error = "";
        return true;
    }

    private static bool TryI2c(NvDisplay display, bool write, byte[] data, out string error)
    {
        error = "";
        var dest = write ? new byte[] { HostAddress } : new byte[] { 0 };
        ApplyChecksum(write ? WriteAddress : ReadAddress, dest, data);
        var destHandle = GCHandle.Alloc(dest, GCHandleType.Pinned);
        var dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        var info = Marshal.AllocHGlobal(64);
        try
        {
            string? last = null;
            foreach (var version in InfoVersions)
            {
                FillInfo(info, version, display.OutputId, write ? WriteAddress : ReadAddress, destHandle.AddrOfPinnedObject(), 1, dataHandle.AddrOfPinnedObject(), data.Length);
                var status = write
                    ? _i2cWrite!(display.Gpu, info)
                    : _i2cRead!(display.Gpu, info);
                if (status == NvOk)
                    return true;
                last = Describe(status);
            }

            error = last ?? "I2C failed.";
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(info);
            destHandle.Free();
            dataHandle.Free();
        }
    }

    private static void FillInfo(
        IntPtr info,
        uint version,
        uint displayMask,
        byte address,
        IntPtr register,
        int registerSize,
        IntPtr data,
        int dataSize)
    {
        for (var i = 0; i < 64; i++)
            Marshal.WriteByte(info, i, 0);
        Marshal.WriteInt32(info, 0, unchecked((int)version));
        Marshal.WriteInt32(info, 4, unchecked((int)displayMask));
        Marshal.WriteByte(info, 8, 1);
        Marshal.WriteByte(info, 9, address);
        Marshal.WriteIntPtr(info, 16, register);
        Marshal.WriteInt32(info, 24, registerSize);
        Marshal.WriteIntPtr(info, 32, data);
        Marshal.WriteInt32(info, 40, dataSize);
        Marshal.WriteInt32(info, 44, unchecked((int)I2cSpeedDeprecated));
        Marshal.WriteInt32(info, 48, 0);
    }

    private static void ApplyChecksum(byte device, byte[] register, byte[] data)
    {
        byte checksum = device;
        foreach (var value in register)
            checksum ^= value;
        for (var i = 0; i < data.Length - 1; i++)
            checksum ^= data[i];
        data[^1] = checksum;
    }

    private static bool NamesMatch(string nvidiaName, string adapter)
    {
        if (string.IsNullOrWhiteSpace(adapter))
            return false;
        return nvidiaName.Contains(adapter.Trim('\\'), StringComparison.OrdinalIgnoreCase) ||
               adapter.Contains(nvidiaName.Trim('\\'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(int status)
    {
        if (_errorMessage is null)
            return "NVAPI status " + status;
        var text = new StringBuilder(64);
        _errorMessage(status, text);
        var message = text.ToString();
        return string.IsNullOrWhiteSpace(message) ? "NVAPI status " + status : message;
    }

    private readonly record struct NvDisplay(IntPtr Handle, string Name, IntPtr Gpu, uint OutputId);
}
