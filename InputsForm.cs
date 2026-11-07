namespace AocInputSwitch;

internal sealed class InputsForm : Form
{
    private readonly HotkeyChord[] _chords;
    private readonly TextBox[] _boxes;
    private readonly Label[] _values;
    private readonly Label _error;
    private int _captureIndex = -1;

    private InputsForm(IReadOnlyList<InputSlot> slots)
    {
        _chords = slots.Select(slot => slot.Hotkey).ToArray();
        _boxes = new TextBox[slots.Count];
        _values = new Label[slots.Count];

        Text = "Inputs";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 80 + (slots.Count * 44));
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        KeyPreview = true;
        Icon = AppImages.Icon;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = slots.Count * 44,
            ColumnCount = 4,
            RowCount = slots.Count,
            Padding = new Padding(12, 12, 12, 0),
            BackColor = Theme.Back,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        for (var i = 0; i < slots.Count; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / slots.Count));
            grid.Controls.Add(new Label
            {
                Text = MonitorSession.Describe(slots[i].Vcp),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted,
                BackColor = Theme.Back,
                Font = Theme.Body,
            }, 0, i);

            var box = new TextBox
            {
                Text = slots[i].Label,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Card,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                Margin = new Padding(0, 8, 8, 8),
            };
            _boxes[i] = box;
            grid.Controls.Add(box, 1, i);

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
            grid.Controls.Add(value, 2, i);

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
            grid.Controls.Add(change, 3, i);
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
        AcceptButton = save;
    }

    public IReadOnlyList<HotkeyChord> Chords => _chords;
    public IReadOnlyList<string> Labels => _boxes.Select(box => box.Text).ToList();

    public static bool TryEdit(
        IWin32Window owner,
        IReadOnlyList<InputSlot> slots,
        out IReadOnlyList<string> labels,
        out IReadOnlyList<HotkeyChord> chords)
    {
        using var form = new InputsForm(slots);
        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            labels = [];
            chords = [];
            return false;
        }

        labels = form.Labels;
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
