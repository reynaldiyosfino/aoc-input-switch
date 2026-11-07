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
    public bool ShowedUnpluggedHint { get; set; }
    public List<string> UnpluggedIds { get; set; } = [];
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
        var template = capabilityCodes.Count > 0
            ? capabilityCodes.Select(SlotFromCode).ToList()
            : Defaults.Select(item => (item.Id, item.Label, item.Vcp)).ToList();

        var slots = new List<InputSlot>(template.Count);
        for (var i = 0; i < template.Count; i++)
        {
            var item = template[i];
            var existing = FindExisting(item.Id, item.Vcp);
            var fallback = DefaultHotkeyForIndex(i);
            var hotkeyText = string.IsNullOrWhiteSpace(existing?.Hotkey) ? fallback : existing!.Hotkey;
            if (!HotkeyChord.TryParse(hotkeyText, out var chord))
            {
                HotkeyChord.TryParse(fallback, out chord);
                warnings.Add($"Hotkey {item.Label} is invalid, using default {fallback}.");
            }

            var label = string.IsNullOrWhiteSpace(existing?.Label) ? item.Label : existing!.Label.Trim();
            slots.Add(new InputSlot(item.Id, label, item.Vcp, chord));
        }

        var seen = new HashSet<HotkeyChord>();
        foreach (var slot in slots)
        {
            if (!seen.Add(slot.Hotkey))
                warnings.Add($"Hotkey {slot.Label} conflicts with another input ({slot.Hotkey.Format()}).");
        }

        SyncInputs(slots);
        warning = warnings.Count == 0 ? null : string.Join(" ", warnings);
        var dead = UnpluggedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return slots.Select(slot => slot with { Available = !dead.Contains(slot.Id) }).ToList();
    }

    public void SetUnplugged(string id, bool unplugged)
    {
        UnpluggedIds.RemoveAll(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (unplugged)
            UnpluggedIds.Add(id);
        Save();
    }

    public void UpdateHotkeys(IReadOnlyList<HotkeyChord> chords)
    {
        for (var i = 0; i < Inputs.Count && i < chords.Count; i++)
            Inputs[i].Hotkey = chords[i].Format();
        Save();
    }

    public void UpdateLabels(IReadOnlyList<string> labels)
    {
        for (var i = 0; i < Inputs.Count && i < labels.Count; i++)
        {
            var text = labels[i].Trim();
            var vcp = Inputs[i].Vcp is int code ? (uint)code : DefaultVcp(Inputs[i].Id);
            Inputs[i].Label = text.Length == 0 ? MonitorSession.Describe(vcp) : text;
        }

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

    private InputConfig? FindExisting(string id, uint vcp)
    {
        return Inputs.FirstOrDefault(input => input.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? Inputs.FirstOrDefault(input => input.Vcp is int code && (uint)code == vcp);
    }

    private void SyncInputs(IReadOnlyList<InputSlot> slots)
    {
        var next = slots.Select(slot => new InputConfig
        {
            Id = slot.Id,
            Label = slot.Label,
            Hotkey = slot.Hotkey.Format(),
            Vcp = (int)slot.Vcp,
        }).ToList();
        if (SameInputs(Inputs, next))
            return;
        Inputs = next;
        Save();
    }

    private static bool SameInputs(List<InputConfig> left, List<InputConfig> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!left[i].Id.Equals(right[i].Id, StringComparison.OrdinalIgnoreCase))
                return false;
            if (left[i].Vcp != right[i].Vcp)
                return false;
            if (!string.Equals(left[i].Label, right[i].Label, StringComparison.Ordinal))
                return false;
            if (!string.Equals(left[i].Hotkey, right[i].Hotkey, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static (string Id, string Label, uint Vcp) SlotFromCode(uint code)
    {
        var label = MonitorSession.Describe(code);
        return (label.ToLowerInvariant(), label, code);
    }

    private static string DefaultHotkeyForIndex(int index)
    {
        if (index < 9)
            return $"Ctrl+Alt+{index + 1}";
        if (index == 9)
            return "Ctrl+Alt+0";
        return $"Ctrl+Alt+F{index - 9}";
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
        if (source.Count == 0)
        {
            return Defaults.Select(item => new InputConfig
            {
                Id = item.Id,
                Label = item.Label,
                Hotkey = item.Hotkey,
                Vcp = (int)item.Vcp,
            }).ToList();
        }

        return source
            .Where(input => !string.IsNullOrWhiteSpace(input.Id))
            .Select(input =>
            {
                var id = input.Id.Trim();
                var vcp = input.Vcp is int code ? (uint)code : DefaultVcp(id);
                var fallbackLabel = vcp == 0 ? id.ToUpperInvariant() : MonitorSession.Describe(vcp);
                return new InputConfig
                {
                    Id = id,
                    Label = string.IsNullOrWhiteSpace(input.Label) ? fallbackLabel : input.Label.Trim(),
                    Hotkey = string.IsNullOrWhiteSpace(input.Hotkey) ? "Ctrl+Alt+1" : input.Hotkey.Trim(),
                    Vcp = (int)vcp,
                };
            })
            .ToList();
    }
}
