namespace AocInputSwitch;

internal sealed class HotkeyForm : Form
{
    private readonly HotkeyChord[] _chords;
    private readonly Label[] _values;
    private readonly Label _error;
    private int _captureIndex = -1;

    private HotkeyForm(IReadOnlyList<InputSlot> slots)
    {
        _chords = slots.Select(slot => slot.Hotkey).ToArray();
        _values = new Label[slots.Count];

        Text = "Set hotkeys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 280);
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        Icon = AppImages.Icon;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 180,
            ColumnCount = 3,
            RowCount = slots.Count,
            Padding = new Padding(12, 12, 12, 0),
            BackColor = Theme.Back,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        for (var i = 0; i < slots.Count; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / slots.Count));
            grid.Controls.Add(new Label
            {
                Text = slots[i].Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Text,
                BackColor = Theme.Back,
                Font = Theme.Body,
            }, 0, i);

            var value = new Label
            {
                Text = _chords[i].Format(),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted,
                BackColor = Theme.Back,
                Font = Theme.Body,
            };
            _values[i] = value;
            grid.Controls.Add(value, 1, i);

            var index = i;
            var change = new Button
            {
                Text = "Change",
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 6, 0, 6),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
            };
            change.FlatAppearance.BorderSize = 0;
            change.Click += (_, _) => BeginCapture(index);
            grid.Controls.Add(change, 2, i);
        }

        _error = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            ForeColor = Color.FromArgb(255, 140, 140),
            BackColor = Theme.Back,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Theme.Back,
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Width = 100,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
        };
        cancel.FlatAppearance.BorderSize = 0;
        var save = new Button
        {
            Text = "Save",
            Width = 100,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Active,
            ForeColor = Theme.Text,
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        Controls.Add(buttons);
        Controls.Add(_error);
        Controls.Add(grid);
        CancelButton = cancel;
    }

    public IReadOnlyList<HotkeyChord> Chords => _chords;

    public static bool TryEdit(IWin32Window owner, IReadOnlyList<InputSlot> slots, out IReadOnlyList<HotkeyChord> chords)
    {
        using var form = new HotkeyForm(slots);
        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            chords = [];
            return false;
        }

        chords = form.Chords;
        return true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_captureIndex < 0)
            return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        if (key == Keys.Escape)
        {
            EndCapture();
            return true;
        }

        if (!HotkeyChord.TryFromKeyData(keyData, out var chord, out var error))
        {
            if (error is not null)
                _error.Text = error;
            return true;
        }

        _chords[_captureIndex] = chord;
        _values[_captureIndex].Text = chord.Format();
        _error.Text = "";
        EndCapture();
        return true;
    }

    private void BeginCapture(int index)
    {
        EndCapture();
        _captureIndex = index;
        _values[index].Text = "Press a shortcut...";
        _values[index].ForeColor = Theme.Text;
        _error.Text = "";
    }

    private void EndCapture()
    {
        if (_captureIndex < 0)
            return;
        _values[_captureIndex].Text = _chords[_captureIndex].Format();
        _values[_captureIndex].ForeColor = Theme.Muted;
        _captureIndex = -1;
    }

    private void SaveAndClose()
    {
        EndCapture();
        for (var i = 0; i < _chords.Length; i++)
        {
            if (!_chords[i].Ctrl && !_chords[i].Alt && !_chords[i].Shift)
            {
                _error.Text = "Use Ctrl, Alt, or Shift.";
                return;
            }

            for (var j = 0; j < i; j++)
            {
                if (_chords[j] != _chords[i])
                    continue;
                _error.Text = $"This shortcut is already used by input {j + 1}.";
                return;
            }
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
