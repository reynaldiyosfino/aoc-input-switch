namespace AocInputSwitch;

internal sealed class SwitchForm : Form
{
    private readonly TrayApplication _app;
    private readonly SlotButton[] _buttons;

    public SwitchForm(TrayApplication app)
    {
        _app = app;
        Text = "AOC Input";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(520, 340);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = AppImages.Icon;

        var title = new Label
        {
            Text = "AOC Input",
            Dock = DockStyle.Top,
            Height = 48,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            BackColor = Theme.Back,
            Padding = new Padding(8, 0, 0, 0),
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8),
            BackColor = Theme.Back,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        _buttons = new SlotButton[4];
        for (var i = 0; i < _buttons.Length; i++)
        {
            var button = new SlotButton();
            var index = i;
            button.Activated += (_, _) =>
            {
                var slots = _app.Slots;
                if (index < slots.Count)
                    _app.RequestSwitch(slots[index]);
            };
            _buttons[i] = button;
            grid.Controls.Add(button, i % 2, i / 2);
        }

        title.Dock = DockStyle.Fill;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Back,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(grid, 0, 1);
        Controls.Add(root);
    }

    public void RefreshSlots()
    {
        var slots = _app.Slots;
        var current = _app.CurrentCode;
        for (var i = 0; i < _buttons.Length; i++)
        {
            if (i >= slots.Count)
            {
                _buttons[i].Bind(null, false);
                continue;
            }

            _buttons[i].Bind(slots[i], current == slots[i].Vcp);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        ShowInTaskbar = Visible;
        if (Visible)
            PlaceOnCurrentScreen();
        base.OnVisibleChanged(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    private void PlaceOnCurrentScreen()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            area.Left + Math.Max(0, (area.Width - Width) / 2),
            area.Top + Math.Max(0, (area.Height - Height) / 2));
    }
}

internal sealed class SlotButton : Panel
{
    private readonly Label _name;
    private readonly Label _hotkey;
    private bool _active;

    public SlotButton()
    {
        Margin = new Padding(8);
        Cursor = Cursors.Hand;
        Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

        _name = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            Cursor = Cursors.Hand,
        };
        _hotkey = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopCenter,
            Font = Theme.Hotkey,
            ForeColor = Theme.Muted,
            Cursor = Cursors.Hand,
        };
        layout.Controls.Add(_name, 0, 0);
        layout.Controls.Add(_hotkey, 0, 1);
        Controls.Add(layout);
        Wire(this);
        ApplyColors();
    }

    public event EventHandler? Activated;

    public void Bind(InputSlot? slot, bool active)
    {
        _active = active;
        _name.Text = slot?.Label ?? "";
        _hotkey.Text = slot?.Hotkey.Format() ?? "";
        Enabled = slot is not null;
        ApplyColors();
    }

    private void Wire(Control control)
    {
        control.Click += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        foreach (Control child in control.Controls)
            Wire(child);
    }

    private void ApplyColors()
    {
        var back = _active ? Theme.Active : Theme.Card;
        BackColor = back;
        foreach (Control child in Controls)
            PaintTree(child, back);
    }

    private static void PaintTree(Control control, Color back)
    {
        control.BackColor = back;
        foreach (Control child in control.Controls)
            PaintTree(child, back);
    }
}
