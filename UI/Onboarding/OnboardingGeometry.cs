using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>Un control del asistente que recibe clics y foco. <see cref="OnboardingHit.Index"/> dice cuál en los que son
/// varios (tarjeta = perfil, casilla = <see cref="SetupFeature"/>, segmento, punto de página).</summary>
public enum OnboardingControl
{
    None, Skip, Back, Next, Dot,
    Card, Feature, ResetFeatures,
    /// <summary>Selectores que ciclan: el control entero va adelante; su flecha ‹, atrás.</summary>
    Language, LanguagePrev,
    Monitor, MonitorPrev,
    Size, Open, Startup, ResetSettings,
    LiveSessions, Permissions,
    /// <summary>Botón principal del aviso que tapa la página («Saltar» / «Reintentar»).</summary>
    OverlayPrimary,
    /// <summary>Botón secundario del aviso («Seguir configurando» / «Cerrar»).</summary>
    OverlaySecondary,
    /// <summary>Interruptor del contador anónimo de uso, en la bienvenida.</summary>
    UsageStats,
    /// <summary>«Ver el tutorial al terminar (2 min)», en el resumen.</summary>
    Tutorial,
}

/// <summary>Control bajo el ratón o con el foco.</summary>
public readonly record struct OnboardingHit(OnboardingControl Control, int Index = 0)
{
    public static readonly OnboardingHit None = new(OnboardingControl.None);

    /// <summary>Los botones de verdad (Enter los pulsa); en el resto, Enter es «Siguiente».</summary>
    public bool IsButton => Control is OnboardingControl.Skip or OnboardingControl.Back or OnboardingControl.Next
        or OnboardingControl.ResetFeatures or OnboardingControl.ResetSettings
        or OnboardingControl.OverlayPrimary or OnboardingControl.OverlaySecondary;
}

/// <summary>Lo que cambia la maquetación de una página: cuántas páginas hay (los puntos), si se pregunta el monitor y el
/// aviso que tapa la página.</summary>
public readonly record struct OnboardingShape(int PageCount, bool AskMonitor, OnboardingOverlay Overlay);

/// <summary>
/// Geometría del asistente de bienvenida dentro del notch. PURA (sin Win32 ni estado): la usan el painter para pintar y la
/// ventana para acertar el clic y mover el foco, así pintar y acertar salen de la misma cuenta (como
/// <see cref="NotchGeometry"/>). Todo en px lógicos × escala y relativo a la pastilla, que va centrada arriba.
///
/// <code>
///  ┌──────────────────────────────── 680 ────────────────────────────────┐
///  │  Título                                               Saltar · Esc  │
///  │  Subtítulo                                                          │
///  │  ┌─ cuerpo de la página (624 × 284) ──────────────────────────────┐ │ 440
///  │  └────────────────────────────────────────────────────────────────┘ │
///  │  [ Atrás ]                 ● ● ━ ● ●                 [ Siguiente ]  │
///  └─────────────────────────────────────────────────────────────────────┘
/// </code>
/// </summary>
public static class OnboardingGeometry
{
    /// <summary>Tamaño del asistente abierto (px lógicos): cabe en 1366 × 768 a 100 % con la barra de tareas y la sombra.</summary>
    public const float Width = 680f, Height = 440f;

    public const float PadX = 28f, BodyTop = 88f, BodyBottom = 372f;
    public const float ButtonHeight = 32f, BackWidth = 104f, NextWidth = 140f, FooterBottom = 20f;
    public const float CardHeight = 50f, CardPitch = 56f, CardColumn = 330f;
    public const float ToggleRowHeight = 38f, TogglePitch = 40f, ToggleColumnGap = 20f, SwitchWidth = 34f, SwitchHeight = 20f;
    public const float SettingPitch = 50f, SettingLabel = 150f, SettingControlX = 190f, ControlHeight = 28f;
    public const float SizeSegment = 78f, SizeSegmentGap = 6f, OpenSegment = 196f;
    public const float SummaryPitch = 27f;

    /// <summary>Líneas del resumen que caben sobre la nota de la copia (siete filas y lo activado en tres).</summary>
    public const int SummaryMaxLines = 9;

    /// <summary>Casillas de la columna izquierda de «Qué se activa» (el resto, a la derecha).</summary>
    public const int LeftToggles = 6;

    /// <summary>Caja de la pastilla de <paramref name="width"/> × <paramref name="height"/> con su centro en
    /// <paramref name="centerX"/> (px de la superficie) y pegada arriba.</summary>
    public static RectangleF PillRect(float width, float height, float centerX) =>
        new(centerX - width / 2f, 0f, Math.Max(0f, width), Math.Max(0f, height));

    private static RectangleF R(RectangleF pill, float s, float x, float y, float w, float h) =>
        new(pill.Left + x * s, pill.Top + y * s, w * s, h * s);

    // ---------------------------------------------------------------- cabecera y pie

    public static RectangleF Title(RectangleF pill, float s) => R(pill, s, PadX, 26f, 470f, 26f);
    public static RectangleF Subtitle(RectangleF pill, float s) => R(pill, s, PadX, 54f, Width - 2 * PadX, 18f);
    public static RectangleF Skip(RectangleF pill, float s) => R(pill, s, Width - PadX - 150f, 26f, 150f, 22f);

    public static RectangleF Back(RectangleF pill, float s) =>
        R(pill, s, PadX, Height - FooterBottom - ButtonHeight, BackWidth, ButtonHeight);

    public static RectangleF Next(RectangleF pill, float s) =>
        R(pill, s, Width - PadX - NextWidth, Height - FooterBottom - ButtonHeight, NextWidth, ButtonHeight);

    /// <summary>Centro del punto <paramref name="index"/> de <paramref name="count"/> (puntos y píldora como los del
    /// expandido: <see cref="NotchGeometry.PageDotPitch"/>), en la fila de los botones del pie.</summary>
    public static PointF DotCenter(RectangleF pill, float s, int count, float index)
    {
        float pitch = NotchGeometry.PageDotPitch * s;
        float x = pill.Left + pill.Width / 2f + (index - (count - 1) / 2f) * pitch;
        float y = pill.Top + (Height - FooterBottom - ButtonHeight / 2f) * s;
        return new PointF(x, y);
    }

    /// <summary>Celda del punto <paramref name="index"/>: el paso entero por el alto de los botones (un punto de 6 px solo
    /// sería imposible de acertar).</summary>
    public static RectangleF DotCell(RectangleF pill, float s, int count, int index)
    {
        var c = DotCenter(pill, s, count, index);
        float pitch = NotchGeometry.PageDotPitch * s;
        return new RectangleF(c.X - pitch / 2f, c.Y - ButtonHeight * s / 2f, pitch, ButtonHeight * s);
    }

    // ---------------------------------------------------------------- bienvenida

    public static RectangleF Logo(RectangleF pill, float s) => R(pill, s, (Width - 96f) / 2f, 70f, 96f, 96f);
    public static RectangleF Wordmark(RectangleF pill, float s) => R(pill, s, PadX, 182f, Width - 2 * PadX, 40f);
    public static RectangleF Tagline(RectangleF pill, float s) => R(pill, s, 68f, 228f, Width - 136f, 22f);
    public static RectangleF Detected(RectangleF pill, float s) => R(pill, s, 128f, 268f, Width - 256f, 26f);
    public static RectangleF KeysHint(RectangleF pill, float s) => R(pill, s, PadX, 336f, Width - 2 * PadX, 18f);

    /// <summary>La línea del contador anónimo de uso (interruptor + explicación en hasta dos líneas), entre la cápsula de
    /// Claude Code y la ayuda del teclado.</summary>
    public static RectangleF UsageStatsRow(RectangleF pill, float s) => R(pill, s, 92f, 300f, Width - 184f, 34f);

    /// <summary>Partes de la línea del contador: el interruptor (centrado en alto) y el texto a su derecha.</summary>
    public static (RectangleF Switch, RectangleF Text) UsageStatsParts(RectangleF row, float s)
    {
        float sh = SwitchHeight * s;
        return (new RectangleF(row.Left, row.Top + (row.Height - sh) / 2f, SwitchWidth * s, sh),
            RectangleF.FromLTRB(row.Left + 44f * s, row.Top, row.Right, row.Bottom));
    }

    // ---------------------------------------------------------------- perfiles

    /// <summary>Tarjeta del perfil <paramref name="index"/> (en el orden de <see cref="SetupProfiles.All"/>).</summary>
    public static RectangleF Card(RectangleF pill, float s, int index) =>
        R(pill, s, PadX, BodyTop + index * CardPitch, CardColumn, CardHeight);

    /// <summary>Partes de una tarjeta: icono, título, la línea de debajo y la etiqueta «Recomendado».</summary>
    public static (RectangleF Icon, RectangleF Title, RectangleF Line, RectangleF Badge) CardParts(RectangleF card, float s)
    {
        float x = card.Left, y = card.Top, w = card.Width;
        var badge = new RectangleF(x + w - 12f * s - 96f * s, y + 9f * s, 96f * s, 16f * s);
        return (new RectangleF(x + 12f * s, y + 11f * s, 28f * s, 28f * s),
            new RectangleF(x + 52f * s, y + 7f * s, w - 64f * s, 19f * s),
            new RectangleF(x + 52f * s, y + 27f * s, w - 64f * s, 16f * s),
            badge);
    }

    /// <summary>El escritorio en miniatura donde se ve el notch del perfil (vista previa en vivo).</summary>
    public static RectangleF PreviewStage(RectangleF pill, float s) =>
        R(pill, s, PadX + CardColumn + 16f, BodyTop, Width - 2 * PadX - CardColumn - 16f, 196f);

    /// <summary>La explicación bajo la vista previa (y el aviso de que falta Claude Code).</summary>
    public static RectangleF PreviewCaption(RectangleF pill, float s) =>
        R(pill, s, PadX + CardColumn + 16f, BodyTop + 204f, Width - 2 * PadX - CardColumn - 16f, BodyBottom - BodyTop - 204f);

    /// <summary>Escala de la vista previa respecto a la del asistente: Trabajando (300 + texto) cabe en el escritorio.</summary>
    public const float PreviewScale = 0.78f;

    // ---------------------------------------------------------------- qué se activa

    /// <summary>Fila de la casilla <paramref name="f"/>: dos columnas, seis a la izquierda.</summary>
    public static RectangleF ToggleRow(RectangleF pill, float s, SetupFeature f)
    {
        int i = (int)f;
        float colW = (Width - 2 * PadX - ToggleColumnGap) / 2f;
        bool left = i < LeftToggles;
        float x = PadX + (left ? 0f : colW + ToggleColumnGap);
        float y = BodyTop + (left ? i : i - LeftToggles) * TogglePitch;
        return R(pill, s, x, y, colW, ToggleRowHeight);
    }

    /// <summary>Partes de una fila con interruptor: el interruptor, el nombre y la línea gris de debajo.</summary>
    public static (RectangleF Switch, RectangleF Label, RectangleF Detail) ToggleParts(RectangleF row, float s)
    {
        float x = row.Left, y = row.Top;
        return (new RectangleF(x, y + 9f * s, SwitchWidth * s, SwitchHeight * s),
            new RectangleF(x + 44f * s, y + 1f * s, row.Width - 44f * s, 19f * s),
            new RectangleF(x + 44f * s, y + 20f * s, row.Width - 44f * s, 16f * s));
    }

    public static RectangleF FeaturesHint(RectangleF pill, float s) => R(pill, s, PadX, 338f, 420f, 34f);

    /// <summary>Nota del Pro: el hueco libre al final de la columna derecha (debajo de la última casilla, sin su interruptor).</summary>
    public static RectangleF ProNote(RectangleF pill, float s)
    {
        float colW = (Width - 2 * PadX - ToggleColumnGap) / 2f;
        int rightRows = SetupFeatures.All.Count - LeftToggles;
        return R(pill, s, PadX + colW + ToggleColumnGap + 44f, BodyTop + rightRows * TogglePitch, colW - 44f, ToggleRowHeight);
    }

    /// <summary>«Restablecer recomendados» (Qué se activa y Ajustes), abajo a la derecha del cuerpo.</summary>
    public static RectangleF ResetButton(RectangleF pill, float s) => R(pill, s, Width - PadX - 190f, 340f, 190f, 30f);

    // ---------------------------------------------------------------- ajustes recomendados

    /// <summary>Filas de los ajustes, en orden: idioma, monitor (solo si se pregunta), tamaño, apertura e inicio con Windows.</summary>
    public static IReadOnlyList<OnboardingControl> SettingRows(bool askMonitor) => askMonitor
        ? new[] { OnboardingControl.Language, OnboardingControl.Monitor, OnboardingControl.Size, OnboardingControl.Open, OnboardingControl.Startup }
        : new[] { OnboardingControl.Language, OnboardingControl.Size, OnboardingControl.Open, OnboardingControl.Startup };

    /// <summary>Partes de la fila del ajuste <paramref name="row"/>: etiqueta, control y la explicación de debajo.
    /// Vacías si esa fila no se ve.</summary>
    public static (RectangleF Label, RectangleF Control, RectangleF Hint) SettingParts(RectangleF pill, float s,
        OnboardingControl row, bool askMonitor)
    {
        int i = IndexOf(SettingRows(askMonitor), row);
        if (i < 0) return (RectangleF.Empty, RectangleF.Empty, RectangleF.Empty);
        float y = BodyTop + i * SettingPitch;
        float cw = row switch
        {
            OnboardingControl.Size => AppConfig.NotchSizeOptions.Length * (SizeSegment + SizeSegmentGap) - SizeSegmentGap,
            OnboardingControl.Open => 2 * OpenSegment + SizeSegmentGap,
            OnboardingControl.Startup => SwitchWidth,
            _ => Width - PadX - SettingControlX,
        };
        float ch = row == OnboardingControl.Startup ? SwitchHeight : ControlHeight;
        float cy = row == OnboardingControl.Startup ? y + (ControlHeight - SwitchHeight) / 2f : y;
        return (R(pill, s, PadX, y + 5f, SettingLabel, 18f), R(pill, s, SettingControlX, cy, cw, ch),
            R(pill, s, SettingControlX, y + ControlHeight + 2f, Width - PadX - SettingControlX, 15f));
    }

    /// <summary>Segmento <paramref name="index"/> de un control segmentado (tamaño u apertura).</summary>
    public static RectangleF Segment(RectangleF control, float s, OnboardingControl row, int index)
    {
        float w = (row == OnboardingControl.Open ? OpenSegment : SizeSegment) * s, gap = SizeSegmentGap * s;
        return new RectangleF(control.Left + index * (w + gap), control.Top, w, control.Height);
    }

    /// <summary>Flechas ‹ › de un selector que cicla (idioma, monitor): a cada lado del control.</summary>
    public static (RectangleF Prev, RectangleF Next) CyclerArrows(RectangleF control) =>
        (new RectangleF(control.Left, control.Top, control.Height, control.Height),
            new RectangleF(control.Right - control.Height, control.Top, control.Height, control.Height));

    /// <summary>La nota «el notch está apagado», abajo a la izquierda del cuerpo de los ajustes.</summary>
    public static RectangleF NotchOffNote(RectangleF pill, float s) => R(pill, s, PadX, 340f, 420f, 30f);

    // ---------------------------------------------------------------- Claude Code

    public static RectangleF ClaudeInfo(RectangleF pill, float s) => R(pill, s, PadX, BodyTop, Width - 2 * PadX, 20f);

    /// <summary>Fila (interruptor + texto) de las sesiones en vivo o de los permisos, y su explicación debajo.</summary>
    public static (RectangleF Row, RectangleF Hint) ClaudeRow(RectangleF pill, float s, OnboardingControl row)
    {
        float y = row == OnboardingControl.LiveSessions ? 120f : 222f;
        float hint = row == OnboardingControl.LiveSessions ? 64f : 80f;
        return (R(pill, s, PadX, y, Width - 2 * PadX, 26f), R(pill, s, PadX + 44f, y + 28f, Width - 2 * PadX - 44f, hint));
    }

    // ---------------------------------------------------------------- resumen

    /// <summary>Fila del resumen que empieza en la línea <paramref name="line"/> y ocupa <paramref name="lines"/> (lo
    /// activado puede ocupar varias): la etiqueta a la izquierda (una línea) y el valor, con ajuste de línea, a la derecha.</summary>
    public static (RectangleF Label, RectangleF Value) SummaryRow(RectangleF pill, float s, int line, int lines = 1) =>
        (R(pill, s, PadX, BodyTop + 4f + line * SummaryPitch, 210f, 24f),
            R(pill, s, PadX + 218f, BodyTop + 4f + line * SummaryPitch, Width - 2 * PadX - 218f,
                Math.Max(1, lines) * SummaryPitch - 4f));

    /// <summary>La nota de la copia de config.json, abajo a la izquierda (hasta dos líneas): a su derecha, el tutorial.</summary>
    public static RectangleF SummaryNote(RectangleF pill, float s) => R(pill, s, PadX, 342f, 300f, 30f);

    /// <summary>«Ver el tutorial al terminar (2 min)»: interruptor y texto, abajo a la derecha del resumen.</summary>
    public static RectangleF TutorialRow(RectangleF pill, float s) => R(pill, s, PadX + 316f, 340f, Width - 2 * PadX - 316f, 34f);

    // ---------------------------------------------------------------- aviso encima

    public static RectangleF OverlayCard(RectangleF pill, float s) => R(pill, s, 150f, 112f, 380f, 208f);
    public static RectangleF OverlayTitle(RectangleF pill, float s) => R(pill, s, 174f, 132f, 332f, 24f);
    public static RectangleF OverlayText(RectangleF pill, float s) => R(pill, s, 174f, 162f, 332f, 96f);
    public static RectangleF OverlaySecondary(RectangleF pill, float s) => R(pill, s, 174f, 272f, 160f, ButtonHeight);
    public static RectangleF OverlayPrimary(RectangleF pill, float s) => R(pill, s, 346f, 272f, 160f, ButtonHeight);

    // ---------------------------------------------------------------- controles, clic y foco

    /// <summary>
    /// Los controles de la página que reciben clic, con su rectángulo, en orden de tabulación. Con un aviso encima, solo sus
    /// dos botones (lo de debajo no se puede tocar). El pie: Atrás (no en la primera), los puntos y Siguiente; «Saltar», el
    /// último. Sin reservas que dependan del estado: lo deshabilitado se pinta atenuado y lo descarta la ventana.
    /// </summary>
    public static IReadOnlyList<(OnboardingHit Hit, RectangleF Rect)> Controls(OnboardingPage page, RectangleF pill, float s,
        OnboardingShape shape)
    {
        var list = new List<(OnboardingHit, RectangleF)>(24);
        if (shape.Overlay != OnboardingOverlay.None)
        {
            list.Add((new(OnboardingControl.OverlaySecondary), OverlaySecondary(pill, s)));
            list.Add((new(OnboardingControl.OverlayPrimary), OverlayPrimary(pill, s)));
            return list;
        }
        switch (page)
        {
            case OnboardingPage.Welcome:
                list.Add((new(OnboardingControl.UsageStats), UsageStatsRow(pill, s)));
                break;
            case OnboardingPage.Profile:
                for (int i = 0; i < SetupProfiles.All.Count; i++) list.Add((new(OnboardingControl.Card, i), Card(pill, s, i)));
                break;
            case OnboardingPage.Features:
                foreach (var f in SetupFeatures.All) list.Add((new(OnboardingControl.Feature, (int)f), ToggleRow(pill, s, f)));
                list.Add((new(OnboardingControl.ResetFeatures), ResetButton(pill, s)));
                break;
            case OnboardingPage.Settings:
                foreach (var row in SettingRows(shape.AskMonitor))
                {
                    var control = SettingParts(pill, s, row, shape.AskMonitor).Control;
                    switch (row)
                    {
                        case OnboardingControl.Size:
                            for (int i = 0; i < AppConfig.NotchSizeOptions.Length; i++)
                                list.Add((new(row, i), Segment(control, s, row, i)));
                            break;
                        case OnboardingControl.Open:
                            for (int i = 0; i < 2; i++) list.Add((new(row, i), Segment(control, s, row, i)));
                            break;
                        case OnboardingControl.Startup:
                            // El interruptor y su etiqueta: toda la fila desde la etiqueta.
                            var label = SettingParts(pill, s, row, shape.AskMonitor).Label;
                            list.Add((new(row), RectangleF.FromLTRB(label.Left, control.Top - 4f * s, control.Right + 8f * s,
                                control.Bottom + 4f * s)));
                            break;
                        default:
                            // Selector que cicla: la flecha de la izquierda va atrás; el resto (texto y flecha derecha), adelante.
                            var (prev, _) = CyclerArrows(control);
                            var back = row == OnboardingControl.Language ? OnboardingControl.LanguagePrev : OnboardingControl.MonitorPrev;
                            list.Add((new(row), control));
                            list.Add((new(back), prev));
                            break;
                    }
                }
                list.Add((new(OnboardingControl.ResetSettings), ResetButton(pill, s)));
                break;
            case OnboardingPage.ClaudeCode:
                list.Add((new(OnboardingControl.LiveSessions), ClaudeRow(pill, s, OnboardingControl.LiveSessions).Row));
                list.Add((new(OnboardingControl.Permissions), ClaudeRow(pill, s, OnboardingControl.Permissions).Row));
                break;
            case OnboardingPage.Summary:
                list.Add((new(OnboardingControl.Tutorial), TutorialRow(pill, s)));
                break;
        }
        if (page != OnboardingPage.Welcome) list.Add((new(OnboardingControl.Back), Back(pill, s)));
        for (int i = 0; i < shape.PageCount; i++) list.Add((new(OnboardingControl.Dot, i), DotCell(pill, s, shape.PageCount, i)));
        list.Add((new(OnboardingControl.Next), Next(pill, s)));
        list.Add((new(OnboardingControl.Skip), Skip(pill, s)));
        return list;
    }

    /// <summary>
    /// Control bajo <paramref name="p"/> (centro del píxel), o <see cref="OnboardingHit.None"/>. Se busca del último al
    /// primero: la flecha ‹ de un selector va después del selector entero en <see cref="Controls"/> y gana dentro de él.
    /// </summary>
    public static OnboardingHit HitTest(Point p, OnboardingPage page, RectangleF pill, float s, OnboardingShape shape)
    {
        if (!pill.Contains(p.X + 0.5f, p.Y + 0.5f)) return OnboardingHit.None;
        var controls = Controls(page, pill, s, shape);
        for (int i = controls.Count - 1; i >= 0; i--)
            if (controls[i].Rect.Contains(p.X + 0.5f, p.Y + 0.5f)) return controls[i].Hit;
        return OnboardingHit.None;
    }

    /// <summary>
    /// Lo que recorre Tab, en orden: los controles de <see cref="Controls"/> que se pueden usar ahora
    /// (<paramref name="usable"/>), sin las flechas ‹ (las cambian ← →), sin los puntos (son atajos del ratón) y con un solo
    /// segmento por control segmentado (el elegido: ← → lo mueven).
    /// </summary>
    public static IReadOnlyList<OnboardingHit> FocusOrder(OnboardingPage page, OnboardingShape shape,
        Func<OnboardingHit, bool> usable, Func<OnboardingControl, int> selectedSegment)
    {
        ArgumentNullException.ThrowIfNull(usable);
        ArgumentNullException.ThrowIfNull(selectedSegment);
        var order = new List<OnboardingHit>(16);
        foreach (var (hit, _) in Controls(page, new RectangleF(0, 0, Width, Height), 1f, shape))
        {
            if (hit.Control is OnboardingControl.LanguagePrev or OnboardingControl.MonitorPrev or OnboardingControl.Dot) continue;
            if (hit.Control is OnboardingControl.Size or OnboardingControl.Open && hit.Index != selectedSegment(hit.Control)) continue;
            if (usable(hit)) order.Add(hit);
        }
        return order;
    }

    /// <summary>El siguiente (o anterior, <paramref name="delta"/> &lt; 0) de <paramref name="order"/> tras
    /// <paramref name="current"/>, dando la vuelta; sin foco, el primero (o el último).</summary>
    public static OnboardingHit MoveFocus(IReadOnlyList<OnboardingHit> order, OnboardingHit current, int delta)
    {
        if (order.Count == 0) return OnboardingHit.None;
        int i = -1;
        for (int k = 0; k < order.Count; k++)
            if (SameControl(order[k], current)) { i = k; break; }
        if (i < 0) return delta < 0 ? order[^1] : order[0];
        int n = order.Count;
        return order[((i + Math.Sign(delta == 0 ? 1 : delta)) % n + n) % n];
    }

    /// <summary>¿Mismo control? En los segmentados vale cualquier segmento (el foco va con el elegido).</summary>
    public static bool SameControl(OnboardingHit a, OnboardingHit b) =>
        a.Control == b.Control && (a.Index == b.Index || a.Control is OnboardingControl.Size or OnboardingControl.Open);

    private static int IndexOf(IReadOnlyList<OnboardingControl> list, OnboardingControl c)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == c) return i;
        return -1;
    }
}

/// <summary>Dónde y a qué escala va el asistente. PURO: la ventana le pasa el DPI y los límites del monitor.</summary>
public static class OnboardingPlacement
{
    /// <summary>Margen (px lógicos) para la sombra a los lados y debajo de la pastilla (el de <c>NotchMotion.ShadowMargin</c>).</summary>
    public const float ShadowMargin = Services.Notch.NotchMotion.ShadowMargin;

    /// <summary>
    /// Escala del asistente: la del DPI, salvo que el asistente (con su sombra) no quepa en el monitor o, hacia abajo, en el
    /// área de trabajo (sin la barra de tareas): entonces lo justo para caber. No usa el tamaño del notch (100–150 %): el
    /// asistente siempre se lee igual. Nunca por debajo de 0,5.
    /// </summary>
    public static float Scale(int dpi, Rectangle monitor, Rectangle workArea) =>
        Scale(dpi, monitor, workArea, OnboardingGeometry.Width, OnboardingGeometry.Height);

    /// <summary>Como <see cref="Scale(int, Rectangle, Rectangle)"/> para otra pastilla de <paramref name="width"/> ×
    /// <paramref name="height"/> px lógicos (el tutorial, más alto que el asistente).</summary>
    public static float Scale(int dpi, Rectangle monitor, Rectangle workArea, float width, float height)
    {
        float s = ClaudeBarWin.Services.Dpi.FactorFor(dpi);
        float down = (workArea.IsEmpty ? monitor.Bottom : workArea.Bottom) - monitor.Top;
        float fitH = down / (height + ShadowMargin);
        float fitW = monitor.Width / (width + 2 * ShadowMargin);
        float fit = MathF.Floor(Math.Min(fitH, fitW) * 100f) / 100f;
        return Math.Max(0.5f, Math.Min(s, fit));
    }

    /// <summary>Superficie: el asistente más la sombra (a los lados y debajo).</summary>
    public static Size Surface(float scale) => Surface(scale, OnboardingGeometry.Width, OnboardingGeometry.Height);

    /// <summary>Superficie de otra pastilla de <paramref name="width"/> × <paramref name="height"/> px lógicos, con su sombra.</summary>
    public static Size Surface(float scale, float width, float height) => new(
        (int)Math.Ceiling((width + 2 * ShadowMargin) * scale - 1e-3f),
        (int)Math.Ceiling((height + ShadowMargin) * scale - 1e-3f));

    /// <summary>Esquina superior izquierda de la superficie: centrada y pegada arriba, como el notch.</summary>
    public static Point TopLeft(Rectangle monitor, Size surface) =>
        new(monitor.Left + (monitor.Width - surface.Width) / 2, monitor.Top);
}
