using System.Reflection;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Regresión v0.3.10: NetSparkle compara la InformationalVersion del ensamblado con la del appcast.
/// Si lleva "+sha" (metadata de build que el SDK añade por defecto), su SemVerLike la considera
/// pre-release y MENOR que la misma versión publicada → bucle infinito de "actualización disponible".
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void InformationalVersion_has_no_build_metadata_so_updater_sees_it_as_current()
    {
        var asm = typeof(ClaudeBarWin.TrayAppContext).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(info);
        Assert.DoesNotContain("+", info);

        var v = asm.GetName().Version!;
        Assert.Equal($"{v.Major}.{v.Minor}.{v.Build}", info);
    }
}
