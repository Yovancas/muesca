using ClaudeBarWin.Config;
using Velopack;
using Velopack.Logging;

namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// La configuración de <c>VelopackApp.Build()…Run()</c>, lo primero de <c>Main</c> (antes de cualquier modo por línea de
/// comandos; el <c>Run()</c> va en el propio <c>Main</c>, que es donde vpk lo busca). Setup.exe y Update.exe llaman a la app
/// con <c>--veloapp-install/updated/obsolete/uninstall &lt;versión&gt;</c>; Velopack ejecuta el hook que toca y cierra el
/// proceso (tiene de 15 a 60 s y no puede enseñar nada), sin llegar al resto de <c>Main</c>. Al actualizar, el orden es: la
/// versión vieja (obsolete), la nueva (updated) y, si Update.exe la vuelve a abrir, <see cref="UpdatedTo"/>. En un arranque
/// normal, si hay una versión ya descargada y sin aplicar, la aplica y vuelve a abrir la app. El AppUserModelID es el que
/// pone Velopack (<c>velopack.ClaudeBar</c>), el mismo que lleva el acceso directo del menú Inicio: los globos de la bandeja
/// salen con su nombre y su icono.
/// </summary>
public static class VelopackStartup
{
    /// <summary>El mutex de instancia única de <c>Program.Main</c>.</summary>
    public const string SingleInstanceMutex = "ClaudeBarWin_SingleInstance";

    public static VelopackApp Configure(VelopackApp app, string[] args) => app
        .SetArgs(args)
        .SetLogger(new AppLogBridge())
        .SetAutoApplyOnStartup(ShouldAutoApply(args, OtherInstanceRunning()))
        .OnAfterInstallFastCallback(OnInstalled)
        .OnAfterUpdateFastCallback(OnUpdated)
        .OnBeforeUninstallFastCallback(OnUninstalling)
        .OnFirstRun(v => AppLog.Info($"velopack: primer arranque de la v{v}"))
        .OnRestarted(v => UpdatedTo = v.ToString());

    /// <summary>Versión a la que se acaba de actualizar, si esta ejecución la abrió Update.exe tras aplicar (para avisar);
    /// null si no.</summary>
    public static string? UpdatedTo { get; private set; }

    /// <summary>
    /// ¿Aplicar al arrancar una versión ya descargada? Solo en un arranque normal (sin argumentos, el acceso directo, o con
    /// <see cref="AppLocation.AutostartArg"/>, el inicio con Windows) y sin otra instancia abierta. Con otros argumentos es un
    /// modo de diagnóstico o de instalación (<c>--report</c>, <c>--uninstall-hooks</c>…), y Velopack lo relanzaría con los
    /// mismos tras aplicar. Con otra instancia abierta, aplicar la cerraría a la fuerza (Update.exe para todo lo que corre en
    /// la carpeta): esta segunda solo avisa a la primera, que aplica al pulsar en el menú o al cerrarse.
    /// </summary>
    internal static bool ShouldAutoApply(string[] args, bool otherInstanceRunning) =>
        !otherInstanceRunning && !args.Any(a => a.StartsWith("--", StringComparison.Ordinal)
                                                && !string.Equals(a, AppLocation.AutostartArg, StringComparison.OrdinalIgnoreCase));

    private static bool OtherInstanceRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(SingleInstanceMutex, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Primera instalación (Setup.exe, <c>--veloapp-install</c>). Los perfiles ya NO se aplican aquí: lo hace el asistente de
    /// la app en el primer arranque. Solo se deja dicho que está pendiente, y solo si no había configuración (quien llega
    /// desde la versión de Inno, o reinstala, ya la tiene). Si sigue instalada la de Inno (se instaló con Setup.exe sin pasar
    /// por el puente), se retira sin su desinstalador (<see cref="LegacyInnoCleanup"/>): si no, quitarla desde «Aplicaciones»
    /// se llevaría los hooks y el helper que usa esta.
    /// </summary>
    private static void OnInstalled(SemanticVersion version)
    {
        AppLog.Enabled = true;
        var outcome = OnboardingState.MarkPendingOnFirstInstall(AppConfig.ConfigPath);
        AppLog.Info($"velopack: instalada la v{version}; asistente {OnboardingState.Describe(outcome)}");
        LegacyInnoCleanup.RetireIfPresent();
    }

    /// <summary>Actualización aplicada (<c>--veloapp-updated</c>, ya desde la carpeta nueva): el helper de los hooks se pone
    /// al día enseguida, sin esperar a que arranque la app. Lo demás (hooks, statusline) lo hace el arranque, como siempre.</summary>
    private static void OnUpdated(SemanticVersion version)
    {
        AppLog.Enabled = true;
        var helper = HelperDeployer.Ensure();
        AppLog.Info($"velopack: actualizada a la v{version}; helper {(helper is null ? "no disponible" : "al día")}");
    }

    /// <summary>Desinstalación (<c>--veloapp-uninstall</c>, con la app ya parada): lo mismo que el desinstalador de Inno, y la
    /// entrada de «Iniciar con Windows» de la clave Run si apunta a esta instalación (Velopack no la toca). Después Velopack borra su carpeta y sus
    /// accesos directos.</summary>
    private static void OnUninstalling(SemanticVersion version)
    {
        AppLog.Enabled = true;
        AppLog.Info($"velopack: desinstalando la v{version}");
        UninstallCleanup.Run(removeStartupShortcut: true);
    }

    /// <summary>Los avisos y errores de Velopack, también en claudebar.log (los detalles siguen en su velopack.log).</summary>
    private sealed class AppLogBridge : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            if (logLevel < VelopackLogLevel.Warning) return;
            if (logLevel == VelopackLogLevel.Warning)
                AppLog.Warn($"velopack: {message}{(exception is null ? "" : $" ({exception.Message})")}");
            else
                AppLog.Error($"velopack: {message}", exception);
        }
    }
}
