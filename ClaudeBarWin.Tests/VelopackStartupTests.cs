using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>El arranque de Velopack (lo primero de Main): cuándo se aplica sola una versión ya descargada.</summary>
public class VelopackStartupTests
{
    [Theory]
    [InlineData(new string[0], false, true)]          // arranque normal: aplica lo descargado
    [InlineData(new string[0], true, false)]          // otra instancia abierta: no la cierra a la fuerza
    [InlineData(new[] { "--autostart" }, false, true)] // inicio con Windows: también es un arranque normal
    [InlineData(new[] { "--AUTOSTART" }, false, true)]
    [InlineData(new[] { "--autostart" }, true, false)]
    [InlineData(new[] { "--autostart", "--report" }, false, false)]
    [InlineData(new[] { "--report" }, false, false)]  // modos por línea de comandos: nunca
    [InlineData(new[] { "--uninstall-hooks" }, false, false)]
    [InlineData(new[] { "--apply-setup", "x.json" }, false, false)]
    public void A_downloaded_version_applies_itself_only_on_a_normal_start(string[] args, bool otherInstance, bool expected)
    {
        Assert.Equal(expected, VelopackStartup.ShouldAutoApply(args, otherInstance));
    }
}
