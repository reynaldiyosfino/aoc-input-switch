namespace AocInputSwitch;

internal sealed class SwitchForm : Form
{
    private readonly TrayApplication _app;
    private readonly Label _title;
    private readonly TableLayoutPanel _grid;
    private readonly List<SlotButton> _buttons = [];
    private int _layoutCount = -1;

    public SwitchForm(TrayApplication app)
    {
        _app = app;
        Text = app.Brand;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(520, 340);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = AppImages.Icon;

        _title = new Label
        {
            Text = app.Brand,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Title,
            ForeColor = Theme.Text,
            BackColor = Theme.Back,
            Padding = new Padding(8, 0, 8, 0),
        };

        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            BackColor = Theme.Back,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Back,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(_title, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        Controls.Add(root);
        RebuildGrid(Math.Max(1, app.Slots.Count));
    }

    public void SetBrand(string brand)
    {
        var text = string.IsNullOrWhiteSpace(brand) ? "Monitor" : brand;
        _title.Text = text;
        Text = text;
    }

    public void RefreshSlots()
    {
        SetBrand(_app.Brand);
        var slots = _app.Slots;
        if (slots.Count != _layoutCount)
            RebuildGrid(Math.Max(1, slots.Count));

        var current = _app.CurrentCode;
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (i >= slots.Count)
            {
                _buttons[i].Visible = false;
                _buttons[i].Bind(null, false, false);
                continue;
            }

            _buttons[i].Visible = true;
            _buttons[i].Bind(slots[i], current == slots[i].Vcp, slots[i].Available);
        }
    }

    private void RebuildGrid(int count)
    {
        _grid.SuspendLayout();
        _grid.Controls.Clear();
        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();
        _buttons.Clear();
        _layoutCount = count;

        var columns = count <= 1 ? 1 : 2;
        var rows = count <= 2 ? 1 : (int)Math.Ceiling(count / 2.0);
        _grid.ColumnCount = columns;
        _grid.RowCount = rows;
        for (var column = 0; column < columns; column++)
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        for (var row = 0; row < rows; row++)
            _grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        var cells = columns * rows;
        for (var i = 0; i < cells; i++)
        {
            var button = new SlotButton();
            var index = i;
            button.Activated += (_, _) =>
            {
                var slots = _app.Slots;
                if (index < slots.Count)
                    _app.RequestSwitch(slots[index]);
            };
            button.ToggleCable += (_, _) =>
            {
                var slots = _app.Slots;
                if (index < slots.Count)
                    _app.ToggleUnplugged(slots[index]);
            };
            _buttons.Add(button);
            button.Visible = i < count;
            _grid.Controls.Add(button, i % columns, i / columns);
        }

        var height = 48 + (rows * 146);
        ClientSize = new Size(columns == 1 ? 280 : 520, Math.Max(196, height));
        _grid.ResumeLayout();
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
    private bool _available = true;

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
    public event EventHandler? ToggleCable;

    public void Bind(InputSlot? slot, bool active, bool available)
    {
        _active = active;
        _available = available && slot is not null;
        _name.Text = slot?.Label ?? "";
        _hotkey.Text = slot?.Hotkey.Format() ?? "";
        var cursor = _available ? Cursors.Hand : Cursors.Default;
        Cursor = cursor;
        foreach (Control child in Controls)
            SetCursor(child, cursor);
        ApplyColors();
    }

    private void Wire(Control control)
    {
        control.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ToggleCable?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (e.Button == MouseButtons.Left && _available)
                Activated?.Invoke(this, EventArgs.Empty);
        };
        foreach (Control child in control.Controls)
            Wire(child);
    }

    private void ApplyColors()
    {
        var back = !_available ? Theme.Disabled : _active ? Theme.Active : Theme.Card;
        var text = _available ? Theme.Text : Theme.DisabledText;
        var muted = _available ? Theme.Muted : Theme.DisabledText;
        BackColor = back;
        _name.ForeColor = text;
        _hotkey.ForeColor = muted;
        foreach (Control child in Controls)
            PaintTree(child, back);
    }

    private static void SetCursor(Control control, Cursor cursor)
    {
        control.Cursor = cursor;
        foreach (Control child in control.Controls)
            SetCursor(child, cursor);
    }

    private static void PaintTree(Control control, Color back)
    {
        control.BackColor = back;
        foreach (Control child in control.Controls)
            PaintTree(child, back);
    }
}
