namespace AocInputSwitch;

internal static class Theme
{
    public static readonly Font Title = new("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Point);
    public static readonly Font Body = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Font Hotkey = new("Segoe UI", 9, FontStyle.Regular, GraphicsUnit.Point);
    public static readonly Color Back = Color.FromArgb(30, 30, 30);
    public static readonly Color Card = Color.FromArgb(45, 45, 48);
    public static readonly Color Active = Color.FromArgb(14, 99, 156);
    public static readonly Color Error = Color.FromArgb(140, 40, 40);
    public static readonly Color Text = Color.White;
    public static readonly Color Muted = Color.FromArgb(186, 201, 214);
}
