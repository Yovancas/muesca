using System.Drawing;
using ClaudeBarWin.Services;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F3 Task 7: el badge se pintaba a 48 px y la shell lo reescalaba (borroso). Ahora se pinta al tamaño
/// exacto del icono pequeño del DPI y se cachea por su especificación: si nada cambió no se crea otro
/// HICON. <c>Renders_exact_size</c> usa GDI+ real (solo Windows); la caché es pura.
/// </summary>
public class TrayIconSizeTests
{
    private const double Warn = 70, Crit = 90;

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void Renders_exact_size(int sizePx)
    {
        var spec = TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: sizePx);
        using var ico = TrayIconRenderer.Render(spec);
        Assert.Equal(sizePx, ico.Width);
        Assert.Equal(sizePx, ico.Height);
    }

    private sealed class Fake : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Same_key_reuses_icon()
    {
        var cache = new TrayIconCache<Fake>();
        var k = TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 20);
        int renders = 0;
        var a = cache.Get(k, () => { renders++; return new Fake(); }, out var retired1);
        var b = cache.Get(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 20),
            () => { renders++; return new Fake(); }, out var retired2);
        Assert.Same(a, b);
        Assert.Equal(1, renders);          // no se recrea el icono (ni su HICON)
        Assert.Null(retired1);
        Assert.Null(retired2);
        Assert.False(a.Disposed);
    }

    [Fact]
    public void New_key_renders_and_retires_the_previous_icon()
    {
        var cache = new TrayIconCache<Fake>();
        var a = cache.Get(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 20),
            () => new Fake(), out _);

        // Cambia el tamaño (otro DPI): icono nuevo; el viejo se entrega para liberarlo DESPUÉS de
        // colocar el nuevo en la bandeja (la caché no lo destruye mientras aún se muestra).
        var b = cache.Get(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 24),
            () => new Fake(), out var retired);
        Assert.NotSame(a, b);
        Assert.Same(a, retired);
        Assert.False(a.Disposed);

        // Cambia el contenido (otro %, otro estado, stale, pendiente): también.
        Assert.NotEqual(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 24),
                        TrayIconRenderer.Spec(43, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 24));
        Assert.NotEqual(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 24),
                        TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, pending: true, sizePx: 24));
        Assert.NotEqual(TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, sizePx: 24),
                        TrayIconRenderer.Spec(42, Theme.Dark, Warn, Crit, UsageStatus.Ok, stale: true, sizePx: 24));

        cache.Dispose();
        Assert.True(b.Disposed);
    }

    [Fact]
    public void Small_icon_size_is_sane()
    {
        // Fuera de Windows (o si la API falla) cae a un tamaño razonable, nunca 0.
        int px = TrayIconRenderer.SmallIconSizePx();
        Assert.InRange(px, 16, 64);
    }
}
