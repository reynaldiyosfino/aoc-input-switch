namespace AocInputSwitch;

internal sealed class ToastForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;

    public ToastForm(string message, bool error)
    {
        var back = error ? Theme.Error : Theme.Active;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = back;
        ForeColor = Theme.Text;

        var measured = TextRenderer.MeasureText(message, Theme.Body, new Size(360, 180), TextFormatFlags.WordBreak);
        Width = Math.Max(240, measured.Width + 36);
        Height = Math.Max(64, measured.Height + 28);

        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = message,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Body,
            ForeColor = Theme.Text,
            BackColor = back,
        });

        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);

        _timer = new System.Windows.Forms.Timer { Interval = error ? 5000 : 1800 };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
    }
}
