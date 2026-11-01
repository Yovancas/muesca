namespace ClaudeBarWin.Services;

public enum TapStatus { Unavailable, Off, Waiting, Active }

/// <summary>v0.4 — estado de la lectura de cuota por statusline, para la fila de ajustes.</summary>
public static class QuotaIntegration
{
    public static TapStatus Status(bool helperAvailable, bool installed, DateTime? lastSeenUtc, DateTime nowUtc) =>
        !helperAvailable ? TapStatus.Unavailable
        : !installed ? TapStatus.Off
        : QuotaPolicy.IsFresh(lastSeenUtc, nowUtc) ? TapStatus.Active
        : TapStatus.Waiting;

    /// <summary>Último momento en que alguna sesión escribió snapshot (lo actualiza TrayAppContext en cada refresco).</summary>
    public static DateTime? LastSeenUtc { get; set; }

    public static TapStatus Current(DateTime nowUtc) =>
        Status(HelperDeployer.Current is not null, StatuslineInstaller.IsInstalled(), LastSeenUtc, nowUtc);
}
