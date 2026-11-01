using static ClaudeBarWin.UI.Notch.NotchNative;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Segunda ventana layered del notch, detrás de la pastilla, donde van la sombra y el halo (lenguaje de
/// movimiento §6.3). <c>WS_EX_TRANSPARENT</c>: Windows la salta al buscar a quién dar el ratón, así sus
/// píxeles semitransparentes no se comen los clics de otras apps (<c>HTTRANSPARENT</c> en la de la pastilla
/// solo reenvía dentro del mismo hilo). Sin foco nunca y sin botón en la barra de tareas.
///
/// <para>Misma superficie y posición que la pastilla: la coloca cada <c>UpdateLayeredWindow</c>. El orden Z
/// (justo debajo de la pastilla) y la visibilidad los lleva <see cref="NotchForm"/>.</para>
/// </summary>
internal sealed class NotchShadowWindow : NativeWindow, IDisposable
{
    /// <summary>Crea la ventana (oculta) si aún no existe. Llamar desde el hilo de UI.</summary>
    public void Create(Rectangle bounds)
    {
        if (Handle != IntPtr.Zero) return;
        CreateHandle(new CreateParams
        {
            Caption = "Muesca Notch Shadow",
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
            X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height,
        });
    }

    /// <summary>Muestra (sin activar) u oculta la ventana. Sin handle no hace nada.</summary>
    public void SetVisible(bool visible)
    {
        if (Handle != IntPtr.Zero) ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;
            case WM_NCHITTEST:   // por si acaso: WS_EX_TRANSPARENT ya la excluye del hit-test
                m.Result = HTTRANSPARENT;
                return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
