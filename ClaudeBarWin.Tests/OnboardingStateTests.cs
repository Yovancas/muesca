using System.Text.Json;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Tests;

/// <summary>
/// v0.5: el asistente de perfiles pasa del instalador a la app. <see cref="AppConfig.OnboardingPending"/> es false en una
/// config que ya existía (aunque no traiga la clave) y true en una config nueva; la primera instalación de Velopack lo marca
/// solo si no había config.
/// </summary>
public sealed class OnboardingStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "claudebar-onboarding-" + Guid.NewGuid().ToString("N"));
    private string ConfigPath => Path.Combine(_dir, "config.json");

    public OnboardingStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void An_existing_config_without_the_key_is_not_pending()
    {
        Assert.False(new AppConfig().OnboardingPending);
        Assert.False(JsonSerializer.Deserialize<AppConfig>("""{"RefreshSeconds":60,"NotchEnabled":true}""")!.OnboardingPending);
        Assert.True(JsonSerializer.Deserialize<AppConfig>("""{"OnboardingPending":true}""")!.OnboardingPending);
    }

    [Fact]
    public void The_flag_roundtrips_through_config_json()
    {
        var back = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(new AppConfig { OnboardingPending = true }))!;
        Assert.True(back.OnboardingPending);
    }

    [Fact]
    public void Loading_without_a_config_creates_one_with_the_wizard_pending()
    {
        var c = AppConfig.Load(ConfigPath);

        Assert.True(c.OnboardingPending);
        Assert.True(File.Exists(ConfigPath));
        Assert.True(AppConfig.Load(ConfigPath).OnboardingPending);   // guardado, no solo en memoria
    }

    [Fact]
    public void Loading_an_existing_config_leaves_it_alone()
    {
        const string json = """{"NotchEnabled":true}""";
        File.WriteAllText(ConfigPath, json);

        var c = AppConfig.Load(ConfigPath);

        Assert.False(c.OnboardingPending);
        Assert.True(c.NotchEnabled);
        Assert.Equal(json, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void An_unreadable_config_counts_as_none()
    {
        // Se aparta a .bad y se sigue con los valores por defecto: los ajustes se perdieron, así que el asistente vuelve.
        File.WriteAllText(ConfigPath, "{ esto no es json");

        var c = AppConfig.Load(ConfigPath);

        Assert.True(c.OnboardingPending);
        Assert.True(File.Exists(ConfigPath + ".bad"));
    }

    [Fact]
    public void First_install_without_config_marks_the_wizard_pending()
    {
        Assert.Equal(OnboardingState.Outcome.Marked, OnboardingState.MarkPendingOnFirstInstall(ConfigPath));

        var c = AppConfig.Load(ConfigPath);
        Assert.True(c.OnboardingPending);
        // Lo demás, los valores por defecto: ningún perfil aplicado.
        Assert.False(c.NotchEnabled);
        Assert.False(c.LiveSessionsEnabled);
    }

    [Fact]
    public void First_install_over_an_existing_config_does_not_touch_it()
    {
        // Quien llega desde la versión de Inno (o reinstala) ya está configurado.
        const string json = """{"NotchEnabled":true,"Language":"es"}""";
        File.WriteAllText(ConfigPath, json);

        Assert.Equal(OnboardingState.Outcome.ExistingConfig, OnboardingState.MarkPendingOnFirstInstall(ConfigPath));

        Assert.Equal(json, File.ReadAllText(ConfigPath));
        Assert.False(AppConfig.Load(ConfigPath).OnboardingPending);
    }

    [Fact]
    public void First_install_that_cannot_write_fails_quietly()
    {
        // La «carpeta» de la config es un archivo: no se puede crear. El hook no puede tumbar la instalación.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "");

        Assert.Equal(OnboardingState.Outcome.Failed, OnboardingState.MarkPendingOnFirstInstall(Path.Combine(blocker, "config.json")));
    }

    [Fact]
    public void Every_outcome_has_a_log_line()
    {
        foreach (var o in Enum.GetValues<OnboardingState.Outcome>())
            Assert.False(string.IsNullOrWhiteSpace(OnboardingState.Describe(o)));
    }
}
