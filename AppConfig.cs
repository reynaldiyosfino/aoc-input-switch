using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AocInputSwitch;

internal sealed record InputSlot(string Id, string Label, uint Vcp, HotkeyChord Hotkey, bool Available = true);

internal readonly record struct LiveInputs(uint? Current, IReadOnlyList<uint> Reported, bool CapsKnown);

internal sealed class InputConfig
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public int? Vcp { get; set; }
}

internal sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly (string Id, string Label, string Hotkey, uint Vcp)[] Defaults =
    [
        ("dp1", "DP1", "Ctrl+Alt+1", 0x0F),
        ("dp2", "DP2", "Ctrl+Alt+2", 0x10),
        ("hdmi1", "HDMI1", "Ctrl+Alt+3", 0x11),
        ("hdmi2", "HDMI2", "Ctrl+Alt+4", 0x12),
    ];

    public bool StartWithWindows { get; set; }
    public List<InputConfig> Inputs { get; set; } = [];

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load()
    {
        var path = SettingsPath;
        AppConfig config;
        if (!File.Exists(path))
        {
            config = CreateDefault();
            config.Save();
            return config;
        }

        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), JsonOptions) ?? CreateDefault();
        }
        catch (JsonException)
        {
            config = CreateDefault();
        }

        config.Inputs = Normalize(config.Inputs);
        return config;
    }

    public void Save()
    {
        Inputs = Normalize(Inputs);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public IReadOnlyList<InputSlot> Resolve(IReadOnlyList<uint> capabilityCodes, out string? warning)
    {
        var warnings = new List<string>();
        var slots = new List<InputSlot>(Defaults.Length);
        foreach (var input in Inputs)
        {
            var fallback = DefaultHotkey(input.Id);
            if (!HotkeyChord.TryParse(input.Hotkey, out var chord))
            {
                HotkeyChord.TryParse(fallback, out chord);
                warnings.Add($"Hotkey {input.Label} is invalid, using default {fallback}.");
            }

            var vcp = input.Vcp is int explicitCode ? (uint)explicitCode : DefaultVcp(input.Id);
            slots.Add(new InputSlot(input.Id, input.Label, vcp, chord));
        }

        var explicitOverride = Inputs.Any(input => input.Vcp is not null);
        var known = slots.Select(slot => slot.Vcp).ToHashSet();
        if (!explicitOverride && capabilityCodes.Count >= 4 && capabilityCodes.All(code => !known.Contains(code)))
        {
            for (var i = 0; i < 4; i++)
                slots[i] = slots[i] with { Vcp = capabilityCodes[i] };
        }

        var seen = new HashSet<HotkeyChord>();
        foreach (var slot in slots)
        {
            if (!seen.Add(slot.Hotkey))
                warnings.Add($"Hotkey {slot.Label} conflicts with another input ({slot.Hotkey.Format()}).");
        }

        warning = warnings.Count == 0 ? null : string.Join(" ", warnings);
        return slots;
    }

    public void UpdateHotkeys(IReadOnlyList<HotkeyChord> chords)
    {
        for (var i = 0; i < Inputs.Count && i < chords.Count; i++)
            Inputs[i].Hotkey = chords[i].Format();
        Save();
    }

    public static uint DefaultVcp(string id)
    {
        foreach (var item in Defaults)
        {
            if (item.Id == id)
                return item.Vcp;
        }

        return 0;
    }

    private static string DefaultHotkey(string id)
    {
        foreach (var item in Defaults)
        {
            if (item.Id == id)
                return item.Hotkey;
        }

        return "Ctrl+Alt+1";
    }

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            StartWithWindows = false,
            Inputs = Normalize([]),
        };
    }

    private static List<InputConfig> Normalize(List<InputConfig>? source)
    {
        source ??= [];
        return Defaults.Select(item =>
        {
            var existing = source.FirstOrDefault(input => input.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase));
            return new InputConfig
            {
                Id = item.Id,
                Label = string.IsNullOrWhiteSpace(existing?.Label) ? item.Label : existing!.Label,
                Hotkey = string.IsNullOrWhiteSpace(existing?.Hotkey) ? item.Hotkey : existing!.Hotkey,
                Vcp = existing?.Vcp,
            };
        }).ToList();
    }
}
