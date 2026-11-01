using ClaudeBarWin.Services;

namespace ClaudeBarWin.Config;

/// <summary>
/// v0.5: con Velopack el instalador no pregunta nada (2-3 s y una pantalla de carga); el asistente de perfiles se abre en
/// la app la primera vez. La instalación solo deja dicho en config.json que está pendiente (<see cref="AppConfig.OnboardingPending"/>).
/// </summary>
public static class OnboardingState
{
    public enum Outcome
    {
        /// <summary>No había config: se ha creado una, con los valores por defecto y el asistente pendiente.</summary>
        Marked,
        /// <summary>Ya había config (quien llega desde la versión de Inno, o reinstala): no se toca.</summary>
        ExistingConfig,
        /// <summary>No se pudo escribir; la app la creará al arrancar (también con el asistente pendiente).</summary>
        Failed,
    }

    /// <summary>Lo llama el hook de primera instalación de Velopack. Nunca lanza.</summary>
    public static Outcome MarkPendingOnFirstInstall(string configPath)
    {
        try
        {
            if (File.Exists(configPath)) return Outcome.ExistingConfig;
            new AppConfig { OnboardingPending = true }.Normalize().SaveOrThrow(configPath);
            return Outcome.Marked;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudo dejar el asistente pendiente en config.json: {ex.Message}");
            return Outcome.Failed;
        }
    }

    /// <summary>Para el log.</summary>
    public static string Describe(Outcome outcome) => outcome switch
    {
        Outcome.Marked => "pendiente (config nueva)",
        Outcome.ExistingConfig => "no hace falta (ya había config)",
        _ => "sin marcar (no se pudo escribir config.json)",
    };
}
