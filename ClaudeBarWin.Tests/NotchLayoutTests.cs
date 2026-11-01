using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests;

/// <summary><see cref="NotchLayout"/>: tamaños por modo (spec §4.5), radio de esquina y escala automática.</summary>
public class NotchLayoutTests
{
    [Theory]
    [InlineData(1920, 1.0f)]
    [InlineData(2560, 1.15f)]
    [InlineData(3840, 1.3f)]
    public void AutoScale(int w, float e) => Assert.Equal(e, NotchLayout.AutoScale(w));

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Radius_is_14s_at_width_30s_and_32s_from_140s(float s)
    {
        Assert.Equal(14 * s, NotchLayout.CornerRadius(30 * s, s), 3);
        Assert.Equal(32 * s, NotchLayout.CornerRadius(140 * s, s), 3);
        Assert.Equal(32 * s, NotchLayout.CornerRadius(600 * s, s), 3);
        Assert.Equal(14 * s, NotchLayout.CornerRadius(10 * s, s), 3);
        Assert.Equal(23 * s, NotchLayout.CornerRadius(85 * s, s), 3);   // t = 0,5
    }

    [Fact]
    public void Working_extra_is_capped_at_200_and_shrinks_down_to_the_minimum()
    {
        Assert.Equal(500f, NotchLayout.TargetSize(NotchMode.Working, 1f, 500).Width);
        Assert.Equal(380f, NotchLayout.TargetSize(NotchMode.Working, 1f, 80).Width);
        // Beta del 29-oct: con «memory · ListAgents» quedaba un hueco negro entre el texto y el %: encoge con el texto.
        Assert.Equal(260f, NotchLayout.TargetSize(NotchMode.Working, 1f, -40).Width);
        Assert.Equal(NotchLayout.WorkingMinWidth, NotchLayout.TargetSize(NotchMode.Working, 1f, -500).Width);
        Assert.Equal(34f, NotchLayout.TargetSize(NotchMode.Working, 1f, 0).Height);
    }

    [Theory]
    [InlineData(NotchMode.Minimal, 44, 4)]
    [InlineData(NotchMode.Idle, 112, 30)]
    [InlineData(NotchMode.Peek, 184, 34)]
    [InlineData(NotchMode.Expanded, 480, 124)]
    [InlineData(NotchMode.Permission, 360, 64)]
    [InlineData(NotchMode.Completed, 310, 60)]
    [InlineData(NotchMode.QuotaHud, 320, 36)]
    [InlineData(NotchMode.QuotaAlert, 310, 60)]
    [InlineData(NotchMode.Hidden, 0, 0)]
    public void Sizes_follow_spec_and_scale(NotchMode m, float w, float h)
    {
        var sz = NotchLayout.TargetSize(m, 1.5f, 0);
        Assert.Equal(w * 1.5f, sz.Width, 3);
        Assert.Equal(h * 1.5f, sz.Height, 3);
    }

    [Fact]
    public void Presets_by_transition()
    {
        Assert.Equal(Motion.SpringGentle, NotchLayout.PresetFor(NotchMode.Hidden, NotchMode.Idle));
        Assert.Equal(Motion.SpringGentle, NotchLayout.PresetFor(NotchMode.Working, NotchMode.Hidden));
        Assert.Equal(Motion.SpringSoft, NotchLayout.PresetFor(NotchMode.Peek, NotchMode.Expanded));
        Assert.Equal(Motion.SpringSoft, NotchLayout.PresetFor(NotchMode.Expanded, NotchMode.Idle));
        Assert.Equal(Motion.SpringCritical, NotchLayout.PresetFor(NotchMode.Idle, NotchMode.Working));
    }

    /// <summary>Pedido del 28-oct: el expandido se ajusta a las sesiones abiertas (sin hueco vacío abajo).</summary>
    [Theory]
    [InlineData(0, false, 124f)]     // manda la columna de cuota: 12 + 98 + 14
    [InlineData(1, false, 124f)]
    [InlineData(4, false, 124f)]     // 4 × 22 = 88 < 98
    [InlineData(5, false, 136f)]     // 12 + 5 × 22 + 14
    [InlineData(6, false, 158f)]
    [InlineData(12, false, 158f)]    // tope de 6 filas (la última dice «+N más»)
    [InlineData(1, true, 150f)]      // aviso integrado: +22 + 4
    public void Expanded_height_follows_the_open_sessions(int sessions, bool alert, float h)
    {
        var size = NotchLayout.ExpandedSize(sessions, alert, 1f);
        Assert.Equal(480f, size.Width, 3);
        Assert.Equal(h, size.Height, 3);
        Assert.Equal(h * 1.5f, NotchLayout.ExpandedSize(sessions, alert, 1.5f).Height, 3);
    }

    // ---------------------------------------------------------------- páginas del expandido (F5 · B2)

    /// <summary>Sin puntos (una sola página), la página Claude mide exactamente lo mismo que el expandido de siempre.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(5, false)]
    [InlineData(12, true)]
    public void Claude_page_without_dots_is_the_current_expanded(int sessions, bool alert)
    {
        var page = NotchLayout.PageSize(NotchPage.Claude, new PageRows(sessions, 0, 0), alert, withDots: false, 1f);
        Assert.Equal(NotchLayout.ExpandedSize(sessions, alert, 1f), page);
    }

    [Theory]
    [InlineData(1, false, 140f)]     // 124 + fila de puntos (16)
    [InlineData(5, false, 152f)]     // 136 + 16
    [InlineData(6, true, 184f)]      // 158 + 26 + 16 = 200 → tope 184
    public void Claude_page_with_dots_adds_the_dots_row_up_to_the_cap(int sessions, bool alert, float h)
    {
        var page = NotchLayout.PageSize(NotchPage.Claude, new PageRows(sessions, 0, 0), alert, withDots: true, 1f);
        Assert.Equal(NotchLayout.ExpandedWidth, page.Width, 3);
        Assert.Equal(h, page.Height, 3);
    }

    [Fact]
    public void Music_page_is_140_high()
    {
        Assert.Equal(140f, NotchLayout.PageSize(NotchPage.Music, default, false, true, 1f).Height, 3);
        Assert.Equal(140f * 1.5f, NotchLayout.PageSize(NotchPage.Music, default, false, true, 1.5f).Height, 3);
        Assert.Equal(140f + 26f, NotchLayout.PageSize(NotchPage.Music, default, true, true, 1f).Height, 3);
    }

    [Fact]
    public void Tasks_page_grows_with_its_rows_up_to_the_cap()
    {
        static PageRows Rows(int n) => new(0, n, 0);
        float h0 = NotchLayout.PageSize(NotchPage.Tasks, Rows(0), false, true, 1f).Height;
        float h1 = NotchLayout.PageSize(NotchPage.Tasks, Rows(1), false, true, 1f).Height;
        float h3 = NotchLayout.PageSize(NotchPage.Tasks, Rows(3), false, true, 1f).Height;
        Assert.Equal(h0, h1, 3);                                    // vacía = una fila (el texto de vacío)
        Assert.Equal(12f + 22f + 22f + 16f + 14f, h1, 3);           // pad + cabecera + 1 fila + puntos + pad
        Assert.Equal(h1 + 2 * NotchLayout.ExpandedLine, h3, 3);
        Assert.Equal(NotchLayout.ExpandedMaxHeight, NotchLayout.PageSize(NotchPage.Tasks, Rows(50), true, true, 1f).Height, 3);
    }

    /// <summary>Bandeja con tarjetas: cabecera + una fila de tarjetas, tenga 1 o 30 entradas (las que no caben van tras las
    /// flechas); vacía, la línea del texto de vacío. Con el aviso integrado cabe sin tocar el tope.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Shelf_page_is_one_row_of_cards_whatever_the_count(float s)
    {
        float H(int n, bool alert = false) => NotchLayout.PageSize(NotchPage.Shelf, new PageRows(0, 0, n), alert, true, s).Height;
        Assert.Equal((12f + 22f + 22f + 16f + 14f) * s, H(0), 3);                  // vacía: como antes
        Assert.Equal((12f + 22f + ShelfLayout.CardHeight + 16f + 14f) * s, H(1), 3);
        Assert.Equal(H(1), H(3), 3);
        Assert.Equal(H(1), H(8), 3);
        Assert.Equal(H(1), H(30), 3);
        Assert.Equal(H(1) + (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * s, H(30, alert: true), 3);
        Assert.True(H(30, alert: true) <= NotchLayout.ExpandedMaxHeight * s + 1e-3f, "con aviso, sin recortar las tarjetas");
    }

    /// <summary>Tope de la superficie: ninguna página (ni el expandido) baja tanto que su sombra se corte.</summary>
    [Fact]
    public void Every_page_fits_under_the_surface_with_its_shadow()
    {
        float room = 200f - NotchMotion.ShadowBlurRadius - NotchMotion.ShadowOffsetY;   // las de siempre, en 200
        Assert.True(NotchLayout.ExpandedMaxHeight <= room);
        Assert.True(NotchLayout.ExpandedSize(99, true, 1f).Height <= NotchLayout.ExpandedMaxHeight);
        // La página Uso (la más alta) cabe con su sombra en la superficie de verdad.
        Assert.True(NotchLayout.UsagePageMaxHeight
            <= ClaudeBarWin.UI.Notch.NotchPlacement.SurfaceHeight - NotchMotion.ShadowBlurRadius - NotchMotion.ShadowOffsetY);
        foreach (var p in Enum.GetValues<NotchPage>())
            foreach (bool alert in new[] { false, true })
                Assert.True(NotchLayout.PageSize(p, new PageRows(99, 99, 99), alert, true, 1f).Height
                    <= (p == NotchPage.Usage ? NotchLayout.UsagePageMaxHeight : NotchLayout.ExpandedMaxHeight) + 1e-3f);
    }

    /// <summary>F5 · B3: reposo con música = carátula mini + título; más ancho que el de los anillos, mismo alto.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Idle_music_is_wider_than_the_rings_with_the_same_height(float s)
    {
        var rings = NotchLayout.TargetSize(NotchMode.Idle, s, 0f);
        var music = NotchLayout.IdleMusicSize(s);
        Assert.Equal(rings.Height, music.Height);
        Assert.True(music.Width > rings.Width);
        Assert.True(music.Width < NotchLayout.TargetSize(NotchMode.Working, s, 0f).Width);
    }
}
