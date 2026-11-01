using System.Drawing;
using System.Drawing.Drawing2D;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// F3 Task 3: caché de pinceles, plumas y fuentes por formulario. Los tests de <see cref="GdiCache"/>
/// crean objetos GDI+ reales (solo Windows); el núcleo <see cref="DisposableCache{TKey}"/> es puro y
/// se prueba con desechables falsos.
/// </summary>
public class GdiCacheTests
{
    [Fact]
    public void Same_color_returns_same_brush()
    {
        using var c = new GdiCache();
        var a = c.Brush(Color.FromArgb(255, 10, 20, 30));
        var b = c.Brush(Color.FromArgb(255, 10, 20, 30));
        Assert.Same(a, b);
        Assert.NotSame(a, c.Brush(Color.FromArgb(128, 10, 20, 30))); // el alfa forma parte de la clave
        Assert.Same(c.Pen(Color.Red, 2f), c.Pen(Color.Red, 2f));
        Assert.NotSame(c.Pen(Color.Red, 2f), c.Pen(Color.Red, 1f));
        Assert.NotSame(c.Pen(Color.Red, 2f), c.Pen(Color.Red, 2f, LineCap.Round));
    }

    [Fact]
    public void Invalidate_disposes_and_recreates()
    {
        using var c = new GdiCache();
        var old = c.Brush(Color.Teal);
        c.Invalidate();
        // Un pincel liberado ya no se puede usar.
        Assert.ThrowsAny<Exception>(() => old.Clone());
        var fresh = c.Brush(Color.Teal);
        Assert.NotSame(old, fresh);
        Assert.Equal(Color.Teal.ToArgb(), fresh.Color.ToArgb());
    }

    [Fact]
    public void Font_keyed_by_family_size_style()
    {
        using var c = new GdiCache();
        var f = c.Font("Segoe UI", 8f, FontStyle.Bold);
        Assert.Same(f, c.Font("Segoe UI", 8f, FontStyle.Bold));
        Assert.NotSame(f, c.Font("Segoe UI", 9f, FontStyle.Bold));
        Assert.NotSame(f, c.Font("Segoe UI", 8f, FontStyle.Regular));
        Assert.Equal(8f, f.Size);
        Assert.True(f.Bold);
    }

    [Fact]
    public void Lease_without_ambient_cache_owns_and_disposes()
    {
        Assert.Null(GdiCache.Current);
        SolidBrush inner;
        using (var lease = GdiCache.BrushFor(Color.Navy))
        {
            inner = lease;
            Assert.Equal(Color.Navy.ToArgb(), inner.Color.ToArgb());
        }
        Assert.ThrowsAny<Exception>(() => inner.Clone());

        using var c = new GdiCache();
        using (GdiCache.Use(c))
        {
            using (var lease = GdiCache.BrushFor(Color.Navy)) inner = lease;
            Assert.Same(inner, c.Brush(Color.Navy)); // con caché ambiente el lease no libera
        }
        Assert.Null(GdiCache.Current);
    }

    // ---- núcleo puro (sin GDI+) ----

    private sealed class Fake : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Core_same_key_reuses_and_clear_disposes_all()
    {
        var cache = new DisposableCache<string>(maxEntries: 8);
        int created = 0;
        var a = cache.Get("a", () => { created++; return new Fake(); });
        var a2 = cache.Get("a", () => { created++; return new Fake(); });
        var b = cache.Get("b", () => { created++; return new Fake(); });
        Assert.Same(a, a2);
        Assert.Equal(2, created);
        Assert.Equal(2, cache.Count);

        cache.Clear();
        Assert.True(a.Disposed);
        Assert.True(b.Disposed);
        Assert.Equal(0, cache.Count);
        Assert.NotSame(a, cache.Get("a", () => new Fake()));
    }

    [Fact]
    public void Core_overflow_never_disposes_mid_frame_only_on_trim()
    {
        var cache = new DisposableCache<int>(maxEntries: 2);
        var first = cache.Get(1, () => new Fake());
        cache.Get(2, () => new Fake());
        cache.Get(3, () => new Fake());            // se pasa del tope: sigue vivo durante el frame
        Assert.False(first.Disposed);
        Assert.Equal(3, cache.Count);

        cache.TrimIfOverflowed();                 // fin de frame: ahora sí se vacía
        Assert.True(first.Disposed);
        Assert.Equal(0, cache.Count);

        cache.Get(1, () => new Fake());
        cache.TrimIfOverflowed();                 // dentro del tope: no toca nada
        Assert.Equal(1, cache.Count);
    }
}
