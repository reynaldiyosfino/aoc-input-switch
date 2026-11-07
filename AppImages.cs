using System.Reflection;

namespace AocInputSwitch;

internal static class AppImages
{
    public static Icon Icon { get; } = LoadIcon();

    private static Icon LoadIcon()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
        if (stream is not null)
            return new Icon(stream);

        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var extracted = Icon.ExtractAssociatedIcon(path);
            if (extracted is not null)
                return extracted;
        }

        return SystemIcons.Application;
    }
}
