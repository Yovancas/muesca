using System.Drawing;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Anillo de la cuota en la letra compacta (pedido del 29-oct), la parte pura: tamaño y alfa atenuados en reposo y enteros
/// con el ratón encima, muelles HoverIn / Rápido sin pasada (con el % en el mismo muelle), sin atenuar con el aviso de
/// cuota, cambio directo con reduce-motion, y la línea de la letra que no salta cuando el % le quita sitio
/// (<see cref="CompactLyrics.LineRect"/>).
/// </summary>
public class CompactQuotaRingTests
{
    private const double Dt = 0.004;   // 250 Hz

    private static void Run(CompactQuotaRing r, double seconds, Action<CompactQuotaRing>? each = null)
    {
        int steps = (int)Math.Round(seconds / Dt);
        for (int i = 0; i < steps; i++)
        {
            r.Advance(Dt);
            each?.Invoke(r);
        }
    }

    // ---------------------------------------------------------------- tamaño y alfa

    [Fact]
    public void Rest_is_smaller_and_faint_and_full_is_the_whole_slot()
    {
        Assert.InRange(CompactLyrics.RingDimScale, 0.6f, 0.7f);
        Assert.Equal(CompactLyrics.SlotSize * CompactLyrics.RingDimScale, CompactLyrics.RingDiameter(0f), 3);
        Assert.Equal(0.45f, CompactLyrics.RingAlpha(0f), 3);
        Assert.Equal(CompactLyrics.SlotSize, CompactLyrics.RingDiameter(1f), 3);
        Assert.Equal(1f, CompactLyrics.RingAlpha(1f), 3);
        // A medio camino, a medio camino; fuera de rango, acotado (nunca más grande que la ranura).
        Assert.InRange(CompactLyrics.RingDiameter(0.5f), CompactLyrics.RingDiameter(0f), CompactLyrics.RingDiameter(1f));
        Assert.Equal(CompactLyrics.SlotSize, CompactLyrics.RingDiameter(1.3f), 3);
        Assert.Equal(CompactLyrics.RingDiameter(0f), CompactLyrics.RingDiameter(-1f), 3);
        Assert.Equal(1f, CompactLyrics.RingAlpha(2f), 3);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(79.9, false)]
    [InlineData(80.0, true)]
    [InlineData(95.0, true)]
    [InlineData(100.0, true)]
    public void Quota_alert_is_from_80_percent(double? pct, bool expected)
    {
        Assert.Equal(expected, CompactLyrics.QuotaAlert(pct));
        Assert.Equal(expected, CompactLyrics.QuotaAlert(pct, new[] { 80, 95 }));                    // los de por defecto
        Assert.Equal(expected, CompactLyrics.QuotaAlert(pct, new AppConfig().Normalize().NotchQuotaThresholds));
    }

    [Theory]
    [InlineData(59.9, false)]
    [InlineData(60.0, true)]   // el primer aviso sale al 60 %: el anillo deja de atenuarse con él, no al 80
    [InlineData(85.0, true)]
    public void Quota_alert_follows_the_lowest_configured_threshold(double pct, bool expected)
    {
        Assert.Equal(expected, CompactLyrics.QuotaAlert(pct, new[] { 60, 90 }));
        Assert.Equal(expected, CompactLyrics.QuotaAlert(pct, new[] { 90, 60 }));   // en cualquier orden
    }

    [Fact]
    public void Without_thresholds_the_alert_is_at_80_and_the_hub_passes_its_own()
    {
        Assert.Equal(80, CompactLyrics.QuotaAlertPct(null));
        Assert.Equal(80, CompactLyrics.QuotaAlertPct(Array.Empty<int>()));
        Assert.Equal(95, CompactLyrics.QuotaAlertPct(new[] { 95 }));
        var hub = new ClaudeStateHub(new QuotaThresholdTracker(new[] { 90, 60, 60 }));
        Assert.Equal(new[] { 60, 90 }, hub.QuotaThresholds);
        Assert.Equal(new[] { 80, 95 }, new ClaudeStateHub().QuotaThresholds);
    }

    // ---------------------------------------------------------------- muelles

    [Fact]
    public void Starts_at_rest_and_nothing_moves()
    {
        var r = new CompactQuotaRing();
        r.Set(hover: false, alert: false);
        Assert.Equal(0f, r.Emphasis);
        Assert.Equal(0f, r.PctReveal);
        Assert.False(r.IsAnimating);
    }

    [Fact]
    public void Hover_grows_with_a_spring_shows_the_pct_and_returns_on_leave()
    {
        var r = new CompactQuotaRing();
        r.Set(hover: true, alert: false);
        Assert.True(r.IsAnimating);
        Assert.Equal(0f, r.Emphasis);   // nada salta: lo mueve el muelle

        float last = 0f;
        Run(r, 0.096, x =>
        {
            Assert.True(x.Emphasis >= last, "sin pasada ni vuelta atrás");
            Assert.Equal(x.Emphasis, x.PctReveal, 4);   // el % va con el mismo muelle
            last = x.Emphasis;
        });
        Assert.True(r.Emphasis is > 0.9f and <= 1f, $"HoverIn: 95 % a ~95 ms ({r.Emphasis})");
        Run(r, 0.4);
        Assert.Equal(1f, r.Emphasis);
        Assert.Equal(1f, r.PctReveal);
        Assert.False(r.IsAnimating);   // quieto: el bucle se para

        r.Set(hover: false, alert: false);
        last = r.Emphasis;
        Run(r, 0.096, x =>
        {
            Assert.True(x.Emphasis <= last, "sin pasada ni vuelta atrás");
            last = x.Emphasis;
        });
        Assert.True(r.Emphasis > 0.1f, $"sale con Rápido, más despacio que entra ({r.Emphasis})");
        Run(r, 0.6);
        Assert.Equal(0f, r.Emphasis);
        Assert.Equal(0f, r.PctReveal);
        Assert.False(r.IsAnimating);
    }

    [Fact]
    public void Leaving_halfway_turns_back_from_where_it_is()
    {
        var r = new CompactQuotaRing();
        r.Set(hover: true, alert: false);
        Run(r, 0.04);
        float mid = r.Emphasis;
        Assert.InRange(mid, 0.3f, 0.9f);

        r.Set(hover: false, alert: false);
        Assert.Equal(mid, r.Emphasis);   // sin salto
        r.Advance(Dt);
        Assert.True(Math.Abs(r.Emphasis - mid) < 0.08f, $"conserva la velocidad ({mid} → {r.Emphasis})");
        Run(r, 1);
        Assert.Equal(0f, r.Emphasis);
    }

    [Fact]
    public void Setting_the_same_target_again_is_idempotent()
    {
        var r = new CompactQuotaRing();
        r.Set(hover: true, alert: false);
        Run(r, 0.04);
        float e = r.Emphasis;
        r.Set(hover: true, alert: false);   // Sync lo pide en cada vuelta
        Assert.Equal(e, r.Emphasis);
        Run(r, 0.5);
        Assert.Equal(1f, r.Emphasis);
    }

    [Fact]
    public void Quota_alert_keeps_the_ring_full_without_the_pct()
    {
        var r = new CompactQuotaRing();
        r.Set(hover: false, alert: true);
        Run(r, 0.5);
        Assert.Equal(1f, r.Emphasis);   // manda la alerta: no se atenúa
        Assert.Equal(0f, r.PctReveal);  // el % sigue siendo cosa del ratón

        r.Set(hover: true, alert: true);
        Run(r, 0.5);
        Assert.Equal(1f, r.Emphasis);
        Assert.Equal(1f, r.PctReveal);

        r.Set(hover: false, alert: true);   // el ratón se va: el anillo sigue entero
        Run(r, 0.5, x => Assert.Equal(1f, x.Emphasis));
        Assert.Equal(0f, r.PctReveal);

        r.Set(hover: false, alert: false);   // la cuota se renueva: vuelve a atenuarse
        Run(r, 1);
        Assert.Equal(0f, r.Emphasis);
    }

    [Fact]
    public void Reduce_motion_and_snap_change_at_once()
    {
        var r = new CompactQuotaRing { ReduceMotion = true };
        r.Set(hover: true, alert: false);
        Assert.Equal(1f, r.Emphasis);
        Assert.Equal(1f, r.PctReveal);
        Assert.False(r.IsAnimating);
        r.Set(hover: false, alert: true);
        Assert.Equal(1f, r.Emphasis);
        Assert.Equal(0f, r.PctReveal);
        r.Set(hover: false, alert: false);
        Assert.Equal(0f, r.Emphasis);
        Assert.False(r.IsAnimating);

        // Sin reduce-motion, «snap» (la letra compacta aún no se ve) también salta.
        var s = new CompactQuotaRing();
        s.Set(hover: true, alert: false, snap: true);
        Assert.Equal(1f, s.Emphasis);
        Assert.Equal(1f, s.PctReveal);
        Assert.False(s.IsAnimating);
    }

    [Fact]
    public void The_compact_view_carries_the_ring()
    {
        var m = new CompactLyricsMotion();
        m.Jump(0, "Hola");
        var ring = new CompactQuotaRing();
        ring.Set(hover: true, alert: false, snap: true);

        var v = NotchForm.CompactViewFor(m, sameLyrics: true, art: true, quota: true, ring);
        Assert.Equal(1f, v.QuotaEmphasis);
        Assert.Equal(1f, v.QuotaReveal);
        var other = NotchForm.CompactViewFor(m, sameLyrics: false, art: true, quota: true, ring);
        Assert.Equal(1f, other.QuotaEmphasis);   // también con la «♪» de la letra que aún no es la actual
        var none = NotchForm.CompactViewFor(m, sameLyrics: true, art: true, quota: true);
        Assert.Equal(0f, none.QuotaEmphasis);    // sin anillo, en reposo
        Assert.Equal(0f, none.QuotaReveal);
    }

    // ---------------------------------------------------------------- la línea no salta

    private static readonly RectangleF Text = new(36.3f, 10f, 226f, 34f);

    [Fact]
    public void A_line_that_fits_does_not_move_when_the_pct_appears()
    {
        for (float taken = 0f; taken <= 40f; taken += 0.5f)
        {
            Assert.Equal(Text, CompactLyrics.LineRect(Text, 60f, taken));    // corta: centrada lejos del %
            Assert.Equal(Text, CompactLyrics.LineRect(Text, 140f, Math.Min(taken, 40f)));
        }
        Assert.Equal(Text, CompactLyrics.LineRect(Text, 0f, 30f));           // sin medida: como siempre
        Assert.Equal(Text, CompactLyrics.LineRect(Text, 200f, 0f));          // sin %: como siempre
    }

    [Fact]
    public void A_longer_line_moves_left_only_as_needed_in_whole_pixels_and_never_jumps()
    {
        const float w = 200f;
        float lastLeft = Text.Left + (Text.Width - w) / 2f;
        for (float taken = 0f; taken <= 40f; taken += 0.25f)
        {
            var r = CompactLyrics.LineRect(Text, w, taken);
            float right = Text.Right - taken;
            float left;
            if (w < Text.Width - taken - 1f)
            {
                // Cabe: centrada en el mismo hueco, desplazada en px enteros lo justo para no pisar el %.
                Assert.Equal(Text.Width, r.Width);
                float shift = Text.Left - r.Left;
                Assert.Equal(MathF.Round(shift), shift, 3);
                left = r.Left + (r.Width - w) / 2f;
                Assert.True(left + w <= right + 0.01f, $"pisa el % ({taken})");
                Assert.True(left >= Text.Left - 0.01f, $"se sale por la izquierda ({taken})");
                if (left + w + 1f <= right && shift > 0f) Assert.Fail($"se movió sin hacer falta ({taken})");
            }
            else
            {
                // No cabe: ocupa lo que queda (con «…»), que mengua con el mismo muelle.
                Assert.Equal(Text.Left, r.Left);
                Assert.Equal(right, r.Right, 3);
                left = r.Left + Math.Max(0f, (r.Width - w) / 2f);
            }
            Assert.True(Math.Abs(left - lastLeft) <= 1.01f, $"salto de {left - lastLeft} px ({taken})");
            lastLeft = left;
        }
    }

    [Fact]
    public void The_pct_room_is_its_width_and_its_gap()
    {
        Assert.Equal(30f + CompactLyrics.PctGap, CompactLyrics.PctRoom(30f, 1f), 3);
        Assert.Equal(45f + CompactLyrics.PctGap * 1.5f, CompactLyrics.PctRoom(45f, 1.5f), 3);
        Assert.Equal(CompactLyrics.PctGap, CompactLyrics.PctRoom(-3f, 1f), 3);
    }
}
