using System.Drawing;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="HoverHitTest"/> es el hit-test PURO del hover (Tarea 3 F3): dado un punto y una lista
/// ordenada de pares (clave → rect), devuelve la clave del primer rect que contiene el punto, o
/// <c>null</c> si ninguno. Sin GDI+/reloj/red: solo geometría → 100% testeable. La precedencia es
/// estable por orden de iteración (el llamador pasa los rects de más específico a menos para resolver
/// solapes de forma determinista).
/// </summary>
public class HoverHitTestTests
{
    private static KeyValuePair<string, Rectangle> Kv(string k, Rectangle r) => new(k, r);

    [Fact]
    public void Resolve_returns_key_when_point_inside()
    {
        var rects = new[]
        {
            Kv("a", new Rectangle(0, 0, 10, 10)),
            Kv("b", new Rectangle(20, 20, 10, 10)),
        };
        Assert.Equal("b", HoverHitTest.Resolve(new Point(25, 25), rects));
    }

    [Fact]
    public void Resolve_returns_null_when_point_outside_all()
    {
        var rects = new[]
        {
            Kv("a", new Rectangle(0, 0, 10, 10)),
            Kv("b", new Rectangle(20, 20, 10, 10)),
        };
        Assert.Null(HoverHitTest.Resolve(new Point(100, 100), rects));
    }

    [Fact]
    public void Resolve_returns_null_for_empty()
    {
        Assert.Null(HoverHitTest.Resolve(new Point(5, 5), Array.Empty<KeyValuePair<string, Rectangle>>()));
    }

    [Fact]
    public void Resolve_first_match_wins_on_overlap()
    {
        // Dos rects que solapan en el punto (5,5): la precedencia es el ORDEN de iteración.
        var rects = new[]
        {
            Kv("top", new Rectangle(0, 0, 10, 10)),
            Kv("bottom", new Rectangle(0, 0, 10, 10)),
        };
        Assert.Equal("top", HoverHitTest.Resolve(new Point(5, 5), rects));
    }

    [Fact]
    public void Resolve_precedence_is_stable_regardless_of_other_non_matching_rects()
    {
        // Rects que NO contienen el punto no alteran la precedencia del que sí.
        var rects = new[]
        {
            Kv("miss1", new Rectangle(100, 100, 5, 5)),
            Kv("hit", new Rectangle(0, 0, 10, 10)),
            Kv("miss2", new Rectangle(200, 200, 5, 5)),
        };
        Assert.Equal("hit", HoverHitTest.Resolve(new Point(3, 3), rects));
    }

    [Fact]
    public void Resolve_skips_empty_rects()
    {
        // Un rect vacío (Width/Height 0) nunca contiene al punto → se ignora.
        var rects = new[]
        {
            Kv("empty", Rectangle.Empty),
            Kv("real", new Rectangle(0, 0, 10, 10)),
        };
        Assert.Equal("real", HoverHitTest.Resolve(new Point(2, 2), rects));
    }

    [Fact]
    public void Resolve_accepts_a_dictionary_directly()
    {
        // El llamado real le pasa los Dictionary<string,Rectangle> existentes del form.
        var rects = new Dictionary<string, Rectangle>
        {
            ["quota"] = new Rectangle(0, 0, 10, 10),
            ["chart"] = new Rectangle(0, 20, 10, 10),
        };
        Assert.Equal("chart", HoverHitTest.Resolve(new Point(5, 25), rects));
        Assert.Null(HoverHitTest.Resolve(new Point(5, 50), rects));
    }
}
