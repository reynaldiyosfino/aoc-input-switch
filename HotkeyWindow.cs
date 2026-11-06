namespace AocInputSwitch;

internal sealed class HotkeyWindow : NativeWindow
{
    public event Action<int>? Pressed;

    public void Open()
    {
        if (Handle != IntPtr.Zero)
            return;

        CreateHandle(new CreateParams
        {
            Parent = (IntPtr)(-3),
            Caption = "AocInputSwitchHotkeys",
        });
    }

    public bool Register(int id, HotkeyChord chord)
    {
        return NativeMethods.RegisterHotKey(Handle, id, chord.Modifiers, chord.VirtualKey);
    }

    public void Unregister(int id)
    {
        if (Handle != IntPtr.Zero)
            NativeMethods.UnregisterHotKey(Handle, id);
    }

    public void CloseWindow()
    {
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WmHotkey)
            Pressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }
}
