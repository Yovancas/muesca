using System.Runtime.InteropServices;

namespace ClaudeBarWin.Services.Packaging;

/// <summary>
/// Que Windows vuelva a abrir la edición de la Store después de que la Store la actualice, y solo entonces (docs/msix.md,
/// «Reapertura al iniciar sesión»). La documentación de las actualizaciones de las apps empaquetadas: «If your application is
/// open when users install an update to it, the application closes. If you want that application to restart after the
/// update completes, call the RegisterApplicationRestart function in every process that you want to restart. Each active
/// window in your application receives a WM_QUERYENDSESSION message. At this point, your application can call the
/// RegisterApplicationRestart function again».
/// <para>Pero una app registrada también es «reiniciable»: con <i>Guardar automáticamente mis apps reiniciables y
/// reiniciarlas al iniciar sesión</i>, Windows la reabriría al iniciar sesión aunque «Iniciar con Windows» esté apagado. Por eso
/// <b>no se registra al arrancar</b>: solo al recibir el aviso de cierre para reemplazar archivos (<c>WM_QUERYENDSESSION</c>
/// con <c>ENDSESSION_CLOSEAPP</c>, que es como Windows cierra una app para actualizarla) y se desregistra en cualquier otro
/// caso (apagar, reiniciar, cerrar sesión, o si alguien cancela el cierre). Lo recibe <see cref="UI.SessionEndWindow"/>.</para>
/// </summary>
public static class StoreRestart
{
    internal const long EndSessionCloseApp = 0x00000001, EndSessionCritical = 0x40000000, EndSessionLogoff = 0x80000000;

    private const int RestartNoCrash = 1, RestartNoHang = 2, RestartNoReboot = 8;

    /// <summary>Ni tras un cuelgue, ni tras dejar de responder, ni tras reiniciar el equipo: solo tras la actualización.</summary>
    internal const int RestartFlags = RestartNoCrash | RestartNoHang | RestartNoReboot;

    public enum Step
    {
        /// <summary>Windows cierra la app para actualizarla: que la vuelva a abrir después.</summary>
        Register,
        /// <summary>Cualquier otro cierre (o ninguno): que no quede como «reiniciable».</summary>
        Unregister,
        /// <summary>El cierre por actualización sigue adelante: no tocar lo que se registró en <c>WM_QUERYENDSESSION</c>.</summary>
        Keep,
    }

    /// <summary>Los indicadores del <c>lParam</c> de <c>WM_QUERYENDSESSION</c> / <c>WM_ENDSESSION</c> (32 bits, también desde
    /// un <c>IntPtr</c> de 32 bits con signo). PURO.</summary>
    internal static long Flags(IntPtr lParam) => lParam.ToInt64() & 0xFFFFFFFFL;

    /// <summary>¿Cierra Windows la app para reemplazar sus archivos (actualización), y no la sesión? PURO.</summary>
    internal static bool IsCloseForUpdate(long flags) => (flags & EndSessionCloseApp) != 0 && (flags & EndSessionLogoff) == 0;

    /// <summary><c>WM_QUERYENDSESSION</c>: registrar solo si es el cierre por actualización. PURO.</summary>
    internal static Step OnQueryEndSession(long flags) => IsCloseForUpdate(flags) ? Step.Register : Step.Unregister;

    /// <summary><c>WM_ENDSESSION</c> (<paramref name="ending"/> = su <c>wParam</c>): si el cierre por actualización sigue,
    /// se deja como está; si se canceló o es otro cierre, se desregistra. PURO.</summary>
    internal static Step OnEndSession(bool ending, long flags) =>
        ending && IsCloseForUpdate(flags) ? Step.Keep : Step.Unregister;

    /// <summary>Lo que dice el paso, contra Windows. Nunca lanza.</summary>
    public static void Apply(Step step)
    {
        try
        {
            switch (step)
            {
                case Step.Register:
                    int hr = RegisterApplicationRestart(null, RestartFlags);
                    if (hr != 0) AppLog.Warn($"edición de la Store: RegisterApplicationRestart devolvió 0x{hr:X8}");
                    else AppLog.Info("edición de la Store: Windows la cierra para actualizarla; se volverá a abrir después");
                    break;
                case Step.Unregister:
                    _ = UnregisterApplicationRestart();   // sin registro previo devuelve un error que no importa
                    break;
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            AppLog.Warn($"edición de la Store: sin RegisterApplicationRestart: {ex.Message}");
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int RegisterApplicationRestart(string? commandLine, int flags);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern int UnregisterApplicationRestart();
}
