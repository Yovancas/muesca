using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Qué enseña el reposo con música: nada (los anillos de siempre), la canción (carátula mini + título) o la
/// letra compacta (la línea actual).</summary>
public enum IdleMusicFace { None, Title, Lyrics }

/// <summary>Ranuras activas de la letra compacta: <see cref="Art"/> = la carátula (si no hay carátula o está apagada, la
/// ranura izquierda enseña una nota «♪»: la izquierda va SIEMPRE); <see cref="Quota"/> = el anillo de 5 h a la derecha.</summary>
public readonly record struct CompactLyricsSlots(bool Art, bool Quota);

/// <summary>Rectángulos de la letra compacta (px físicos): ranura izquierda, hueco de la línea y ranura derecha (vacía
/// sin la cuota).</summary>
public readonly record struct CompactLyricsRects(RectangleF Left, RectangleF Text, RectangleF Right);

/// <summary>
/// Letra compacta en el notch PEQUEÑO (reposo con música sonando y letra sincronizada). PURO: el instante y las medidas
/// llegan por parámetro. Medidas (px lógicos × escala):
/// <code>
/// ┌───────────────────────────────────────────────────────┐   alto 34
/// │ [♪/arte]        Stay stocked up, real shit        (◯) │   ancho 300 + extra (0..200, a saltos de 16)
/// └───────────────────────────────────────────────────────┘
/// </code>
/// <list type="bullet">
/// <item>Izquierda: 10 de margen + ranura de 18 (carátula o «♪») + 8 de hueco. Siempre, para que pasar de una pista con
/// carátula a otra sin ella no cambie el ancho.</item>
/// <item>Derecha: con la cuota, 8 + anillo de 18 + 12 de margen; sin ella, 14 de margen. El anillo va atenuado en reposo
/// (<see cref="RingDimScale"/> del tamaño y <see cref="RingDimAlpha"/> de alfa) y entero con el ratón encima o con el
/// aviso de cuota (<see cref="CompactQuotaRing"/>); con el ratón encima, el % a su izquierda le quita su sitio al hueco de
/// la línea (<see cref="PctRoom"/>): la pastilla no cambia de ancho.</item>
/// <item>La línea va centrada en el hueco que dejan las ranuras; lo que no cabe ni con el extra máximo, con «…».</item>
/// <item>El extra descuenta lo que ocupan las ranuras activas: es lo que el texto mide de más sobre el hueco a 300,
/// redondeado hacia arriba a <see cref="WidthStep"/> (como Trabajando: cambiar de línea no hace bailar la pastilla a
/// cada px) y acotado a 0..<see cref="ExtraMax"/>.</item>
/// </list>
/// </summary>
public static class CompactLyrics
{
    public const float BaseWidth = 300f, ExtraMax = 200f, Height = 34f, WidthStep = 16f;
    public const float EdgePad = 10f, SlotSize = 18f, SlotGap = 8f, TextPad = 14f, RightPad = 12f;

    /// <summary>Lo que ocupa la ranura izquierda con sus márgenes (siempre está).</summary>
    public const float LeftInset = EdgePad + SlotSize + SlotGap;

    /// <summary>Anillo de la cuota en reposo (pedido del 29-oct): al 65 % de la ranura y al 45 % de alfa, con su color de
    /// umbral. Entero (énfasis 1): la ranura entera y alfa 1.</summary>
    public const float RingDimScale = 0.65f, RingDimAlpha = 0.45f;

    /// <summary>Hueco (px lógicos) entre el % y la ranura del anillo.</summary>
    public const float PctGap = 5f;

    /// <summary>Umbral del aviso sin umbrales configurados: el primero de <c>NotchQuotaThresholds</c> por defecto.</summary>
    public const double DefaultQuotaAlertPct = 80;

    /// <summary>Desde qué % de la cuota de 5 h el anillo no se atenúa (manda la alerta): el umbral más bajo de
    /// <c>NotchQuotaThresholds</c>, con el que salta el primer aviso de cuota (80 por defecto). Antes, 80 fijo: con
    /// [60, 90] el aviso salía al 60 % y el anillo seguía atenuado hasta el 80. PURO.</summary>
    public static double QuotaAlertPct(IReadOnlyList<int>? thresholds) =>
        thresholds is { Count: > 0 } ? thresholds.Min() : DefaultQuotaAlertPct;

    /// <summary>¿La cuota de 5 h está en aviso (≥ <see cref="QuotaAlertPct"/> de <paramref name="thresholds"/>)? Sin dato,
    /// no. El color del anillo sigue siendo el de riesgo de siempre (Warn ≥ 80, Critical ≥ 95). PURO.</summary>
    public static bool QuotaAlert(double? pct, IReadOnlyList<int>? thresholds = null) => pct >= QuotaAlertPct(thresholds);

    /// <summary>Diámetro del anillo (px lógicos) con énfasis <paramref name="emphasis"/> (0 = atenuado, 1 = entero).</summary>
    public static float RingDiameter(float emphasis) =>
        SlotSize * (RingDimScale + (1f - RingDimScale) * Math.Clamp(emphasis, 0f, 1f));

    /// <summary>Alfa del anillo (sobre el del contenido) con énfasis <paramref name="emphasis"/>.</summary>
    public static float RingAlpha(float emphasis) => RingDimAlpha + (1f - RingDimAlpha) * Math.Clamp(emphasis, 0f, 1f);

    /// <summary>Lo que el % (de <paramref name="pctWidth"/> px físicos) le quita al hueco de la línea por la derecha
    /// cuando se ve del todo, a escala <paramref name="s"/>: su ancho y su hueco hasta el anillo. Así entre la línea y el
    /// % queda el mismo hueco que entre la línea y el anillo.</summary>
    public static float PctRoom(float pctWidth, float s) => Math.Max(0f, pctWidth) + PctGap * s;

    /// <summary>
    /// Rectángulo (px físicos) en el que se pinta CENTRADA la línea de <paramref name="lineWidth"/> px cuando el % le quita
    /// <paramref name="taken"/> px al hueco <paramref name="text"/> por la derecha. Para que la letra no salte al aparecer
    /// el %: mientras quepa donde está, ni se mueve (el mismo <paramref name="text"/>); si lo pisaría, se desplaza a la
    /// izquierda lo justo y en px enteros; y si no cabe ni así, ocupa lo que queda (con «…»), que crece y mengua con el
    /// mismo muelle que el %. PURO.
    /// </summary>
    public static RectangleF LineRect(RectangleF text, float lineWidth, float taken)
    {
        taken = Math.Clamp(taken, 0f, text.Width);
        if (taken <= 0f) return text;
        float room = text.Width - taken;
        if (!(lineWidth > 0f)) return text;
        if (lineWidth >= room - 1f) return new RectangleF(text.Left, text.Top, room, text.Height);
        float overlap = text.Left + (text.Width + lineWidth) / 2f - (text.Right - taken);   // lo que pisaría centrada
        if (overlap <= 0f) return text;
        var r = text;
        r.Offset(-MathF.Ceiling(overlap), 0f);
        return r;
    }

    /// <summary>Lo que ocupa la derecha: la ranura de la cuota con sus márgenes o solo el margen.</summary>
    public static float RightInset(bool quota) => quota ? SlotGap + SlotSize + RightPad : TextPad;

    /// <summary>Hueco de la línea a 300 de ancho con esas ranuras.</summary>
    public static float TextRoom(CompactLyricsSlots slots) => BaseWidth - LeftInset - RightInset(slots.Quota);

    /// <summary>
    /// ¿Puede llegar a verse la letra compacta de la pista actual? Es la parte «pequeña» de la regla de red
    /// (<see cref="LyricsController.Wanted"/>): hace falta una pista (sonando o en pausa: una pausa breve —un salto, el
    /// búfer— no debe cancelar la petición en vuelo ni repetirla al reanudar), el notch a la vista (no oculto ni en
    /// pantalla completa) y no tener «Minimalista» (con él el reposo nunca enseña la canción). Encogido SOLO
    /// (<see cref="NotchMode.Minimal"/> sin el ajuste) sí cuenta: al llegar la letra, <see cref="KeepAwake"/> despierta el
    /// notch (pedido del 29-oct). Trabajando, un aviso o el panel abierto también: son de paso y el reposo vuelve con la
    /// letra lista. PURO.
    /// </summary>
    public static bool CanShow(bool trackPresent, bool hiddenForFullscreen, bool minimalSetting, NotchMode mode) =>
        trackPresent && !hiddenForFullscreen && !minimalSetting && mode is not NotchMode.Hidden;

    /// <summary>
    /// ¿Mantener el notch despierto? (pedido del 29-oct: «cuando esté sonando una canción con letra no desaparezca»):
    /// música y letras activas, la letra pequeña activa, la pista SONANDO y letra sincronizada. La máquina de estados no
    /// encoge el reposo mientras dure (<see cref="NotchStateMachine.SetKeepAwake"/>). PURO.
    /// </summary>
    public static bool KeepAwake(bool musicEnabled, bool lyricsEnabled, bool compactEnabled, NowPlaying? track,
        LyricsState state, int lineCount) =>
        musicEnabled && lyricsEnabled && compactEnabled && track is { Playing: true } && state == LyricsState.Found && lineCount > 0;

    /// <summary>
    /// Qué enseña el reposo con música. <paramref name="idleMusic"/>: el reposo enseña la canción (modo Reposo, música
    /// activa y <see cref="IdleMusic.Show"/>). La letra compacta exige además el ajuste de letras y el de la letra
    /// pequeña, la pista SONANDO (en pausa vuelve al título) y letra SINCRONIZADA (<see cref="LyricsState.Found"/> con
    /// líneas): sin letra, buscando, plana o con error, el título de siempre.
    /// </summary>
    public static IdleMusicFace Face(bool idleMusic, bool lyricsEnabled, bool compactEnabled, NowPlaying? track,
        LyricsState state, int lineCount)
    {
        if (!idleMusic) return IdleMusicFace.None;
        return lyricsEnabled && compactEnabled && track is { Playing: true } && state == LyricsState.Found && lineCount > 0
            ? IdleMusicFace.Lyrics : IdleMusicFace.Title;
    }

    /// <summary>Índice de la línea vigente en el instante de la LETRA <paramref name="lyricsPosition"/> (ya con el desfase
    /// del controlador: <see cref="LyricsController.LyricsPosition"/>), con el mismo adelanto que la página Música.</summary>
    public static int LineIndex(IReadOnlyList<LyricLine> lines, TimeSpan lyricsPosition) =>
        LrcParser.IndexAt(lines, lyricsPosition, MusicClock.LyricLead);

    /// <summary>Texto de la línea <paramref name="index"/>; antes de la primera, fuera de rango o vacía, «♪».</summary>
    public static string LineText(IReadOnlyList<LyricLine> lines, int index)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (index < 0 || index >= lines.Count) return LrcParser.PauseText;
        var t = lines[index].Text;
        return string.IsNullOrWhiteSpace(t) ? LrcParser.PauseText : t.Trim();
    }

    /// <summary>
    /// Cuánto falta (tiempo del reproductor, velocidad 1) para que cambie la línea: el instante de la siguiente menos el
    /// adelanto, desde <paramref name="lyricsPosition"/>. null tras la última (no hay nada más que despertar). Nunca menos
    /// de 1 ms: si el plazo saltó un pelo antes de tiempo, se vuelve a mirar enseguida en vez de no despertar.
    /// </summary>
    public static TimeSpan? NextLineIn(IReadOnlyList<LyricLine> lines, TimeSpan lyricsPosition)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0) return null;
        int next = LineIndex(lines, lyricsPosition) + 1;
        if (next >= lines.Count) return null;
        var left = lines[next].At - MusicClock.LyricLead - lyricsPosition;
        return left > TimeSpan.FromMilliseconds(1) ? left : TimeSpan.FromMilliseconds(1);
    }

    /// <summary>Ancho extra (px lógicos, 0..<see cref="ExtraMax"/>, múltiplo de <see cref="WidthStep"/> salvo el tope) para una línea que
    /// mide <paramref name="textWidth"/> px lógicos con esas ranuras.</summary>
    public static float Extra(float textWidth, CompactLyricsSlots slots)
    {
        if (!(textWidth > 0f)) return 0f;
        float over = textWidth - TextRoom(slots);
        if (over <= 0f) return 0f;
        return Math.Min(ExtraMax, MathF.Ceiling(over / WidthStep) * WidthStep);
    }

    /// <summary>Tamaño de la pastilla (px físicos) con <paramref name="extra"/> (se acota a 0..<see cref="ExtraMax"/>).</summary>
    public static SizeF Size(float extra, float scale) =>
        new((BaseWidth + Math.Clamp(extra, 0f, ExtraMax)) * scale, Height * scale);

    /// <summary>Rectángulos dentro de <paramref name="pill"/> (px físicos). La MISMA cuenta para medir y para pintar.</summary>
    public static CompactLyricsRects Compute(RectangleF pill, float s, CompactLyricsSlots slots)
    {
        float d = SlotSize * s, cy = pill.Top + pill.Height / 2f;
        var left = new RectangleF(pill.Left + EdgePad * s, cy - d / 2f, d, d);
        var right = slots.Quota ? new RectangleF(pill.Right - (RightPad + SlotSize) * s, cy - d / 2f, d, d) : RectangleF.Empty;
        float tl = pill.Left + LeftInset * s, tr = pill.Right - RightInset(slots.Quota) * s;
        return new CompactLyricsRects(left, new RectangleF(tl, pill.Top, Math.Max(0f, tr - tl), pill.Height), right);
    }
}

/// <summary>
/// Cambio de línea de la letra compacta (lenguaje de movimiento §3.0, «texto que cambia dentro del mismo diseño» con la
/// subida del contenido nuevo). PURO, el instante llega por parámetro: la línea que se va sale en el primer 60 % de
/// <see cref="Duration"/> (≈ 70 ms) subiendo hasta −<see cref="Rise"/> px; la nueva entra en los 120 ms con ease-out
/// subiendo de +<see cref="Rise"/> a 0, siempre en px enteros (el texto no se emborrona). Con reduce-motion, corte seco.
/// Quieto ⇒ <see cref="IsAnimating"/> = false (el bucle se para y el siguiente repintado es el plazo de la línea).
/// </summary>
public sealed class CompactLyricsMotion
{
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(Motion.Motion.TextCrossfadeMs);

    /// <summary>Recorrido vertical (px lógicos) de la línea que entra y de la que sale.</summary>
    public const float Rise = NotchMotion.ContentRise;

    private const float OutPortion = 0.6f;
    private TimeSpan _at;

    public bool ReduceMotion { get; set; }

    /// <summary>Línea a la vista (índice en la letra; −1 = antes de la primera) y su texto; null = nada aún.</summary>
    public int Index { get; private set; } = int.MinValue;
    public string? Text { get; private set; }

    /// <summary>Línea que se va durante el cruce; null sin cruce.</summary>
    public string? PreviousText { get; private set; }

    /// <summary>Avance del cruce 0..1 (1 = quieto).</summary>
    public float Blend { get; private set; } = 1f;

    public bool IsAnimating => Blend < 1f;

    /// <summary>Alfa de la línea nueva (ease-out).</summary>
    public float Alpha => EaseOut(Blend);

    /// <summary>Alfa de la que se va: fuera en el primer 60 % del cruce.</summary>
    public float PreviousAlpha => PreviousText is null ? 0f : 1f - Math.Clamp(Blend / OutPortion, 0f, 1f);

    /// <summary>Desplazamiento vertical de la nueva en px lógicos enteros (+Rise → 0: entra desde abajo).</summary>
    public int Offset => (int)MathF.Round(Rise * (1f - EaseOut(Blend)));

    /// <summary>Desplazamiento de la que se va en px lógicos enteros (0 → −Rise: sale hacia arriba).</summary>
    public int PreviousOffset => -(int)MathF.Round(Rise * EaseOut(Math.Clamp(Blend / OutPortion, 0f, 1f)));

    /// <summary>La línea que toca ahora: si es otra (otro índice u otro texto), cruce; la primera de todas, sin cruce.</summary>
    public void Show(int index, string text, TimeSpan now)
    {
        if (index == Index && text == Text) return;
        var previous = Text;
        Index = index;
        Text = text;
        if (ReduceMotion || previous is null)
        {
            PreviousText = null;
            Blend = 1f;
            return;
        }
        PreviousText = previous;
        Blend = 0f;
        _at = now;
    }

    /// <summary>Coloca la línea sin cruce (letra nueva, otra pista o la maquetación aún no se ve).</summary>
    public void Jump(int index, string text)
    {
        Index = index;
        Text = text;
        PreviousText = null;
        Blend = 1f;
    }

    public void Step(TimeSpan now)
    {
        if (Blend >= 1f) return;
        float k = (float)((now - _at).TotalMilliseconds / Duration.TotalMilliseconds);
        if (ReduceMotion || k >= 1f)
        {
            Blend = 1f;
            PreviousText = null;
        }
        else Blend = Math.Max(0f, k);
    }

    private static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float u = 1f - t;
        return 1f - u * u * u;
    }
}

/// <summary>
/// Anillo de la cuota de 5 h en la letra compacta (pedido del 29-oct). PURO: el tiempo llega por parámetro.
/// <list type="bullet">
/// <item><see cref="Emphasis"/>: 0 = atenuado (reposo), 1 = entero (<see cref="CompactLyrics.RingDiameter"/> y
/// <see cref="CompactLyrics.RingAlpha"/>). Entero con el ratón encima (la Vista previa) o con el aviso de cuota: el aviso
/// manda y no se atenúa.</item>
/// <item><see cref="PctReveal"/>: el % junto al anillo, solo con el ratón encima; su sitio sale del hueco de la línea con
/// este mismo muelle (<see cref="CompactLyrics.LineRect"/>).</item>
/// <item>Muelles del lenguaje de movimiento §2: HoverIn al entrar (95 % a 95 ms) y Rápido al salir (159 ms), sin pasada:
/// interrumpibles, sin perder la velocidad. Con reduce-motion (o <c>snap</c>), cambio directo.</item>
/// </list>
/// Quieto ⇒ <see cref="IsAnimating"/> = false (el bucle se para).
/// </summary>
public sealed class CompactQuotaRing
{
    private readonly Motion.SpringValue _ring = new(0, Motion.Motion.HoverIn);
    private readonly Motion.SpringValue _pct = new(0, Motion.Motion.HoverIn);

    public bool ReduceMotion { get; set; }

    /// <summary>0 = atenuado, 1 = entero.</summary>
    public float Emphasis => (float)Math.Clamp(_ring.Value, 0.0, 1.0);

    /// <summary>0 = sin %, 1 = el % a la vista y con todo su sitio.</summary>
    public float PctReveal => (float)Math.Clamp(_pct.Value, 0.0, 1.0);

    public bool IsAnimating => !_ring.IsSettled || !_pct.IsSettled;

    /// <summary>Lo que toca: <paramref name="hover"/> = el ratón encima del notch (la Vista previa); <paramref name="alert"/> =
    /// aviso de cuota (<see cref="CompactLyrics.QuotaAlert"/>). <paramref name="snap"/>: sin animación (la letra compacta aún
    /// no se ve: aparece ya como toca). Idempotente.</summary>
    public void Set(bool hover, bool alert, bool snap = false)
    {
        Aim(_ring, hover || alert, snap);
        Aim(_pct, hover, snap);
    }

    /// <summary>Integra <paramref name="dtSeconds"/> segundos.</summary>
    public void Advance(double dtSeconds)
    {
        _ring.Advance(dtSeconds);
        _pct.Advance(dtSeconds);
    }

    private void Aim(Motion.SpringValue s, bool on, bool snap)
    {
        double target = on ? 1 : 0;
        if (snap || ReduceMotion)
        {
            s.Snap(target);
            return;
        }
        if (s.Target == target) return;
        s.SetPreset(on ? Motion.Motion.HoverIn : Motion.Motion.Rapido);
        s.SetTarget(target);
    }
}
