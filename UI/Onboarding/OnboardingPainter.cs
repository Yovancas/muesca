using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>
/// Una foto de lo que pinta el asistente en este frame. La construye <see cref="OnboardingForm"/> con los valores ANIMADOS
/// (forma, alfas, realces, cruce de páginas) y los textos ya formateados (<see cref="OnboardingView"/>); el painter la pinta
/// tal cual.
/// <list type="bullet">
/// <item><see cref="Width"/>/<see cref="Height"/>: caja de la pastilla en px físicos; <see cref="CenterX"/>: su centro en la
/// superficie (el del notch de verdad, para que el relevo no se mueva ni medio píxel).</item>
/// <item><see cref="Previous"/>: la página que sale en un cambio de página, con <c>1 − PageBlend</c> de alfa;
/// <see cref="PageShift"/>/<see cref="PreviousShift"/>: su desplazamiento horizontal en px lógicos enteros.</item>
/// <item><see cref="Glow"/>: realce de cada tarjeta de perfil al pasar el ratón (0..1, con el acento).
/// <see cref="Knobs"/>: posición animada del botón de cada interruptor (0 = apagado, 1 = encendido), por
/// <see cref="OnboardingPainter.KnobIndex"/>.</item>
/// <item><see cref="Preview"/>: el notch de la vista previa (lo pinta el painter del notch, a escala) o <c>null</c> sin
/// notch. <see cref="Landed"/>: el notch de reposo al aterrizar, a SU escala, que también pinta el painter del notch.</item>
/// </list>
/// </summary>
public sealed record OnboardingFrame(float Width, float Height, float Scale, float CenterX, OnboardingView View,
    OnboardingShape Shape, float Squash = 0f, float ContentAlpha = 1f, float ContentOffsetY = 0f, float LogoAlpha = 0f,
    OnboardingView? Previous = null, float PageBlend = 1f, float PageShift = 0f, float PreviousShift = 0f,
    float IndicatorPos = 0f, OnboardingHit Hover = default, float HoverIntensity = 0f, OnboardingHit Pressed = default,
    OnboardingHit Focus = default, bool ShowFocus = false, IReadOnlyList<float>? Glow = null, IReadOnlyList<float>? Knobs = null,
    float OverlayAlpha = 0f, NotchFrame? Preview = null, NotchFrame? Landed = null, float ShadowAlpha = 0f);

/// <summary>
/// Pinta el asistente de bienvenida dentro del notch: la misma forma negra con orejas (<see cref="NotchGeometry.PillPath"/>),
/// el logo, las páginas con la geometría de <see cref="OnboardingGeometry"/> (la MISMA con que la ventana acierta el clic),
/// la vista previa de cada perfil y el notch de reposo al aterrizar, estos dos con el <see cref="NotchContentPainter"/> de
/// verdad. Como él: fuentes cacheadas por escala, un pincel y un lápiz reutilizados y el alfa del contenido cuantizado a
/// 1/32. El texto llega formateado: aquí no se formatea nada.
/// </summary>
public sealed partial class OnboardingPainter : IDisposable
{
    // Blanco translúcido sobre el negro de la pastilla (como los botones del notch).
    private static readonly Color CardFill = Color.FromArgb(14, 255, 255, 255);
    private static readonly Color ControlFill = Color.FromArgb(20, 255, 255, 255);
    private static readonly Color ControlHover = Color.FromArgb(34, 255, 255, 255);
    private static readonly Color TrackOff = Color.FromArgb(56, 255, 255, 255);
    private static readonly Color OverlayDim = Color.FromArgb(150, 0, 0, 0);
    private static readonly Color OverlayFill = Color.FromArgb(255, 28, 28, 31);
    private static readonly Color WallTop = Color.FromArgb(58, 66, 104), WallBottom = Color.FromArgb(132, 86, 120);
    private const float PressDarken = 0.06f, DisabledAlpha = 0.4f, DotAlpha = 0.35f;

    private readonly Theme _t = Theme.Dark;
    private readonly NotchContentPainter _notch;
    private readonly SolidBrush _brush = new(Color.White);
    private readonly Pen _pen = new(Color.White) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    private readonly StringFormat _left, _right, _center, _wrap, _wrapMiddle;
    private float _fontScale = -1f;
    private Font? _display, _title, _body, _bold, _caption, _small;
    private float _alpha = 1f;
    // El logo grande de la bienvenida y el pequeño del notch que baja (y del tutorial), ya pintados: uno por tamaño y DPI.
    private readonly BrandMarkCache _logo = new(), _miniLogo = new();

    public OnboardingPainter(Strings strings)
    {
        _notch = new NotchContentPainter(strings ?? throw new ArgumentNullException(nameof(strings)));
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
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.LineLimit,
        };
        // Con ajuste de línea y centrado en alto (la línea del contador de uso, junto a su interruptor).
        _wrapMiddle = (StringFormat)_wrap.Clone();
        _wrapMiddle.LineAlignment = StringAlignment.Center;
    }

    /// <summary>Textos del painter del notch (vista previa y aterrizaje), para el idioma elegido en el asistente.</summary>
    public Strings Strings
    {
        get => _notch.Strings;
        set => _notch.Strings = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>El painter del notch de verdad: la ventana le pide las medidas de la vista previa y del aterrizaje.</summary>
    public NotchContentPainter Notch => _notch;

    /// <summary>Posición en <see cref="OnboardingFrame.Knobs"/> del interruptor de una casilla (0..10) o de las filas sueltas.</summary>
    public static int KnobIndex(OnboardingControl control, int index = 0) => control switch
    {
        OnboardingControl.Feature => index,
        OnboardingControl.Startup => SetupFeatures.All.Count,
        OnboardingControl.LiveSessions => SetupFeatures.All.Count + 1,
        OnboardingControl.Permissions => SetupFeatures.All.Count + 2,
        OnboardingControl.UsageStats => SetupFeatures.All.Count + 3,
        OnboardingControl.Tutorial => SetupFeatures.All.Count + 4,
        _ => -1,
    };

    /// <summary>Cuántos interruptores hay (la longitud de <see cref="OnboardingFrame.Knobs"/>).</summary>
    public static int KnobCount => SetupFeatures.All.Count + 5;

    // ---------------------------------------------------------------- frame

    /// <summary>
    /// Pinta el frame en una superficie de <paramref name="surface"/> y devuelve el rectángulo tocado (para que el renderer
    /// borre solo eso en el siguiente). Sin sombra: va en <see cref="PaintShadow"/>, en la ventana que no recibe clics.
    /// </summary>
    public RectangleF Paint(Graphics g, Size surface, OnboardingFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Width < 1f || f.Height < 1f) return RectangleF.Empty;
        SetQuality(g);
        _alpha = 1f;

        if (f.Landed is { } landed)
        {
            // Ya es el notch de reposo: lo pinta el painter de verdad, a su escala, en el mismo sitio que el notch real.
            var state = g.Save();
            try
            {
                float dx = f.CenterX - surface.Width / 2f;
                g.TranslateTransform(dx, 0f);
                var drawn = _notch.Paint(g, surface, landed);
                drawn.Offset(dx, 0f);
                return drawn;
            }
            finally { g.Restore(state); }
        }

        float s = f.Scale;
        var bounds = BeginShell(g, f.Width, f.Height, f.CenterX, s, f.Squash, f.LogoAlpha, out var clip);
        try
        {
            _alpha = Quantize(f.ContentAlpha);
            if (_alpha > 0f)
            {
                // El contenido va maquetado en el asistente ABIERTO, centrado y pegado arriba; la forma lo recorta como una
                // máscara al desplegarse y al encogerse (no se desliza con los bordes).
                var open = OnboardingGeometry.PillRect(OnboardingGeometry.Width * s, OnboardingGeometry.Height * s, f.CenterX);
                if (f.ContentOffsetY != 0f) g.TranslateTransform(0f, MathF.Round(f.ContentOffsetY * s));
                PaintContent(g, open, s, f);
            }
        }
        finally
        {
            g.Restore(clip);
            _alpha = 1f;
        }
        return bounds;
    }

    /// <summary>Sombra bajo el asistente: la misma textura que la del notch (<see cref="NotchContentPainter.PaintShadow"/>).</summary>
    public RectangleF PaintShadow(Graphics g, Size surface, OnboardingFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Landed is not null) return RectangleF.Empty;
        return ShellShadow(g, surface, f.Width, f.Height, f.Scale, f.Squash, f.CenterX, f.ShadowAlpha);
    }

    /// <summary>
    /// La forma negra con orejas del notch grande (asistente o tutorial) con su squash, centrada en <paramref name="centerX"/>
    /// y pegada arriba; deja el recorte puesto a la pastilla, las fuentes de la escala listas y, mientras baja, el logo
    /// pequeño dentro. Devuelve el rectángulo tocado; el llamante pinta el contenido y restaura <paramref name="clip"/>.
    /// </summary>
    private RectangleF BeginShell(Graphics g, float width, float height, float centerX, float s, float squash, float logoAlpha,
        out GraphicsState clip)
    {
        var pill = OnboardingGeometry.PillRect(width, height, centerX);
        var squashed = NotchMotion.Squashed(width, height, squash);
        var shape = OnboardingGeometry.PillRect(squashed.Width, squashed.Height, centerX);
        float radius = NotchLayout.CornerRadius(shape.Width, s), ears = NotchMotion.EarSize(shape.Width, s);
        using (var path = NotchGeometry.PillPath(shape, radius, ears))
        {
            _brush.Color = Color.Black;
            g.FillPath(_brush, path);
        }
        EnsureFonts(s);
        clip = g.Save();
        g.SetClip(RectangleF.Intersect(pill, shape));
        if (logoAlpha > 0f) PaintMiniLogo(g, pill, s, logoAlpha);
        return RectangleF.FromLTRB(shape.Left - ears, shape.Top, shape.Right + ears, shape.Bottom);
    }

    /// <summary>Sombra de una pastilla de <paramref name="width"/> × <paramref name="height"/> centrada en
    /// <paramref name="centerX"/>: la textura del notch, con su alfa.</summary>
    private RectangleF ShellShadow(Graphics g, Size surface, float width, float height, float scale, float squash, float centerX,
        float alpha)
    {
        if (alpha <= 0.004f || width < 1f || height < 1f) return RectangleF.Empty;
        var shadow = new NotchFrame(width, height, scale, NotchMode.Expanded, null, Array.Empty<Models.SessionState>(),
            null, 0f, 0f, Squash: squash, ShadowAlpha: alpha);
        var state = g.Save();
        try
        {
            float dx = centerX - surface.Width / 2f;
            g.TranslateTransform(dx, 0f);
            var drawn = _notch.PaintShadow(g, surface, shadow);
            drawn.Offset(dx, 0f);
            return drawn;
        }
        finally { g.Restore(state); }
    }

    // ---------------------------------------------------------------- contenido

    private void PaintContent(Graphics g, RectangleF pill, float s, OnboardingFrame f)
    {
        var v = f.View;
        float contentAlpha = _alpha;
        // Cruce de páginas: la que sale y la que entra, cada una con su alfa y su desplazamiento (px enteros).
        if (f.Previous is { } prev && f.PageBlend < 1f) PageLayer(g, pill, s, f, prev, contentAlpha * (1f - f.PageBlend), f.PreviousShift);
        PageLayer(g, pill, s, f, v, contentAlpha * Math.Clamp(f.PageBlend, 0f, 1f), f.PageShift);
        _alpha = contentAlpha;

        bool covered = v.Overlay is not null && f.OverlayAlpha > 0f;
        PaintFooter(g, pill, s, f);
        if (covered) PaintOverlay(g, pill, s, f, v.Overlay!);
        if (f.ShowFocus && f.Focus.Control != OnboardingControl.None && (v.Overlay is null || f.Focus.Control is
                OnboardingControl.OverlayPrimary or OnboardingControl.OverlaySecondary))
            PaintFocus(g, pill, s, f);
    }

    private void PageLayer(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v, float alpha, float shift)
    {
        _alpha = Quantize(alpha);
        if (_alpha <= 0f) return;
        var state = g.Save();
        try
        {
            if (shift != 0f) g.TranslateTransform(MathF.Round(shift * s), 0f);
            if (v.Page != OnboardingPage.Welcome)
            {
                DrawText(g, v.Title, _title!, _t.Foreground, OnboardingGeometry.Title(pill, s), _left);
                DrawText(g, v.Subtitle, _body!, _t.TextMuted, OnboardingGeometry.Subtitle(pill, s), _left);
            }
            switch (v.Page)
            {
                case OnboardingPage.Welcome: PaintWelcome(g, pill, s, f, v); break;
                case OnboardingPage.Profile: PaintProfiles(g, pill, s, f, v); break;
                case OnboardingPage.Features: PaintFeatures(g, pill, s, f, v); break;
                case OnboardingPage.Settings: PaintSettings(g, pill, s, f, v); break;
                case OnboardingPage.ClaudeCode: PaintClaude(g, pill, s, f, v); break;
                case OnboardingPage.Summary: PaintSummary(g, pill, s, f, v); break;
            }
        }
        finally { g.Restore(state); }
    }

    // ---- bienvenida

    private void PaintWelcome(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        PaintLogo(g, OnboardingGeometry.Logo(pill, s), s);
        DrawText(g, v.Wordmark, _display!, _t.Foreground, OnboardingGeometry.Wordmark(pill, s), _center);
        DrawText(g, v.Tagline, _body!, _t.TextMuted, OnboardingGeometry.Tagline(pill, s), _center);

        // «Claude Code detectado» en una cápsula: verde con un visto, o atenuada si falta.
        var chip = OnboardingGeometry.Detected(pill, s);
        float tw = g.MeasureString(v.Detected, _caption!, int.MaxValue, _left).Width;
        float w = Math.Min(chip.Width, tw + 36f * s);
        var r = new RectangleF(chip.Left + (chip.Width - w) / 2f, chip.Top, w, chip.Height);
        FillRound(g, r, r.Height / 2f, ControlFill);
        float cx = r.Left + 14f * s, cy = r.Top + r.Height / 2f;
        if (v.DetectedOk)
        {
            SetPen(_t.Ok, 1.8f * s);
            g.DrawLines(_pen, new[] { new PointF(cx - 4f * s, cy), new PointF(cx - 1f * s, cy + 3f * s), new PointF(cx + 4f * s, cy - 3.5f * s) });
        }
        else
        {
            _brush.Color = Fade(_t.TextMuted);
            g.FillEllipse(_brush, cx - 3f * s, cy - 3f * s, 6f * s, 6f * s);
        }
        DrawText(g, v.Detected, _caption!, v.DetectedOk ? _t.Foreground : _t.TextMuted,
            RectangleF.FromLTRB(cx + 10f * s, r.Top, r.Right - 8f * s, r.Bottom), _left);

        // El contador anónimo de uso: su interruptor y lo que manda, a la vista antes de que salga ningún aviso.
        if (v.UsageStats is { } stats)
        {
            var row = OnboardingGeometry.UsageStatsRow(pill, s);
            HoverRow(g, row, s, f, new OnboardingHit(OnboardingControl.UsageStats), stats.Enabled);
            var (sw, text) = OnboardingGeometry.UsageStatsParts(row, s);
            Switch(g, sw, Knob(f, OnboardingControl.UsageStats, 0, stats.On), s);
            DrawText(g, stats.Label, _caption!, _t.TextMuted, text, _wrapMiddle);
        }
        DrawText(g, v.KeysHint, _small!, _t.TextMuted, OnboardingGeometry.KeysHint(pill, s), _center);
    }

    /// <summary>
    /// Logo de Muesca (dibujado en vector, sin imágenes: <see cref="BrandMark"/>, el de branding/muesca.svg): el cuadrado con
    /// el degradado violeta → azul y, colgando de su borde de arriba, el notch negro con los dos anillos de la cuota. Es lo que
    /// hace la app: la cuota, en el notch.
    /// </summary>
    private void PaintLogo(Graphics g, RectangleF r, float s) => _logo.Paint(g, r, _alpha, s);

    /// <summary>El logo pequeño dentro del notch de reposo mientras baja (y en el del tutorial): el logo y «Muesca».</summary>
    private void PaintMiniLogo(Graphics g, RectangleF pill, float s, float alpha)
    {
        _alpha = Quantize(alpha);
        if (_alpha <= 0f) return;
        float d = MiniLogoSize * s, gap = 6f * s;
        float tw = MiniLogoNameWidth(g);
        float total = d + gap + tw, x = pill.Left + (pill.Width - total) / 2f, cy = pill.Top + pill.Height / 2f;
        _miniLogo.Paint(g, new RectangleF(x, cy - d / 2f, d, d), _alpha, s);
        DrawText(g, Brand.Name, _bold!, _t.Foreground, new RectangleF(x + d + gap, pill.Top, tw + 2f, pill.Height), _left);
        _alpha = 1f;
    }

    /// <summary>Lado del logo pequeño, en px lógicos.</summary>
    internal const float MiniLogoSize = 16f;

    // Ancho de «Muesca» con la fuente de la escala vigente: se mide una vez por fuente, no en cada frame de la bajada.
    private Font? _miniNameFont;
    private float _miniNameW;

    private float MiniLogoNameWidth(Graphics g)
    {
        if (!ReferenceEquals(_miniNameFont, _bold))
        {
            _miniNameFont = _bold;
            _miniNameW = g.MeasureString(Brand.Name, _bold!, int.MaxValue, _left).Width;
        }
        return _miniNameW;
    }

    // ---- perfiles

    private void PaintProfiles(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        for (int i = 0; i < v.Cards.Count; i++)
        {
            var card = v.Cards[i];
            var r = OnboardingGeometry.Card(pill, s, i);
            bool selected = i == v.SelectedCard;
            float glow = f.Glow is { } gl && i < gl.Count ? Math.Clamp(gl[i], 0f, 1f) : 0f;
            // Fondo: el blanco tenue de siempre, hacia el acento con el ratón encima; la elegida, con su borde de acento.
            var fill = Lerp(CardFill, Color.FromArgb(46, _t.Accent), Math.Max(glow, selected ? 0.55f : 0f));
            if (Pressed(f, OnboardingControl.Card, i)) fill = Darken(fill, PressDarken);
            using (var path = RoundPath(r, 10f * s))
            {
                _brush.Color = Fade(fill);
                g.FillPath(_brush, path);
                if (selected || glow > 0.01f)
                {
                    SetPen(Color.FromArgb(selected ? 255 : (int)(160 * glow), _t.Accent), (selected ? 1.6f : 1f) * s);
                    g.DrawPath(_pen, path);
                }
            }
            var (icon, title, line, badge) = OnboardingGeometry.CardParts(r, s);
            PaintProfileIcon(g, icon, s, card.Profile, selected || glow > 0.5f ? _t.Accent : _t.Foreground);
            float titleRight = title.Right;
            if (card.Recommended)
            {
                float bw = g.MeasureString(card.Title, _bold!, int.MaxValue, _left).Width;
                float tagW = g.MeasureString(v.RecommendedBadge, _small!, int.MaxValue, _left).Width + 12f * s;
                var tag = new RectangleF(Math.Min(title.Left + bw + 8f * s, badge.Right - tagW), badge.Top, tagW, badge.Height);
                FillRound(g, tag, tag.Height / 2f, Color.FromArgb(48, _t.Accent));
                DrawText(g, v.RecommendedBadge, _small!, _t.AccentText, tag, _center);
                titleRight = tag.Left - 6f * s;
            }
            DrawText(g, card.Title, _bold!, _t.Foreground, RectangleF.FromLTRB(title.Left, title.Top, titleRight, title.Bottom), _left);
            DrawText(g, card.Line, _caption!, _t.TextMuted, line, _left);
            if (selected) PaintCheck(g, new RectangleF(r.Right - 22f * s, r.Bottom - 22f * s, 14f * s, 14f * s), s);
        }
        PaintPreview(g, pill, s, f);
        DrawWrapped(g, v.PreviewCaption, _caption!, _t.TextMuted, OnboardingGeometry.PreviewCaption(pill, s));
    }

    /// <summary>Visto bueno de la tarjeta elegida, abajo a la derecha.</summary>
    private void PaintCheck(Graphics g, RectangleF r, float s)
    {
        _brush.Color = Fade(_t.Accent);
        g.FillEllipse(_brush, r);
        SetPen(_t.OnAccent, 1.6f * s);
        float x = r.Left, y = r.Top, d = r.Width;
        g.DrawLines(_pen, new[] { new PointF(x + d * 0.28f, y + d * 0.52f), new PointF(x + d * 0.44f, y + d * 0.68f),
            new PointF(x + d * 0.72f, y + d * 0.36f) });
    }

    /// <summary>Icono de cada perfil, dibujado con trazos (sin fuentes ni imágenes).</summary>
    private void PaintProfileIcon(Graphics g, RectangleF r, float s, SetupProfile profile, Color color)
    {
        float w = Math.Max(1.4f, 1.8f * s), cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        SetPen(color, w);
        switch (profile)
        {
            case SetupProfile.QuotaOnly:
            {
                // Un anillo de cuota al 70 %.
                var ring = RectangleF.Inflate(r, -4f * s, -4f * s);
                SetPen(Color.FromArgb(70, color), w);
                g.DrawEllipse(_pen, ring);
                SetPen(color, w);
                g.DrawArc(_pen, ring, -90f, 250f);
                break;
            }
            case SetupProfile.Coder:
            {
                // Una terminal: «›_».
                var box = RectangleF.Inflate(r, -2f * s, -5f * s);
                using (var path = RoundPath(box, 4f * s)) g.DrawPath(_pen, path);
                g.DrawLines(_pen, new[] { new PointF(box.Left + 5f * s, cy - 3.5f * s), new PointF(box.Left + 9f * s, cy),
                    new PointF(box.Left + 5f * s, cy + 3.5f * s) });
                g.DrawLine(_pen, box.Left + 12f * s, cy + 3.5f * s, box.Left + 18f * s, cy + 3.5f * s);
                break;
            }
            case SetupProfile.FullNotch:
            {
                // Una pantalla con el notch colgando de su borde de arriba y una nota debajo.
                var screen = RectangleF.Inflate(r, -1.5f * s, -4f * s);
                using (var path = RoundPath(screen, 4f * s)) g.DrawPath(_pen, path);
                var notch = new RectangleF(cx - 6.5f * s, screen.Top, 13f * s, 6f * s);
                using (var path = NotchGeometry.PillPath(notch, 3f * s))
                {
                    _brush.Color = Fade(color);
                    g.FillPath(_brush, path);
                }
                _brush.Color = Fade(color);
                g.FillEllipse(_brush, cx - 3.5f * s, screen.Bottom - 6.5f * s, 4.5f * s, 3.5f * s);
                g.DrawLine(_pen, cx + 0.6f * s, screen.Bottom - 5f * s, cx + 0.6f * s, screen.Bottom - 11f * s);
                g.DrawLine(_pen, cx + 0.6f * s, screen.Bottom - 11f * s, cx + 4f * s, screen.Bottom - 9.5f * s);
                break;
            }
            case SetupProfile.Minimal:
            {
                // La misma pantalla, solo con la raya arriba.
                var screen = RectangleF.Inflate(r, -1.5f * s, -4f * s);
                SetPen(Color.FromArgb(110, color), w);
                using (var path = RoundPath(screen, 4f * s)) g.DrawPath(_pen, path);
                SetPen(color, 2.6f * s);
                g.DrawLine(_pen, cx - 4.5f * s, screen.Top + 2.5f * s, cx + 4.5f * s, screen.Top + 2.5f * s);
                break;
            }
            default:
            {
                // Tres reguladores.
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Top + (7f + i * 7f) * s, knob = r.Left + (new[] { 18f, 9f, 14f })[i] * s;
                    SetPen(Color.FromArgb(110, color), w);
                    g.DrawLine(_pen, r.Left + 3f * s, y, r.Right - 3f * s, y);
                    _brush.Color = Fade(color);
                    g.FillEllipse(_brush, knob - 3f * s, y - 3f * s, 6f * s, 6f * s);
                }
                break;
            }
        }
    }

    /// <summary>
    /// La vista previa: un escritorio en miniatura con el notch del perfil colgando de su borde de arriba, pintado por el
    /// painter del notch con su sombra. Sin notch (Solo cuota), la barra de tareas con el icono y su panel.
    /// </summary>
    private void PaintPreview(Graphics g, RectangleF pill, float s, OnboardingFrame f)
    {
        var stage = OnboardingGeometry.PreviewStage(pill, s);
        using var stagePath = RoundPath(stage, 12f * s);
        using (var wall = new LinearGradientBrush(stage, Fade(WallTop), Fade(WallBottom), LinearGradientMode.Vertical))
            g.FillPath(wall, stagePath);
        var state = g.Save();
        try
        {
            g.SetClip(stagePath, CombineMode.Intersect);
            if (f.Preview is { } frame)
            {
                // El notch se pinta con su propia transparencia de contenido; la de la página, con la de la ventana del escritorio.
                var size = new Size((int)Math.Round(stage.Width), (int)Math.Round(stage.Height));
                g.TranslateTransform(stage.Left, stage.Top);
                var faded = frame with { ContentAlpha = frame.ContentAlpha * _alpha, ShadowAlpha = frame.ShadowAlpha * _alpha };
                _notch.PaintShadow(g, size, faded);
                _notch.Paint(g, size, faded);
            }
            else PaintTrayMock(g, stage, s);
        }
        finally { g.Restore(state); }
        SetQuality(g);   // el painter del notch deja su calidad; se vuelve a la nuestra
    }

    /// <summary>Solo cuota: la barra de tareas con el icono de la cuota y, encima, el panel con las dos barras.</summary>
    private void PaintTrayMock(Graphics g, RectangleF stage, float s)
    {
        float barH = 22f * s;
        _brush.Color = Fade(Color.FromArgb(200, 18, 18, 22));
        g.FillRectangle(_brush, stage.Left, stage.Bottom - barH, stage.Width, barH);
        float d = 12f * s, x = stage.Right - 34f * s, cy = stage.Bottom - barH / 2f, w = Math.Max(1.5f, 2f * s);
        var ring = new RectangleF(x, cy - d / 2f, d, d);
        SetPen(Color.FromArgb(70, 255, 255, 255), w);
        g.DrawEllipse(_pen, ring);
        SetPen(_t.Accent, w);
        g.DrawArc(_pen, ring, -90f, 150f);

        var panel = new RectangleF(stage.Right - 132f * s, stage.Bottom - barH - 78f * s, 120f * s, 70f * s);
        FillRound(g, panel, 9f * s, Color.FromArgb(235, 28, 28, 31));
        for (int i = 0; i < 2; i++)
        {
            float y = panel.Top + (14f + i * 26f) * s;
            DrawText(g, i == 0 ? "5h" : "7d", _small!, _t.TextMuted, new RectangleF(panel.Left + 10f * s, y - 6f * s, 30f * s, 12f * s), _left);
            var track = new RectangleF(panel.Left + 10f * s, y + 8f * s, panel.Width - 20f * s, 4f * s);
            FillRound(g, track, 2f * s, _t.Track);
            FillRound(g, new RectangleF(track.Left, track.Top, track.Width * (i == 0 ? 0.42f : 0.18f), track.Height), 2f * s, _t.Foreground);
        }
    }

    // ---- qué se activa

    private void PaintFeatures(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        foreach (var feature in SetupFeatures.All)
        {
            int i = (int)feature;
            if (i >= v.Toggles.Count) break;
            var t = v.Toggles[i];
            var row = OnboardingGeometry.ToggleRow(pill, s, feature);
            HoverRow(g, row, s, f, new OnboardingHit(OnboardingControl.Feature, i), t.Enabled);
            var (sw, label, detail) = OnboardingGeometry.ToggleParts(row, s);
            ToggleRow(g, sw, label, detail, t, Knob(f, OnboardingControl.Feature, i, t.On), s);
        }
        if (v.ProNote is { } proNote) DrawWrapped(g, proNote, _caption!, _t.AccentText, OnboardingGeometry.ProNote(pill, s));
        DrawWrapped(g, v.FeaturesHint, _caption!, _t.TextMuted, OnboardingGeometry.FeaturesHint(pill, s));
        Button(g, OnboardingGeometry.ResetButton(pill, s), v.Reset, primary: false, s, f, new(OnboardingControl.ResetFeatures));
    }

    /// <summary>Interruptor (pista y botón con su posición animada) con su nombre y su línea gris; atenuado si no se puede tocar.</summary>
    private void ToggleRow(Graphics g, RectangleF sw, RectangleF label, RectangleF detail, OnboardingToggle t, float knob, float s)
    {
        float saved = _alpha;
        if (!t.Enabled) _alpha = Quantize(_alpha * DisabledAlpha);
        try
        {
            Switch(g, sw, knob, s);
            if (t.Pro) label = ProTag(g, label, t.Label, s);
            DrawText(g, t.Label, _body!, _t.Foreground, label, _left);
            if (t.Detail.Length > 0) DrawText(g, t.Detail, _caption!, _t.TextMuted, detail, _left);
        }
        finally { _alpha = saved; }
    }

    /// <summary>Etiqueta «Pro» (como la de «Recomendado»: el acento tenue) justo después del nombre, o al final de la fila si
    /// el nombre no cabe; devuelve lo que queda para el nombre.</summary>
    private RectangleF ProTag(Graphics g, RectangleF label, string text, float s)
    {
        string tagText = Strings.OnbProTag;
        float tagW = g.MeasureString(tagText, _small!, int.MaxValue, _left).Width + 10f * s, gap = 6f * s;
        float textW = g.MeasureString(text, _body!, int.MaxValue, _left).Width;
        float x = Math.Min(label.Left + textW + gap, label.Right - tagW);
        if (x < label.Left) return label;
        var tag = new RectangleF(x, label.Top + 2f * s, tagW, Math.Max(1f, label.Height - 4f * s));
        FillRound(g, tag, tag.Height / 2f, Color.FromArgb(48, _t.Accent));
        DrawText(g, tagText, _small!, _t.AccentText, tag, _center);
        return RectangleF.FromLTRB(label.Left, label.Top, Math.Max(label.Left, x - gap), label.Bottom);
    }

    private void Switch(Graphics g, RectangleF r, float knob, float s)
    {
        knob = Math.Clamp(knob, 0f, 1f);
        FillRound(g, r, r.Height / 2f, Lerp(TrackOff, _t.Accent, knob));
        float d = r.Height - 4f * s, x = r.Left + 2f * s + (r.Width - 4f * s - d) * knob;
        _brush.Color = Fade(Color.White);
        g.FillEllipse(_brush, x, r.Top + 2f * s, d, d);
    }

    // ---- ajustes

    private void PaintSettings(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        bool ask = f.Shape.AskMonitor;
        foreach (var row in v.Settings)
        {
            var (label, control, hint) = OnboardingGeometry.SettingParts(pill, s, row.Row, ask);
            if (control.IsEmpty) continue;
            float saved = _alpha;
            if (!row.Enabled) _alpha = Quantize(_alpha * DisabledAlpha);
            try
            {
                DrawText(g, row.Label, _body!, _t.Foreground, label, _left);
                switch (row.Row)
                {
                    case OnboardingControl.Size:
                    case OnboardingControl.Open:
                        for (int i = 0; i < row.Segments.Count; i++)
                            Segment(g, OnboardingGeometry.Segment(control, s, row.Row, i), row.Segments[i], i == row.Selected,
                                i == row.Recommended, s, f, new OnboardingHit(row.Row, i));
                        break;
                    case OnboardingControl.Startup:
                        Switch(g, control, Knob(f, OnboardingControl.Startup, 0, row.On), s);
                        break;
                    default:
                        Cycler(g, control, row.Value, s, f, row.Row);
                        break;
                }
                DrawText(g, row.Hint, _caption!, _t.TextMuted, hint, _left);
            }
            finally { _alpha = saved; }
        }
        if (v.NotchOffNote is { } note) DrawWrapped(g, note, _caption!, _t.WarnText, OnboardingGeometry.NotchOffNote(pill, s));
        Button(g, OnboardingGeometry.ResetButton(pill, s), v.Reset, primary: false, s, f, new(OnboardingControl.ResetSettings));
    }

    /// <summary>Selector que cicla: pista redondeada, ‹ › a los lados y el valor en medio.</summary>
    private void Cycler(Graphics g, RectangleF r, string value, float s, OnboardingFrame f, OnboardingControl row)
    {
        var back = row == OnboardingControl.Language ? OnboardingControl.LanguagePrev : OnboardingControl.MonitorPrev;
        bool hot = Hovered(f, row, 0) || Hovered(f, back, 0);
        var fill = hot ? Lerp(ControlFill, ControlHover, f.HoverIntensity) : ControlFill;
        if (Pressed(f, row, 0) || Pressed(f, back, 0)) fill = Darken(fill, PressDarken);
        FillRound(g, r, 7f * s, fill);
        var (prev, next) = OnboardingGeometry.CyclerArrows(r);
        Chevron(g, prev, s, left: true);
        Chevron(g, next, s, left: false);
        DrawText(g, value, _body!, _t.Foreground, RectangleF.FromLTRB(prev.Right + 4f * s, r.Top, next.Left - 4f * s, r.Bottom), _center);
    }

    private void Chevron(Graphics g, RectangleF r, float s, bool left)
    {
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, k = 3.5f * s;
        SetPen(_t.TextMuted, Math.Max(1.4f, 1.6f * s));
        g.DrawLines(_pen, left
            ? new[] { new PointF(cx + k / 2f, cy - k), new PointF(cx - k / 2f, cy), new PointF(cx + k / 2f, cy + k) }
            : new[] { new PointF(cx - k / 2f, cy - k), new PointF(cx + k / 2f, cy), new PointF(cx - k / 2f, cy + k) });
    }

    /// <summary>Un segmento: el elegido con el acento; el recomendado lleva un punto de acento a la derecha del texto.</summary>
    private void Segment(Graphics g, RectangleF r, string text, bool selected, bool recommended, float s, OnboardingFrame f,
        OnboardingHit hit)
    {
        var fill = selected ? _t.Accent : Hovered(f, hit.Control, hit.Index) ? Lerp(ControlFill, ControlHover, f.HoverIntensity) : ControlFill;
        if (Pressed(f, hit.Control, hit.Index)) fill = Darken(fill, PressDarken);
        FillRound(g, r, 7f * s, fill);
        var color = selected ? _t.OnAccent : _t.Foreground;
        DrawText(g, text, selected ? _bold! : _body!, color, r, _center);
        if (!recommended) return;
        float tw = g.MeasureString(text, selected ? _bold! : _body!, int.MaxValue, _left).Width;
        float d = 4f * s, x = Math.Min(r.Right - d - 4f * s, r.Left + (r.Width + tw) / 2f + 4f * s);
        _brush.Color = Fade(selected ? _t.OnAccent : _t.Accent);
        g.FillEllipse(_brush, x, r.Top + (r.Height - d) / 2f, d, d);
    }

    // ---- Claude Code

    private void PaintClaude(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        DrawText(g, v.ClaudeInfo, _body!, _t.Foreground, OnboardingGeometry.ClaudeInfo(pill, s), _left);
        ClaudeToggle(g, pill, s, f, OnboardingControl.LiveSessions, v.Live, v.LiveHint);
        ClaudeToggle(g, pill, s, f, OnboardingControl.Permissions, v.Permissions, v.PermissionsHint);
    }

    private void ClaudeToggle(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingControl control, OnboardingToggle t,
        string hint)
    {
        var (row, hintRect) = OnboardingGeometry.ClaudeRow(pill, s, control);
        HoverRow(g, row, s, f, new OnboardingHit(control), t.Enabled);
        var sw = new RectangleF(row.Left + 4f * s, row.Top + (row.Height - OnboardingGeometry.SwitchHeight * s) / 2f,
            OnboardingGeometry.SwitchWidth * s, OnboardingGeometry.SwitchHeight * s);
        var label = RectangleF.FromLTRB(sw.Right + 10f * s, row.Top, row.Right, row.Bottom);
        ToggleRow(g, sw, label, RectangleF.Empty, t with { Detail = "" }, Knob(f, control, 0, t.On), s);
        DrawWrapped(g, hint, _caption!, _t.TextMuted, hintRect);
    }

    // ---- resumen

    private void PaintSummary(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingView v)
    {
        int line = 0;
        foreach (var row in v.Summary)
        {
            if (line >= OnboardingGeometry.SummaryMaxLines) break;
            int lines = Math.Clamp(row.Lines, 1, OnboardingGeometry.SummaryMaxLines - line);
            var (label, value) = OnboardingGeometry.SummaryRow(pill, s, line, lines);
            DrawText(g, row.Label, _body!, _t.TextMuted, label, _left);
            if (lines == 1) DrawText(g, row.Value, _bold!, _t.Foreground, value, _left);
            else DrawWrapped(g, row.Value, _bold!, _t.Foreground, RectangleF.FromLTRB(value.Left, value.Top + 3f * s, value.Right, value.Bottom));
            line += lines;
            // Separador fino entre filas, como en el panel.
            _brush.Color = Fade(Color.FromArgb(18, 255, 255, 255));
            g.FillRectangle(_brush, label.Left, value.Bottom + 1f * s, value.Right - label.Left, Math.Max(1f, s));
        }
        DrawWrapped(g, v.SummaryNote, _caption!, _t.TextMuted, OnboardingGeometry.SummaryNote(pill, s));
        if (v.Tutorial is { } tutorial)
        {
            // «Ver el tutorial al terminar (2 min)»: el interruptor con su texto, como la línea del contador de la bienvenida.
            var row = OnboardingGeometry.TutorialRow(pill, s);
            HoverRow(g, row, s, f, new OnboardingHit(OnboardingControl.Tutorial), tutorial.Enabled);
            var (sw, text) = OnboardingGeometry.UsageStatsParts(row, s);
            Switch(g, sw, Knob(f, OnboardingControl.Tutorial, 0, tutorial.On), s);
            DrawText(g, tutorial.Label, _caption!, _t.Foreground, text, _wrapMiddle);
        }
    }

    // ---- pie, aviso y foco

    private void PaintFooter(Graphics g, RectangleF pill, float s, OnboardingFrame f)
    {
        var v = f.View;
        if (v.CanGoBack) Button(g, OnboardingGeometry.Back(pill, s), v.Back, primary: false, s, f, new(OnboardingControl.Back));
        Button(g, OnboardingGeometry.Next(pill, s), v.Next, primary: true, s, f, new(OnboardingControl.Next));

        // Puntos y píldora como los del expandido (§3.9 #5).
        int n = v.Pages.Count;
        float d = NotchGeometry.PageDotDiameter * s;
        for (int i = 0; i < n; i++)
        {
            float away = Math.Clamp(Math.Abs(i - f.IndicatorPos), 0f, 1f);
            if (away <= 0f) continue;
            var c = OnboardingGeometry.DotCenter(pill, s, n, i);
            bool hot = Hovered(f, OnboardingControl.Dot, i);
            _brush.Color = Fade(Color.FromArgb((int)(255 * (hot ? 0.6f : DotAlpha) * away), _t.Foreground));
            g.FillEllipse(_brush, c.X - d / 2f, c.Y - d / 2f, d, d);
        }
        var ic = OnboardingGeometry.DotCenter(pill, s, n, f.IndicatorPos);
        float w = NotchGeometry.PageIndicatorWidth * s, h = Math.Max(2f, NotchGeometry.PageIndicatorHeight * s);
        SetPen(_t.Foreground, h);
        g.DrawLine(_pen, ic.X - (w - h) / 2f, ic.Y, ic.X + (w - h) / 2f, ic.Y);

        var skip = OnboardingGeometry.Skip(pill, s);
        bool skipHot = Hovered(f, OnboardingControl.Skip, 0);
        DrawText(g, v.Skip, _caption!, skipHot ? _t.Foreground : _t.TextMuted, skip, _right);
    }

    private void PaintOverlay(Graphics g, RectangleF pill, float s, OnboardingFrame f, OnboardingOverlayView o)
    {
        float saved = _alpha;
        _alpha = Quantize(saved * f.OverlayAlpha);
        try
        {
            _brush.Color = Fade(OverlayDim);
            g.FillRectangle(_brush, pill);
            var card = OnboardingGeometry.OverlayCard(pill, s);
            FillRound(g, card, 14f * s, OverlayFill);
            using (var path = RoundPath(card, 14f * s))
            {
                SetPen(Color.FromArgb(26, 255, 255, 255), Math.Max(1f, s));
                g.DrawPath(_pen, path);
            }
            DrawText(g, o.Title, _title!, _t.Foreground, OnboardingGeometry.OverlayTitle(pill, s), _left);
            DrawWrapped(g, o.Text, _body!, _t.TextMuted, OnboardingGeometry.OverlayText(pill, s));
            Button(g, OnboardingGeometry.OverlaySecondary(pill, s), o.Secondary, primary: false, s, f, new(OnboardingControl.OverlaySecondary));
            Button(g, OnboardingGeometry.OverlayPrimary(pill, s), o.Primary, primary: true, s, f, new(OnboardingControl.OverlayPrimary));
        }
        finally { _alpha = saved; }
    }

    /// <summary>Anillo de foco (teclado): 2 px de acento alrededor del control con el foco.</summary>
    private void PaintFocus(Graphics g, RectangleF pill, float s, OnboardingFrame f)
    {
        RectangleF r = RectangleF.Empty;
        foreach (var (hit, rect) in OnboardingGeometry.Controls(f.View.Page, pill, s, f.Shape))
            if (OnboardingGeometry.SameControl(hit, f.Focus) && (hit.Index == f.Focus.Index || r.IsEmpty))
            {
                r = rect;
                if (hit.Index == f.Focus.Index) break;
            }
        if (r.IsEmpty) return;
        r = RectangleF.Inflate(r, 3f * s, 3f * s);
        using var path = RoundPath(r, 9f * s);
        SetPen(_t.Accent, 2f * s);
        g.DrawPath(_pen, path);
    }

    // ---------------------------------------------------------------- piezas

    private void Button(Graphics g, RectangleF r, string text, bool primary, float s, OnboardingFrame f, OnboardingHit hit) =>
        Button(g, r, text, primary, s, Hovered(f, hit.Control, hit.Index), f.HoverIntensity, Pressed(f, hit.Control, hit.Index));

    /// <summary>Botón del pie (el principal con el acento y <see cref="Theme.OnAccent"/>): con el ratón encima cambia de tono
    /// con la intensidad animada sin perder contraste (<see cref="ColorMath.HoverFill"/>); pulsado encoge 1 px y se oscurece.</summary>
    private void Button(Graphics g, RectangleF r, string text, bool primary, float s, bool hot, float intensity, bool pressed)
    {
        if (pressed) r = RectangleF.Inflate(r, -Math.Max(1f, MathF.Round(s)), -Math.Max(1f, MathF.Round(s)));
        var fill = primary ? _t.Accent : ControlFill;
        if (hot) fill = primary ? ColorMath.HoverFill(fill, intensity) : Lerp(ControlFill, ControlHover, intensity);
        if (pressed) fill = Darken(fill, PressDarken);
        FillRound(g, r, 8f * s, fill);
        DrawText(g, text, primary ? _bold! : _body!, primary ? _t.OnAccent : _t.Foreground, r, _center);
    }

    /// <summary>Realce de una fila con el ratón encima (blanco tenue que entra con la intensidad animada).</summary>
    private void HoverRow(Graphics g, RectangleF row, float s, OnboardingFrame f, OnboardingHit hit, bool enabled)
    {
        if (!enabled || !Hovered(f, hit.Control, hit.Index) || f.HoverIntensity <= 0.01f) return;
        var r = RectangleF.FromLTRB(row.Left - 6f * s, row.Top - 1f * s, row.Right + 4f * s, row.Bottom + 1f * s);
        FillRound(g, r, 8f * s, Color.FromArgb((int)(16 * f.HoverIntensity), 255, 255, 255));
    }

    private static bool Hovered(OnboardingFrame f, OnboardingControl c, int index) => f.Hover.Control == c && f.Hover.Index == index;
    private static bool Pressed(OnboardingFrame f, OnboardingControl c, int index) =>
        f.Pressed.Control == c && f.Pressed.Index == index && f.Hover.Control == c && f.Hover.Index == index;

    private static float Knob(OnboardingFrame f, OnboardingControl c, int index, bool on)
    {
        int i = KnobIndex(c, index);
        return f.Knobs is { } k && i >= 0 && i < k.Count ? k[i] : on ? 1f : 0f;
    }

    private void FillRound(Graphics g, RectangleF r, float radius, Color color)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundPath(r, radius);
        _brush.Color = Fade(color);
        g.FillPath(_brush, path);
    }

    private static GraphicsPath RoundPath(RectangleF r, float radius)
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

    private void DrawText(Graphics g, string? text, Font font, Color color, RectangleF r, StringFormat fmt)
    {
        if (r.Width <= 1f || r.Height <= 1f || string.IsNullOrEmpty(text)) return;
        _brush.Color = Fade(color);
        g.DrawString(text, font, _brush, r, fmt);
    }

    /// <summary>Texto con ajuste de línea dentro de <paramref name="r"/> (lo que no cabe, con «…» por palabra).</summary>
    private void DrawWrapped(Graphics g, string? text, Font font, Color color, RectangleF r) => DrawText(g, text, font, color, r, _wrap);

    private void SetPen(Color c, float width)
    {
        _pen.Color = Fade(c);
        _pen.Width = width;
    }

    private Color Fade(Color c) => _alpha >= 1f ? c : Color.FromArgb((int)(c.A * _alpha), c);

    private static float Quantize(float a) => NotchContentPainter.Quantize(a);

    private static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb((int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static Color Darken(Color c, float amount) =>
        Color.FromArgb(c.A, (int)(c.R * (1f - amount)), (int)(c.G * (1f - amount)), (int)(c.B * (1f - amount)));

    private static void SetQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.Bilinear;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    /// <summary>Fuentes en PÍXELES (el Graphics del DIB está a 96 DPI y la escala la ponemos nosotros), por escala.</summary>
    private void EnsureFonts(float scale)
    {
        if (Math.Abs(scale - _fontScale) < 0.001f && _body is not null) return;
        DisposeFonts();
        // Otro DPI: los logos pintados para el anterior ya no valen (se rehacen en el siguiente frame).
        _logo.Invalidate();
        _miniLogo.Invalidate();
        string family = Typography.Body.FontFamily.Name;
        _display = new Font(family, 30f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _title = new Font(family, 19f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _body = new Font(family, 13f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _bold = new Font(family, 13f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _caption = new Font(family, 11.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _small = new Font(family, 10.5f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _fontScale = scale;
    }

    private void DisposeFonts()
    {
        _display?.Dispose(); _title?.Dispose(); _body?.Dispose(); _bold?.Dispose(); _caption?.Dispose(); _small?.Dispose();
        _display = _title = _body = _bold = _caption = _small = null;
        _miniNameFont = null;
    }

    public void Dispose()
    {
        DisposeFonts();
        _logo.Dispose();
        _miniLogo.Dispose();
        _notch.Dispose();
        _brush.Dispose();
        _pen.Dispose();
        _left.Dispose();
        _right.Dispose();
        _center.Dispose();
        _wrap.Dispose();
        _wrapMiddle.Dispose();
    }
}
