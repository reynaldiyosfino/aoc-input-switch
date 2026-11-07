namespace AocInputSwitch;

internal sealed class RenameForm : Form
{
    private readonly TextBox[] _boxes;
    private readonly Label _error;

    private RenameForm(IReadOnlyList<InputSlot> slots)
    {
        _boxes = new TextBox[slots.Count];

        Text = "Rename inputs";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(460, 80 + (slots.Count * 40));
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Icon = AppImages.Icon;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = slots.Count * 40,
            ColumnCount = 2,
            RowCount = slots.Count,
            Padding = new Padding(12, 12, 12, 0),
            BackColor = Theme.Back,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

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
            };
            _boxes[i] = box;
            grid.Controls.Add(box, 1, i);
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

    public IReadOnlyList<string> Labels => _boxes.Select(box => box.Text).ToList();

    public static bool TryEdit(IWin32Window owner, IReadOnlyList<InputSlot> slots, out IReadOnlyList<string> labels)
    {
        using var form = new RenameForm(slots);
        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            labels = [];
            return false;
        }

        labels = form.Labels;
        return true;
    }

    private void SaveAndClose()
    {
        _error.Text = "";
        DialogResult = DialogResult.OK;
        Close();
    }
}
