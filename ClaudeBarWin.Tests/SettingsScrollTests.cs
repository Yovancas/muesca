using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// v0.3.7: el panel de ajustes limita su alto (MaxPanelHeightPct del área útil) y el contenido
/// rueda. Aquí se verifica la matemática PURA del scroll (clamp + geometría del pulgar) que vive en
/// <see cref="DashboardSettingsView"/>; el estado/los eventos de rueda viven en DashboardForm.
/// </summary>
public class SettingsScrollTests
{
    // ---------------- ClampScroll ----------------

    [Fact]
    public void Clamp_is_zero_when_content_fits()
    {
        // Sin overflow no hay scroll posible: cualquier offset colapsa a 0.
        Assert.Equal(0, DashboardSettingsView.ClampScroll(0, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(120, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(-50, contentH: 300, viewportH: 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(10, contentH: 400, viewportH: 400)); // justo
    }

    [Fact]
    public void Clamp_bounds_scroll_to_overflow_range()
    {
        // contenido 1000, viewport 400 → rango válido [0, 600].
        Assert.Equal(0, DashboardSettingsView.ClampScroll(-1, 1000, 400));
        Assert.Equal(0, DashboardSettingsView.ClampScroll(0, 1000, 400));
        Assert.Equal(250, DashboardSettingsView.ClampScroll(250, 1000, 400));
        Assert.Equal(600, DashboardSettingsView.ClampScroll(600, 1000, 400));
        Assert.Equal(600, DashboardSettingsView.ClampScroll(9999, 1000, 400));
    }

    // ---------------- F10 (v0.3.9): el scroll PERSISTE entre aperturas, pero se re-acota ----------------
    // ShowSettings ya NO pone _settingsScroll = 0 (state-preservation): reabrir conserva la posición.
    // LayoutContent acota el scroll guardado con ClampScroll en CADA pasada, así que si el contenido
    // ENCOGIÓ desde la última apertura (menos filas) el offset persistido se re-bornea al nuevo overflow
    // y el pulgar NUNCA queda fuera de rango. Aquí se modela ese contrato de re-acotado.

    [Fact]
    public void Persisted_scroll_is_reclamped_when_content_shrinks_on_reopen()
    {
        const int viewportH = 400;
        // 1ª apertura: panel alto (contenido 1000) → el usuario baja al fondo (overflow 600).
        int saved = DashboardSettingsView.ClampScroll(600, contentH: 1000, viewportH: viewportH);
        Assert.Equal(600, saved); // posición persistida al fondo

        // 2ª apertura: el contenido encogió a 500 (overflow nuevo = 100). El offset guardado (600) está
        // fuera de rango; al re-acotar debe caer a 100 (fondo del nuevo contenido), no quedarse en 600.
        int reclamped = DashboardSettingsView.ClampScroll(saved, contentH: 500, viewportH: viewportH);
        Assert.Equal(100, reclamped);

        // Y el pulgar resultante sigue DENTRO de la pista (no se sale por abajo).
        var thumb = DashboardSettingsView.ThumbRect(300, trackTop: 70, viewportH: viewportH,
            contentH: 500, scroll: reclamped);
        Assert.True(thumb.Bottom <= 70 + viewportH, $"el pulgar ({thumb.Bottom}) rebasa la pista tras encoger");
    }

    [Fact]
    public void Persisted_scroll_collapses_to_zero_when_content_now_fits()
    {
        // Caso extremo: tras encoger, el contenido CABE entero (sin overflow). El offset persistido
        // (cualquiera) debe colapsar a 0 — y el pulgar desaparece (sin barra).
        int saved = 480;
        Assert.Equal(0, DashboardSettingsView.ClampScroll(saved, contentH: 300, viewportH: 400));
        Assert.Equal(Rectangle.Empty,
            DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 300, scroll: saved));
    }

    // ---------------- ThumbRect ----------------

    [Fact]
    public void Thumb_is_empty_when_content_fits()
    {
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 300, scroll: 0));
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 400, scroll: 0));
        Assert.Equal(Rectangle.Empty, DashboardSettingsView.ThumbRect(300, 70, viewportH: 0, contentH: 400, scroll: 0));
    }

    [Fact]
    public void Thumb_proportional_and_pinned_to_track_ends()
    {
        const int trackX = 300, trackTop = 70, viewportH = 400, contentH = 1000;

        // Alto proporcional: viewport²/contenido = 160px (≥ mínimo de 24).
        var top = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 0);
        Assert.Equal(trackX, top.X);
        Assert.Equal(DashboardSettingsView.ScrollBarW, top.Width);
        Assert.Equal(160, top.Height);
        Assert.Equal(trackTop, top.Y); // scroll 0 → pegado arriba

        // Scroll máximo (600) → el pulgar termina exactamente al final de la pista.
        var bottom = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 600);
        Assert.Equal(trackTop + viewportH, bottom.Bottom);

        // Scroll intermedio: dentro de la pista, monótono.
        var mid = DashboardSettingsView.ThumbRect(trackX, trackTop, viewportH, contentH, scroll: 300);
        Assert.InRange(mid.Y, top.Y + 1, bottom.Y - 1);
    }

    [Fact]
    public void Thumb_never_smaller_than_minimum_grabbable_size()
    {
        // Contenido enorme → el pulgar no baja de 24px (visible/agarrable) y sigue dentro de la pista.
        var t = DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 100_000, scroll: 0);
        Assert.Equal(24, t.Height);
        var tMax = DashboardSettingsView.ThumbRect(300, 70, viewportH: 400, contentH: 100_000, scroll: 99_600);
        Assert.Equal(70 + 400, tMax.Bottom);
    }

    [Fact]
    public void Scroll_constants_are_sane()
    {
        // El tope deja SIEMPRE aire en pantalla (<100%) y la rueda avanza algo perceptible.
        Assert.InRange(DashboardSettingsView.MaxPanelHeightPct, 40, 90);
        Assert.True(DashboardSettingsView.WheelStepPx > 0);
    }

    // ---------------- WheelToPixels (acumulador rueda/trackpad) ----------------
    // Ratón clásico: un diente de ±120 = ±WheelStepPx exacto, sin resto, simétrico.
    // Trackpad de precisión: ráfagas de deltas pequeños se ACUMULAN hasta el mismo total, sin perder
    // nada y sin tragarse los deltas diminutos (la división entera de antes los truncaba a 0).

    [Fact]
    public void Wheel_classic_mouse_one_notch_is_exactly_one_step()
    {
        // +120 (un diente hacia arriba) → exactamente WheelStepPx px, resto 0 (sensación de siempre).
        var (px, rest) = DashboardSettingsView.WheelToPixels(0, 120);
        Assert.Equal(DashboardSettingsView.WheelStepPx, px);
        Assert.Equal(0, rest);
    }

    [Fact]
    public void Wheel_classic_mouse_is_symmetric_down()
    {
        // −120 (un diente hacia abajo) → exactamente −WheelStepPx, resto 0: simétrico al caso de subir.
        var (px, rest) = DashboardSettingsView.WheelToPixels(0, -120);
        Assert.Equal(-DashboardSettingsView.WheelStepPx, px);
        Assert.Equal(0, rest);
    }

    [Fact]
    public void Wheel_trackpad_burst_accumulates_exactly_one_notch()
    {
        // 8 eventos de +15 = +120 acumulado ⇒ MISMO desplazamiento que un diente (WheelStepPx px) en
        // total, sin perder nada por el camino (lo que la división entera /120 truncaba a 0).
        int acc = 0, total = 0;
        for (int i = 0; i < 8; i++)
        {
            var (px, rest) = DashboardSettingsView.WheelToPixels(acc, 15);
            total += px;
            acc = rest;
        }
        Assert.Equal(DashboardSettingsView.WheelStepPx, total);
        Assert.Equal(0, acc); // el acumulador acaba limpio (8·15 = 120 = diente entero)
    }

    [Fact]
    public void Wheel_tiny_deltas_eventually_scroll_and_are_not_swallowed()
    {
        // Deltas diminutos (+1 repetido): con /120 cada uno truncaba a 0 y el panel NUNCA rodaba.
        // Acumulando, terminan produciendo desplazamiento real (no se "tragan").
        int acc = 0, total = 0;
        for (int i = 0; i < 120; i++)
        {
            var (px, rest) = DashboardSettingsView.WheelToPixels(acc, 1);
            total += px;
            acc = rest;
        }
        // 120 eventos de +1 = +120 acumulado ⇒ un diente completo (WheelStepPx px).
        Assert.Equal(DashboardSettingsView.WheelStepPx, total);
    }

    [Fact]
    public void Wheel_negative_burst_is_exact_mirror_of_positive()
    {
        // La misma ráfaga (8·15) en negativo produce EXACTAMENTE el mismo desplazamiento con signo opuesto.
        int accP = 0, totalP = 0, accN = 0, totalN = 0;
        for (int i = 0; i < 8; i++)
        {
            var (pxP, restP) = DashboardSettingsView.WheelToPixels(accP, 15);
            totalP += pxP; accP = restP;
            var (pxN, restN) = DashboardSettingsView.WheelToPixels(accN, -15);
            totalN += pxN; accN = restN;
        }
        Assert.Equal(totalP, -totalN);
        Assert.Equal(accP, -accN); // el acumulador también es espejo exacto
    }

    [Fact]
    public void Wheel_remainder_is_carried_across_events()
    {
        // Resto conservado: un +60 da píxeles PARCIALES; otro +60 completa el diente exacto sin pérdida.
        var (px1, rest1) = DashboardSettingsView.WheelToPixels(0, 60);
        var (px2, rest2) = DashboardSettingsView.WheelToPixels(rest1, 60);
        // 60+60 = 120 = un diente ⇒ la suma de ambos eventos = WheelStepPx exacto.
        Assert.Equal(DashboardSettingsView.WheelStepPx, px1 + px2);
        Assert.Equal(0, rest2); // tras completar el diente el acumulador queda limpio
        Assert.True(px1 > 0);   // el primer +60 ya desplazó (no esperó al diente completo)
    }
}
