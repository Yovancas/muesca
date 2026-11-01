using System.Drawing.Drawing2D;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI.Notch;
using ClaudeBarWin.UI.Tutorial;

namespace ClaudeBarWin.UI.Onboarding;

/// <summary>
/// El tutorial dentro del notch grande, con las MISMAS piezas que la bienvenida: la forma con orejas y su sombra, el logo al
/// bajar, las fuentes, los botones, el visto de acento y el painter del notch de verdad para el notch de demostración y el
/// chip «Pro». La geometría es la de <see cref="TutorialGeometry"/> (la misma con que la ventana acierta el clic); el texto
/// llega formateado (<see cref="TutorialView"/>): aquí no se formatea nada.
/// </summary>
public sealed partial class OnboardingPainter
{
    private static readonly Color TrackDim = Color.FromArgb(40, 255, 255, 255);
    private static readonly Color MenuFill = Color.FromArgb(250, 40, 40, 44);
    private static readonly Color MenuHover = Color.FromArgb(40, 255, 255, 255);
    private static readonly Color EffectFill = Color.FromArgb(230, 20, 20, 23);
    private static readonly Color FileFill = Color.FromArgb(240, 242, 242, 246);

    /// <summary>Pinta el frame del tutorial y devuelve el rectángulo tocado (sin sombra: va en <see cref="PaintTutorialShadow"/>).</summary>
    public RectangleF PaintTutorial(Graphics g, Size surface, TutorialFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        if (f.Width < 1f || f.Height < 1f) return RectangleF.Empty;
        SetQuality(g);
        _alpha = 1f;
        float s = f.Scale;
        var bounds = BeginShell(g, f.Width, f.Height, f.CenterX, s, f.Squash, f.LogoAlpha, out var clip);
        try
        {
            _alpha = Quantize(f.ContentAlpha);
            if (_alpha > 0f)
            {
                var open = OnboardingGeometry.PillRect(TutorialGeometry.Width * s, TutorialGeometry.Height * s, f.CenterX);
                if (f.ContentOffsetY != 0f) g.TranslateTransform(0f, MathF.Round(f.ContentOffsetY * s));
                PaintTutorialContent(g, open, s, f);
            }
        }
        finally
        {
            g.Restore(clip);
            _alpha = 1f;
        }
        return bounds;
    }

    /// <summary>Sombra bajo el tutorial (la textura del notch).</summary>
    public RectangleF PaintTutorialShadow(Graphics g, Size surface, TutorialFrame f)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(f);
        return ShellShadow(g, surface, f.Width, f.Height, f.Scale, f.Squash, f.CenterX, f.ShadowAlpha);
    }

    private void PaintTutorialContent(Graphics g, RectangleF pill, float s, TutorialFrame f)
    {
        var v = f.View;
        float contentAlpha = _alpha;
        TutorialHeader(g, pill, s, f);
        TutorialStage(g, pill, s, f);
        _alpha = contentAlpha;
        // Cruce de pasos: el bloque que sale y el que entra, cada uno con su alfa y su desplazamiento (px enteros).
        if (f.Previous is { } prev && f.Blend < 1f) StepBlock(g, pill, s, f, prev, contentAlpha * (1f - f.Blend), f.PreviousShift);
        StepBlock(g, pill, s, f, v, contentAlpha * Math.Clamp(f.Blend, 0f, 1f), f.Shift);
        _alpha = contentAlpha;
        TutorialFooter(g, pill, s, f);
        if (f.ShowFocus && f.Focus.Control != TutorialControl.None) TutorialFocus(g, pill, s, f);
    }

    // ---- cabecera: capítulo y barra de progreso

    private void TutorialHeader(Graphics g, RectangleF pill, float s, TutorialFrame f)
    {
        var v = f.View;
        DrawText(g, v.ChapterLabel, _small!, _t.TextMuted, TutorialGeometry.ChapterLabel(pill, s), _left);
        DrawText(g, v.ChapterTitle, _title!, _t.Foreground, TutorialGeometry.ChapterTitle(pill, s), _left);
        bool exitHot = TutHovered(f, TutorialControl.Exit, 0);
        DrawText(g, v.Exit, _caption!, exitHot ? _t.Foreground : _t.TextMuted, TutorialGeometry.Exit(pill, s), _right);

        int n = v.Chapters.Count;
        for (int i = 0; i < n; i++)
        {
            var mark = v.Chapters[i];
            var r = TutorialGeometry.ChapterMark(pill, s, n, i);
            var fill = mark.Done ? _t.Accent : TrackDim;
            if (mark.Current) fill = mark.Done ? Lerp(_t.Accent, Color.White, 0.25f) : Color.FromArgb(150, 255, 255, 255);
            if (TutHovered(f, TutorialControl.Chapter, i)) fill = Lerp(fill, Color.White, 0.25f * f.HoverIntensity);
            FillRound(g, r, r.Height / 2f, fill);
        }
    }

    // ---- escenario: el escritorio de mentira, el notch de demostración y la bandeja

    private void TutorialStage(Graphics g, RectangleF pill, float s, TutorialFrame f)
    {
        var stage = TutorialGeometry.Stage(pill, s);
        using var stagePath = RoundPath(stage, 12f * s);
        using (var wall = new LinearGradientBrush(stage, Fade(WallTop), Fade(WallBottom), LinearGradientMode.Vertical))
            g.FillPath(wall, stagePath);
        var state = g.Save();
        try
        {
            g.SetClip(stagePath, CombineMode.Intersect);
            var st = f.Stage;
            if (st is { Tray: not TutorialTray.None }) TrayMock(g, stage, s, f, st);
            if (st is { File: true } && st.Drag is null) SampleFile(g, TutorialGeometry.SampleFile(stage, s), s, f.View, hint: true);
            if (f.Notch is { } frame)
            {
                // El notch de demostración cuelga del borde de arriba del escenario: lo pinta el painter del notch de verdad, con
                // su sombra; el fundido de la página se multiplica por el suyo.
                var inner = g.Save();
                // Al píxel entero, como el clic (TutorialScene.Local): lo pintado y lo acertado coinciden.
                g.TranslateTransform(MathF.Round(stage.Left), MathF.Round(stage.Top));
                var size = TutorialGeometry.StageSurface(stage);
                var faded = frame with { ContentAlpha = frame.ContentAlpha * _alpha, ShadowAlpha = frame.ShadowAlpha * _alpha };
                _notch.PaintShadow(g, size, faded);
                _notch.Paint(g, size, faded);
                g.Restore(inner);
                SetQuality(g);
            }
            if (st is { File: true, Drag: { } at })
            {
                // Arrastrándose: el icono sigue al ratón (encima del notch, como un arrastre de Windows).
                var file = TutorialGeometry.SampleFile(stage, s);
                var icon = TutorialGeometry.FileIcon(file, s);
                float dx = at.X - (icon.Left + icon.Width / 2f), dy = at.Y - (icon.Top + icon.Height / 2f);
                file.Offset(dx, dy);
                float saved = _alpha;
                _alpha = Quantize(_alpha * 0.85f);
                SampleFile(g, file, s, f.View, hint: false);
                _alpha = saved;
            }
            if (f.View.Effect is { } effect) EffectLine(g, TutorialGeometry.Effect(stage, s), s, effect);
        }
        finally { g.Restore(state); }
    }

    /// <summary>El archivo de ejemplo: una hoja blanca con la esquina doblada y «PDF», su nombre y la pista de arrastrar.</summary>
    private void SampleFile(Graphics g, RectangleF file, float s, TutorialView v, bool hint)
    {
        var icon = TutorialGeometry.FileIcon(file, s);
        float fold = 11f * s;
        using (var path = new GraphicsPath())
        {
            path.AddLines(new[]
            {
                new PointF(icon.Left, icon.Top), new PointF(icon.Right - fold, icon.Top), new PointF(icon.Right, icon.Top + fold),
                new PointF(icon.Right, icon.Bottom), new PointF(icon.Left, icon.Bottom),
            });
            path.CloseFigure();
            _brush.Color = Fade(FileFill);
            g.FillPath(_brush, path);
        }
        _brush.Color = Fade(Color.FromArgb(200, 200, 205));
        g.FillPolygon(_brush, new[] { new PointF(icon.Right - fold, icon.Top), new PointF(icon.Right - fold, icon.Top + fold),
            new PointF(icon.Right, icon.Top + fold) });
        var badge = new RectangleF(icon.Left + 4f * s, icon.Bottom - 18f * s, icon.Width - 8f * s, 12f * s);
        FillRound(g, badge, 3f * s, Color.FromArgb(0xD9, 0x4A, 0x3D));
        DrawText(g, "PDF", _small!, Color.White, badge, _center);
        var name = new RectangleF(file.Left - 20f * s, icon.Bottom + 4f * s, file.Width + 40f * s, 16f * s);
        DrawText(g, v.SampleFile, _caption!, _t.Foreground, name, _center);
        if (hint)
            DrawText(g, v.DragHint, _small!, _t.TextMuted, new RectangleF(name.Left, name.Bottom, name.Width, 14f * s), _center);
    }

    /// <summary>Lo que «pasaría» de verdad: una cápsula oscura con el texto, abajo en el escenario.</summary>
    private void EffectLine(Graphics g, RectangleF r, float s, string text)
    {
        float tw = g.MeasureString(text, _caption!, int.MaxValue, _left).Width + 28f * s;
        var pillRect = new RectangleF(r.Left + Math.Max(0f, (r.Width - tw) / 2f), r.Top, Math.Min(r.Width, tw), r.Height);
        FillRound(g, pillRect, pillRect.Height / 2f, EffectFill);
        DrawText(g, text, _caption!, _t.Foreground, RectangleF.Inflate(pillRect, -10f * s, 0f), _center);
    }

    /// <summary>La barra de tareas de mentira con el icono de Muesca y, según el paso, el panel, el menú, el submenú de
    /// monitores o los ajustes, con los textos de verdad.</summary>
    private void TrayMock(Graphics g, RectangleF stage, float s, TutorialFrame f, TutorialStageView st)
    {
        var tray = f.View.Tray;
        var bar = TutorialGeometry.Taskbar(stage, s);
        _brush.Color = Fade(Color.FromArgb(225, 18, 18, 22));
        g.FillRectangle(_brush, bar);
        var icon = TutorialGeometry.TrayIcon(stage, s);
        if (st.IconHover) FillRound(g, RectangleF.Inflate(icon, 4f * s, 3f * s), 4f * s, MenuHover);
        float w = Math.Max(1.5f, 2.2f * s);
        var ring = RectangleF.Inflate(icon, -4f * s, -4f * s);
        SetPen(Color.FromArgb(70, 255, 255, 255), w);
        g.DrawEllipse(_pen, ring);
        SetPen(_t.Accent, w);
        g.DrawArc(_pen, ring, -90f, 150f);
        DrawText(g, tray.Clock, _caption!, _t.Foreground, TutorialGeometry.TrayClock(stage, s), _center);

        switch (st.Tray)
        {
            case TutorialTray.Panel:
            {
                var panel = TutorialGeometry.Panel(stage, s);
                FillRound(g, panel, 10f * s, MenuFill);
                DrawText(g, tray.PanelTitle, _bold!, _t.Foreground, new RectangleF(panel.Left + 14f * s, panel.Top + 10f * s,
                    panel.Width - 28f * s, 20f * s), _left);
                for (int i = 0; i < tray.Panel.Count; i++)
                {
                    var row = tray.Panel[i];
                    float y = panel.Top + (42f + i * 40f) * s;
                    DrawText(g, row.Label, _caption!, _t.TextMuted, new RectangleF(panel.Left + 14f * s, y, 60f * s, 16f * s), _left);
                    DrawText(g, row.Pct, _bold!, _t.Foreground, new RectangleF(panel.Right - 74f * s, y, 60f * s, 16f * s), _right);
                    var track = new RectangleF(panel.Left + 14f * s, y + 20f * s, panel.Width - 28f * s, 5f * s);
                    FillRound(g, track, 2.5f * s, _t.Track);
                    FillRound(g, new RectangleF(track.Left, track.Top, track.Width * row.Fraction, track.Height), 2.5f * s, _t.Foreground);
                }
                break;
            }
            case TutorialTray.Menu:
            case TutorialTray.Monitor:
            {
                var menu = TutorialGeometry.Menu(stage, s);
                FillRound(g, menu, 8f * s, MenuFill);
                foreach (var item in Enum.GetValues<TutorialMenuItem>())
                {
                    int i = (int)item;
                    if (i >= tray.Menu.Count) break;
                    var r = TutorialGeometry.MenuItemRect(stage, s, item);
                    bool open = st.Tray == TutorialTray.Monitor && item == TutorialMenuItem.Monitor;
                    if (st.MenuHover == i || open) FillRound(g, r, 4f * s, MenuHover);
                    var color = st.MenuGoal == i ? _t.AccentText : _t.Foreground;
                    DrawText(g, tray.Menu[i], _body!, color, RectangleF.FromLTRB(r.Left + 10f * s, r.Top, r.Right - 8f * s, r.Bottom), _left);
                }
                if (st.Tray == TutorialTray.Monitor)
                {
                    var sub = TutorialGeometry.MonitorMenu(stage, s, tray.Monitors.Count);
                    FillRound(g, sub, 8f * s, MenuFill);
                    for (int i = 0; i < tray.Monitors.Count; i++)
                    {
                        var r = new RectangleF(sub.Left + 4f * s, sub.Top + (TutorialGeometry.MenuPad + i * TutorialGeometry.MenuRow) * s,
                            sub.Width - 8f * s, TutorialGeometry.MenuRow * s);
                        // La primera (sin Pro) abre la tarjeta; el resto se ve sin poder elegirse, como en la bandeja de verdad.
                        DrawText(g, tray.Monitors[i], _caption!, i == 0 ? _t.AccentText : _t.TextMuted,
                            RectangleF.FromLTRB(r.Left + 8f * s, r.Top, r.Right - 6f * s, r.Bottom), _left);
                    }
                }
                break;
            }
            case TutorialTray.Settings:
            {
                var panel = TutorialGeometry.Settings(stage, s);
                FillRound(g, panel, 10f * s, MenuFill);
                DrawText(g, tray.SettingsTitle, _bold!, _t.Foreground,
                    new RectangleF(panel.Left + 14f * s, panel.Top + 8f * s, panel.Width - 28f * s, 20f * s), _left);
                foreach (var row in Enum.GetValues<TutorialSettingsRow>())
                {
                    int i = (int)row;
                    if (i >= tray.Settings.Count) break;
                    var r = TutorialGeometry.SettingsRowRect(stage, s, row);
                    if (st.SettingsHover == i) FillRound(g, r, 5f * s, MenuHover);
                    bool link = row == TutorialSettingsRow.Tutorial;
                    var color = st.SettingsGoal == i || link ? _t.AccentText : _t.Foreground;
                    DrawText(g, tray.Settings[i], _body!, color, RectangleF.FromLTRB(r.Left + 8f * s, r.Top, r.Right - 30f * s, r.Bottom),
                        _left);
                    if (!link)
                    {
                        // Un interruptor o un selector de mentira a la derecha: es una vista, no se toca.
                        var sw = new RectangleF(r.Right - 30f * s, r.Top + (r.Height - 14f * s) / 2f, 24f * s, 14f * s);
                        FillRound(g, sw, sw.Height / 2f, row == TutorialSettingsRow.UsageStats ? _t.Accent : TrackOff);
                    }
                    _brush.Color = Fade(Color.FromArgb(18, 255, 255, 255));
                    g.FillRectangle(_brush, r.Left, r.Bottom - 1f, r.Width, Math.Max(1f, s * 0.75f));
                }
                break;
            }
        }
    }

    // ---- el paso: título, frase, teclas, el «hecho» y el chip «Pro»

    private void StepBlock(Graphics g, RectangleF pill, float s, TutorialFrame f, TutorialView v, float alpha, float shift)
    {
        _alpha = Quantize(alpha);
        if (_alpha <= 0f) return;
        var state = g.Save();
        try
        {
            if (shift != 0f) g.TranslateTransform(MathF.Round(shift * s), 0f);
            bool chapterEnd = v.Phase != TutorialPhase.Step;
            if (!chapterEnd) DrawText(g, v.StepLabel, _small!, _t.TextMuted, TutorialGeometry.StepLabel(pill, s), _left);
            var title = TutorialGeometry.StepTitle(pill, s);
            float titleW = g.MeasureString(v.StepTitle, _title!, int.MaxValue, _left).Width;
            var badge = TutorialGeometry.DoneBadge(pill, s);
            float titleRight = Math.Min(title.Right, badge.Left - 8f * s);
            if (v.Pro)
            {
                // El chip «Pro» del notch, justo después del título (o al final del hueco si no cabe).
                float cw = NotchProLayout.ChipWidth * s, ch = NotchProLayout.ChipHeight * s;
                float x = Math.Min(title.Left + titleW + 8f * s, titleRight - cw);
                _notch.PaintProChip(g, new RectangleF(x, title.Top + (title.Height - ch) / 2f, cw, ch), s, _alpha);
                SetQuality(g);
                titleRight = x - 6f * s;
            }
            DrawText(g, v.StepTitle, _title!, _t.Foreground, RectangleF.FromLTRB(title.Left, title.Top, titleRight, title.Bottom), _left);
            if (v.Done) DoneBadge(g, badge, s, v, chapterEnd ? 1f : f.DonePop, chapterEnd ? 1f : f.DoneAlpha);
            DrawWrapped(g, v.StepText, _body!, _t.TextMuted, TutorialGeometry.StepText(pill, s));
            if (v.ProLine is { } proLine)
            {
                // Cierre de capítulo sin el Pro: la línea discreta con su enlace; las teclas suben a la fila del número de paso.
                DrawText(g, v.KeyHint, _small!, _t.TextMuted, TutorialGeometry.StepLabel(pill, s), _left);
                DrawText(g, proLine, _caption!, _t.TextMuted, TutorialGeometry.KeyHint(pill, s), _left);
                bool hot = TutHovered(f, TutorialControl.ProLink, 0);
                DrawText(g, v.ProLink, _caption!, hot ? Lerp(_t.AccentText, Color.White, 0.3f * f.HoverIntensity) : _t.AccentText,
                    TutorialGeometry.ProLink(pill, s), _right);
            }
            else DrawText(g, v.KeyHint, _small!, _t.TextMuted, TutorialGeometry.KeyHint(pill, s), _left);
        }
        finally { g.Restore(state); }
    }

    /// <summary>«✓ Hecho» a la derecha del título: el visto de acento (el de la tarjeta elegida de la bienvenida) que entra con
    /// el muelle de atención (<paramref name="pop"/>) y se funde (<paramref name="alpha"/>).</summary>
    private void DoneBadge(Graphics g, RectangleF badge, float s, TutorialView v, float pop, float alpha)
    {
        float saved = _alpha;
        _alpha = Quantize(_alpha * Math.Clamp(alpha, 0f, 1f));
        if (_alpha <= 0f) { _alpha = saved; return; }
        try
        {
            float tw = g.MeasureString(v.DoneLabel, _bold!, int.MaxValue, _left).Width;
            float d = 16f * s * Math.Max(0.1f, pop);
            float cx = badge.Right - tw - 6f * s - 8f * s, cy = badge.Top + badge.Height / 2f;
            PaintCheck(g, new RectangleF(cx - d / 2f, cy - d / 2f, d, d), s);
            DrawText(g, v.DoneLabel, _bold!, _t.AccentText, RectangleF.FromLTRB(badge.Right - tw - 1f, badge.Top, badge.Right, badge.Bottom),
                _right);
        }
        finally { _alpha = saved; }
    }

    // ---- pie: Atrás, los puntos de los pasos, Saltar y Siguiente

    private void TutorialFooter(Graphics g, RectangleF pill, float s, TutorialFrame f)
    {
        var v = f.View;
        if (v.CanGoBack) TutButton(g, TutorialGeometry.Back(pill, s), v.Back, primary: false, s, f, TutorialControl.Back);
        if (v.CanSkip) TutButton(g, TutorialGeometry.Skip(pill, s), v.Skip, primary: false, s, f, TutorialControl.Skip);
        float saved = _alpha;
        if (!v.NextPrimary) _alpha = Quantize(_alpha * DisabledAlpha);
        TutButton(g, TutorialGeometry.Next(pill, s), v.Next, primary: v.NextPrimary, s, f, TutorialControl.Next);
        _alpha = saved;

        if (v.Phase != TutorialPhase.Step) return;
        int n = v.StepStates.Count;
        float d = NotchGeometry.PageDotDiameter * s;
        for (int i = 0; i < n; i++)
        {
            float away = Math.Clamp(Math.Abs(i - f.IndicatorPos), 0f, 1f);
            if (away <= 0f) continue;
            var c = TutorialGeometry.StepDotCenter(pill, s, n, i);
            bool hot = TutHovered(f, TutorialControl.StepDot, i);
            var color = v.StepStates[i] == TutorialStepStatus.Done ? _t.Accent : _t.Foreground;
            float a = v.StepStates[i] == TutorialStepStatus.Done ? 0.9f : hot ? 0.6f : DotAlpha;
            _brush.Color = Fade(Color.FromArgb((int)(255 * a * away), color));
            g.FillEllipse(_brush, c.X - d / 2f, c.Y - d / 2f, d, d);
        }
        var ic = TutorialGeometry.StepDotCenter(pill, s, n, f.IndicatorPos);
        float w = NotchGeometry.PageIndicatorWidth * s, h = Math.Max(2f, NotchGeometry.PageIndicatorHeight * s);
        SetPen(_t.Foreground, h);
        g.DrawLine(_pen, ic.X - (w - h) / 2f, ic.Y, ic.X + (w - h) / 2f, ic.Y);
    }

    private void TutButton(Graphics g, RectangleF r, string text, bool primary, float s, TutorialFrame f, TutorialControl control) =>
        Button(g, r, text, primary, s, TutHovered(f, control, 0), f.HoverIntensity, TutPressed(f, control, 0));

    /// <summary>Anillo de foco (teclado): 2 px de acento alrededor del control con el foco.</summary>
    private void TutorialFocus(Graphics g, RectangleF pill, float s, TutorialFrame f)
    {
        var r = RectangleF.Empty;
        foreach (var (hit, rect) in TutorialGeometry.Controls(pill, s, f.View.Chapters.Count, f.View.StepStates.Count, f.View.Phase,
                     f.View.ProLink is not null))
            if (hit == f.Focus) { r = rect; break; }
        if (r.IsEmpty) return;
        r = RectangleF.Inflate(r, 3f * s, 3f * s);
        using var path = RoundPath(r, (f.Focus.Control == TutorialControl.Notch ? 14f : 9f) * s);
        SetPen(_t.Accent, 2f * s);
        g.DrawPath(_pen, path);
    }

    private static bool TutHovered(TutorialFrame f, TutorialControl c, int index) =>
        f.Hover.Control == c && (f.Hover.Index == index || c is not (TutorialControl.Chapter or TutorialControl.StepDot));

    private static bool TutPressed(TutorialFrame f, TutorialControl c, int index) =>
        f.Pressed.Control == c && f.Pressed.Index == index && TutHovered(f, c, index);
}
