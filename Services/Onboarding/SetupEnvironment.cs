using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch.Platform;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Services.Onboarding;

/// <summary>
/// Lo que el asistente de bienvenida necesita del equipo, igual que <c>--apply-setup</c> en <c>Program.cs</c>: los monitores
/// con su DPI y si hay Claude Code (<see cref="SetupMachine"/>), y los efectos de <see cref="SetupApply"/> (hooks con el
/// <see cref="HookInstaller"/> de la bandeja e inicio con Windows con <see cref="StartupManager"/>). Capa Win32 fina.
/// (<c>Program.RunApplySetup</c> hace hoy lo mismo por su cuenta; al fusionar puede llamar a esto.)
/// </summary>
public static class SetupEnvironment
{
    /// <summary>Monitores (con su DPI efectivo) y si Claude Code está instalado (existe su carpeta de configuración).</summary>
    public static SetupMachine DetectMachine()
    {
        var monitors = new List<SetupMonitor>();
        try
        {
            foreach (var m in MonitorCatalog.Current())
            {
                int dpi = 96;
                try
                {
                    var center = new NotchNative.POINT { X = m.Bounds.Left + m.Bounds.Width / 2, Y = m.Bounds.Top + m.Bounds.Height / 2 };
                    var hmon = NotchNative.MonitorFromPoint(center, NotchNative.MONITOR_DEFAULTTONEAREST);
                    if (hmon != IntPtr.Zero && NotchNative.GetDpiForMonitor(hmon, NotchNative.MDT_EFFECTIVE_DPI,
                            out uint dx, out _) == 0 && dx > 0)
                        dpi = (int)dx;
                }
                catch { /* sin shcore: 96 */ }
                monitors.Add(new SetupMonitor(m.DeviceName, m.Bounds, m.IsPrimary, dpi));
            }
        }
        catch (Exception ex) { AppLog.Warn($"asistente: no se pudieron leer los monitores: {ex.Message}"); }
        return new SetupMachine(monitors, Directory.Exists(ClaudePaths.ConfigDir));
    }

    /// <summary>Hooks (con backup de settings.json y el helper desplegado) e inicio con Windows, como el menú de la bandeja.</summary>
    public static SetupEffects Effects() => new(
        HooksInstalled: HookInstaller.IsInstalled,
        InstallHooks: permissions => HookInstaller.Install(HookScript.Contents(),
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), HelperDeployer.Ensure(), permissions),
        SetStartWithWindows: on => { if (on) StartupManager.Enable(); else StartupManager.Disable(); });

    /// <summary>
    /// Aplica lo elegido en el asistente sobre <c>config.json</c> con el mismo núcleo que <c>--apply-setup</c>
    /// (<see cref="SetupApply"/>), sin JSON en disco, y lo anota en el log igual. Con los hooks ya puestos de antes, la entrada
    /// de los permisos desde el notch se pone al día aquí (al arrancar ya pasó: la app está en marcha).
    /// </summary>
    public static SetupOutcome Apply(SetupAnswers answers, SetupMachine machine)
    {
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(machine);
        var request = answers.ToRequest(machine);
        var outcome = SetupApply.Apply(request, AppConfig.ConfigPath, machine, Effects(), DateTime.UtcNow);

        var detail = outcome.Changed.Count == 0 ? "sin cambios" : "cambia " + string.Join(", ", outcome.Changed);
        if (outcome.Status == SetupStatus.Applied)
            AppLog.Info($"asistente del notch: {outcome.Message}; {detail}" +
                        (outcome.ConfigBackup is { } b ? $"; copia en {b}" : "") +
                        (outcome.HooksInstalled ? "; sesiones en vivo instaladas" : ""));
        else
            AppLog.Warn($"asistente del notch: no se aplicó nada ({outcome.Status}): {outcome.Message}");
        foreach (var ignored in outcome.Ignored) AppLog.Warn($"asistente del notch: ignorado {ignored}");
        if (outcome.HooksError is { } err) AppLog.Warn($"asistente del notch: no se pudieron instalar las sesiones en vivo: {err}");

        if (outcome.Status == SetupStatus.Applied && !outcome.HooksInstalled && outcome.Changed.Contains(nameof(AppConfig.NotchPermissions)))
        {
            try
            {
                bool on = AppConfig.Load().NotchPermissions;
                HookInstaller.ApplyPermissions(DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), HelperDeployer.Current, on);
            }
            catch (Exception ex) { AppLog.Warn($"asistente del notch: no se pudo poner al día la entrada de permisos: {ex.Message}"); }
        }
        return outcome;
    }

    /// <summary>
    /// «Empezar» o «Saltar» del asistente: aplica con <paramref name="apply"/> y, solo si fue bien, apaga
    /// <see cref="AppConfig.OnboardingPending"/> con <paramref name="mutateConfig"/> (el camino de la bandeja, que guarda y pone
    /// la app al día). Si falla (o lanza), sigue pendiente: el asistente vuelve a salir en el próximo arranque. Con él se
    /// guardan el interruptor del contador de uso y que su aviso ya se vio (<see cref="SetupAnswers.UsageStats"/>).
    /// </summary>
    public static SetupOutcome ApplyOnboarding(SetupAnswers answers, Func<SetupAnswers, SetupOutcome> apply,
        Action<Action<AppConfig>> mutateConfig)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(mutateConfig);
        var outcome = apply(answers);
        if (outcome.Status == SetupStatus.Applied)
            mutateConfig(c =>
            {
                c.OnboardingPending = false;
                // La bienvenida enseña la línea del contador de uso con su interruptor: ya se ha visto, y vale lo elegido.
                if (answers.UsageStats is bool stats)
                {
                    c.UsageStatsEnabled = stats;
                    c.UsageStats = Telemetry.UsagePing.NoticeSeenInApp(c.UsageStats);
                }
            });
        return outcome;
    }
}
