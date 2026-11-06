using System.Drawing.Drawing2D;

namespace AocInputSwitch;

internal static class AppImages
{
    public static Icon Icon { get; } = CreateIcon();

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var brush = new SolidBrush(Theme.Active);
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            using var text = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString("A", font, text, new RectangleF(0, 1, 32, 32), format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
