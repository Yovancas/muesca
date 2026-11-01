using ClaudeBarWin.Services.Packaging;

namespace ClaudeBarWin.UI;

/// <summary>
/// Edición de la Store: ventana de nivel superior oculta, en el hilo de UI, que recibe los avisos de cierre de Windows
/// (docs/msix.md). Windows los manda a cada ventana de nivel superior: <c>WM_QUERYENDSESSION</c> y luego
/// <c>WM_ENDSESSION</c>; al cerrar sesión, al apagar y cuando la Store actualiza la app con ella abierta (Restart Manager, con
/// <c>ENDSESSION_CLOSEAPP</c>). Sustituye a <c>SystemEvents.SessionEnded</c>, que no dice si el cierre es para actualizar.
/// <list type="bullet">
/// <item><c>WM_QUERYENDSESSION</c>: registra el reinicio solo si es el cierre por actualización y lo quita si no
/// (<see cref="StoreRestart"/>). Nunca se opone al cierre.</item>
/// <item><c>WM_ENDSESSION</c> con <c>wParam</c> = TRUE: llama a <c>onEnding</c> <b>dentro del propio mensaje</b>. Cuando el
/// procedimiento de ventana devuelve, Windows puede terminar el proceso en cualquier momento: un cierre encolado para después
/// (<c>PostToUi</c>) no llegaría a ejecutarse y se perderían el guardado y quitar el icono de la bandeja.</item>
/// </list>
/// </summary>
internal sealed class SessionEndWindow : NativeWindow, IDisposable
{
    internal const int WmQueryEndSession = 0x0011, WmEndSession = 0x0016;
    private const int WsExToolWindow = 0x00000080, WsExNoActivate = 0x08000000;

    private readonly Action<string> _onEnding;
    private bool _dispatching, _disposeWhenDone, _ended;

    /// <param name="onEnding">El cierre ordenado de la app, síncrono (recibe el motivo, para el log). Se llama una vez.</param>
    public SessionEndWindow(Action<string> onEnding)
    {
        _onEnding = onEnding;
        // Sin WS_VISIBLE ni padre: de nivel superior (a las ventanas solo de mensajes no les llegan estos avisos) y oculta.
        CreateHandle(new CreateParams { Caption = "Muesca session end", ExStyle = WsExToolWindow | WsExNoActivate });
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmQueryEndSession:
                StoreRestart.Apply(StoreRestart.OnQueryEndSession(StoreRestart.Flags(m.LParam)));
                m.Result = (IntPtr)1;   // TRUE: la app no se opone
                return;
            case WmEndSession:
                bool ending = m.WParam != IntPtr.Zero;
                long flags = StoreRestart.Flags(m.LParam);
                StoreRestart.Apply(StoreRestart.OnEndSession(ending, flags));
                if (ending && !_ended)
                {
                    _ended = true;
                    _dispatching = true;
                    try { _onEnding(Describe(flags)); }
                    catch (Exception ex) { Services.AppLog.Error("fin de sesión: el cierre ordenado falló", ex); }
                    finally { _dispatching = false; }
                    if (_disposeWhenDone) Dispose();
                }
                m.Result = IntPtr.Zero;
                return;
        }
        base.WndProc(ref m);
    }

    /// <summary>Para el log: por qué se cierra.</summary>
    internal static string Describe(long flags) =>
        StoreRestart.IsCloseForUpdate(flags) ? "actualización" : (flags & StoreRestart.EndSessionLogoff) != 0 ? "cierre de sesión" : "apagado";

    /// <summary>Destruye la ventana; si se llama desde el propio aviso (el cierre ordenado la dispone), al acabar el aviso.</summary>
    public void Dispose()
    {
        if (_dispatching) { _disposeWhenDone = true; return; }
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
