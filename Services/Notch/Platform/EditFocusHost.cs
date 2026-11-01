using System.Runtime.InteropServices;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// <see cref="IEditFocusHost"/> real (F5 · B5): <c>WS_EX_NOACTIVATE</c> con <c>Get/SetWindowLongPtr</c> y el primer plano
/// con <c>SetForegroundWindow</c>. Windows solo deja traer una ventana al frente al proceso que recibió la última entrada:
/// la edición empieza con un clic en el propio notch, así que se cumple. Nunca lanza (un fallo = «no se pudo»).
/// </summary>
public sealed class EditFocusHost : IEditFocusHost
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private readonly Func<IntPtr> _self;

    /// <param name="self">El HWND del notch (se lee cada vez: la ventana puede recrear su handle).</param>
    public EditFocusHost(Func<IntPtr> self) => _self = self ?? throw new ArgumentNullException(nameof(self));

    public IntPtr Self => _self();

    public IntPtr Foreground()
    {
        try { return GetForegroundWindow(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return IntPtr.Zero; }
    }

    public void SetNoActivate(bool on)
    {
        var h = Self;
        if (h == IntPtr.Zero) return;
        try
        {
            long style = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
            long next = on ? style | WS_EX_NOACTIVATE : style & ~WS_EX_NOACTIVATE;
            if (next != style) SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(next));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Warn($"notch: no se pudo cambiar WS_EX_NOACTIVATE: {ex.Message}");
        }
    }

    public bool Activate()
    {
        var h = Self;
        if (h == IntPtr.Zero) return false;
        try { return SetForegroundWindow(h) && GetForegroundWindow() == h; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public bool IsWindow(IntPtr hwnd)
    {
        try { return hwnd != IntPtr.Zero && IsWindowNative(hwnd); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    public void Restore(IntPtr hwnd)
    {
        try { SetForegroundWindow(hwnd); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { /* sin foco que devolver */ }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowNative(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
