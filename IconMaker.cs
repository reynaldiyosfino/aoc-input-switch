using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace AocInputSwitch;

internal static class IconMaker
{
    private const char Desktop = '\uf108';
    private const char Refresh = '\uf021';

    public static int WriteAssets()
    {
        var assets = FindAssetsDirectory();
        var fontPath = Path.Combine(assets, "fontawesome-webfont.ttf");
        if (!File.Exists(fontPath))
            return Fail("Font Awesome TTF was not found at " + fontPath);

        using var fonts = new PrivateFontCollection();
        fonts.AddFontFile(fontPath);
        var family = fonts.Families[0];

        using var master = Render(256, family);
        var pngPath = Path.Combine(assets, "app.png");
        master.Save(pngPath, ImageFormat.Png);

        var sizes = new[] { 16, 32, 48, 256 };
        var pngs = new List<byte[]>(sizes.Length);
        foreach (var size in sizes)
        {
            using var frame = size == 256 ? (Bitmap)master.Clone() : Render(size, family);
            pngs.Add(ToPng(frame));
        }

        var icoPath = Path.Combine(assets, "app.ico");
        File.WriteAllBytes(icoPath, PackIco(pngs, sizes));
        Console.WriteLine("Wrote " + pngPath);
        Console.WriteLine("Wrote " + icoPath);
        return 0;
    }

    private static Bitmap Render(int size, FontFamily family)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var pad = size * 0.04f;
        var radius = size * 0.22f;
        var tile = new RectangleF(pad, pad, size - (pad * 2), size - (pad * 2));
        using (var path = RoundedRect(tile, radius))
        using (var fill = new SolidBrush(Color.Black))
            graphics.FillPath(fill, path);

        using var white = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        var desktopSize = size * (size <= 32 ? 0.62f : 0.68f);
        using (var desktopFont = new Font(family, desktopSize, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var desktopBox = new RectangleF(0, size * 0.02f, size, size * 0.96f);
            graphics.DrawString(Desktop.ToString(), desktopFont, white, desktopBox, format);
        }

        var refreshSize = size * (size <= 32 ? 0.22f : 0.24f);
        using (var refreshFont = new Font(family, refreshSize, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var refreshBox = new RectangleF(0, size * -0.04f, size, size * 0.86f);
            graphics.DrawString(Refresh.ToString(), refreshFont, white, refreshBox, format);
        }

        return bitmap;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static byte[] PackIco(List<byte[]> pngs, int[] sizes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngs.Count);

        var offset = 6 + (16 * pngs.Count);
        for (var i = 0; i < pngs.Count; i++)
        {
            var edge = sizes[i];
            writer.Write((byte)(edge >= 256 ? 0 : edge));
            writer.Write((byte)(edge >= 256 ? 0 : edge));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(pngs[i].Length);
            writer.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
            writer.Write(png);

        return stream.ToArray();
    }

    private static string FindAssetsDirectory()
    {
        var start = Directory.GetCurrentDirectory();
        for (var dir = start; dir is not null; dir = Directory.GetParent(dir)?.FullName)
        {
            var assets = Path.Combine(dir, "Assets");
            if (File.Exists(Path.Combine(assets, "fontawesome-webfont.ttf")))
                return assets;
        }

        return Path.Combine(start, "Assets");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
