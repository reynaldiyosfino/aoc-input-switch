namespace AocInputSwitch;

internal readonly record struct HotkeyChord(bool Ctrl, bool Alt, bool Shift, Keys Key)
{
    public uint Modifiers =>
        (uint)((Ctrl ? 0x0002 : 0) | (Alt ? 0x0001 : 0) | (Shift ? 0x0004 : 0) | NativeMethods.ModNorepeat);

    public uint VirtualKey => (uint)Key;

    public string Format()
    {
        var parts = new List<string>(4);
        if (Ctrl)
            parts.Add("Ctrl");
        if (Alt)
            parts.Add("Alt");
        if (Shift)
            parts.Add("Shift");
        parts.Add(FormatKey(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        var ctrl = false;
        var alt = false;
        var shift = false;
        Keys? key = null;
        foreach (var part in parts)
        {
            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
            }
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
            }
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
            }
            else if (TryParseKey(part, out var parsed))
            {
                if (key is not null)
                    return false;
                key = parsed;
            }
            else
            {
                return false;
            }
        }

        if (key is null || !(ctrl || alt || shift))
            return false;

        chord = new HotkeyChord(ctrl, alt, shift, key.Value);
        return true;
    }

    public static bool TryFromKeyData(Keys keyData, out HotkeyChord chord, out string? error)
    {
        chord = default;
        error = null;
        var key = keyData & Keys.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.Menu or Keys.ShiftKey or Keys.LWin or Keys.RWin)
            return false;

        var ctrl = keyData.HasFlag(Keys.Control);
        var alt = keyData.HasFlag(Keys.Alt);
        var shift = keyData.HasFlag(Keys.Shift);
        if (!ctrl && !alt && !shift)
        {
            error = "Use Ctrl, Alt, or Shift.";
            return false;
        }

        chord = new HotkeyChord(ctrl, alt, shift, key);
        return true;
    }

    public override string ToString() => Format();

    private static string FormatKey(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9)
            return ((int)(key - Keys.D0)).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9)
            return "Num" + (int)(key - Keys.NumPad0);
        return key.ToString();
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            key = Keys.D0 + (token[0] - '0');
            return true;
        }

        if (token.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            token.Length == 4 &&
            token[3] is >= '0' and <= '9' &&
            !token.Equals("NumLock", StringComparison.OrdinalIgnoreCase))
        {
            key = Keys.NumPad0 + (token[3] - '0');
            return true;
        }

        if (!Enum.TryParse(token, true, out key) || key == Keys.None)
            return false;

        if (key is Keys.Control or Keys.Shift or Keys.Alt or Keys.ControlKey or Keys.ShiftKey or Keys.Menu)
            return false;

        return true;
    }
}
