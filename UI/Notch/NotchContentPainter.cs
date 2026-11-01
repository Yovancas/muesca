using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Pinta la pastilla y su contenido según el modo (spec §4.5 + lenguaje de movimiento §3):
/// <list type="bullet">
/// <item>Idle: «◯ 5h  ◯ 7d»; con música sonando y sin Claude trabajando (<see cref="MusicView.IdleFace"/>), carátula
/// mini + título (F5 · B3); con letra sincronizada (<see cref="MusicView.Compact"/>), la letra compacta: carátula o «♪»,
/// la línea actual centrada y el anillo de 5 h (atenuado; entero y con el % con el ratón encima, también en la Vista
/// previa); cuando le toca (<see cref="IdleView.Clock"/>, F5 · B7), hora y fecha.</item>
/// <item>Working: spinner + «proyecto · herramienta» + % de 5 h.</item>
/// <item>Peek: Working/Idle con la de 7 días al lado.</item>
/// <item>Gato (F5 · B7, <see cref="IdleView.Mascot"/>): dos ojos a la izquierda del contenido del reposo y de la vista
/// previa, o en lugar de la raya de Mínimo (cápsula negra con los ojos, en el color de la cuota si está en aviso).</item>
/// <item>Expanded: el aviso vigente, si hay, arriba; debajo la página actual (F5). Página Claude = cuota 5h/7d con su
/// reset y lista de sesiones; con varias páginas, puntos abajo con la píldora indicadora y cruce de 120 ms con
/// desplazamiento de 8 px al cambiar de página (<see cref="NotchPagesView"/>). Página Música (B3): carátula, título y
/// artista, anterior / pausa / siguiente, barra de progreso con tiempos y el botón «Letra», que divide la vista con la
/// línea actual resaltada y la anterior y la siguiente atenuadas; la geometría es la de <see cref="MusicLayout"/>.</item>
/// <item>Completed / QuotaHud / QuotaAlert / Permission: título + detalle. Permission sin petición del helper es
/// SOLO LECTURA («Responde en la terminal»); con ella (F2) lleva los botones Denegar / Permitir abajo a la
/// derecha, en <see cref="NotchGeometry.PermissionButtons"/>: atenuados (texto al 55 %) y con el relleno de la
/// cuenta atrás de izquierda a derecha hasta armarse; tras el clic, «Permitido» / «Denegado» y el otro al 30 %. Con
/// <see cref="NotchFrame.ReadOnly"/> (un notch por monitor y este no es el principal efectivo), el Permiso con botones y el
/// Recordatorio salen sin ellos: título, detalle y «Haz clic para responder aquí» (o la respuesta que se dio en el otro).</item>
/// </list>
/// La pastilla es siempre negra (es un hueco en la pantalla, no una ventana del tema), así que el
/// contenido usa los tokens del tema oscuro. La forma lleva orejas cóncavas y squash-stretch; el contenido
/// se pinta con la caja sin deformar. La sombra y el halo (texturas 9-slice generadas una vez por escala)
/// van aparte, en <see cref="PaintShadow"/>, porque se entregan a la ventana de sombra (que no recibe clics).
/// Texto en gris con <c>AntiAliasGridFit</c> (ClearType sobre alfa deja halos de color). Fuentes cacheadas
/// por escala; pincel y lápiz únicos reutilizados (se les cambia el color, con el alfa cuantizado a
/// 1/32), así un frame no reserva nada en el montón salvo los strings del texto.
/// </summary>
public sealed partial class NotchContentPainter : IDisposable
{
    // Rejilla del contenido (px lógicos, × escala al pintar).
    private const float PadX = 14f, Gap = 8f, RightSlot = 40f;

    /// <summary>Lo que ocupa Working sin el texto: márgenes, spinner, huecos y la ranura del %.</summary>
    public const float WorkingChromeWidth = 2 * PadX + NotchMotion.SpinnerDiameter + 2 * Gap + RightSlot;

    /// <summary>Hueco (px lógicos) que la pastilla de Working base (300) deja al texto; lo que el texto mida de más o
    /// de menos se pide como <c>workingTextExtra</c> a <see cref="NotchLayout.TargetSize"/>.</summary>
    public const float WorkingTextBase = 300f - WorkingChromeWidth;

    /// <summary>El ancho de Working va a saltos de 16 px: cambiar de herramienta (Bash → Read) no lo hace bailar.</summary>
    public const float WorkingWidthStep = 16f;

    /// <summary>Filas máximas de la lista de sesiones del expandido.</summary>
    public const int MaxListedSessions = NotchLayout.ExpandedMaxRows;

    private readonly Theme _t;
    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private readonly StringFormat _left, _right, _center;
    // Detalle del Permiso con botones: ajuste de línea SIN recorte ni «…» (LineLimit: una línea que no cabe entera no se
    // pinta, y entonces no cuenta como completo).
    private readonly StringFormat _wrap;

    /// <summary>Icono del aviso (px lógicos), a la izquierda del título.</summary>
    private const float AlertIconSize = 18f;

    // Caché de PermissionDetailLines (el Sync del notch la pregunta en cada vuelta mientras hay un Permiso).
    private string? _detailLinesText;
    private float _detailLinesScale = -1f;
    private int _detailLines;

    // Botones del Permiso: blanco translúcido sobre el negro de la pastilla (la base es la pista del armado).
    private static readonly Color ButtonBase = Color.FromArgb(16, 255, 255, 255);
    private static readonly Color ButtonDeny = Color.FromArgb(34, 255, 255, 255);
    private static readonly Color ButtonAllow = Color.FromArgb(60, 255, 255, 255);
    private const float ButtonRadius = 7f, ButtonDimText = 0.55f, ButtonDimOther = 0.3f, ButtonPressDarken = 0.06f;

    // Contraste adaptativo (F5 · B6): sobre fondo oscuro, contorno de 1 px blanco al 14 % y raya de Mínimo gris claro.
    private static readonly Color OutlineOnDark = Color.FromArgb(36, 255, 255, 255);
    /// <summary>Raya de Mínimo sobre fondo oscuro (sin la cuota en aviso).</summary>
    public static readonly Color MinimalOnDark = Color.FromArgb(212, 212, 216);

    private float _fontScale = -1f;
    private Font? _title, _body, _caption, _small;

    private Bitmap? _measureBmp;
    private Graphics? _measureG;

    private float _alpha = 1f;   // alfa de contenido del frame en curso (cuantizado a 1/32)

    private BlurSlice? _shadow, _halo;

    // Carátula decodificada UNA vez por imagen (la instantánea de música reutiliza el mismo array mientras no cambie).
    private byte[]? _artSource;
    private Bitmap? _art;
    private readonly PointF[] _tri = new PointF[3];
    private readonly PointF[] _quad = new PointF[4];          // morph pausa ↔ reproducir: una barra / media punta
    private readonly float[] _insets = new float[4];
    private List<PointF> _clipA = new(8), _clipB = new(8);   // recorte por semiplanos de RoundConvex
    private readonly PointF[]?[] _polys = new PointF[]?[12];  // resultado del recorte, por nº de vértices (sin arrays por fotograma)
    // Repaso redondeado de los glifos de música (RoundConvex/RoundTriangle): uno solo, como _brush, sin Pen por llamada.
    private readonly Pen _glyphPen = new(Color.White) { LineJoin = LineJoin.Round };

    public NotchContentPainter(Strings strings, Theme? theme = null)
    {
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _t = theme ?? Theme.Dark;
        _left = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
        };
        _right = (StringFormat)_left.Clone();
        _right.Alignment = StringAlignment.Far;
        _center = (StringFormat)_left.Clone();
        _center.Alignment = StringAlignment.Center;
        _wrap = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.None,
            FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.MeasureTrailingSpaces,
        };
    }

    /// <summary>
    /// Lo que se pintó del último Permiso con botones (<see cref="Paint"/> lo rehace en cada frame; <c>null</c> si el
    /// frame no tenía uno). <see cref="PermissionPaint.Complete"/> = el detalle se pintó ENTERO (sin recortar, dentro de
    /// la pastilla y por encima de los botones), con los botones y el contenido a la vista: <c>NotchForm</c> solo acepta
    /// el clic si lo que se pintó es lo que se aprueba.
    /// </summary>
    public PermissionPaint? LastPermissionPaint { get; private set; }

    /// <summary>Ancho (px) de la columna de texto del Permiso a su tamaño final (360 lógicos).</summary>
    public static float PermissionTextWidth(float scale) =>
        Math.Max(0f, NotchLayout.PermissionWidth - 2f * PadX - AlertIconSize - Gap) * scale;

    /// <summary>
    /// Líneas (1..<see cref="NotchLayout.PermissionDetailMaxLines"/>) que ocupa el detalle del Permiso con ajuste de
    /// línea en la columna de <see cref="PermissionTextWidth"/>; 0 si NO cabe entero en ese máximo (entonces el aviso va
    /// en solo lectura). Sin detalle, 1 (el alto base). La misma medida que usa <see cref="Paint"/>.
    /// </summary>
    public int PermissionDetailLines(string? detail, float scale)
    {
        if (string.IsNullOrEmpty(detail) || scale <= 0) return 1;
        if (string.Equals(detail, _detailLinesText, StringComparison.Ordinal) && Math.Abs(scale - _detailLinesScale) < 0.001f)
            return _detailLines;
        EnsureFonts(scale);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var area = new SizeF(PermissionTextWidth(scale),
            NotchLayout.PermissionDetailMaxLines * NotchLayout.PermissionDetailLine * scale);
        var (fits, lines) = MeasureDetail(_measureG, detail, area);
        _detailLinesText = detail;
        _detailLinesScale = scale;
        _detailLines = fits ? Math.Max(1, lines) : 0;
        return _detailLines;
    }

    /// <summary>¿El texto cabe ENTERO en <paramref name="area"/> con ajuste de línea, en como mucho 4 líneas?</summary>
    private (bool Fits, int Lines) MeasureDetail(Graphics g, string text, SizeF area)
    {
        if (area.Width <= 1f || area.Height <= 1f) return (false, 0);
        var size = g.MeasureString(text, _caption!, area, _wrap, out int chars, out int lines);
        bool fits = chars >= text.Length && lines >= 1 && lines <= NotchLayout.PermissionDetailMaxLines
            && size.Height <= area.Height + 0.5f;
        return (fits, lines);
    }

    /// <summary>Textos del idioma vigente (se puede cambiar en caliente al cambiar de idioma).</summary>
    public Strings Strings { get; set; }

    /// <summary>Fondo bajo el notch (F5 · B6, lo decide <see cref="AdaptiveContrast"/>). <see cref="NotchContrast.OnLight"/> =
    /// el notch de siempre; <see cref="NotchContrast.OnDark"/> = contorno tenue y raya de Mínimo gris claro.</summary>
    public NotchContrast Contrast { get; set; } = NotchContrast.OnLight;

    /// <summary>
    /// Pinta la pastilla del frame centrada arriba en una superficie de <paramref name="surface"/> y
    /// devuelve el rectángulo tocado, orejas incluidas (para que el renderer borre solo eso en el frame
    /// siguiente). Sin sombra ni halo: esos van en <see cref="PaintShadow"/>, en otra ventana.
    /// </summary>
    public RectangleF Paint(Graphics g, Size surface, NotchFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Mode == NotchMode.Hidden || f.Width < 1f || f.Height < 1f) return RectangleF.Empty;

        float sc = f.Scale;
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);                 // caja del contenido
        var shape = ShapeRect(f, surface);                                             // forma dibujada
        float radius = NotchLayout.CornerRadius(shape.Width, sc);
        float ears = NotchMotion.EarSize(shape.Width, sc);
        var bounds = RectangleF.FromLTRB(shape.Left - ears, shape.Top, shape.Right + ears, shape.Bottom);

        SetQuality(g);
        _alpha = 1f;
        LastPermissionPaint = null;

        // Forma: negra y opaca (la atenuación global la hace el renderer con el alfa de la ventana).
        // En Mínimo, con la cuota de 5 h en aviso (desde el primer umbral configurado), la raya toma el color de la cuota.
        using (var path = NotchGeometry.PillPath(shape, radius, ears))
        {
            _brush.Color = f.Mode == NotchMode.Minimal && f.Idle?.Mascot is null ? MinimalColor(f) : Color.Black;
            g.FillPath(_brush, path);
            // Mínimo con el pomodoro en marcha: la raya se llena con lo que queda (salvo con la cuota en aviso: su color avisa).
            if (f.Mode == NotchMode.Minimal && f.Idle?.Mascot is null && f.Focus is { Face: true } focus
                && !QuotaWarns(f))
                FocusMinimalBar(g, path, shape, focus);
        }
        if (Contrast == NotchContrast.OnDark && f.Mode != NotchMode.Minimal) PaintOutline(g, shape, radius, ears, sc);
        if (f.ReduceMotion && f.Mode == NotchMode.Permission)
        {
            // Sin rebote de atención: borde Warn fijo de 1 px, por dentro de la forma.
            float w = Math.Max(1f, sc);
            using var inner = NotchGeometry.PillPath(RectangleF.FromLTRB(shape.Left + w / 2f, shape.Top,
                shape.Right - w / 2f, shape.Bottom - w / 2f), radius);
            SetPen(_t.Warn, w);
            g.DrawPath(_pen, inner);
        }

        if (f.Idle is { Mascot: { } look } idle && f.Mode is NotchMode.Idle or NotchMode.Minimal or NotchMode.Peek)
            PaintMascot(g, pill, shape, sc, f, look, idle.MascotAlpha);

        _alpha = Quantize(f.ContentAlpha);
        if (_alpha <= 0f || f.Mode == NotchMode.Minimal) return bounds;

        EnsureFonts(sc);
        var state = g.Save();
        g.SetClip(RectangleF.Intersect(pill, shape));
        if (f.ContentOffsetY != 0f) g.TranslateTransform(0f, MathF.Round(f.ContentOffsetY * sc));
        try
        {
            switch (f.Mode)
            {
                case NotchMode.Idle:
                    PaintIdle(g, pill, sc, f);
                    // Versión nueva: el punto de acento, solo con los anillos o el reloj (la canción y la letra llenan la pastilla).
                    if (f.Update is not null && f.Music is not { IdleFace: true }) PaintUpdateDot(g, pill, sc);
                    break;
                case NotchMode.Working: PaintWorking(g, pill, sc, f, peek: false); break;
                case NotchMode.Peek when f.Focus is { Face: true } focus && PrimaryActive(f.Sessions) is null:
                    PaintFocusPeek(g, pill, sc, f, focus);
                    if (f.Update is not null) PaintUpdateDot(g, pill, sc);
                    break;
                case NotchMode.Peek:
                    PaintWorking(g, IdleScene.ContentArea(pill, f.Idle?.Mascot is not null, sc), sc, f, peek: true);
                    if (f.Update is not null) PaintUpdateDot(g, pill, sc);
                    break;
                case NotchMode.Expanded: PaintExpanded(g, pill, sc, f); break;
                case NotchMode.Completed:
                case NotchMode.QuotaHud:
                case NotchMode.QuotaAlert:
                case NotchMode.Permission:
                case NotchMode.Reminder:
                    if (f.Alert is { } a) PaintAlert(g, pill, sc, f, a);
                    break;
            }
        }
        finally { g.Restore(state); }
        return bounds;
    }

    /// <summary>
    /// Pinta la sombra y el halo del frame (texturas 9-slice cacheadas por escala, sin blur por frame) y
    /// devuelve el rectángulo tocado; vacío si no hay nada que pintar. Van en la ventana de sombra
    /// (<c>WS_EX_TRANSPARENT</c>): en la de la pastilla, sus píxeles con alfa &gt; 0 fuera de la zona de clic
    /// se comerían los clics de otras apps (lenguaje de movimiento §6.3).
    /// </summary>
    public RectangleF PaintShadow(Graphics g, Size surface, NotchFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Mode == NotchMode.Hidden || f.Width < 1f || f.Height < 1f) return RectangleF.Empty;

        float sc = f.Scale;
        var shape = ShapeRect(f, surface);
        SetQuality(g);
        var bounds = RectangleF.Empty;
        if (f.ShadowAlpha > 0.004f)
            bounds = Blur(ref _shadow, sc, NotchMotion.ShadowBlurRadius, Color.Black,
                NotchMotion.ShadowMaxAlpha).Draw(g, shape, NotchMotion.ShadowOffsetY * sc, f.ShadowAlpha);
        if (f.HaloAlpha > 0.004f)
        {
            var halo = Blur(ref _halo, sc, NotchMotion.HaloRadius, _t.Critical,
                NotchMotion.HaloMaxAlpha).Draw(g, shape, 0f, f.HaloAlpha);
            bounds = bounds.IsEmpty ? halo : RectangleF.Union(bounds, halo);
        }
        return bounds;
    }

    /// <summary>
    /// Filas de sesiones que caben en el expandido con una pastilla de <paramref name="pillHeight"/> px
    /// físicos (como mucho <see cref="MaxListedSessions"/>). Si hay más sesiones que filas, la última fila
    /// es «+N más». La usa <c>NotchForm</c> para limitar la rueda con la MISMA cuenta que se pinta.
    /// </summary>
    public static int ExpandedRows(float pillHeight, float scale, bool withAlert, bool withDots = false)
    {
        if (scale <= 0) return 1;
        float inner = pillHeight - (NotchLayout.ExpandedPadTop + NotchLayout.ExpandedPadBottom) * scale;
        if (withAlert) inner -= (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * scale;   // el aviso integrado ocupa la primera línea
        if (withDots) inner -= NotchLayout.PageDotsRow * scale;                                    // fila de puntos de página
        return RowsIn(inner, scale);
    }

    /// <summary>Filas de sesión que caben en <paramref name="innerHeight"/> px (1..<see cref="MaxListedSessions"/>).</summary>
    private static int RowsIn(float innerHeight, float scale)
    {
        if (scale <= 0) return 1;
        float line = NotchLayout.ExpandedLine * scale;
        innerHeight += 0.5f * scale;                            // tolerancia de redondeo de la escala
        return Math.Max(1, Math.Min(MaxListedSessions, (int)(innerHeight / line)));
    }

    /// <summary>
    /// Contorno sobre fondo oscuro: 1 px por DENTRO de la forma (orejas incluidas), así no sale de lo que ya se borra
    /// y se pinta. Sin el tramo de arriba: la pastilla está pegada al borde de la pantalla.
    /// </summary>
    private void PaintOutline(Graphics g, RectangleF shape, float radius, float ears, float sc)
    {
        float w = Math.Max(1f, sc);
        var state = g.Save();
        try
        {
            g.SetClip(RectangleF.FromLTRB(shape.Left - ears, shape.Top + w, shape.Right + ears, shape.Bottom));
            using var inner = NotchGeometry.PillPath(RectangleF.FromLTRB(shape.Left + w / 2f, shape.Top,
                shape.Right - w / 2f, shape.Bottom - w / 2f), radius, ears);
            SetPen(OutlineOnDark, w);
            g.DrawPath(_pen, inner);
        }
        finally { g.Restore(state); }
    }

    /// <summary>Forma dibujada: la caja del frame con su squash-stretch, centrada arriba.</summary>
    private static RectangleF ShapeRect(NotchFrame f, Size surface)
    {
        var squashed = NotchMotion.Squashed(f.Width, f.Height, f.Squash);
        return NotchGeometry.PillRect(squashed.Width, squashed.Height, surface);
    }

    private static void SetQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    /// <summary>
    /// Ancho (px lógicos) que el texto de Working necesita de más (positivo) o le sobra (negativo) respecto a
    /// <see cref="WorkingTextBase"/>, redondeado hacia arriba a <see cref="WorkingWidthStep"/>; lo usa <c>NotchForm</c>
    /// como <c>workingTextExtra</c>. Sin sesiones activas → 0.
    /// </summary>
    public float MeasureWorkingExtra(IReadOnlyList<SessionState> sessions, float scale)
    {
        if (scale <= 0) return 0f;
        string text = WorkingText(sessions, Strings);
        if (text.Length == 0) return 0f;
        EnsureFonts(scale);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float w = _measureG.MeasureString(text, _body!, int.MaxValue, _left).Width / scale;
        float extra = MathF.Ceiling((w - WorkingTextBase) / WorkingWidthStep) * WorkingWidthStep;
        return extra == 0f ? 0f : extra;   // sin «-0»
    }

    /// <summary>La sesión que protagoniza Working: la activa con actividad más reciente.</summary>
    internal static SessionState? PrimaryActive(IReadOnlyList<SessionState> sessions)
    {
        SessionState? best = null;
        foreach (var x in sessions)
            if (x.Phase.IsActive() && (best is null || x.LastActivityUtc > best.LastActivityUtc)) best = x;
        return best;
    }

    /// <summary>«proyecto · herramienta» de la sesión protagonista (+N si hay más activas); "" si ninguna.</summary>
    internal static string WorkingText(IReadOnlyList<SessionState> sessions, Strings str)
    {
        var p = PrimaryActive(sessions);
        if (p is null) return "";
        string what = p.Phase == SessionPhase.Compacting ? str.SessionPhaseCompacting
            : !string.IsNullOrEmpty(p.PendingTool) ? p.PendingTool!
            : !string.IsNullOrEmpty(p.LastTool) ? p.LastTool! : str.SessionPhaseProcessing;
        string name = string.IsNullOrEmpty(p.ProjectName) ? SessionState.ProjectNameFromCwd(p.Cwd) : p.ProjectName;
        int others = sessions.Count(x => x.Phase.IsActive()) - 1;
        return others > 0 ? $"{name} · {what}  +{others}" : $"{name} · {what}";
    }

    /// <summary>Alfa en pasos de 1/<see cref="NotchMotion.AlphaSteps"/> (sin escalones visibles y sin
    /// colores nuevos en cada frame de un fundido).</summary>
    internal static float Quantize(float a) =>
        MathF.Round(Math.Clamp(a, 0f, 1f) * NotchMotion.AlphaSteps) / NotchMotion.AlphaSteps;

    // ---------------------------------------------------------------- modos

    private void PaintIdle(Graphics g, RectangleF pill, float s, NotchFrame f)
    {
        // Pomodoro en marcha: el mini anillo con lo que queda (manda sobre la canción, el reloj y los anillos; con la cuota de 5 h
        // en aviso, su anillo sigue al lado).
        if (f.Focus is { Face: true } focus)
        {
            PaintFocusIdle(g, pill, s, f, focus);
            return;
        }
        if (f.Music is { IdleFace: true, Track: { } track })
        {
            if (f.Music.Compact is { } compact) PaintCompactLyrics(g, pill, s, track, compact, f.Quota);
            else PaintIdleMusic(g, pill, s, track, f.Music.Equalizer);
            return;
        }
        var area = IdleScene.ContentArea(pill, f.Idle?.Mascot is not null, s);
        if (f.Idle is { Clock: true } clock) PaintClock(g, area, s, clock.Time, clock.Date);
        else PaintQuotaPair(g, area, s, f, withPct: false);
    }

    // Reloj del reposo (F5 · B7): «18:55  jue 29 oct», hora en el cuerpo y fecha atenuada, centrados.
    private const float ClockGap = 6f;

    /// <summary>Ancho (px) de «hora  fecha» a escala <paramref name="s"/>; el reposo se ensancha si no cabe.</summary>
    public float MeasureClock(float s, string time, string date)
    {
        EnsureFonts(s);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return ClockWidth(_measureG, s, time, date);
    }

    private float ClockWidth(Graphics g, float s, string time, string date) =>
        g.MeasureString(time, _body!, int.MaxValue, _left).Width + ClockGap * s + TextW(g, date);

    private void PaintClock(Graphics g, RectangleF area, float s, string time, string date)
    {
        float tw = g.MeasureString(time, _body!, int.MaxValue, _left).Width;
        float x = area.Left + Math.Max(0f, (area.Width - ClockWidth(g, s, time, date)) / 2f);
        DrawText(g, time, _body!, _t.Foreground, new RectangleF(x, area.Top, tw + 1f, area.Height), _left);
        x += tw + ClockGap * s;
        DrawText(g, date, _caption!, _t.TextMuted, new RectangleF(x, area.Top, Math.Max(0f, area.Right - x), area.Height), _left);
    }

    /// <summary>
    /// Ojos del gato (geometría de <see cref="IdleScene.EyeRects"/>) con su propio alfa, dentro de la forma. Claros; en la
    /// raya con la cuota de 5 h en aviso (primer umbral configurado), del color de la cuota (la cápsula es negra: el aviso pasa a los ojos).
    /// </summary>
    private void PaintMascot(Graphics g, RectangleF pill, RectangleF shape, float s, NotchFrame f, MascotLook look, float alpha)
    {
        _alpha = Quantize(alpha);
        if (_alpha <= 0f) return;
        var state = g.Save();
        g.SetClip(RectangleF.Intersect(pill, shape));
        try
        {
            var color = f.Mode == NotchMode.Minimal && QuotaWarns(f) && f.Quota?.FiveHour is { } w
                ? QuotaColor(w.UtilizationPct, text: false) : _t.Foreground;
            _brush.Color = Fade(color);
            var (l, r) = IdleScene.EyeRects(IdleScene.MascotCenter(pill, f.Mode, s), look, s);
            g.FillEllipse(_brush, l);
            g.FillEllipse(_brush, r);
        }
        finally { g.Restore(state); }
    }

    /// <summary>Carátula mini del reposo con música (px físicos): 18 px, a 10 del borde izquierdo y centrada en alto.</summary>
    public static RectangleF IdleMusicArt(RectangleF pill, float s)
    {
        float d = IdleArt * s;
        return new RectangleF(pill.Left + 10f * s, pill.Top + (pill.Height - d) / 2f, d, d);
    }

    private const float IdleArt = 18f;

    /// <summary>Reposo con música: carátula mini + título (la cuota sigue a un hover de distancia, en la vista previa).</summary>
    private void PaintIdleMusic(Graphics g, RectangleF pill, float s, NowPlaying track, float[]? eq)
    {
        var art = IdleMusicArt(pill, s);
        AlbumArtImage(g, art, track, 4f * s);
        float x = art.Right + 8f * s;
        float right = pill.Right - 12f * s;
        if (eq is not null)
        {
            // Ecualizador «está sonando» a la derecha, del color de la carátula (el ritmo, como la referencia).
            float w = EqualizerWidth(s), h = EqHeight * s;
            Equalizer(g, new RectangleF(right - w, pill.Top + (pill.Height - h) / 2f, w, h), eq, track.Accent, s);
            right -= w + 8f * s;
        }
        DrawText(g, track.Title, _body!, _t.Foreground, new RectangleF(x, pill.Top, Math.Max(0f, right - x), pill.Height), _left);
    }

    /// <summary>Ancho (px lógicos) de una línea de la letra compacta con la fuente con que se pinta; lo usa <c>NotchForm</c>
    /// para pedir el ancho extra (<see cref="CompactLyrics.Extra"/>).</summary>
    public float MeasureCompactLine(string text, float scale)
    {
        if (scale <= 0 || string.IsNullOrEmpty(text)) return 0f;
        EnsureFonts(scale);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return _measureG.MeasureString(text, _title!, int.MaxValue, _left).Width / scale;
    }

    /// <summary>
    /// Letra compacta: ranura izquierda (carátula mini redondeada como en el reposo con música o, sin ella, una nota «♪»
    /// atenuada), la línea actual centrada en el hueco entre las ranuras (con «…» si no cabe) y, a la derecha, el anillo de
    /// 5 h con su color por umbral: atenuado y más pequeño en reposo, entero con el ratón encima o con el aviso de cuota
    /// (<see cref="CompactLyricsView.QuotaEmphasis"/>, centrado en su ranura). Con el ratón encima, el % a la izquierda
    /// del anillo le quita su sitio al hueco de la línea con el mismo muelle: la línea solo se aparta si lo pisaría
    /// (<see cref="CompactLyrics.LineRect"/>) y el % (quieto, fundiéndose) se destapa desde el anillo con el sitio que la
    /// línea deja, así nunca se pisan. En un cambio de línea se pintan las dos con su alfa y su desplazamiento (px
    /// enteros), recortadas a su hueco para que nunca pisen las ranuras ni el %.
    /// </summary>
    private void PaintCompactLyrics(Graphics g, RectangleF pill, float s, NowPlaying track, CompactLyricsView v, RealUsage? quota)
    {
        var r = CompactLyrics.Compute(pill, s, v.Slots);
        if (v.Art && ArtBitmap(track.ArtPng) is not null) AlbumArtImage(g, r.Left, track, 4f * s);
        else NoteGlyph(g, r.Left, _t.TextMuted);

        float saved = _alpha, taken = 0f;
        if (!r.Right.IsEmpty)
        {
            var five = quota?.FiveHour;
            float d = CompactLyrics.RingDiameter(v.QuotaEmphasis) * s;
            _alpha = Quantize(saved * CompactLyrics.RingAlpha(v.QuotaEmphasis));
            Ring(g, r.Right.Left + (r.Right.Width - d) / 2f, r.Right.Top + r.Right.Height / 2f, d, five?.UtilizationPct);
            _alpha = saved;
            if (v.QuotaReveal > 0f)
            {
                string pct = Pct(five);
                float pw = TextW(g, pct);
                taken = MathF.Round(CompactLyrics.PctRoom(pw, s) * Math.Clamp(v.QuotaReveal, 0f, 1f));
                _alpha = Quantize(saved * v.QuotaReveal);
                if (_alpha > 0f && taken > 0f)
                {
                    var clip = g.Save();
                    try
                    {
                        g.SetClip(RectangleF.FromLTRB(r.Right.Left - taken, pill.Top, r.Right.Left, pill.Bottom), CombineMode.Intersect);
                        float x = r.Right.Left - CompactLyrics.PctGap * s - pw;
                        DrawText(g, pct, _caption!, PctColor(five, text: true), new RectangleF(x, pill.Top, pw + 1f, pill.Height), _left);
                    }
                    finally { g.Restore(clip); }
                }
                _alpha = saved;
            }
        }

        var state = g.Save();
        try
        {
            g.SetClip(RectangleF.FromLTRB(r.Text.Left, r.Text.Top, r.Text.Right - taken, r.Text.Bottom), CombineMode.Intersect);
            CompactLine(g, r.Text, s, v.PreviousLine, v.PreviousAlpha, v.PreviousOffset, saved, taken);
            CompactLine(g, r.Text, s, v.Line, v.Alpha, v.Offset, saved, taken);
        }
        finally
        {
            _alpha = saved;
            g.Restore(state);
        }
    }

    private void CompactLine(Graphics g, RectangleF text, float s, string? line, float alpha, int offset, float contentAlpha,
        float taken)
    {
        if (string.IsNullOrEmpty(line)) return;
        _alpha = Quantize(contentAlpha * alpha);
        if (_alpha <= 0f) return;
        var at = taken > 0f
            ? CompactLyrics.LineRect(text, g.MeasureString(line, _title!, int.MaxValue, _left).Width, taken)
            : text;
        at.Offset(0f, MathF.Round(offset * s));
        bool pause = line == LrcParser.PauseText;
        DrawText(g, line, _title!, pause ? _t.TextMuted : _t.Foreground, at, _center);
    }

    /// <summary>Nota «♪» dibujada en código (sin depender de las fuentes): cabeza ovalada abajo a la izquierda, plica y
    /// banderín, centrada en <paramref name="slot"/> al 78 % de su lado.</summary>
    private void NoteGlyph(Graphics g, RectangleF slot, Color color)
    {
        float d = Math.Min(slot.Width, slot.Height) * 0.78f;
        float x = slot.Left + (slot.Width - d) / 2f, y = slot.Top + (slot.Height - d) / 2f;
        float headW = d * 0.46f, headH = d * 0.34f, headX = x + d * 0.14f, headY = y + d - headH - d * 0.04f;
        float stem = Math.Max(1.25f, d * 0.1f), stemX = headX + headW - stem / 2f, top = y + d * 0.06f;
        _brush.Color = Fade(color);
        var state = g.Save();
        try
        {
            // Cabeza un poco inclinada, como la de una nota escrita.
            g.TranslateTransform(headX + headW / 2f, headY + headH / 2f);
            g.RotateTransform(-20f);
            g.FillEllipse(_brush, -headW / 2f, -headH / 2f, headW, headH);
        }
        finally { g.Restore(state); }
        SetPen(color, stem);
        g.DrawLine(_pen, stemX, headY + headH * 0.45f, stemX, top);
        g.DrawBezier(_pen, stemX, top, stemX + d * 0.12f, top + d * 0.08f, stemX + d * 0.3f, top + d * 0.18f,
            stemX + d * 0.22f, top + d * 0.42f);
    }

    private const float EqBarW = 2f, EqGap = 2f, EqHeight = 14f;

    private static float EqualizerWidth(float s) =>
        (Services.Notch.Features.EqualizerBars.Count * EqBarW + (Services.Notch.Features.EqualizerBars.Count - 1) * EqGap) * s;

    /// <summary>Barras del ecualizador, centradas en vertical (crecen hacia arriba y hacia abajo), con extremos rectos.</summary>
    private void Equalizer(Graphics g, RectangleF r, float[] heights, Color color, float s)
    {
        _brush.Color = Fade(color);
        float bw = EqBarW * s, x = r.Left, cy = r.Top + r.Height / 2f;
        foreach (var k in heights)
        {
            float h = Math.Max(bw, r.Height * Math.Clamp(k, 0f, 1f));
            g.FillRectangle(_brush, x, cy - h / 2f, bw, h);
            x += bw + EqGap * s;
        }
    }

    // Grupo «◯ 5h  ◯ 7d» (reposo) o «◯ 5h 89%  ◯ 7d 48%» (vista previa), juntos y centrados en la pastilla.
    private const float PairIconGap = 5f, PairPctGap = 5f, PairGroupGap = 11f;

    private float RingFor(bool withPct) => withPct ? NotchMotion.RingDiameterPeek : NotchMotion.RingDiameter;

    private float TextW(Graphics g, string t) => g.MeasureString(t, _caption!, int.MaxValue, _left).Width;

    /// <summary>Ancho (px) del grupo de dos anillos a escala <paramref name="s"/> con esos textos de %.</summary>
    public float MeasureQuotaPair(float s, bool withPct, string pct5, string pct7)
    {
        EnsureFonts(s);
        _measureBmp ??= new Bitmap(1, 1);
        _measureG ??= Graphics.FromImage(_measureBmp);
        _measureG.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return PairWidth(_measureG, s, withPct, pct5, pct7);
    }

    private float PairWidth(Graphics g, float s, bool withPct, string pct5, string pct7)
    {
        float one(string label, string pct) => (RingFor(withPct) + PairIconGap) * s + TextW(g, label)
            + (withPct ? PairPctGap * s + TextW(g, pct) : 0f);
        return one("5h", pct5) + PairGroupGap * s + one("7d", pct7);
    }

    private void PaintQuotaPair(Graphics g, RectangleF pill, float s, NotchFrame f, bool withPct)
    {
        var five = f.Quota?.FiveHour;
        var seven = f.Quota?.SevenDay;
        string p5 = Pct(five), p7 = Pct(seven);
        float cy = pill.Top + pill.Height / 2f;
        float ring = RingFor(withPct) * s;
        float x = pill.Left + (pill.Width - PairWidth(g, s, withPct, p5, p7)) / 2f;

        void One(UsageWindow? w, string label, string pct)
        {
            Ring(g, x, cy, ring, w?.UtilizationPct);
            x += ring + PairIconGap * s;
            float lw = TextW(g, label);
            DrawText(g, label, _caption!, _t.TextMuted, new RectangleF(x, pill.Top, lw + 1f, pill.Height), _left);
            x += lw;
            if (!withPct) return;
            x += PairPctGap * s;
            float pw = TextW(g, pct);
            DrawText(g, pct, _caption!, PctColor(w, text: true), new RectangleF(x, pill.Top, pw + 1f, pill.Height), _left);
            x += pw;
        }

        One(five, "5h", p5);
        x += PairGroupGap * s;
        One(seven, "7d", p7);
    }

    private void PaintWorking(Graphics g, RectangleF pill, float s, NotchFrame f, bool peek)
    {
        float cy = pill.Top + pill.Height / 2f;
        float x = pill.Left + PadX * s;
        string text = WorkingText(f.Sessions, Strings);
        bool active = text.Length > 0;

        // Vista previa estrecha sin sesión activa: lo mismo que el reposo (anillo · 5h · %), sin rótulos cortados.
        if (peek && !active && !SevenDayFits(pill.Width, s, NotchMotion.RingDiameterPeek))
        {
            PaintQuotaPair(g, pill, s, f, withPct: true);
            return;
        }

        float icon = active ? NotchMotion.SpinnerDiameter
            : peek ? NotchMotion.RingDiameterPeek : NotchMotion.RingDiameter;
        if (active) Spinner(g, x, cy, icon * s, f.SpinnerPhase, f.ReduceMotion);
        else Ring(g, x, cy, icon * s, f.Quota?.FiveHour?.UtilizationPct);
        x += (icon + Gap) * s;

        // Derecha: 5h (y 7d en la vista previa), en ranuras fijas para que el texto no baile.
        float right = pill.Right - PadX * s;
        DrawText(g, Pct(f.Quota?.FiveHour), _caption!, PctColor(f.Quota?.FiveHour, text: true),
            new RectangleF(right - RightSlot * s, pill.Top, RightSlot * s, pill.Height), _right);
        right -= RightSlot * s;
        if (peek && SevenDayFits(pill.Width, s, icon))
        {
            DrawText(g, "5h", _caption!, _t.TextMuted,
                new RectangleF(right - 18f * s, pill.Top, 18f * s, pill.Height), _right);
            right -= (18f + Gap) * s;
            DrawText(g, Pct(f.Quota?.SevenDay), _caption!, PctColor(f.Quota?.SevenDay, text: true),
                new RectangleF(right - RightSlot * s, pill.Top, RightSlot * s, pill.Height), _right);
            right -= RightSlot * s;
            DrawText(g, "7d", _caption!, _t.TextMuted,
                new RectangleF(right - 18f * s, pill.Top, 18f * s, pill.Height), _right);
            right -= 18f * s;
        }
        // Pomodoro en marcha: su mini anillo, sin texto (Claude manda), a la izquierda de la cuota (FocusFace.WorkingSlot).
        if (active && f.Focus is { Face: true } focus)
        {
            float d = FocusFace.WorkingRing * s;
            right -= Gap * s;
            FocusMiniRing(g, right - d, cy, d, focus);
            right -= d;
        }
        right -= Gap * s;

        // El texto de la izquierda solo si le queda un hueco legible (en la vista previa estrecha no cabe).
        if (right - x >= MinLabelWidth * s)
        {
            string label = active ? text : Strings.NoActiveSessions;
            DrawText(g, label, _body!, active ? _t.Foreground : _t.TextMuted,
                new RectangleF(x, pill.Top, right - x, pill.Height), _left);
        }
    }

    private const float MinLabelWidth = 28f;

    // Expandido: margen lateral, columna de cuota y separación entre columnas (los altos, en NotchLayout).
    private const float ExpandedPadX = 18f, ExpandedQuotaCol = 180f, ExpandedColGap = 28f, ExpandedWindowGap = 10f;

    /// <summary>
    /// ¿Cabe el bloque «7d %» en la vista previa sin pisar el icono? Icono + «5h %» + «7d %» con sus márgenes:
    /// a 160 de ancho (proporciones de la referencia) no cabe y la vista previa enseña solo 5h.
    /// </summary>
    internal static bool SevenDayFits(float pillWidth, float s, float iconDiameter)
    {
        float needed = 2 * PadX + iconDiameter + Gap + 2 * (RightSlot + 18f) + 2 * Gap;
        return pillWidth >= needed * s;
    }

    private void PaintExpanded(Graphics g, RectangleF pill, float s, NotchFrame f)
    {
        // La tarjeta del Pro ocupa el expandido entero: sin la línea de arriba ni las páginas.
        if (f.ProCard is { } card)
        {
            PaintProCard(g, pill, s, card);
            return;
        }
        float line = NotchLayout.ExpandedLine * s;
        var inner = ExpandedInner(pill, s, withAlert: false, withDots: false);

        // Aviso vigente integrado arriba, común a todas las páginas (con el notch abierto no se pinta encima: se integra). Sin
        // aviso, esa línea es de la versión nueva, si la hay.
        if (f.Alert is { } a)
        {
            var (title, detail, color) = AlertLines(a, f);
            string lineText = detail is null ? title : $"{title} · {detail}";
            DrawText(g, lineText, _body!, color, new RectangleF(inner.Left, inner.Top, inner.Width, line), _left);
        }
        else if (f.Update is { } u) PaintUpdateRow(g, NotchUpdateLayout.Row(pill, s), s, u);
        inner = ExpandedInner(pill, s, HasTopLine(f), HasDots(f));

        if (f.Pages is not { Pages.Count: > 1 } pv)
        {
            PaintClaudePage(g, inner, s, f);
            return;
        }

        PageDots(g, pill, s, pv);
        // Cruce de páginas (lenguaje de movimiento §3.3, variante sobria): la que sale y la que entra, cada una con su
        // alfa y su desplazamiento horizontal en px enteros.
        if (pv.Previous is { } prev && pv.Blend < 1f) PageLayer(g, prev, inner, s, f, 1f - pv.Blend, pv.PreviousShift);
        PageLayer(g, pv.Current, inner, s, f, pv.Blend, pv.CurrentShift);
    }

    private static bool HasDots(NotchFrame f) => f.Pages is { Pages.Count: > 1 };

    /// <summary>Zona de contenido de una página del expandido (la de <see cref="MusicLayout"/>): la usa <c>NotchForm</c> para
    /// acertar el clic con la MISMA cuenta que se pinta.</summary>
    public static RectangleF PageInner(RectangleF pill, float s, bool withAlert, bool withDots) =>
        ExpandedInner(pill, s, withAlert, withDots);

    /// <summary>Zona de contenido del expandido: sin márgenes, sin la línea del aviso integrado y sin la fila de puntos.</summary>
    private static RectangleF ExpandedInner(RectangleF pill, float s, bool withAlert, bool withDots)
    {
        float pad = ExpandedPadX * s, top = pill.Top + NotchLayout.ExpandedPadTop * s;
        if (withAlert) top += (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * s;
        float bottom = pill.Bottom - (NotchLayout.ExpandedPadBottom + (withDots ? NotchLayout.PageDotsRow : 0f)) * s;
        return RectangleF.FromLTRB(pill.Left + pad, top, pill.Right - pad, Math.Max(top, bottom));
    }

    /// <summary>Columna de la lista de sesiones de la página Claude (la usa <c>NotchForm</c> para saber si la rueda va a
    /// la lista o cambia de página).</summary>
    public static RectangleF SessionColumn(RectangleF pill, float s, bool withAlert, bool withDots)
    {
        var inner = ExpandedInner(pill, s, withAlert, withDots);
        float lx = inner.Left + (ExpandedQuotaCol + ExpandedColGap) * s;
        return RectangleF.FromLTRB(Math.Min(lx, inner.Right), inner.Top, inner.Right, inner.Bottom);
    }

    /// <summary>Una página con su alfa (sobre el del frame) y desplazada <paramref name="shift"/> px lógicos.</summary>
    private void PageLayer(Graphics g, NotchPage page, RectangleF inner, float s, NotchFrame f, float alpha, float shift)
    {
        float saved = _alpha;
        _alpha = Quantize(saved * alpha);
        if (_alpha <= 0f) { _alpha = saved; return; }
        var state = g.Save();
        try
        {
            if (shift != 0f) g.TranslateTransform(MathF.Round(shift * s), 0f);
            if (NotchProLock.Has(f.Locked, page))
            {
                PaintLockedPage(g, inner, s, page);
                return;
            }
            switch (page)
            {
                case NotchPage.Claude: PaintClaudePage(g, inner, s, f); break;
                case NotchPage.Music: PaintMusicPage(g, inner, s, f.Music); break;
                case NotchPage.Tasks: PaintTasksPage(g, inner, s, f.Tasks); break;
                case NotchPage.Shelf: PaintShelfPage(g, inner, s, f.Shelf); break;
                case NotchPage.Usage: PaintUsagePage(g, inner, s, f.Usage); break;
                case NotchPage.Focus: PaintFocusPage(g, inner, s, f.Focus); break;
            }
        }
        finally
        {
            g.Restore(state);
            _alpha = saved;
        }
    }

    /// <summary>Página Claude: el expandido de siempre (cuota 5h/7d a la izquierda y sesiones a la derecha).</summary>
    private void PaintClaudePage(Graphics g, RectangleF inner, float s, NotchFrame f)
    {
        float line = NotchLayout.ExpandedLine * s;

        // Columna izquierda: cuota 5h / 7d (etiqueta + % + barra + reset).
        float colW = ExpandedQuotaCol * s;
        float y = inner.Top;
        foreach (var (label, win) in new[] { ("5h", f.Quota?.FiveHour), ("7d", f.Quota?.SevenDay) })
        {
            if (y + 2 * line > inner.Bottom + 1f) break;
            DrawText(g, label, _caption!, _t.TextMuted, new RectangleF(inner.Left, y, colW, line), _left);
            DrawText(g, Pct(win), _body!, PctColor(win, text: true), new RectangleF(inner.Left, y, colW, line), _right);
            Bar(g, new RectangleF(inner.Left, y + line, colW, 4f * s), win?.UtilizationPct);
            string cd = UsageFormat.CountdownAt(win?.ResetsAt, Strings.Resetting, Now(f));
            if (cd.Length > 0)
                DrawText(g, $"{Strings.ResetsIn} {cd}", _caption!, _t.TextMuted,
                    new RectangleF(inner.Left, y + line + 6f * s, colW, line * 0.8f), _left);
            y += 2 * line + ExpandedWindowGap * s;
        }

        // Columna derecha: sesiones (urgentes primero, luego las más recientes).
        float lx = inner.Left + colW + ExpandedColGap * s;
        var list = f.Sessions.Where(x => x.Phase != SessionPhase.Ended)
            .OrderBy(x => x.Phase.Priority()).ThenByDescending(x => x.LastActivityUtc).ToList();
        int rows = RowsIn(inner.Height, s);
        if (list.Count == 0)
        {
            DrawText(g, Strings.NoActiveSessions, _body!, _t.TextMuted,
                new RectangleF(lx, inner.Top, inner.Right - lx, line), _left);
            return;
        }
        int shown = list.Count > rows ? rows - 1 : list.Count;
        y = inner.Top;
        for (int i = 0; i < shown; i++, y += line)
            SessionRow(g, list[i], new RectangleF(lx, y, inner.Right - lx, line), s, f.SpinnerPhase, f.ReduceMotion);
        if (list.Count > shown)
            DrawText(g, string.Format(Strings.Culture, Strings.NotchMoreSessionsFmt, list.Count - shown), _caption!,
                _t.TextMuted, new RectangleF(lx + 14f * s, y, inner.Right - lx, line), _left);
    }

    /// <summary>Página aún sin contenido propio (Música, Tareas y Bandeja llegan en B3–B5): cabecera y texto de vacío.</summary>
    private void PaintPlaceholderPage(Graphics g, RectangleF inner, float s, string header, string empty)
    {
        float line = NotchLayout.ExpandedLine * s;
        DrawText(g, header, _title!, _t.Foreground, new RectangleF(inner.Left, inner.Top, inner.Width, line), _left);
        var body = RectangleF.FromLTRB(inner.Left, inner.Top + line, inner.Right, Math.Max(inner.Top + line, inner.Bottom));
        DrawText(g, empty, _body!, _t.TextMuted, body, _center);
    }

    // ---------------------------------------------------------------- página Bandeja (F5 · B4)

    // Tarjetas, flechas y botón de la bandeja: blanco translúcido sobre el negro (como los de la música).
    private static readonly Color ShelfSelectedFill = Color.FromArgb(40, 255, 255, 255);
    private static readonly Color ShelfButtonFill = Color.FromArgb(16, 255, 255, 255);
    private static readonly Color ShelfTileFill = Color.FromArgb(22, 255, 255, 255);
    private static readonly Color ShelfDropOutline = Color.FromArgb(150, 255, 255, 255);
    private const float ShelfDropInset = 2f, ShelfMissingDim = 0.4f, ShelfArrowDim = 0.35f, ShelfGlyphSize = 18f;
    /// <summary>Radio del realce de las filas de lista (las de Tareas).</summary>
    private const float ShelfRowRadius = 6f;

    // Nombres recortados de las tarjetas: medir con GDI+ es caro, así que se recortan una vez por nombre y escala.
    private readonly Dictionary<(string Name, bool File), string> _shelfNames = new();
    private float _shelfNamesScale = -1f;
    private const int ShelfNamesMax = 128;

    /// <summary>
    /// Página Bandeja: cabecera con «Vaciar» («Quitar (N)» con selección) y una fila de tarjetas, la más nueva a la izquierda:
    /// miniatura del sistema (o icono dibujado), nombre recortado y, en gris, tamaño o tipo. Si no caben todas, flechas ‹ ›
    /// a los lados (atenuadas en los extremos) y la fila desplazada <see cref="ShelfView.Scroll"/>, recortada al visor.
    /// Entrada borrada o movida: atenuada y con «No disponible». Con algo aceptable arrastrándose encima, contorno alrededor
    /// de la página. Geometría de <see cref="ShelfLayout"/>.
    /// </summary>
    private void PaintShelfPage(Graphics g, RectangleF inner, float s, ShelfView? v)
    {
        if (v is null)
        {
            PaintPlaceholderPage(g, inner, s, Strings.NotchPageShelf, Strings.NotchShelfEmpty);
            return;
        }
        if (v.DropTarget) ShelfDropFrame(g, inner, s);

        int count = v.Items.Count;
        var r = ShelfLayout.Compute(inner, s, count);
        DrawText(g, Strings.NotchPageShelf, _title!, _t.Foreground, r.Title, _left);
        if (count == 0)
        {
            var line = new RectangleF(r.List.Left, r.List.Top, r.List.Width, Math.Min(r.List.Height, NotchLayout.ExpandedLine * s));
            DrawText(g, v.DropTarget ? Strings.NotchShelfDropHere : Strings.NotchShelfEmpty, _body!,
                v.DropTarget ? _t.Foreground : _t.TextMuted, line, _center);
            return;
        }

        ShelfClearButton(g, r.ClearButton, v, s);
        if (r.Overflows)
        {
            float end = ShelfLayout.ScrollFor(ShelfLayout.MaxFirst(count));
            ShelfArrow(g, r.Previous, ShelfControl.Previous, v, s, enabled: v.Scroll > 0.5f);
            ShelfArrow(g, r.Next, ShelfControl.Next, v, s, enabled: v.Scroll < end - 0.5f);
        }
        var (first, last) = r.Range(v.Scroll);
        var state = g.Save();
        try
        {
            // Lo que se desliza fuera del visor no se pinta encima de las flechas (1 px de margen para el antialias).
            g.SetClip(RectangleF.Inflate(r.Viewport, Math.Max(1f, s), 0f), CombineMode.Intersect);
            for (int i = first; i <= last; i++) ShelfCard(g, r, r.Card(i, v.Scroll), v, i, s);
        }
        finally { g.Restore(state); }
    }

    /// <summary>Contorno de «suelta aquí»: 1 px redondeado justo por fuera de la zona de contenido.</summary>
    private void ShelfDropFrame(Graphics g, RectangleF inner, float s)
    {
        float d = ShelfDropInset * s;
        var box = RectangleF.FromLTRB(inner.Left - 2f * d, inner.Top - d, inner.Right + 2f * d, inner.Bottom + d);
        using var path = RoundedRect(Rectangle.Round(box), 8f * s);
        SetPen(ShelfDropOutline, Math.Max(1f, s));
        g.DrawPath(_pen, path);
    }

    /// <summary>«Vaciar» / «Quitar (N)»: cápsula translúcida con hover y pulsación como los botones de la música.</summary>
    private void ShelfClearButton(Graphics g, RectangleF r, ShelfView v, float s)
    {
        bool pressed = v.Pressed.Control == ShelfControl.Clear;
        float inset = Math.Max(1f, MathF.Round(s));
        if (pressed) r = RectangleF.Inflate(r, -inset, -inset);
        var fill = ShelfButtonFill;
        if (v.Hover.Control == ShelfControl.Clear && v.HoverIntensity > 0f)
            fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f))), fill);
        if (pressed) fill = Darken(fill, MusicPressDarken);
        using (var path = RoundedRect(Rectangle.Round(r), r.Height / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        string text = v.Selected.Count > 0
            ? string.Format(Strings.Culture, Strings.NotchShelfRemoveFmt, v.Selected.Count)
            : Strings.NotchShelfClear;
        DrawText(g, text, _caption!, _t.Foreground, r, _center);
    }

    /// <summary>Una tarjeta: fondo de selección y/o hover (pulsada, 1 px menos por lado y un 6 % más oscura), miniatura o icono
    /// dibujado, nombre recortado y la línea gris: «No disponible», «Copiado» o el detalle (tamaño o tipo).</summary>
    private void ShelfCard(Graphics g, ShelfRects r, RectangleF card, ShelfView v, int i, float s)
    {
        var item = v.Items[i];
        bool available = i < v.Available.Count && v.Available[i];
        bool selected = v.Selected.Contains(item.Id);
        bool pressed = v.Pressed.Control == ShelfControl.Card && v.Pressed.Index == i;
        bool hover = v.Hover.Control == ShelfControl.Card && v.Hover.Index == i && v.HoverIntensity > 0f;

        int a = (selected ? ShelfSelectedFill.A : 0)
            + (hover ? (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f)) : 0);
        if (pressed) a = Math.Max(a, MusicHoverFill.A);
        if (a > 0)
        {
            float inset = pressed ? Math.Max(1f, MathF.Round(s)) : 0f;
            var bg = RectangleF.Inflate(card, -inset, -inset);
            var fill = Color.FromArgb(Math.Min(255, a), 255, 255, 255);
            if (pressed) fill = Darken(fill, MusicPressDarken);
            using var path = RoundedRect(Rectangle.Round(bg), ShelfLayout.CardRadius * s);
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }

        float dim = available ? 1f : ShelfMissingDim;
        var thumb = r.Thumb(card);
        if (v.Thumbs is { } thumbs && i < thumbs.Count && thumbs[i] is { } bmp) ShelfThumb(g, thumb, bmp, s, dim);
        else ShelfTile(g, thumb, ShelfThumbPolicy.GlyphFor(item), s, dim);

        var name = r.Name(card);
        DrawText(g, ShelfName(g, item, name.Width, s), _caption!, available ? _t.Foreground : _t.TextMuted, name, _center);
        bool copied = available && v.Copied == item.Id;
        string detail = !available ? Strings.NotchShelfMissing
            : copied ? Strings.NotchShelfCopied
            : v.Details is { } details && i < details.Count ? details[i]
            : ShelfCardText.Detail(item, null, Strings);
        DrawText(g, detail, _small!, copied ? _t.Foreground : _t.TextMuted, r.Detail(card), _center);
    }

    /// <summary>Nombre que cabe en <paramref name="width"/> px (<see cref="ShelfCardText.FitName"/>: los archivos conservan la
    /// extensión), medido una vez por nombre y escala.</summary>
    private string ShelfName(Graphics g, ShelfItem item, float width, float s)
    {
        if (Math.Abs(s - _shelfNamesScale) > 0.001f || _shelfNames.Count >= ShelfNamesMax)
        {
            _shelfNames.Clear();
            _shelfNamesScale = s;
        }
        var key = (item.Display, item.Kind == ShelfKind.File);
        if (_shelfNames.TryGetValue(key, out var fit)) return fit;
        // Un px de holgura: DrawString con «…» no debe recortar otra vez lo que la medida dio por justo.
        fit = ShelfCardText.FitName(item.Display, width - 1f, t => g.MeasureString(t, _caption!, int.MaxValue, _left).Width,
            keepExtension: key.Item2);
        _shelfNames[key] = fit;
        return fit;
    }

    /// <summary>Miniatura del sistema centrada en su hueco: a su tamaño (el shell ya la dio a la medida: sin reescalar, nítida)
    /// o encogida si viniera mayor, con las esquinas redondeadas. Atenuada si la entrada ya no está.</summary>
    private void ShelfThumb(Graphics g, RectangleF box, Bitmap bmp, float s, float dim)
    {
        float k = Math.Min(1f, Math.Min(box.Width / bmp.Width, box.Height / bmp.Height));
        int w = (int)MathF.Round(bmp.Width * k), h = (int)MathF.Round(bmp.Height * k);
        if (w <= 0 || h <= 0) return;
        var dst = new Rectangle((int)MathF.Round(box.Left + (box.Width - w) / 2f), (int)MathF.Round(box.Top + (box.Height - h) / 2f), w, h);
        var state = g.Save();
        try
        {
            using var path = RoundedRect(dst, Math.Min(ShelfLayout.ThumbRadius * s, Math.Min(w, h) / 2f));
            g.SetClip(path, CombineMode.Intersect);
            g.InterpolationMode = k < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float alpha = _alpha * dim;
            if (alpha >= 1f) g.DrawImage(bmp, dst);
            else
            {
                // Solo en los fundidos o con la entrada atenuada: fuera de ellos no se reserva nada.
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                g.DrawImage(bmp, dst, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
        }
        finally { g.Restore(state); }
    }

    /// <summary>Icono dibujado mientras no hay miniatura, o si no la habrá (enlaces, textos, accesos directos, un archivo que ya
    /// no está o que el shell no supo pintar): azulejo translúcido con el glifo del tipo.</summary>
    private void ShelfTile(Graphics g, RectangleF box, ShelfGlyph glyph, float s, float dim)
    {
        var tile = RectangleF.Inflate(box, -4f * s, -4f * s);
        using (var path = RoundedRect(Rectangle.Round(tile), ShelfLayout.ThumbRadius * s))
        {
            _brush.Color = Fade(Color.FromArgb((int)(ShelfTileFill.A * dim), ShelfTileFill));
            g.FillPath(_brush, path);
        }
        float gs = ShelfGlyphSize * s;
        var icon = new RectangleF(box.Left + (box.Width - gs) / 2f, box.Top + (box.Height - gs) / 2f, gs, gs);
        ShelfIcon(g, icon, glyph, Color.FromArgb((int)(255 * dim), _t.Foreground), s);
    }

    /// <summary>Flecha ‹ › de la fila: cápsula con hover y pulsación como los demás botones; sin nada más hacia ese lado,
    /// atenuada y sin realce.</summary>
    private void ShelfArrow(Graphics g, RectangleF r, ShelfControl which, ShelfView v, float s, bool enabled)
    {
        bool pressed = enabled && v.Pressed.Control == which;
        if (pressed) r = RectangleF.Inflate(r, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
        var fill = ShelfButtonFill;
        if (enabled && v.Hover.Control == which && v.HoverIntensity > 0f)
            fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * Math.Clamp(v.HoverIntensity, 0f, 1f))), fill);
        if (pressed) fill = Darken(fill, MusicPressDarken);
        using (var path = RoundedRect(Rectangle.Round(r), r.Width / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        // Chevron de dos trazos (el lápiz compartido tiene los extremos redondos).
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, hw = 2.5f * s, hh = 5f * s;
        float dir = which == ShelfControl.Previous ? -1f : 1f;
        SetPen(enabled ? _t.Foreground : Color.FromArgb((int)(255 * ShelfArrowDim), _t.Foreground), Math.Max(1f, 1.5f * s));
        g.DrawLine(_pen, cx - dir * hw, cy - hh, cx + dir * hw, cy);
        g.DrawLine(_pen, cx + dir * hw, cy, cx - dir * hw, cy + hh);
    }

    /// <summary>Glifo dibujado del tipo de entrada (sin fuentes de símbolos): hoja con la esquina doblada, carpeta,
    /// flecha de enlace o líneas de texto.</summary>
    private void ShelfIcon(Graphics g, RectangleF r, ShelfGlyph glyph, Color color, float s)
    {
        float w = Math.Max(1f, 1.5f * s);
        SetPen(color, w);
        float l = r.Left + w / 2f, t = r.Top + w / 2f, rt = r.Right - w / 2f, b = r.Bottom - w / 2f;
        float iw = rt - l, ih = b - t;
        switch (glyph)
        {
            case ShelfGlyph.Folder:
            {
                float tab = ih * 0.25f;
                g.DrawLine(_pen, l, t, l + iw * 0.38f, t);
                g.DrawLine(_pen, l + iw * 0.38f, t, l + iw * 0.5f, t + tab);
                g.DrawLine(_pen, l + iw * 0.5f, t + tab, rt, t + tab);
                g.DrawLine(_pen, rt, t + tab, rt, b);
                g.DrawLine(_pen, rt, b, l, b);
                g.DrawLine(_pen, l, b, l, t);
                break;
            }
            case ShelfGlyph.Link:
                g.DrawLine(_pen, l + s, b - s, rt, t);
                g.DrawLine(_pen, rt - iw * 0.45f, t, rt, t);
                g.DrawLine(_pen, rt, t, rt, t + ih * 0.45f);
                break;
            case ShelfGlyph.Text:
                for (int k = 0; k < 3; k++)
                {
                    float y = t + ih * (0.15f + 0.35f * k);
                    g.DrawLine(_pen, l, y, k == 2 ? l + iw * 0.6f : rt, y);
                }
                break;
            default:
            {
                float fold = iw * 0.35f, left = l + iw * 0.1f, right = rt - iw * 0.1f;
                g.DrawLine(_pen, left, t, right - fold, t);
                g.DrawLine(_pen, right - fold, t, right, t + fold);
                g.DrawLine(_pen, right, t + fold, right, b);
                g.DrawLine(_pen, right, b, left, b);
                g.DrawLine(_pen, left, b, left, t);
                break;
            }
        }
    }

    // ---------------------------------------------------------------- página Música (F5 · B3)

    // Botones de la música: blanco translúcido sobre el negro (como los del Permiso).
    private static readonly Color MusicHoverFill = Color.FromArgb(28, 255, 255, 255);
    private static readonly Color LyricsButtonFill = Color.FromArgb(16, 255, 255, 255);
    private static readonly Color LyricsButtonOn = Color.FromArgb(52, 255, 255, 255);
    private const float MusicPressDarken = 0.06f, MusicIcon = 12f, ArtRadius = 6f;

    /// <summary>Página Música: sin música, cabecera + «No suena nada»; con música, la vista normal y/o la dividida con la
    /// letra (cruce lineal entre las dos con <see cref="MusicView.SplitBlend"/>).</summary>
    private void PaintMusicPage(Graphics g, RectangleF inner, float s, MusicView? m)
    {
        if (m?.Track is not { } track)
        {
            PaintPlaceholderPage(g, inner, s, Strings.NotchPageMusic, Strings.NotchNothingPlaying);
            return;
        }
        float split = m.LyricsEnabled ? Math.Clamp(m.SplitBlend, 0f, 1f) : 0f;
        if (split < 1f) MusicLayer(g, inner, s, m, track, lyrics: false, 1f - split);
        if (split > 0f) MusicLayer(g, inner, s, m, track, lyrics: true, split);
    }

    private void MusicLayer(Graphics g, RectangleF inner, float s, MusicView m, NowPlaying track, bool lyrics, float alpha)
    {
        float saved = _alpha;
        _alpha = Quantize(saved * alpha);
        if (_alpha <= 0f) { _alpha = saved; return; }
        try
        {
            var r = MusicLayout.Compute(inner, s, lyrics);
            AlbumArtImage(g, r.Art, track, ArtRadius * s);
            var titleRect = r.Title;
            if (m.Equalizer is { } eq)
            {
                float w = EqualizerWidth(s), h = EqHeight * s;
                Equalizer(g, new RectangleF(titleRect.Right - w, titleRect.Top + (titleRect.Height - h) / 2f, w, h), eq, track.Accent, s);
                titleRect = RectangleF.FromLTRB(titleRect.Left, titleRect.Top, titleRect.Right - w - 8f * s, titleRect.Bottom);
            }
            DrawText(g, track.Title, _title!, _t.Foreground, titleRect, _left);
            DrawText(g, track.Artist, _caption!, _t.TextMuted, r.Artist, _left);

            MusicButton(g, r.Previous, MusicControl.Previous, m, s);
            MusicButton(g, r.PlayPause, MusicControl.PlayPause, m, s);
            MusicButton(g, r.Next, MusicControl.Next, m, s);
            if (m.LyricsEnabled) LyricsButton(g, r.LyricsButton, m, s, on: lyrics);

            // Barra: pista del tema y el relleno con el acento de la carátula; con el ratón encima, hasta 2 px más gruesa.
            var bar = r.Bar;
            if (m.Hover == MusicControl.Progress && m.HoverIntensity > 0f)
            {
                float grow = s * Math.Clamp(m.HoverIntensity, 0f, 1f);
                bar = RectangleF.FromLTRB(bar.Left, bar.Top - grow, bar.Right, bar.Bottom + grow);
            }
            _brush.Color = Fade(_t.Track);
            g.FillRectangle(_brush, bar);
            if (track.Duration > TimeSpan.Zero)
            {
                float k = (float)Math.Clamp(m.Position.TotalMilliseconds / track.Duration.TotalMilliseconds, 0.0, 1.0);
                if (k > 0f)
                {
                    _brush.Color = Fade(track.Accent);
                    g.FillRectangle(_brush, bar.Left, bar.Top, bar.Width * k, bar.Height);
                }
            }
            DrawText(g, MusicFormat.Time(m.Position), _caption!, _t.TextMuted, r.Elapsed, _left);
            if (track.Duration > TimeSpan.Zero)
                DrawText(g, MusicFormat.Time(track.Duration), _caption!, _t.TextMuted, r.Total, _right);

            if (lyrics) LyricsColumn(g, r, m, s);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Anterior / reproducir-pausa / siguiente: icono dibujado (sin fuentes de símbolos); hover = círculo
    /// translúcido con su intensidad animada; pulsado = encoge 1 px por lado y se oscurece un 6 % (§3.4). En reposo son
    /// glifos macizos; mientras anima, el central se transforma entre barras y triángulo (<see cref="MusicView.PlayMorph"/>)
    /// y el triángulo de siguiente/anterior se desliza recortado (<see cref="MusicView.NextSkip"/>,
    /// <see cref="MusicView.PreviousSkip"/>).</summary>
    private void MusicButton(Graphics g, RectangleF r, MusicControl which, MusicView m, float s)
    {
        if (m.Hover == which && m.HoverIntensity > 0f)
        {
            _brush.Color = Fade(Color.FromArgb((int)(MusicHoverFill.A * Math.Clamp(m.HoverIntensity, 0f, 1f)), MusicHoverFill));
            g.FillEllipse(_brush, r);
        }
        bool pressed = m.Pressed == which;
        float icon = MusicIcon * s - (pressed ? 2f * Math.Max(1f, MathF.Round(s)) : 0f);
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, h = icon / 2f;
        var color = Fade(pressed ? Darken(_t.Foreground, MusicPressDarken) : _t.Foreground);
        _brush.Color = color;
        // Estilo glifo iOS (pedido del 29-oct): formas macizas y gruesas con las esquinas suavizadas.
        float round = Math.Max(1f, icon * 0.09f);                 // radio de las puntas de los triángulos
        switch (which)
        {
            case MusicControl.PlayPause when m.PlayMorph is { } morph:
                PlayPauseMorph(g, color, icon, cx, cy, round, Math.Clamp(morph, 0f, 1f));
                break;
            case MusicControl.PlayPause when m.Track?.Playing == true:
            {
                float w = icon * 0.32f, gap = icon * 0.2f, left = cx - (w + gap / 2f);
                FillRoundRect(g, new RectangleF(left, cy - h, w, icon), w * 0.3f);
                FillRoundRect(g, new RectangleF(cx + gap / 2f, cy - h, w, icon), w * 0.3f);
                break;
            }
            case MusicControl.PlayPause:
                RoundTriangle(g, color, round, cx - h * 0.62f, cy - h, cx - h * 0.62f, cy + h, cx + h * 0.9f, cy);
                break;
            case MusicControl.Previous:
            {
                float bar = icon * 0.16f, tri = icon * 0.68f, x0 = cx - h;
                // Deslizamiento: hacia la izquierda hasta meterse en la barra; vuelve a entrar por el borde derecho. El
                // recorte llega a media barra y la barra se pinta encima: el triángulo se funde con ella, sin rendija.
                float u = Math.Clamp(m.PreviousSkip, -1f, 1f);
                if (u < 1f)
                {
                    float dx = u > 0f ? -u * (icon - bar) : -u * (tri + 1f);
                    var state = g.Save();
                    if (u != 0f)
                        g.SetClip(RectangleF.FromLTRB(x0 + bar / 2f, cy - icon, x0 + icon + 0.5f, cy + icon), CombineMode.Intersect);
                    RoundTriangle(g, color, round, x0 + icon + dx, cy - h, x0 + icon + dx, cy + h, x0 + icon - tri + dx, cy);
                    g.Restore(state);
                }
                FillRoundRect(g, new RectangleF(x0, cy - h, bar, icon), bar * 0.35f);
                break;
            }
            case MusicControl.Next:
            {
                float bar = icon * 0.16f, tri = icon * 0.68f, x0 = cx - h;
                // Deslizamiento: hacia la derecha hasta meterse en la barra; vuelve a entrar por el borde izquierdo
                // (recorte hasta media barra, que se pinta encima).
                float u = Math.Clamp(m.NextSkip, -1f, 1f);
                if (u < 1f)
                {
                    float dx = u > 0f ? u * (icon - bar) : u * (tri + 1f);
                    var state = g.Save();
                    if (u != 0f)
                        g.SetClip(RectangleF.FromLTRB(x0 - 0.5f, cy - icon, x0 + icon - bar / 2f, cy + icon), CombineMode.Intersect);
                    RoundTriangle(g, color, round, x0 + dx, cy - h, x0 + dx, cy + h, x0 + tri + dx, cy);
                    g.Restore(state);
                }
                FillRoundRect(g, new RectangleF(x0 + icon - bar, cy - h, bar, icon), bar * 0.35f);
                break;
            }
        }
    }

    /// <summary>
    /// Botón central a media transición: cada barra de pausa es un cuadrilátero que se interpola, vértice a vértice, hacia
    /// su mitad del triángulo (la izquierda hacia la mitad de la base; la derecha hacia la punta, que acaba con dos vértices
    /// juntos). Las esquinas exteriores van redondeadas; las de la costura entre mitades pierden el redondeo según se
    /// juntan y al final se solapan, así el triángulo sale entero y sin raya.
    /// </summary>
    private void PlayPauseMorph(Graphics g, Color color, float icon, float cx, float cy, float round, float t)
    {
        float h = icon / 2f;
        // Barras (las del reposo «sonando»).
        float w = icon * 0.32f, gap = icon * 0.2f, l0 = cx - (w + gap / 2f), r0 = cx + gap / 2f;
        // Triángulo (el del reposo «en pausa») partido por su mitad horizontal.
        float tx0 = cx - h * 0.62f, tx1 = cx + h * 0.9f, xm = (tx0 + tx1) / 2f;
        float half = h * (tx1 - xm) / (tx1 - tx0);                 // medio alto del triángulo en la costura

        PointF L(float ax, float ay, float bx, float by) => new(ax + (bx - ax) * t, ay + (by - ay) * t);
        float radius = w * 0.3f + (round - w * 0.3f) * t;
        float seam = radius * (1f - t);                           // costura: redondeada como barra, plana como triángulo

        _quad[0] = L(l0, cy - h, tx0, cy - h);
        _quad[1] = L(l0 + w, cy - h, xm, cy - half);
        _quad[2] = L(l0 + w, cy + h, xm, cy + half);
        _quad[3] = L(l0, cy + h, tx0, cy + h);
        _insets[0] = radius; _insets[1] = seam; _insets[2] = radius; _insets[3] = radius;
        RoundConvex(g, color, radius, _quad, _insets);

        _quad[0] = L(r0, cy - h, xm, cy - half);
        _quad[1] = L(r0 + w, cy - h, tx1, cy);
        _quad[2] = L(r0 + w, cy + h, tx1, cy);
        _quad[3] = L(r0, cy + h, xm, cy + half);
        _insets[0] = radius; _insets[1] = radius; _insets[2] = radius; _insets[3] = seam;
        RoundConvex(g, color, radius, _quad, _insets);
    }

    /// <summary>
    /// Polígono convexo macizo con las esquinas redondeadas: se mete hacia dentro cada lado según
    /// <paramref name="insets"/> (lado i = del vértice i al i+1) y se repasa el contorno con un trazo de unión redonda de
    /// radio <paramref name="radius"/>. Un lado metido el radio queda en su sitio con las esquinas redondeadas; uno sin
    /// meter sobresale el radio (sirve para solapar dos piezas). Se recorta por semiplanos, así un lado casi nulo (la punta
    /// que se cierra) desaparece sin dar la vuelta al polígono.
    /// </summary>
    private void RoundConvex(Graphics g, Color color, float radius, PointF[] pts, float[] insets)
    {
        int n = pts.Length;
        float area = 0f;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            area += a.X * b.Y - b.X * a.Y;
        }
        float sign = area >= 0f ? 1f : -1f;
        _clipA.Clear();
        _clipA.AddRange(pts);
        for (int i = 0; i < n && _clipA.Count >= 3; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % n];
            float ex = b.X - a.X, ey = b.Y - a.Y, len = MathF.Sqrt(ex * ex + ey * ey);
            if (len < 1e-3f) continue;
            float nx = -ey / len * sign, ny = ex / len * sign;       // normal hacia dentro
            float c = nx * a.X + ny * a.Y + insets[i];               // dentro: nx·x + ny·y ≥ c
            _clipB.Clear();
            for (int j = 0; j < _clipA.Count; j++)
            {
                var p = _clipA[j]; var q = _clipA[(j + 1) % _clipA.Count];
                float dp = nx * p.X + ny * p.Y - c, dq = nx * q.X + ny * q.Y - c;
                if (dp >= 0f) _clipB.Add(p);
                if ((dp >= 0f) != (dq >= 0f))
                {
                    float k = dp / (dp - dq);
                    _clipB.Add(new PointF(p.X + (q.X - p.X) * k, p.Y + (q.Y - p.Y) * k));
                }
            }
            (_clipA, _clipB) = (_clipB, _clipA);
        }
        if (_clipA.Count < 3) { g.FillPolygon(_brush, pts); return; }   // demasiado pequeño para redondear
        var inner = Polygon(_clipA);
        g.FillPolygon(_brush, inner);
        if (radius <= 0.05f) return;
        g.DrawPolygon(GlyphPen(color, radius), inner);
    }

    /// <summary>Copia <paramref name="pts"/> en un array reutilizado de su mismo tamaño (FillPolygon no admite longitud).
    /// El recorte de un cuadrilátero por cuatro semiplanos no pasa de 8 vértices; por si acaso, más allá se crea uno.</summary>
    private PointF[] Polygon(List<PointF> pts)
    {
        int n = pts.Count;
        var arr = n < _polys.Length ? _polys[n] ??= new PointF[n] : new PointF[n];
        pts.CopyTo(arr);
        return arr;
    }

    /// <summary>El trazo de repaso con el color y el radio de este glifo.</summary>
    private Pen GlyphPen(Color color, float radius)
    {
        _glyphPen.Color = color;
        _glyphPen.Width = radius * 2f;
        return _glyphPen;
    }

    /// <summary>Rectángulo macizo con esquinas redondeadas (coordenadas con decimales).</summary>
    private void FillRoundRect(Graphics g, RectangleF r, float radius)
    {
        float d = Math.Min(Math.Min(r.Width, r.Height), radius * 2f);
        if (d < 0.5f) { g.FillRectangle(_brush, r); return; }
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(_brush, path);
    }

    /// <summary>Triángulo macizo con las puntas redondeadas: se rellena un triángulo algo menor y se repasa su contorno
    /// con un trazo de unión redonda, que redondea las esquinas sin cambiar el tamaño total.</summary>
    private void RoundTriangle(Graphics g, Color color, float radius, float x1, float y1, float x2, float y2, float x3, float y3)
    {
        float cxT = (x1 + x2 + x3) / 3f, cyT = (y1 + y2 + y3) / 3f;
        PointF In(float x, float y)
        {
            float dx = x - cxT, dy = y - cyT, len = MathF.Sqrt(dx * dx + dy * dy);
            float k = len > radius * 2f ? (len - radius * 1.6f) / len : 0.5f;
            return new PointF(cxT + dx * k, cyT + dy * k);
        }
        _tri[0] = In(x1, y1); _tri[1] = In(x2, y2); _tri[2] = In(x3, y3);
        g.FillPolygon(_brush, _tri);
        g.DrawPolygon(GlyphPen(color, radius), _tri);
    }

    /// <summary>Botón «Letra»: cápsula translúcida, más clara con la vista de letra abierta; hover y pulsación como los
    /// demás botones. Dentro, el icono de la letra (bocadillo con comillas, <see cref="LyricsIcon"/>); pulsado encoge con la
    /// cápsula. Sin letra para la pista, el icono va atenuado. Si el icono no se pudiera leer, el texto «Letra» de siempre.</summary>
    private void LyricsButton(Graphics g, RectangleF r, MusicView m, float s, bool on)
    {
        bool pressed = m.Pressed == MusicControl.Lyrics;
        float inset = Math.Max(1f, MathF.Round(s));
        if (pressed) r = RectangleF.Inflate(r, -inset, -inset);
        var fill = on ? LyricsButtonOn : LyricsButtonFill;
        if (m.Hover == MusicControl.Lyrics && m.HoverIntensity > 0f)
            fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * Math.Clamp(m.HoverIntensity, 0f, 1f))), fill);
        if (pressed) fill = Darken(fill, MusicPressDarken);
        using (var path = RoundedRect(Rectangle.Round(r), r.Height / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        bool none = m.LyricsState == LyricsState.NotFound;
        var color = none && !on ? _t.TextMuted : _t.Foreground;
        float size = MathF.Round(LyricsIconSize * s - (pressed ? 2f * inset : 0f));
        var icon = LyricsIconPath(size);
        if (icon is null)
        {
            DrawText(g, Strings.NotchLyrics, _caption!, color, r, _center);
            return;
        }
        // El trazado está hecho en (0,0)–(size,size): se lleva al centro de la cápsula, en px enteros (bordes nítidos).
        float x = MathF.Round(r.Left + (r.Width - size) / 2f), y = MathF.Round(r.Top + (r.Height - size) / 2f);
        var state = g.Save();
        try
        {
            g.TranslateTransform(x, y);
            _brush.Color = Fade(color);
            g.FillPath(_brush, icon);
        }
        finally { g.Restore(state); }
    }

    /// <summary>Lado (px lógicos) del icono de la letra dentro de su cápsula de 22 de alto.</summary>
    private const float LyricsIconSize = 16f;

    // Trazado del icono a su tamaño en px (cacheado: se rehace solo si cambia el tamaño, no en cada frame).
    private GraphicsPath? _lyricsIcon;
    private float _lyricsIconSize = -1f;

    /// <summary>El icono de la letra a <paramref name="size"/> px, en (0,0); <c>null</c> si no hay icono.</summary>
    private GraphicsPath? LyricsIconPath(float size)
    {
        if (size < 1f || LyricsIcon.Segments is not { } segs) return null;
        if (_lyricsIcon is not null && Math.Abs(size - _lyricsIconSize) < 0.01f) return _lyricsIcon;
        _lyricsIcon?.Dispose();
        _lyricsIcon = SvgPath.ToGraphicsPath(segs, LyricsIcon.ViewBox, new RectangleF(0f, 0f, size, size));
        _lyricsIconSize = size;
        return _lyricsIcon;
    }

    /// <summary>Columna de la letra: anterior (atenuada), actual (resaltada) y siguiente (atenuada), con el cruce de 120 ms
    /// al cambiar de línea; o el estado (buscando / sin letra / no disponible). La letra plana (letras v2) va igual, con
    /// sus tiempos estimados y un realce más suave (sin negrita ni hover: sus líneas no llevan a ningún instante). Debajo,
    /// en la fila de los tiempos, los botones de la letra y el aviso pequeño.</summary>
    private void LyricsColumn(Graphics g, MusicRects r, MusicView m, float s)
    {
        LyricsToolsRow(g, r, m, s);
        bool plain = m.LyricsState == LyricsState.Plain;
        if (m.LyricsState == LyricsState.Loading)
        {
            LyricsSearching(g, r, m, s);
            return;
        }
        if (m.LyricsState is not (LyricsState.Found or LyricsState.Plain) || m.Lines is not { Count: > 0 } lines)
        {
            string text = m.LyricsState switch
            {
                LyricsState.NotFound or LyricsState.Plain => Strings.NotchNoLyrics,
                LyricsState.Error => Strings.NotchLyricsUnavailable,
                _ => Strings.NotchLyricsLoading,
            };
            DrawText(g, text, _body!, _t.TextMuted, r.LyricLine(0), _left);
            return;
        }

        if (!plain && m.Hover == MusicControl.LyricLine && m.HoverIntensity > 0f && LineAt(lines, m.Line + m.HoverLine) is not null)
        {
            var row = r.LyricLine(m.HoverLine);
            var hr = RectangleF.FromLTRB(row.Left - 4f * s, row.Top, row.Right, row.Bottom);
            using var path = RoundedRect(Rectangle.Round(hr), 6f * s);
            _brush.Color = Fade(Color.FromArgb((int)(MusicHoverFill.A * Math.Clamp(m.HoverIntensity, 0f, 1f)), MusicHoverFill));
            g.FillPath(_brush, path);
        }

        float blend = Math.Clamp(m.LineBlend, 0f, 1f);
        if (blend < 1f && m.PreviousLine != m.Line) LyricLines(g, r, lines, m.PreviousLine, 1f - blend, plain);
        LyricLines(g, r, lines, m.Line, blend, plain);
    }

    // Buscando la letra: búfer de partículas y fuentes de sus tres tamaños, reutilizados (nada nuevo por fotograma).
    private readonly LyricsParticle[] _particles = new LyricsParticle[LyricsSearchParticles.Count];
    private readonly Font?[] _searchFonts = new Font?[3];
    private float _searchFontScale = -1f;
    private const float SearchAreaWidth = 140f;               // ancho (px lógicos) por el que suben las letras

    /// <summary>
    /// Buscando la letra: «Buscando la letra…» pequeño y atenuado en la fila de abajo de la columna y, por encima, letras
    /// sueltas que suben y se desvanecen (<see cref="LyricsSearchParticles"/>), en el ancho del texto aproximadamente. Las
    /// letras nacen justo encima del texto (no lo tapan) y se apagan antes del borde de arriba. Con reduce-motion, fotograma fijo.
    /// </summary>
    private void LyricsSearching(Graphics g, MusicRects r, MusicView m, float s)
    {
        var textRow = r.LyricLine(1);
        DrawText(g, Strings.NotchLyricsLoading, _caption!, _t.TextMuted, textRow, _left);

        var col = r.LyricsColumn;
        float w = Math.Min(col.Width, SearchAreaWidth * s);
        float top = col.Top, bottom = textRow.Top + 3f * s;
        if (w <= 4f || bottom - top <= 4f) return;
        LyricsSearchParticles.Fill(_particles, m.SearchSeconds, w, bottom - top, still: m.ReduceMotion);
        EnsureSearchFonts(s);
        var fg = _t.Foreground;
        foreach (var p in _particles)
        {
            if (p.Alpha <= 0.02f) continue;
            var font = _searchFonts[p.Scale < 0.9f ? 0 : p.Scale < 1.05f ? 1 : 2]!;
            _brush.Color = Fade(Color.FromArgb((int)(fg.A * p.Alpha), fg));
            g.DrawString(p.Glyph, font, _brush, col.Left + p.X, top + p.Y, _center);
        }
    }

    /// <summary>Fuentes de las letras que suben (pequeña / media / grande, en negrita): se rehacen solo si cambia la escala.</summary>
    private void EnsureSearchFonts(float scale)
    {
        if (Math.Abs(scale - _searchFontScale) < 0.001f && _searchFonts[0] is not null) return;
        DisposeSearchFonts();
        string family = Typography.Body.FontFamily.Name;
        _searchFonts[0] = new Font(family, 9f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _searchFonts[1] = new Font(family, 11f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _searchFonts[2] = new Font(family, 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _searchFontScale = scale;
    }

    private void DisposeSearchFonts()
    {
        for (int i = 0; i < _searchFonts.Length; i++)
        {
            _searchFonts[i]?.Dispose();
            _searchFonts[i] = null;
        }
    }

    private void LyricLines(Graphics g, MusicRects r, IReadOnlyList<LyricLine> lines, int current, float alpha, bool plain)
    {
        float saved = _alpha;
        _alpha = Quantize(saved * alpha);
        if (_alpha <= 0f) { _alpha = saved; return; }
        try
        {
            for (int off = -1; off <= 1; off++)
            {
                // Antes de la primera línea, la actual es la pausa «♪» y la siguiente, la primera.
                string? text = off == 0 && current < 0 ? LrcParser.PauseText : LineAt(lines, current + off);
                if (text is null) continue;
                bool now = off == 0;
                // Plana: la actual es una estimación ⇒ realce suave (cuerpo normal, sin negrita).
                var font = now ? (plain ? _body! : _title!) : _caption!;
                DrawText(g, text, font, now ? _t.Foreground : _t.TextMuted, r.LyricLine(off), _left);
            }
        }
        finally { _alpha = saved; }
    }

    private static string? LineAt(IReadOnlyList<LyricLine> lines, int i) => i >= 0 && i < lines.Count ? lines[i].Text : null;

    /// <summary>Fila de los tiempos en la vista de letra (letras v2): −0,5 s / +0,5 s (solo con letra sincronizada),
    /// «Otra letra» con la fuente y, a su izquierda, el aviso pequeño atenuado. Los textos llegan hechos en la vista.</summary>
    private void LyricsToolsRow(Graphics g, MusicRects r, MusicView m, float s)
    {
        bool nudge = (m.Tools & LyricsTools.Nudge) != 0;
        if (nudge)
        {
            ToolButton(g, r.NudgeBack, MusicControl.NudgeBack, Strings.NotchLyricsEarlier, m, s, dim: false);
            ToolButton(g, r.NudgeForward, MusicControl.NudgeForward, Strings.NotchLyricsLater, m, s, dim: false);
        }
        if ((m.Tools & LyricsTools.Other) != 0)
            ToolButton(g, r.OtherLyrics, MusicControl.OtherLyrics, m.SourceLabel ?? Strings.NotchLyricsOther, m, s, m.SourceBusy);
        if (m.Notice is { Length: > 0 } notice)
            DrawText(g, notice, _caption!, _t.TextMuted, r.LyricsNotice(nudge), _right);
    }

    /// <summary>Botón pequeño de la vista de letra: la cápsula de «Letra» (hover y pulsación iguales) a la altura de la
    /// fila de los tiempos. <paramref name="dim"/>: texto atenuado (pidiendo candidatos o sin otra letra).</summary>
    private void ToolButton(Graphics g, RectangleF r, MusicControl which, string label, MusicView m, float s, bool dim)
    {
        if (r.IsEmpty) return;
        bool pressed = m.Pressed == which;
        float inset = Math.Max(1f, MathF.Round(s));
        if (pressed) r = RectangleF.Inflate(r, -inset, -inset);
        var fill = LyricsButtonFill;
        if (m.Hover == which && m.HoverIntensity > 0f)
            fill = Color.FromArgb(Math.Min(255, fill.A + (int)(MusicHoverFill.A * Math.Clamp(m.HoverIntensity, 0f, 1f))), fill);
        if (pressed) fill = Darken(fill, MusicPressDarken);
        using (var path = RoundedRect(Rectangle.Round(r), r.Height / 2f))
        {
            _brush.Color = Fade(fill);
            g.FillPath(_brush, path);
        }
        DrawText(g, label, _caption!, dim ? _t.TextMuted : _t.Foreground, r, _center);
    }

    /// <summary>Carátula con esquinas redondeadas; sin imagen, un hueco con el acento y una nota.</summary>
    private void AlbumArtImage(Graphics g, RectangleF r, NowPlaying track, float radius)
    {
        var bmp = ArtBitmap(track.ArtPng);
        var dst = Rectangle.Round(r);
        using var path = RoundedRect(dst, radius);
        if (bmp is null)
        {
            _brush.Color = Fade(Color.FromArgb(48, track.Accent));
            g.FillPath(_brush, path);
            DrawText(g, LrcParser.PauseText, r.Height >= 32f ? _title! : _caption!, track.Accent, r, _center);
            return;
        }
        var state = g.Save();
        try
        {
            g.SetClip(path, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (_alpha >= 1f) g.DrawImage(bmp, dst);
            else
            {
                // Solo en los cruces (alfa < 1): fuera de ellos no se reserva nada.
                using var attrs = new ImageAttributes();
                attrs.SetColorMatrix(new ColorMatrix { Matrix33 = _alpha });
                g.DrawImage(bmp, dst, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attrs);
            }
        }
        finally { g.Restore(state); }
    }

    /// <summary>La carátula decodificada; se decodifica de nuevo solo si llega otra imagen. PNG ilegible → sin imagen.</summary>
    private Bitmap? ArtBitmap(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        if (ReferenceEquals(png, _artSource)) return _art;
        _art?.Dispose();
        _art = null;
        _artSource = png;
        try
        {
            using var ms = new MemoryStream(png, writable: false);
            using var img = Image.FromStream(ms);
            _art = new Bitmap(img);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            AppLog.Warn($"notch: carátula ilegible: {ex.Message}");
        }
        return _art;
    }

    /// <summary>
    /// Puntos de página (6 px, atenuados) y la píldora indicadora de 16 × 3 que se desliza entre ellos (§3.9 #5). Cada
    /// punto se desvanece según se le acerca la píldora, así no asoma por encima y por debajo de ella.
    /// </summary>
    private void PageDots(Graphics g, RectangleF pill, float s, NotchPagesView pv)
    {
        int n = pv.Pages.Count;
        float d = NotchGeometry.PageDotDiameter * s;
        for (int i = 0; i < n; i++)
        {
            float away = Math.Clamp(Math.Abs(i - pv.IndicatorPos), 0f, 1f);
            if (away <= 0f) continue;
            var c = NotchGeometry.PageDotCenter(pill, s, n, i);
            _brush.Color = Fade(Color.FromArgb((int)(255 * PageDotAlpha * away), _t.Foreground));
            g.FillEllipse(_brush, c.X - d / 2f, c.Y - d / 2f, d, d);
        }
        var ic = NotchGeometry.PageDotCenter(pill, s, n, pv.IndicatorPos);
        float w = NotchGeometry.PageIndicatorWidth * s, h = Math.Max(2f, NotchGeometry.PageIndicatorHeight * s);
        // Línea con extremos redondos (el lápiz compartido los tiene): una cápsula sin GraphicsPath por frame.
        SetPen(_t.Foreground, h);
        g.DrawLine(_pen, ic.X - (w - h) / 2f, ic.Y, ic.X + (w - h) / 2f, ic.Y);
        if (pv.Arrows is { } arrows)
        {
            PageArrowGlyph(g, pill, s, n, PageArrow.Previous, arrows.Previous);
            PageArrowGlyph(g, pill, s, n, PageArrow.Next, arrows.Next);
        }
    }

    /// <summary>
    /// Flecha ‹ o › de página: un chevron con el estilo de los puntos (tenue, <see cref="PageDotAlpha"/>) que pasa al acento
    /// con el ratón encima. Tamaño, desplazamiento y alfa llegan ya animados (<see cref="PageArrows"/>); dos trazos con el
    /// lápiz compartido (extremos redondos), sin reservas.
    /// </summary>
    private void PageArrowGlyph(Graphics g, RectangleF pill, float s, int n, PageArrow side, PageArrowLook look)
    {
        if (look.Alpha <= 0.004f) return;
        var c = NotchGeometry.PageArrowCenter(pill, s, n, side);
        float dir = (int)side, k = s * look.Scale;
        float tipX = c.X + dir * (look.Offset * s + NotchGeometry.PageArrowHalfWidth * k);
        float backX = tipX - dir * 2f * NotchGeometry.PageArrowHalfWidth * k, hh = NotchGeometry.PageArrowHalfHeight * k;
        float alpha = (PageDotAlpha + (1f - PageDotAlpha) * look.Hover) * look.Alpha;
        var color = MixColor(_t.Foreground, _t.Accent, look.Hover);
        SetPen(Color.FromArgb(Math.Clamp((int)(255 * alpha), 0, 255), color), Math.Max(1.25f, NotchGeometry.PageArrowStroke * s));
        g.DrawLine(_pen, backX, c.Y - hh, tipX, c.Y);
        g.DrawLine(_pen, tipX, c.Y, backX, c.Y + hh);
    }

    private static Color MixColor(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(255, (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Alfa de los puntos de las páginas no visibles.</summary>
    private const float PageDotAlpha = 0.35f;

    private void PaintAlert(Graphics g, RectangleF pill, float s, NotchFrame f, NotchAlert a)
    {
        bool readOnly = f.ReadOnly && NotchAlertRouting.HasButtons(a);
        if (!readOnly && a.Kind == NotchAlertKind.Permission && a.RequestId is not null)
        {
            PaintPermissionActions(g, pill, s, f, a);
            return;
        }
        if (!readOnly && a.Kind == NotchAlertKind.Reminder)
        {
            PaintReminderActions(g, pill, s, f, a);
            return;
        }
        if (!readOnly && a.Kind == NotchAlertKind.Focus)
        {
            PaintFocusAlert(g, pill, s, f, a);
            return;
        }
        var (title, detail, color) = AlertLines(a, f);
        bool quota = a.Kind is NotchAlertKind.QuotaHud or NotchAlertKind.QuotaAlert;
        float x = pill.Left + PadX * s, icon = (quota ? NotchMotion.RingDiameter : 18f) * s;
        float right = pill.Right - PadX * s;
        double? pct = quota ? WindowFor(a, f.Quota)?.UtilizationPct : null;

        if (a.Kind == NotchAlertKind.QuotaHud)
        {
            // HUD de una línea: anillo + «Cuota 5h al 80 %» + mini barra. Informa, no reclama.
            float cy = pill.Top + pill.Height / 2f;
            Ring(g, x, cy, icon, pct);
            float barW = 60f * s;
            Bar(g, new RectangleF(right - barW, cy - 2f * s, barW, 4f * s), pct);
            x += icon + Gap * s;
            DrawText(g, title, _body!, _t.Foreground,
                new RectangleF(x, pill.Top, Math.Max(0f, right - barW - Gap * s - x), pill.Height), _left);
            return;
        }

        // Dos o tres líneas: icono (anillo en la cuota) a la izquierda, título y detalle (y la pista en Permiso o en solo lectura).
        bool hint = a.Kind == NotchAlertKind.Permission || readOnly;
        float lineH = (hint ? 16f : 18f) * s;
        int lines = 1 + (detail is null ? 0 : 1) + (hint ? 1 : 0);
        float blockTop = pill.Top + (pill.Height - lines * lineH) / 2f;
        if (quota) Ring(g, x, blockTop + lineH / 2f, icon, pct);
        else AlertIcon(g, a.Kind, x, blockTop + lineH / 2f, icon, color);
        x += icon + Gap * s;
        var col = new RectangleF(x, blockTop, Math.Max(0f, right - x), lineH);
        DrawText(g, title, _title!, color, col, _left);
        if (detail is not null)
        {
            col.Y += lineH;
            DrawText(g, detail, _caption!, _t.Foreground, col, _left);
        }
        if (hint)
        {
            col.Y += lineH;
            var (text, hintColor) = readOnly ? ReadOnlyHint(f.PermissionAnswer) : (Strings.NotchAnswerInTerminal, _t.TextMuted);
            DrawText(g, text, _caption!, hintColor, col, _left);
        }
    }

    /// <summary>Pista del aviso en solo lectura: la respuesta que se dio en el principal efectivo o, si aún no, que un clic aquí
    /// trae los botones.</summary>
    private (string Text, Color Color) ReadOnlyHint(PermissionVerdict answer) => answer switch
    {
        PermissionVerdict.Allow => (Strings.NotchAllowed, _t.Ok),
        PermissionVerdict.Deny => (Strings.NotchDenied, _t.WarnText),
        _ => (Strings.NotchMirrorClickHint, _t.TextMuted),
    };

    /// <summary>
    /// Permiso con botones: icono, «Espera tu OK · proyecto» y debajo «herramienta: comando» ENTERO, con ajuste de línea
    /// (hasta 4 líneas, saltos de línea incluidos; nunca «…»); Denegar y Permitir abajo a la derecha (la MISMA geometría
    /// que usa <c>NotchForm</c> para el clic). Deja en <see cref="LastPermissionPaint"/> si el detalle se pintó entero.
    /// </summary>
    private void PaintPermissionActions(Graphics g, RectangleF pill, float s, NotchFrame f, NotchAlert a)
    {
        var (title, detail, color) = AlertLines(a, f);
        var (deny, allow) = NotchGeometry.PermissionButtons(pill, s);
        float x = pill.Left + PadX * s, right = pill.Right - PadX * s, icon = AlertIconSize * s, titleH = 18f * s;
        float lineH = NotchLayout.PermissionDetailLine * s;
        int lines = detail is null ? 0 : PermissionDetailLines(detail, s);
        int reserved = detail is null ? 0 : lines > 0 ? lines : NotchLayout.PermissionDetailMaxLines;
        float areaBottom = Math.Min(pill.Bottom, deny.IsEmpty ? pill.Bottom : deny.Top - 2f * s);
        float blockTop = pill.Top + Math.Max(0f, (areaBottom - pill.Top - titleH - reserved * lineH) / 2f);
        AlertIcon(g, a.Kind, x, blockTop + titleH / 2f, icon, color);
        x += icon + Gap * s;
        float colW = Math.Max(0f, right - x);
        DrawText(g, title, _title!, color, new RectangleF(x, blockTop, colW, titleH), _left);

        bool complete = detail is null;
        if (detail is not null)
        {
            // El detalle va en lo que queda hasta los botones: LineLimit no pinta una línea que no cabe entera, y la
            // medida sobre ESTE rectángulo (ancho y alto de verdad, no los finales) dice si se ve todo.
            float top = blockTop + titleH;
            var r = new RectangleF(x, top, colW, Math.Max(0f, areaBottom - top));
            complete = MeasureDetail(g, detail, r.Size).Fits;
            if (r.Width > 1f && r.Height > 1f)
            {
                _brush.Color = Fade(_t.Foreground);
                g.DrawString(detail, _caption!, _brush, r, _wrap);
            }
        }
        // Completo = todo el detalle a la vista, botones dibujados y contenido y ventana ya opacos (ni en pleno fundido ni
        // atenuada por un arrastre de Snap).
        LastPermissionPaint = new PermissionPaint(a.RequestId!, a.Title, a.Detail,
            complete && !deny.IsEmpty && _alpha >= PaintedAlphaMin && f.PillAlpha >= PaintedAlphaMin);
        if (deny.IsEmpty) return;   // la forma aún se está abriendo
        PermissionButton(g, deny, allow: false, s, f);
        PermissionButton(g, allow, allow: true, s, f);
    }

    /// <summary>Alfa de contenido mínimo para dar por «visto» el Permiso (el fundido de entrada ya casi acabó).</summary>
    private const float PaintedAlphaMin = 0.95f;

    /// <summary>Botón del Permiso (y, con <paramref name="label"/>, del recordatorio: «Hecha» / «+10 min»).</summary>
    private void PermissionButton(Graphics g, Rectangle r, bool allow, float s, NotchFrame f, string? label = null)
    {
        var mine = allow ? PermissionVerdict.Allow : PermissionVerdict.Deny;
        bool answered = f.PermissionAnswer != PermissionVerdict.None;
        bool pressed = f.PermissionAnswer == mine;
        bool armed = answered || f.PermissionArm >= 1f;
        // Pulsación (spec de movimiento §3.4): encoge 1 px por lado y se oscurece un 6 %, al instante; la acción va al soltar.
        bool held = !answered && armed && f.PermissionHeld == mine;
        if (held)
        {
            int inset = Math.Max(1, (int)Math.Round(s));
            r = Rectangle.Inflate(r, -inset, -inset);
        }
        float saved = _alpha;
        if (answered && !pressed) _alpha = Quantize(_alpha * ButtonDimOther);
        try
        {
            var fill = allow ? ButtonAllow : ButtonDeny;
            if (held) fill = Darken(fill, ButtonPressDarken);
            using (var path = RoundedRect(r, ButtonRadius * s))
            {
                // Cuenta atrás visible: el fondo normal entra de izquierda a derecha sobre la pista (con reduce-motion
                // no hay relleno: pista hasta armarse y luego el fondo, de golpe). Cada parte se pinta UNA vez: el
                // tramo relleno es exactamente el fondo normal, no fondo encima de pista.
                float split = armed ? r.Width : f.ReduceMotion ? 0f : r.Width * Math.Clamp(f.PermissionArm, 0f, 1f);
                var state = g.Save();
                g.SetClip(path, CombineMode.Intersect);
                if (split > 0f)
                {
                    _brush.Color = Fade(fill);
                    g.FillRectangle(_brush, r.Left, r.Top, split, r.Height);
                }
                if (split < r.Width)
                {
                    _brush.Color = Fade(ButtonBase);
                    g.FillRectangle(_brush, r.Left + split, r.Top, r.Width - split, r.Height);
                }
                g.Restore(state);
            }
            if (!armed) _alpha = Quantize(_alpha * ButtonDimText);
            string text = pressed ? (allow ? Strings.NotchAllowed : Strings.NotchDenied)
                : label ?? (allow ? Strings.NotchAllow : Strings.NotchDeny);
            Color color = pressed ? (allow ? _t.Ok : _t.WarnText) : _t.Foreground;
            DrawText(g, text, allow ? _title! : _body!, color, r, _center);
        }
        finally { _alpha = saved; }
    }

    private static GraphicsPath RoundedRect(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(2f * radius, Math.Min(r.Width, r.Height));
        if (d < 1f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.Left, r.Top, d, d, 180f, 90f);
        path.AddArc(r.Right - d, r.Top, d, d, 270f, 90f);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    /// <summary>El «ahora» de las cuentas atrás del frame: el reloj de la demo en el tutorial (<see cref="NotchFrame.Clock"/>)
    /// o, en el notch de verdad, el de la máquina.</summary>
    private static DateTimeOffset Now(NotchFrame f) => f.Clock ?? DateTimeOffset.Now;

    /// <summary>Título, detalle y color de un aviso, ya con los textos del idioma (la cuenta atrás, contra <see cref="Now"/>).</summary>
    private (string Title, string? Detail, Color Color) AlertLines(NotchAlert a, NotchFrame f)
    {
        var quota = f.Quota;
        switch (a.Kind)
        {
            case NotchAlertKind.Permission:
                return ($"{Strings.NotchWaitingOk} · {a.Title}", a.Detail, _t.WarnText);
            case NotchAlertKind.Completed:
                return (Strings.NotchDone, string.IsNullOrEmpty(a.Detail) ? a.Title : $"{a.Title} · {a.Detail}", _t.Ok);
            case NotchAlertKind.Reminder:
                return (Strings.NotchReminder, a.Title, _t.AccentText);
            case NotchAlertKind.Notice:
                return (Strings.NotchProTrialEndedTitle, Strings.NotchProTrialEndedDetail, _t.AccentText);
            case NotchAlertKind.Focus:
            {
                var (ended, next) = FocusText.AlertPhases(a.Title, a.Detail);
                var (title, detail, _) = FocusAlertTexts(ended, next);
                return (title, detail, FocusPalette.Text(ended, _t));
            }
            default:
            {
                string title = string.Format(Strings.Culture, Strings.NotchQuotaFmt, a.Title, a.Detail ?? "");
                string cd = UsageFormat.CountdownAt(WindowFor(a, quota)?.ResetsAt, Strings.Resetting, Now(f));
                string? detail = cd.Length > 0 ? $"{Strings.ResetsIn} {cd}" : null;
                return a.Kind == NotchAlertKind.QuotaAlert
                    ? (title, detail, _t.CriticalText)
                    : (title, detail, _t.WarnText);
            }
        }
    }

    private static UsageWindow? WindowFor(NotchAlert a, RealUsage? q) =>
        a.Title == "7d" ? q?.SevenDay : q?.FiveHour;

    // ---------------------------------------------------------------- piezas

    private void SessionRow(Graphics g, SessionState x, RectangleF r, float s, float spinnerPhase, bool reduceMotion)
    {
        float cy = r.Top + r.Height / 2f, dot = 6f * s;
        if (x.Phase.IsActive()) Spinner(g, r.Left, cy, 8f * s, spinnerPhase, reduceMotion);
        else
        {
            _brush.Color = Fade(PhaseColor(x.Phase));
            g.FillEllipse(_brush, r.Left + 1f * s, cy - dot / 2f, dot, dot);
        }
        float tx = r.Left + 14f * s;
        string phase = PhaseLabel(x);
        float phaseW = Math.Min(110f * s, r.Width * 0.45f);
        string name = string.IsNullOrEmpty(x.ProjectName) ? SessionState.ProjectNameFromCwd(x.Cwd) : x.ProjectName;
        DrawText(g, name, _body!, _t.Foreground, new RectangleF(tx, r.Top, Math.Max(0f, r.Right - tx - phaseW - 4f * s), r.Height), _left);
        DrawText(g, phase, _caption!, PhaseColor(x.Phase), new RectangleF(r.Right - phaseW, r.Top, phaseW, r.Height), _right);
    }

    private string PhaseLabel(SessionState x) => x.Phase switch
    {
        SessionPhase.Processing => !string.IsNullOrEmpty(x.LastTool) ? x.LastTool! : Strings.SessionPhaseProcessing,
        SessionPhase.Compacting => Strings.SessionPhaseCompacting,
        SessionPhase.WaitingForApproval => Strings.SessionPhaseWaitingApproval,
        SessionPhase.WaitingForInput => Strings.SessionPhaseWaitingInput,
        _ => Strings.SessionPhaseIdle,
    };

    private Color PhaseColor(SessionPhase p) => p switch
    {
        SessionPhase.Processing or SessionPhase.Compacting => _t.AccentText,
        SessionPhase.WaitingForApproval => _t.WarnText,
        SessionPhase.WaitingForInput => _t.Ok,
        _ => _t.TextMuted,
    };

    /// <summary>Anillo de cuota: pista al 15 %, arco desde las 12 en sentido horario, extremos redondos.
    /// Color por umbral (neutro &lt; 80, Warn ≥ 80, Critical ≥ 95); el fundido entre colores lo anima la ventana.</summary>
    private void Ring(Graphics g, float x, float cy, float d, double? pct)
    {
        float w = Math.Max(1.5f, NotchMotion.RingStroke * d / NotchMotion.RingDiameter);
        var r = new RectangleF(x + w / 2f, cy - d / 2f + w / 2f, d - w, d - w);
        SetPen(Color.FromArgb((int)(255 * NotchMotion.RingTrackAlpha), _t.Foreground), w);
        g.DrawEllipse(_pen, r);
        if (pct is not { } p || p <= 0) return;
        SetPen(QuotaColor(p, text: false), w);
        g.DrawArc(_pen, r, -90f, (float)(Math.Clamp(p, 0, 100) * 3.6));
    }

    /// <summary>Spinner de Trabajando: arco de 280° en Ok, extremos redondos, sin pista. Con
    /// reduce-motion, glifo fijo «…».</summary>
    private void Spinner(Graphics g, float x, float cy, float d, float phase, bool reduceMotion)
    {
        if (reduceMotion)
        {
            DrawText(g, "…", _body!, _t.Ok, new RectangleF(x, cy - d, d * 1.5f, 2 * d), _left);
            return;
        }
        float w = Math.Max(1.25f, NotchMotion.SpinnerStroke * d / NotchMotion.SpinnerDiameter);
        var r = new RectangleF(x + w / 2f, cy - d / 2f + w / 2f, d - w, d - w);
        SetPen(_t.Ok, w);
        float turn = phase - MathF.Floor(phase);
        g.DrawArc(_pen, r, turn * 360f - 90f, NotchMotion.SpinnerSweepDeg);
    }

    private void Bar(Graphics g, RectangleF r, double? pct)
    {
        _brush.Color = Fade(_t.Track);
        g.FillRectangle(_brush, r);
        if (pct is not { } p || p <= 0) return;
        _brush.Color = Fade(QuotaColor(p, text: false));
        g.FillRectangle(_brush, r.X, r.Y, r.Width * (float)(Math.Clamp(p, 0, 100) / 100.0), r.Height);
    }

    private void AlertIcon(Graphics g, NotchAlertKind kind, float x, float cy, float d, Color color)
    {
        var r = new RectangleF(x, cy - d / 2f, d, d);
        _brush.Color = Fade(color);
        g.FillEllipse(_brush, r);
        SetPen(Color.Black, Math.Max(1.5f, d * 0.12f));
        if (kind == NotchAlertKind.Completed)
        {
            // Visto bueno.
            g.DrawLines(_pen, new[]
            {
                new PointF(r.Left + d * 0.28f, r.Top + d * 0.52f),
                new PointF(r.Left + d * 0.44f, r.Top + d * 0.68f),
                new PointF(r.Left + d * 0.72f, r.Top + d * 0.36f),
            });
        }
        else if (kind == NotchAlertKind.Reminder) ReminderGlyph(g, r, d);
        else if (kind == NotchAlertKind.Focus) FocusGlyph(g, r, d);
        else if (kind == NotchAlertKind.Notice)
        {
            // Información («i»): el punto arriba y el palo debajo.
            g.DrawLine(_pen, r.Left + d / 2f, r.Top + d * 0.26f, r.Left + d / 2f, r.Top + d * 0.27f);
            g.DrawLine(_pen, r.Left + d / 2f, r.Top + d * 0.44f, r.Left + d / 2f, r.Top + d * 0.74f);
        }
        else
        {
            // Exclamación (permiso).
            g.DrawLine(_pen, r.Left + d / 2f, r.Top + d * 0.26f, r.Left + d / 2f, r.Top + d * 0.56f);
            g.DrawLine(_pen, r.Left + d / 2f, r.Top + d * 0.74f, r.Left + d / 2f, r.Top + d * 0.75f);
        }
    }

    private void DrawText(Graphics g, string text, Font font, Color color, RectangleF r, StringFormat fmt)
    {
        if (r.Width <= 1f || string.IsNullOrEmpty(text)) return;
        _brush.Color = Fade(color);
        g.DrawString(text, font, _brush, r, fmt);
    }

    private void SetPen(Color c, float width)
    {
        _pen.Color = Fade(c);
        _pen.Width = width;
    }

    /// <summary>Aplica el alfa de contenido del frame (ya cuantizado) al color.</summary>
    private static Color Darken(Color c, float amount) =>
        Color.FromArgb(c.A, (int)(c.R * (1f - amount)), (int)(c.G * (1f - amount)), (int)(c.B * (1f - amount)));

    private Color Fade(Color c) => _alpha >= 1f ? c : Color.FromArgb((int)(c.A * _alpha), c);

    private string Pct(UsageWindow? w) =>
        w is null ? "—" : UsageFormat.Percent(Math.Round(w.UtilizationPct), Strings.Culture);

    private Color PctColor(UsageWindow? w, bool text) => w is null ? _t.TextMuted : QuotaColor(w.UtilizationPct, text);

    /// <summary>Color de cuota por umbral: neutro, Warn ≥ 80, Critical ≥ 95.</summary>
    private Color QuotaColor(double pct, bool text) =>
        pct >= 95 ? (text ? _t.CriticalText : _t.Critical)
        : pct >= 80 ? (text ? _t.WarnText : _t.Warn)
        : _t.Foreground;

    /// <summary>¿La cuota de 5 h está en aviso? Desde el primer umbral configurado (<see cref="NotchFrame.QuotaAlertPct"/>, 80
    /// por defecto), como el anillo de la letra compacta (<see cref="CompactLyrics.QuotaAlert"/>).</summary>
    private static bool QuotaWarns(NotchFrame f) => f.Quota?.FiveHour is { } w && w.UtilizationPct >= f.QuotaAlertPct;

    /// <summary>Raya de Mínimo: negra (gris claro sobre fondo oscuro), o del color de la cuota de 5 h si está en aviso.</summary>
    private Color MinimalColor(NotchFrame f) =>
        QuotaWarns(f) && f.Quota?.FiveHour is { } w ? QuotaColor(w.UtilizationPct, text: false)
        : Contrast == NotchContrast.OnDark ? MinimalOnDark : Color.Black;

    private static BlurSlice Blur(ref BlurSlice? cache, float scale, float radius, Color color, float maxAlpha)
    {
        if (cache is null || Math.Abs(cache.Scale - scale) > 0.001f)
        {
            cache?.Dispose();
            cache = new BlurSlice(scale, radius * scale, color, maxAlpha);
        }
        return cache;
    }

    /// <summary>Fuentes en PÍXELES (no puntos): el Graphics del DIB está a 96 DPI y la escala ya la
    /// aplicamos nosotros. Se recrean solo si cambia la escala.</summary>
    private void EnsureFonts(float scale)
    {
        if (Math.Abs(scale - _fontScale) < 0.001f && _body is not null) return;
        DisposeFonts();
        string family = Typography.Body.FontFamily.Name;
        _title = new Font(family, 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _body = new Font(family, 12.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _caption = new Font(family, 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _small = new Font(family, 10f * scale, FontStyle.Regular, GraphicsUnit.Pixel);   // detalle de las tarjetas de la bandeja
        _fontScale = scale;
    }

    private void DisposeFonts()
    {
        _title?.Dispose(); _body?.Dispose(); _caption?.Dispose(); _small?.Dispose();
        _title = _body = _caption = _small = null;
    }

    public void Dispose()
    {
        DisposeFonts();
        DisposeTaskFormats();
        _art?.Dispose();
        _shadow?.Dispose();
        _halo?.Dispose();
        _measureG?.Dispose();
        _measureBmp?.Dispose();
        _lyricsIcon?.Dispose();
        DisposeSearchFonts();
        DisposeUsage();
        DisposeFocus();
        _brush.Dispose();
        _pen.Dispose();
        _glyphPen.Dispose();
        _left.Dispose();
        _right.Dispose();
        _center.Dispose();
        _wrap.Dispose();
    }

    /// <summary>
    /// Sombra/halo barato (spec de movimiento §3.0): una textura cuadrada de 8·radio de lado (96 px a radio
    /// 12) con un rectángulo desenfocado por 3 pasadas de caja, generada UNA vez por escala, y estirada en
    /// 9 trozos alrededor de la forma. El alfa del frame va por <see cref="ColorMatrix"/> (9 DrawImage).
    /// </summary>
    private sealed class BlurSlice : IDisposable
    {
        private readonly Bitmap _tex;
        private readonly int _r, _border;
        private readonly ImageAttributes _attrs = new();
        private readonly ColorMatrix _cm = new();
        private readonly PointF[] _dst = new PointF[3];
        private float _lastAlpha = -1f;

        public BlurSlice(float scale, float radiusPx, Color color, float maxAlpha)
        {
            Scale = scale;
            _r = Math.Max(1, (int)MathF.Round(radiusPx));
            _border = 2 * _r;
            int size = 8 * _r;

            var mask = new float[size * size];
            for (int y = _r; y < size - _r; y++)
                for (int x = _r; x < size - _r; x++) mask[y * size + x] = 1f;
            int pr = Math.Max(1, _r / 3);   // 3 pasadas de radio r/3 ≈ gaussiana de alcance r
            for (int pass = 0; pass < 3; pass++)
            {
                BoxBlur(mask, size, pr, horizontal: true);
                BoxBlur(mask, size, pr, horizontal: false);
            }

            _tex = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            var data = _tex.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new int[size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float a = Math.Clamp(mask[y * size + x] * maxAlpha, 0f, 1f);
                        int A = (int)MathF.Round(a * 255f);
                        int R = (int)MathF.Round(color.R * a), G = (int)MathF.Round(color.G * a), B = (int)MathF.Round(color.B * a);
                        row[x] = (A << 24) | (R << 16) | (G << 8) | B;   // premultiplicado
                    }
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size);
                }
            }
            finally { _tex.UnlockBits(data); }
            _attrs.SetWrapMode(WrapMode.TileFlipXY);   // sin sangrado de bordes al estirar los trozos
        }

        public float Scale { get; }

        /// <summary>Pinta alrededor de <paramref name="shape"/> (desplazada <paramref name="offsetY"/>) y
        /// devuelve el rectángulo cubierto.</summary>
        public RectangleF Draw(Graphics g, RectangleF shape, float offsetY, float alpha)
        {
            var d = RectangleF.FromLTRB(shape.Left - _r, shape.Top - _r + offsetY, shape.Right + _r, shape.Bottom + _r + offsetY);
            if (Math.Abs(alpha - _lastAlpha) > 0.001f)
            {
                _cm.Matrix33 = Math.Clamp(alpha, 0f, 1f);
                _attrs.SetColorMatrix(_cm);
                _lastAlpha = alpha;
            }
            float b = Math.Min(_border, Math.Min(d.Width, d.Height) / 2f);
            int t = _tex.Width;
            Span<float> sx = stackalloc float[] { 0, _border, t - _border, t };
            Span<float> dx = stackalloc float[] { d.Left, d.Left + b, d.Right - b, d.Right };
            Span<float> dy = stackalloc float[] { d.Top, d.Top + b, d.Bottom - b, d.Bottom };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                {
                    if (dx[i + 1] - dx[i] < 0.01f || dy[j + 1] - dy[j] < 0.01f) continue;
                    _dst[0] = new PointF(dx[i], dy[j]);
                    _dst[1] = new PointF(dx[i + 1], dy[j]);
                    _dst[2] = new PointF(dx[i], dy[j + 1]);
                    g.DrawImage(_tex, _dst, RectangleF.FromLTRB(sx[i], sx[j], sx[i + 1], sx[j + 1]), GraphicsUnit.Pixel, _attrs);
                }
            return d;
        }

        private static void BoxBlur(float[] m, int size, int r, bool horizontal)
        {
            var line = new float[size];
            float inv = 1f / (2 * r + 1);
            for (int k = 0; k < size; k++)
            {
                for (int i = 0; i < size; i++) line[i] = horizontal ? m[k * size + i] : m[i * size + k];
                float sum = 0f;
                for (int i = -r; i <= r; i++) if (i >= 0 && i < size) sum += line[i];
                for (int i = 0; i < size; i++)
                {
                    float v = sum * inv;
                    if (horizontal) m[k * size + i] = v; else m[i * size + k] = v;
                    int outI = i - r, inI = i + r + 1;
                    if (outI >= 0) sum -= line[outI];
                    if (inI < size) sum += line[inI];
                }
            }
        }

        public void Dispose()
        {
            _tex.Dispose();
            _attrs.Dispose();
        }
    }
}
