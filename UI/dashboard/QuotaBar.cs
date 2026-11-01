using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.UI;

/// <summary>
/// Barra de cuota unificada (cuerpo + cabecera). Sustituye a las gemelas casi idénticas
/// <c>DashboardDataView.DrawBar</c> y <c>DashboardHeader.DrawCriticalBar</c>: una sola rutina
/// que ambas llaman, para que toda señal nueva (pace marker, ticks de umbral) se escriba una vez.
/// Conserva la simetría medir(draw=false)/pintar(draw=true): avanza y devuelve el mismo <c>y</c>.
/// </summary>
public static class QuotaBar
{
    // T11: alto/radio de la barra en px de diseño (96 DPI) proyectados al DPI vigente — antes eran
    // const y al 125/150% la barra quedaba fina respecto al texto crecido. A factor 1.0, idénticos.
    private static int BarH => Dpi.Scale(11);
    private static int BarRadius => Dpi.Scale(5);

    /// <summary>
    /// Dibuja etiqueta + % + barra + línea de reset y devuelve el nuevo y.
    /// El color sigue el criterio de F1: PaceStatus→Ok/Warn/Critical, con fallback a <see cref="ColorMath.RiskColor"/>.
    ///
    /// <para>F3 (tween): <paramref name="displayUtil"/> es el override <b>eased</b> de la utilización para
    /// el <b>ancho de relleno y el número</b>; el <b>color se calcula con la utilización objetivo</b>
    /// (<c>win.UtilizationPct</c>) para que no parpadee de color durante el tween. Si es <c>null</c>
    /// (render-test, reduce-motion, cabecera sin motion) ⇒ comportamiento idéntico a hoy (usa <c>util</c>).</para>
    /// <para>Lenguaje de movimiento §4 #5: <paramref name="displayNumber"/> es la cifra del mismo muelle
    /// (entera mientras se mueve), <paramref name="fillColor"/> el color del relleno mezclado con Rápido y
    /// <paramref name="trail"/> el rastro de una renovación (micro-interacción 2). Todo opcional y solo
    /// de pintado: el alto reservado no cambia (medir == pintar).</para>
    /// </summary>
    public static int Draw(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim,
        double? displayUtil = null, double? displayNumber = null, Color? fillColor = null,
        (double Pct, double Alpha)? trail = null, Func<string, (string? Prev, double Blend)>? lineBlend = null)
    {
        double util = win?.UtilizationPct ?? 0;
        // Ancho con el valor animado (si lo hay); la cifra sale del mismo muelle; el color del relleno se
        // mezcla con Rápido hacia el del objetivo (sin arcoíris).
        double shown = displayUtil ?? util;
        double number = displayNumber ?? shown;
        double clamped = Math.Clamp(shown / 100.0, 0.0, 1.0);
        // F4 (v0.3.9 g2): el RELLENO va por % REAL (RiskColor) — más lleno = más cálido, longitud y color
        // coherentes. Antes se coloreaba por PACE: una barra al 57% salía ROJA y otra al 84% VERDE en la
        // misma columna (el color contradecía la longitud). La señal de ritmo se mueve al pace-marker ▾
        // (más abajo), coloreado por PaceStatus. El relleno usa SIEMPRE el objetivo (no parpadea con el tween).
        Color c = fillColor ?? ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        // Estado por forma (a11y, daltónicos): por UMBRAL de % real, coherente con el relleno — mismo
        // mapeo color↔forma que el tray. El ritmo (pace) ya no secuestra ni el relleno ni el glifo.
        UsageStatus status = util >= cfg.CriticalThresholdPct ? UsageStatus.Critical
            : util >= cfg.WarnThresholdPct ? UsageStatus.Warn : UsageStatus.Ok;
        // El TEXTO (glifo + %) usa la variante AA del color de estado por % real (T6b): como texto pequeño
        // Critical oscuro caía a 3.7:1 y Warn claro a 2.8:1. Sigue el % (no el pace), igual que el relleno.
        Color textColor = Theme.PaceTextColor(theme, StatusToPace(status));

        if (draw)
        {
            g.DrawString(label, labelFont, fg, x, y);
            // Glifo de forma de 1 carácter + % a la derecha, ambos en el color de estado.
            string glyph = Tray.ShapeGlyph(Tray.ShapeFor(status));
            // El número usa el valor eased (shown); el glifo/estado va por el objetivo (no parpadea).
            // % con la cultura del idioma elegido (T2): "12.5%" en inglés, "12,5%" en español.
            string right = $"{glyph} {UsageFormat.Percent(number, s.Culture)}";
            using var valBrush = GdiCache.BrushFor(textColor);
            // T10 (§3 #16): medida central tipográfica + pintado con el mismo formato → la tinta del %
            // termina exacta en x+w y toda la columna derecha del panel queda alineada.
            TextMetrics.DrawRight(g, right, Typography.Mono, valBrush, x + w, y);
        }
        y += Dpi.Scale(22); // fila label/% (T11: escala con el DPI, como las fuentes)

        if (draw)
        {
            using var trackBrush = GdiCache.BrushFor(theme.Track);
            Shapes.FillRounded(g, trackBrush, new Rectangle(x, y, w, BarH), BarRadius);
            // Rastro de la renovación: hasta donde llegaba la barra, al 25 % de alfa y fundiéndose.
            if (trail is { } tr && tr.Alpha > 0)
            {
                int tw = (int)Math.Round(w * Math.Clamp(tr.Pct / 100.0, 0.0, 1.0));
                int ta = (int)Math.Round(Math.Clamp(tr.Alpha, 0.0, 1.0) * c.A);
                if (tw > 1 && ta > 0)
                {
                    using var trailBrush = GdiCache.BrushFor(Color.FromArgb(ta, c));
                    Shapes.FillRounded(g, trailBrush, new Rectangle(x, y, tw, BarH), BarRadius);
                }
            }
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                using var fillBrush = GdiCache.BrushFor(c);
                Shapes.FillRounded(g, fillBrush, new Rectangle(x, y, fw, BarH), BarRadius);
            }

            // Ticks de umbral: muescas finas (1px) en Warn/Critical, tras el relleno para no quedar
            // tapadas por las esquinas redondeadas. Neutro (theme.TickOnTrack — Separator era ≈ Track
            // en los 3 temas y el tick desaparecía, T3b), nunca Accent.
            using (var tickPen = GdiCache.PenFor(theme.TickOnTrack, 1f))
            {
                int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
                int cx = QuotaBarGeometry.TickX(x, w, cfg.CriticalThresholdPct);
                g.DrawLine(tickPen, wx, y, wx, y + BarH - 1);
                g.DrawLine(tickPen, cx, y, cx, y + BarH - 1);
            }

            // Pace marker: "dónde deberías ir" según el ritmo ideal. Sobresale MarkerOvershoot (2px)
            // arriba/abajo y lleva un ▾ clampado a esa misma altura: el triángulo antiguo subía hasta
            // y-5 e invadía los descendentes de la fila etiqueta/% (T3c). Solo cuando hay pace.
            // F4 (v0.3.9 g2): el marcador ES AHORA la señal de RITMO — se colorea por PaceStatus con la
            // variante AA del color (PaceTextColor: Ok→Ok, Over→WarnText, Critical→CriticalText) para que
            // el ritmo siga siendo glanceable en los 3 temas sin secuestrar el color del relleno (% real).
            if (pace is { } pm)
            {
                int mx = QuotaBarGeometry.MarkerX(x, w, pm.IdealPct);
                Color markerColor = Theme.PaceTextColor(theme, pm.Status);
                using var markerPen = GdiCache.PenFor(markerColor, 2f);
                g.DrawLine(markerPen, mx, y - QuotaBarGeometry.MarkerOvershootPx, mx, y + BarH + 1);
                using var markerBrush = GdiCache.BrushFor(markerColor);
                g.FillPolygon(markerBrush, QuotaBarGeometry.PaceTriangle(mx, y));
            }
        }
        y += BarH + 3;

        // Cuenta atrás a resolución de minutos (micro-interacción 4): sin tick de 1 Hz.
        string cd = UsageFormat.CountdownMinutesAt(win?.ResetsAt, s.Resetting, DateTimeOffset.Now);
        if (draw && cd.Length > 0)
        {
            // "resetea en 2h 13m · mar 18:42" — relativo (countdown) + hora local absoluta, con el
            // día abreviado del idioma elegido (T2: la UI inglesa mostraba "jue 02:12").
            // T8c: la línea se elide al ancho de la fila (locales largos / panel angosto la
            // desbordaban). Solo pintado: el alto reservado no cambia → medir==pintar.
            string abs = UsageFormat.ResetAbsolute(win?.ResetsAt, s.Culture);
            string line = abs.Length > 0 ? $"{s.ResetsIn} {cd} · {abs}" : $"{s.ResetsIn} {cd}";
            line = TextWrap.FitLine(line, x, x + w, 0, t => g.MeasureString(t, smallFont).Width);
            var (prev, blend) = lineBlend?.Invoke(line) ?? (null, 1.0);
            if (prev is null || blend >= 1 || dim is not SolidBrush sb)
                g.DrawString(line, smallFont, dim, x, y);
            else
                DrawChangedDigits(g, prev, line, blend, smallFont, sb.Color, x, y);
        }
        return y + Dpi.Scale(14); // línea de reset (T11)
    }

    /// <summary>
    /// Micro-interacción 4: cruza (120 ms, lineal) SOLO el tramo que cambia entre dos versiones de la
    /// línea de reset; prefijo y sufijo comunes se pintan quietos. Cada tramo se pinta con el formato
    /// por defecto en la x tipográfica acumulada, así su tinta cae donde la pintaría la línea entera.
    /// </summary>
    private static void DrawChangedDigits(Graphics g, string prev, string line, double blend, Font font, Color color,
        float x, float y)
    {
        var (pre, oldMid, newMid, suf) = TextDiff.Split(prev, line);
        float Typo(string t) => t.Length == 0 ? 0f : g.MeasureString(t, font, int.MaxValue, TextMetrics.Typographic).Width;
        using var full = GdiCache.BrushFor(color);
        if (pre.Length > 0) g.DrawString(pre, font, full, x, y);
        float mx = x + Typo(pre);
        int aOld = (int)Math.Round(color.A * (1 - blend)), aNew = (int)Math.Round(color.A * blend);
        if (oldMid.Length > 0 && aOld > 0)
        {
            using var ob = GdiCache.BrushFor(Color.FromArgb(aOld, color));
            g.DrawString(oldMid, font, ob, mx, y);
        }
        if (newMid.Length > 0 && aNew > 0)
        {
            using var nb = GdiCache.BrushFor(Color.FromArgb(aNew, color));
            g.DrawString(newMid, font, nb, mx, y);
        }
        if (suf.Length > 0) g.DrawString(suf, font, full, mx + Typo(newMid), y);
    }

    /// <summary>
    /// Mapea el estado de cuota por % real (<see cref="UsageStatus"/>) al estado de pace que entiende
    /// <see cref="Theme.PaceTextColor"/>, para reutilizar sus variantes AA de texto (Ok/WarnText/CriticalText)
    /// en el % y el glifo de la barra. F4: el texto sigue el % real, no el ritmo (Warn≡Over como texto).
    /// </summary>
    private static PaceStatus StatusToPace(UsageStatus s) => s switch
    {
        UsageStatus.Critical => PaceStatus.Critical,
        UsageStatus.Warn => PaceStatus.Over,
        _ => PaceStatus.Ok
    };
}

/// <summary>
/// Geometría pura y testeable de la barra de cuota: proyección de un porcentaje al eje X de la barra.
/// Sin estado ni dependencias de GDI+; usada por el pace marker y los ticks de umbral (Tarea 3).
/// </summary>
public static class QuotaBarGeometry
{
    /// <summary>
    /// X (px) del marcador para un porcentaje <paramref name="pct"/> dentro de la barra que arranca en
    /// <paramref name="x"/> con ancho <paramref name="w"/>. Recorta el resultado a [x, x+w].
    /// </summary>
    public static int MarkerX(int x, int w, double pct)
    {
        double p = Math.Clamp(pct, 0.0, 100.0);
        return x + (int)Math.Round(w * p / 100.0);
    }

    /// <summary>X (px) de un tick de umbral; comparte la proyección de <see cref="MarkerX"/>.</summary>
    public static int TickX(int x, int w, double thresholdPct) => MarkerX(x, w, thresholdPct);

    /// <summary>
    /// Overshoot máximo del pace marker por encima de la barra (px): el mismo que ya usa la línea
    /// vertical. Es el techo del clamp del ▾ (T3c): nada del marcador sube más que esto.
    /// </summary>
    public const int MarkerOvershoot = 2;

    /// <summary><see cref="MarkerOvershoot"/> proyectado al DPI en el punto de uso (F3 Task 5).</summary>
    internal static int MarkerOvershootPx => Dpi.Scale(MarkerOvershoot);

    /// <summary>
    /// Puntos del ▾ del pace marker, clampados a la fila de la barra (T3c): la base queda en
    /// <c>barY - MarkerOvershoot</c> (donde ya arrancaba la línea) y la punta entra 1px en la barra,
    /// apuntando hacia abajo. El triángulo antiguo (base en barY-5) invadía la fila del label.
    /// </summary>
    public static Point[] PaceTriangle(int mx, int barY) => new[]
    {
        new Point(mx - 3, barY - MarkerOvershootPx),
        new Point(mx + 3, barY - MarkerOvershootPx),
        new Point(mx, barY + 1),
    };

    /// <summary>
    /// Ancho del track de una fila compacta (mini-cuota con el % right-aligned en la MISMA banda
    /// vertical): el track se corta <paramref name="gap"/> px antes del texto para no tacharlo (T3a).
    /// Nunca negativo (locales largos / panel estrecho ⇒ el track desaparece antes que pisar el texto).
    /// </summary>
    public static int CompactTrackWidth(int w, int rightTextW, int gap) => Math.Max(0, w - rightTextW - gap);
}
