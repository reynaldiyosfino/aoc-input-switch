using Microsoft.Win32;

namespace AocInputSwitch;

internal sealed class TrayApplication : ApplicationContext
{
    private const string RunValueName = "AocInputSwitch";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly NotifyIcon _notify;
    private readonly HotkeyWindow _hotkeys = new();
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Control _invoker = new();
    private AppConfig _config;
    private MonitorSession? _monitor;
    private IReadOnlyList<InputSlot> _slots = [];
    private uint? _current;
    private int _generation;
    private int _busy;
    private ToastForm? _toast;
    private SwitchForm? _switchForm;

    public TrayApplication()
    {
        _ = _invoker.Handle;
        _config = AppConfig.Load();
        TryOpenMonitor();
        _slots = _config.Resolve(_monitor?.CapabilityInputCodes ?? [], out var warning);

        _hotkeys.Pressed += OnHotkey;
        _hotkeys.Open();
        ApplyHotkeys();
        if (_config.StartWithWindows)
            WriteRunKey(true);

        _notify = new NotifyIcon
        {
            Icon = AppImages.Icon,
            Visible = true,
            Text = "AOC Input",
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _notify.ContextMenuStrip.Opening += (_, _) => PopulateMenu();
        _notify.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ShowSwitcher();
        };

        _timer = new System.Windows.Forms.Timer { Interval = 4000 };
        _timer.Tick += (_, _) => RefreshCurrent();
        Application.Idle += OnFirstIdle;
        if (warning is not null)
            ShowToast(warning, error: true);
    }

    public bool IsExiting { get; private set; }
    public IReadOnlyList<InputSlot> Slots => _slots;
    public uint? CurrentCode => _current;

    public void RequestSwitch(InputSlot slot)
    {
        if (!slot.Available)
            return;
        if (Interlocked.Exchange(ref _busy, 1) == 1)
            return;

        var generation = Interlocked.Increment(ref _generation);
        Task.Run(() =>
        {
            var ok = false;
            var already = false;
            var message = "AOC monitor was not found.";
            uint? read = null;
            try
            {
                if (_monitor is null)
                    TryOpenMonitor();

                if (_monitor is null)
                {
                    message = "AOC monitor was not found.";
                }
                else
                {
                    read = _monitor.ReadInputCode(out _);
                    if (read == slot.Vcp)
                    {
                        ok = true;
                        already = true;
                        message = "Already on " + slot.Label;
                    }
                    else if (_monitor.TrySetInput(slot.Vcp, out var error))
                    {
                        ok = true;
                        read = _monitor.ReadInputCode(out _) ?? slot.Vcp;
                        message = slot.Label;
                    }
                    else
                    {
                        message = string.IsNullOrWhiteSpace(error)
                            ? "Could not change input. Turn on DDC/CI in the AOC on-screen menu."
                            : error;
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }

            _invoker.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _busy, 0);
                if (generation != _generation)
                    return;
                if (ok)
                    _current = read ?? slot.Vcp;
                ShowToast(already ? message : (ok ? NameFor(_current) : message), error: !ok);
                UpdateVisuals();
            });
        });
    }

    public void BeginHotkeyEdit()
    {
        UnregisterHotkeys();
        try
        {
            var owner = EnsureSwitcherHiddenOwner();
            if (!HotkeyForm.TryEdit(owner, _slots, out var chords))
                return;
            _config.UpdateHotkeys(chords);
            _slots = ApplyAvailability(_config.Resolve(_monitor?.CapabilityInputCodes ?? [], out var warning), _current, null, capsKnown: false);
            if (warning is not null)
                ShowToast(warning, error: true);
            _switchForm?.RefreshSlots();
            RefreshCurrent();
        }
        finally
        {
            ApplyHotkeys();
        }
    }

    public void ToggleStartup()
    {
        _config.StartWithWindows = !_config.StartWithWindows;
        _config.Save();
        WriteRunKey(_config.StartWithWindows);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _invoker.Dispose();
            _notify.Visible = false;
            _notify.Dispose();
            UnregisterHotkeys();
            _hotkeys.CloseWindow();
            _monitor?.Dispose();
            _toast?.Dispose();
            _switchForm?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        RefreshCurrent();
        _timer.Start();
    }

    private void OnHotkey(int id)
    {
        var index = id - 1;
        if (index < 0 || index >= _slots.Count)
            return;
        var slot = _slots[index];
        if (!slot.Available)
            return;
        RequestSwitch(slot);
    }

    private void RefreshCurrent()
    {
        var generation = _generation;
        Task.Run(() =>
        {
            var live = _monitor?.ReadLiveInputs();
            _invoker.BeginInvoke(() =>
            {
                if (generation != _generation)
                    return;
                _current = live?.Current;
                var previous = _slots.Select(slot => slot.Available).ToArray();
                _slots = ApplyAvailability(_slots, _current, live?.Reported, live?.CapsKnown == true);
                if (!previous.SequenceEqual(_slots.Select(slot => slot.Available)))
                    ApplyHotkeys();
                UpdateVisuals();
            });
        });
    }

    private void ShowSwitcher()
    {
        if (_switchForm is null || _switchForm.IsDisposed)
            _switchForm = new SwitchForm(this);
        _switchForm.RefreshSlots();
        if (!_switchForm.Visible)
            _switchForm.Show();
        _switchForm.Activate();
    }

    private IWin32Window EnsureSwitcherHiddenOwner()
    {
        if (_switchForm is null || _switchForm.IsDisposed)
            _switchForm = new SwitchForm(this);
        return _switchForm;
    }

    private void PopulateMenu()
    {
        var menu = _notify.ContextMenuStrip!;
        menu.Items.Clear();
        menu.Items.Add(new ToolStripMenuItem("Active: " + NameFor(_current)) { Enabled = false });
        foreach (var slot in _slots)
        {
            var item = new ToolStripMenuItem(slot.Label)
            {
                Checked = _current == slot.Vcp,
                Enabled = slot.Available,
                ShortcutKeyDisplayString = slot.Hotkey.Format(),
            };
            var chosen = slot;
            item.Click += (_, _) => RequestSwitch(chosen);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var open = new ToolStripMenuItem("Open window");
        open.Click += (_, _) => ShowSwitcher();
        menu.Items.Add(open);

        var edit = new ToolStripMenuItem("Set hotkeys");
        edit.Click += (_, _) => BeginHotkeyEdit();
        menu.Items.Add(edit);

        var startup = new ToolStripMenuItem("Run at Windows startup")
        {
            Checked = _config.StartWithWindows,
        };
        startup.Click += (_, _) => ToggleStartup();
        menu.Items.Add(startup);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitApplication();
        menu.Items.Add(exit);
    }

    private void ApplyHotkeys()
    {
        UnregisterHotkeys();
        var seen = new HashSet<HotkeyChord>();
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.Available || !seen.Add(slot.Hotkey))
                continue;
            if (!_hotkeys.Register(i + 1, slot.Hotkey))
                ShowToast($"Hotkey for {slot.Label} is already in use: {slot.Hotkey.Format()}", error: true);
        }
    }

    private void UnregisterHotkeys()
    {
        for (var i = 1; i <= 4; i++)
            _hotkeys.Unregister(i);
    }

    private void UpdateVisuals()
    {
        var name = NameFor(_current);
        var tip = "AOC Input: " + name;
        _notify.Text = tip.Length <= 63 ? tip : tip[..63];
        if (_switchForm is { Visible: true })
            _switchForm.RefreshSlots();
    }

    private string NameFor(uint? code)
    {
        if (code is null)
            return "unknown";
        var slot = _slots.FirstOrDefault(item => item.Vcp == code.Value);
        return slot?.Label ?? MonitorSession.Describe(code.Value);
    }

    private void ShowToast(string message, bool error)
    {
        if (_toast is { IsDisposed: false })
            _toast.Close();
        _toast = new ToastForm(message, error);
        _toast.Show();
        if (error)
            _notify.ShowBalloonTip(5000, "AOC Input", message, ToolTipIcon.Error);
    }

    private void TryOpenMonitor()
    {
        if (MonitorSession.TryOpen(out var session, out _, out _))
            _monitor = session;
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _timer.Stop();
        _notify.Visible = false;
        if (_switchForm is { IsDisposed: false })
            _switchForm.Close();
        ExitThread();
    }

    private static IReadOnlyList<InputSlot> ApplyAvailability(
        IReadOnlyList<InputSlot> slots,
        uint? current,
        IReadOnlyList<uint>? reported,
        bool capsKnown)
    {
        if (!capsKnown || reported is null || reported.Count == 0)
            return slots.Select(slot => slot with { Available = true }).ToList();

        var live = reported.ToHashSet();
        if (current is uint code)
            live.Add(code);
        return slots.Select(slot => slot with { Available = live.Contains(slot.Vcp) }).ToList();
    }

    private static void WriteRunKey(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
        else
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}
