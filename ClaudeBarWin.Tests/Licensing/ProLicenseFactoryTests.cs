using System.Reflection;
using ClaudeBarWin.Services.Licensing;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>
/// Qué licencia se elige al arrancar: por identidad de paquete, y el override <c>CLAUDEBAR_PRO</c> solo en Debug. Los tests
/// de la solución se pasan en Release (<c>dotnet test -c Release</c>): ahí se comprueba que el override no existe.
/// </summary>
public class ProLicenseFactoryTests
{
    private sealed class NoStore : IProStateStore
    {
        public ProSnapshot Load() => default;
        public void SaveTrialStart(DateTime startUtc) { }
        public void SaveLastKnownPro(bool owns) { }
    }

    private static IProLicense Create(bool packaged, string? env) =>
        ProLicenseFactory.Create(packaged, () => throw new InvalidOperationException("sin Store en los tests"), new NoStore(),
            a => a(), null, name => name == ProLicenseFactory.OverrideVariable ? env : null);

    [Fact]
    public async Task Without_package_identity_it_is_the_free_edition_with_the_store_link()
    {
        using var license = Create(packaged: false, env: null);
        var free = Assert.IsType<FreeEditionLicense>(license);
        Assert.Equal(ProState.Free, free.State);
        Assert.False(free.CanPurchaseHere);
        Assert.Null(free.TrialDaysLeft);
        Assert.False(free.TrialEnded);
        Assert.StartsWith("ms-windows-store://pdp/?productid=", free.StoreUrl);
        Assert.Equal(PurchaseResult.NotAvailable, (await free.PurchaseAsync()));
        Assert.Equal(PurchaseResult.NotAvailable, (await free.RestoreAsync()));
    }

    [Fact]
    public void With_package_identity_it_asks_the_store()
    {
        Assert.Throws<InvalidOperationException>(() => Create(packaged: true, env: null));   // creó el backend de la Store
    }

    [Fact]
    public void The_free_edition_link_uses_the_configured_store_id()
    {
        using var license = new FreeEditionLicense("9NTEST123456");
        Assert.Equal("ms-windows-store://pdp/?productid=9NTEST123456", license.StoreUrl);
    }

#if DEBUG
    [Theory]
    [InlineData("pro", ProState.Pro)]
    [InlineData("TRIAL", ProState.Trial)]
    [InlineData(" free ", ProState.Free)]
    public void In_debug_the_variable_forces_the_state(string value, ProState expected)
    {
        using var license = Create(packaged: false, env: value);
        Assert.Equal(expected, license.State);
        Assert.True(license.CanPurchaseHere);
    }

    [Fact]
    public void In_debug_an_unknown_value_is_ignored()
    {
        using var license = Create(packaged: false, env: "gratis");
        Assert.IsType<FreeEditionLicense>(license);
    }
#else
    [Theory]
    [InlineData("pro")]
    [InlineData("trial")]
    [InlineData("free")]
    public void In_release_the_variable_does_nothing(string value)
    {
        using var license = Create(packaged: false, env: value);
        Assert.IsType<FreeEditionLicense>(license);
        Assert.Equal(ProState.Free, license.State);
    }

    [Fact]
    public void In_release_the_override_is_not_even_compiled()
    {
        var asm = typeof(ProLicenseFactory).Assembly;
        Assert.Null(typeof(ProLicenseFactory).GetMethod("DebugOverride", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));
        Assert.DoesNotContain(asm.GetTypes(), t => t.Name == "DebugProLicense");
        // Y no es un ensamblado de Debug (sin esto el test de arriba no probaría nada).
        var debuggable = asm.GetCustomAttribute<System.Diagnostics.DebuggableAttribute>();
        Assert.False(debuggable?.IsJITOptimizerDisabled ?? false);
    }
#endif
}
