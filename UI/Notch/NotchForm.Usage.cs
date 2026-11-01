using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Página Uso del notch: lo del panel de la bandeja (cuota 5 h / 7 d con su reinicio, cuota por modelo y uso extra, ritmo y
/// cuándo se agota, gasto equivalente por modelo, plan y salud de Claude) con gráficas interactivas. Archivo aparte para no
/// mezclarse con el resto de la ventana.
/// <list type="bullet">
/// <item><b>Datos fuera del frame:</b> los números (<see cref="UsageText"/>) se rehacen al llegar una foto de la bandeja, al
/// cambiar de idioma o de minuto con la página a la vista (cuenta atrás); la gráfica (<see cref="UsageChartBuilder"/>) al
/// llegar sus datos, al cambiar de modo, de rango, de ventana destacada o de series ocultas. Los datos de cada rango los lee la
/// bandeja en segundo plano (<see cref="NotchShared.RequestUsageSeries"/>: viejos de más de un minuto y sin petición en vuelo)
/// y vuelven al hilo de UI; al pintar solo se leen.</item>
/// <item><b>Gratis:</b> números, selector Gasto | Cuota y tarjetas (clic = destacar esa ventana). <b>Pro</b>
/// (<see cref="ProFeature.NotchUsage"/>, por <see cref="NotchProGating"/>): hover con raya, puntos y tooltip; rangos 1H … 30D con
/// morphing (<see cref="UsageChartMorph"/>: muelle Ajuste; con «reducir movimiento», fundido); proyección del ritmo; leyenda
/// y rueda para alternar series. Sin el Pro, la gráfica quieta con el chip «Pro» (clic = la tarjeta).</item>
/// <item><b>Varias ventanas:</b> solo la interactiva responde a la gráfica (como el botón «Actualizar»); en las demás, un clic
/// ahí pide serlo.</item>
/// <item><b>Abrirla:</b> clic en un anillo de la vista previa o en la columna de cuota de la página Claude ⇒ página Uso con esa
/// ventana destacada (modo Cuota y su rango).</item>
/// </list>
/// 0 % de CPU en reposo: los muelles (morphing, raya del hover, tooltip y realces) se encajan y el bucle se para; el minuto de
/// la cuenta atrás entra en el plazo de un disparo de <see cref="ScheduleWake"/>.
/// </summary>
public sealed partial class NotchForm
{
    // Raya del hover en unidades de casilla (se desliza entre dos con HoverIn) y alfa del tooltip (HoverIn al entrar, Rápido al salir).
    private static readonly SpringPreset UsageBucketPreset = Motion.HoverIn with { EpsX = 0.01, EpsV = 0.1 };

    private readonly UsageChartMorph _usageMorph = new();
    private readonly HoverHighlight _usageHover = new();
    private readonly SpringValue _usageBucket = new(0, UsageBucketPreset);
    private readonly SpringValue _usageTip = new(0, Motion.HoverIn);
    private UsageChartMode _usageMode = UsageChartMode.Spend;
    private ChartRange _usageRange = UsageRanges.DefaultSpend;   // el que elige el Pro
    private QuotaFocus _usageFocus = QuotaFocus.None;
    private HashSet<string> _usageHiddenSpend = new(StringComparer.Ordinal), _usageHiddenQuota = new(StringComparer.Ordinal);
    private UsageNumbers? _usageNumbers;
    private long _usageNumbersMinute = -1;                       // minuto local de la cuenta atrás pintada
    private object? _usageChartKey;                              // de qué está hecha la gráfica actual
    private UsageHit _usageHoverHit, _usageHeld;
    private bool _usagePlot;                                     // el ratón está sobre la gráfica (hover Pro)
    private int _usageTipIndex = -1;
    private int _usageWheelAccum;

    /// <summary>¿La página Uso está en el carrusel?</summary>
    private bool UsageEnabled => _pages.Contains(NotchPage.Usage);

    /// <summary>¿La página Uso es la del expandido (objetivo)?</summary>
    private bool UsagePageVisible => UsageEnabled && !_hiddenForFullscreen && _sm.Mode == NotchMode.Expanded
        && _carousel.Current == NotchPage.Usage;

    /// <summary>¿Se PINTA la página Uso quieta y entera? Solo entonces acepta ratón (lo que se pinta es lo que se acciona).</summary>
    private bool UsagePageShown => UsagePageVisible && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f
        && _pageAnim.Current == NotchPage.Usage && !_pageAnim.IsAnimating && !_proCardOpen;

    /// <summary>Sin el Pro: la gráfica quieta con el chip (lo decide <see cref="NotchProGating"/> con <see cref="ProGate"/>).</summary>
    private bool UsageLocked => (_options.Locked & NotchProLocks.UsageCharts) != 0;

    /// <summary>Gráfica interactiva: con el Pro y en la ventana interactiva.</summary>
    private bool UsageInteractive => _interactive && !UsageLocked;

    /// <summary>Rango que se enseña: el elegido con el Pro; sin él, el propio de lo que se mira (24H el gasto, la duración de la
    /// ventana en la cuota).</summary>
    private ChartRange UsageRange => !UsageLocked ? _usageRange
        : UsageMode == UsageChartMode.Spend ? UsageRanges.DefaultSpend : UsageRanges.For(_usageFocus);

    private IReadOnlySet<string> UsageHidden => UsageLocked ? EmptyHidden
        : UsageMode == UsageChartMode.Spend ? _usageHiddenSpend : _usageHiddenQuota;

    private static readonly IReadOnlySet<string> EmptyHidden = new HashSet<string>();

    /// <summary>El modo que se enseña: el elegido, salvo con «Mostrar gasto estimado» apagado, que es Cuota (ni selector, ni
    /// gasto por modelo, ni su tooltip). Lo elegido se conserva: al encender el ajuste vuelve.</summary>
    private UsageChartMode UsageMode => UsageViewState.ShownMode(_usageMode, _options.ShowSpend);

    // ---------------------------------------------------------------- lo que se recuerda entre aperturas

    /// <summary>Modo, ventana y rango elegidos (lo que se recuerda; las series ocultas no).</summary>
    internal UsageViewState UsageViewNow => new(_usageMode, _usageFocus, _usageRange);

    /// <summary>El modo que se enseña ahora (tests).</summary>
    internal UsageChartMode UsageModeShown => UsageMode;

    /// <summary>Toma lo recordado (al crearse la ventana o al restaurarlo la bandeja), sin transición ni guardarlo otra vez.</summary>
    private void LoadUsageView(UsageViewState v)
    {
        _usageMode = v.Mode;
        _usageFocus = v.Focus;
        _usageRange = v.Range;
    }

    /// <summary>La bandeja restauró lo guardado: números y gráfica con ello, sin transición.</summary>
    private void OnUsageViewRestored()
    {
        if (!Alive) return;
        LoadUsageView(_shared.UsageView);
        ResetUsagePointer();
        ResetUsageView();
    }

    /// <summary>Lo elegido pasa al contenedor compartido, que pide guardarlo (la bandeja lo escribe en segundo plano).</summary>
    private void SaveUsageView() => _shared.SaveUsageView(UsageViewNow);

    // ---------------------------------------------------------------- datos

    /// <summary>
    /// Foto nueva de la bandeja: números y, en la cuota, la gráfica si cambió lo que pinta de la foto (el % y el reinicio de
    /// cada ventana, la hora de agotarse): son números en vivo, así que de golpe; solo si ya había una transición en curso se
    /// sigue desde la forma intermedia. Con la página a la vista se piden los datos del rango si ya son viejos y el bucle se
    /// arranca (un <c>Go</c> sin <c>Pump</c> dejaría la gráfica congelada a medio camino, sin hover ni proyección).
    /// </summary>
    private void OnUsageChanged()
    {
        if (!Alive) return;
        RebuildUsageNumbers();
        if (!UsagePageVisible)
        {
            RebuildUsageChart(animate: false);
            return;
        }
        RequestUsageSeries();
        RebuildUsageChart(animate: _usageMorph.IsAnimating);
        Frame();
        Pump();
    }

    /// <summary>Llegaron los datos de un rango: si es el que se enseña, la gráfica nueva (con su transición si se ve).</summary>
    private void OnUsageSeries(ChartRange range)
    {
        if (!Alive || range != UsageRange) return;
        RebuildUsageChart(animate: UsagePageVisible);
        if (UsagePageVisible)
        {
            Frame();
            Pump();
        }
    }

    /// <summary>No se pudieron leer los datos de un rango: si es el que se espera, se dice en la gráfica (se reintenta al
    /// minuto, en <see cref="RefreshUsageMinute"/>).</summary>
    private void OnUsageSeriesError(ChartRange range)
    {
        if (!Alive || range != UsageRange || !UsagePageVisible) return;
        RenderFrame();
    }

    private void RequestUsageSeries() => _shared.RequestUsageSeries(UsageRange, DateTime.UtcNow);

    /// <summary>¿Es la gráfica que se ve la del rango elegido, se está cargando o falló?</summary>
    internal UsageChartStatus UsageStatus =>
        UsageChartState.Of(_usageMorph.To, UsageRange, _shared.UsageSeriesHasFailed(UsageRange));

    /// <summary>Números de la página con la foto, el idioma y el minuto de ahora.</summary>
    private void RebuildUsageNumbers()
    {
        var input = _shared.Usage;
        _usageNumbers = UsageText.Build(input?.Snapshot, input?.Plan, DateTime.UtcNow, _usageFocus, _painter.Strings);
        _usageNumbersMinute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
    }

    /// <summary>Con la página a la vista, al cambiar de minuto (cuenta atrás «2h 13m»): números nuevos y, si los datos del
    /// rango son viejos o su lectura falló, se vuelven a pedir. Desde <c>Sync</c>.</summary>
    private void RefreshUsageMinute()
    {
        if (!UsagePageVisible || _usageNumbers is null) return;
        if (DateTime.Now.Ticks / TimeSpan.TicksPerMinute == _usageNumbersMinute) return;
        RebuildUsageNumbers();
        RequestUsageSeries();
    }

    /// <summary>Plazo hasta el siguiente minuto con la página a la vista (para <see cref="ScheduleWake"/>); null si no se ve.</summary>
    private TimeSpan? UsageNextMinuteIn() => UsagePageVisible
        ? TimeSpan.FromTicks(TimeSpan.TicksPerMinute - DateTime.Now.Ticks % TimeSpan.TicksPerMinute) + TimeSpan.FromMilliseconds(50)
        : null;

    /// <summary>
    /// Rehace la gráfica si cambió algo de lo que la hace (modo, rango, datos, ventana destacada, series ocultas, idioma, Pro o
    /// la foto en la cuota). Con <paramref name="animate"/>, transición (<see cref="UsageChartMorph.Go"/>); si no, salto.
    /// </summary>
    private void RebuildUsageChart(bool animate)
    {
        var range = UsageRange;
        var data = _shared.UsageSeries(range);
        var mode = UsageMode;
        var snap = mode == UsageChartMode.Quota ? _shared.Usage?.Snapshot : null;
        var hidden = UsageHidden;
        // De la foto, solo sus VALORES: cada refresco de la bandeja trae una foto nueva aunque no cambie nada.
        var key = (mode, range, data, _usageFocus, string.Join("\n", hidden.Order(StringComparer.Ordinal)), _painter.Strings,
            UsageLocked, QuotaChartKey(snap), string.Join(",", _hub.QuotaThresholds));
        if (_usageMorph.To is not null && Equals(key, _usageChartKey)) return;
        // Sin datos de este rango todavía y ya con una gráfica: se queda la que hay hasta que lleguen (sin destellos de «Cargando…»).
        if (data is null && _usageMorph.To is { Loading: false } && _usageMorph.To.Mode == mode) return;
        _usageChartKey = key;

        UsageChartModel model;
        if (data is null) model = UsageChartBuilder.Loading(mode, range);
        else if (mode == UsageChartMode.Spend)
            model = UsageChartBuilder.Spend(data.Spend, range, _shared.UsageSlots.Assign(UsageChartBuilder.Families(data.Spend)),
                hidden, _painter.Strings);
        else
        {
            var u = snap?.Usage;
            var q = new UsageQuotaInputs(u?.FiveHour, u?.SevenDay, snap?.PaceFive, snap?.PaceSeven, _hub.QuotaThresholds);
            model = UsageChartBuilder.Quota(data.Percent, range, data.AtUtc, _usageFocus, q, hidden, projection: !UsageLocked,
                _painter.Strings);
        }
        // Las transiciones son Pro (ProFeature.NotchUsage): sin Pro, todo cambio salta. Un solo punto, sin «if (pro)» sueltos.
        if (animate && !UsageLocked) _usageMorph.Go(model, _options.ReduceMotion, _clock.Elapsed);
        else _usageMorph.Jump(model);
        if (_usageTipIndex >= model.Count) _usageTipIndex = model.Count - 1;
    }

    /// <summary>Lo que la gráfica de la cuota toma de la foto (el % y el reinicio de cada ventana y su hora de agotarse), por
    /// valor; null sin foto.</summary>
    internal static object? QuotaChartKey(AppSnapshot? snap) => snap is null ? null
        : (snap.Usage?.FiveHour?.UtilizationPct, snap.Usage?.FiveHour?.ResetsAt, snap.Usage?.SevenDay?.UtilizationPct,
            snap.Usage?.SevenDay?.ResetsAt, snap.PaceFive?.EtaUtc, snap.PaceSeven?.EtaUtc);

    /// <summary>La página Uso pasa a verse: cuenta atrás al día, datos del rango si son viejos y la gráfica sin transición.</summary>
    private void EnterUsagePage()
    {
        RebuildUsageNumbers();
        RequestUsageSeries();
        _usageMorph.Settle();
        RebuildUsageChart(animate: false);
        ResetUsagePointer();
    }

    /// <summary>
    /// Abre la página Uso con la ventana <paramref name="focus"/> destacada (clic en un anillo o en la columna de cuota): modo
    /// Cuota y el rango de esa ventana. Con el expandido ya abierto, con su cruce; si se está abriendo, de golpe y sin que Sync
    /// lo devuelva a Claude (como la Bandeja al arrastrar).
    /// </summary>
    private void ShowUsagePage(QuotaFocus focus)
    {
        if (!UsageEnabled) return;
        FocusUsage(focus, animate: false);
        if (GoToRequestedPage(NotchPage.Usage)) EnterUsagePage();   // con cruce, PageChanged ya entra en la página
    }

    /// <summary>
    /// Abre el expandido en la página Uso con la ventana <paramref name="focus"/> destacada (como un clic en su anillo). false si
    /// la página no está en el carrusel, la ventana está oculta o se está mudando.
    /// </summary>
    internal bool OpenUsagePage(QuotaFocus focus)
    {
        if (!Alive || !UsageEnabled || _hiddenForFullscreen || _migrateTo is not null) return false;
        EndTaskEdit(save: true, restoreFocus: true);
        _proCardOpen = false;
        if (_sm.Mode != NotchMode.Expanded) _sm.OpenExpanded(DateTime.UtcNow);
        ShowUsagePage(focus);
        Sync();
        return true;
    }

    /// <summary>Los números que enseña la página Uso (null hasta hacerlos).</summary>
    internal UsageNumbers? UsageNumbersView => _usageNumbers;

    /// <summary>La gráfica que enseña (o a la que va) la página Uso.</summary>
    internal UsageChartModel? UsageChart => _usageMorph.To;

    /// <summary>¿Está la gráfica en plena transición?</summary>
    internal bool UsageChartAnimating => _usageMorph.IsAnimating;

    /// <summary>Destaca la ventana <paramref name="focus"/>: modo Cuota con su rango (el Pro conserva lo que oculte de la cuota).</summary>
    private void FocusUsage(QuotaFocus focus, bool animate)
    {
        _usageFocus = focus;
        _usageMode = UsageChartMode.Quota;
        if (focus != QuotaFocus.None) _usageRange = UsageRanges.For(focus);
        _usageHiddenQuota.Clear();
        SaveUsageView();
        RebuildUsageNumbers();   // la tarjeta destacada
        RequestUsageSeries();
        RebuildUsageChart(animate);
    }

    // ---------------------------------------------------------------- ratón

    private void ResetUsagePointer()
    {
        _usageHeld = default;
        _usageWheelAccum = 0;
        _usagePlot = false;
        if (_usageHover.Key is not null || _usageHover.Visible) _usageHover.Reset();
        _usageTip.Snap(0);
        _usageTipIndex = -1;
    }

    private UsageRects UsageRects() => UsageLayout.Compute(MusicInner(), _scale);

    /// <summary>Control de la página Uso bajo <paramref name="p"/>; nada si la página no se pinta quieta.</summary>
    private UsageHit UsageHitAt(Point p, bool? interactive = null)
    {
        if (!UsagePageShown || _usageMorph.To is not { } m) return default;
        return UsageLayout.HitTest(new PointF(p.X + 0.5f, p.Y + 0.5f), UsageRects(), m.Series.Count, interactive ?? UsageInteractive,
            UsageLocked, UsageLayout.LegendSlotsFor(m.Mode, m.Series.Count), modes: _options.ShowSpend);
    }

    /// <summary>Realce del control bajo el cursor y, sobre la gráfica (Pro), la raya del hover que se desliza a la casilla.</summary>
    private void UsageHover(Point p)
    {
        var hit = UsageHitAt(p);
        bool changed = UsagePlotHover(p, hit.Control == UsageControl.Plot);
        var control = hit.Control is UsageControl.Plot ? default : hit;
        if (control.Control == UsageControl.None)
        {
            if (_usageHover.Key is not null)
            {
                _usageHover.Leave(snap: _options.ReduceMotion);
                changed = true;
            }
        }
        else
        {
            string key = $"{control.Control}:{control.Index}";
            if (key != _usageHover.Key)
            {
                _usageHoverHit = control;
                _usageHover.Enter(key, RectangleF.Empty, snap: _options.ReduceMotion);
                changed = true;
            }
        }
        if (!changed) return;
        Frame();
        Pump();
    }

    /// <summary>Raya del hover: a la casilla bajo el cursor (al entrar, ya en su sitio; luego se desliza) y el tooltip de esa
    /// casilla; al salir, el tooltip se funde. <c>true</c> si cambió algo que pintar.</summary>
    private bool UsagePlotHover(Point p, bool over)
    {
        var m = _usageMorph.To;
        if (!over || m is null || m.Count == 0 || m.Loading || m.Empty || _usageMorph.IsAnimating
            || UsageStatus != UsageChartStatus.Ready)
        {
            if (!_usagePlot) return false;
            _usagePlot = false;
            _usageTip.SetPreset(Motion.Rapido);
            _usageTip.SetTarget(0);
            if (_options.ReduceMotion) _usageTip.Snap(0);
            return true;
        }
        var plot = UsageRects().Plot;
        int i = UsageChartHit.Bucket(p.X + 0.5f, plot.Left, plot.Width, m.Count, m.PastFraction, m.Shape.FirstX);
        if (i < 0) return UsagePlotHover(p, over: false);
        bool entering = !_usagePlot || _usageTip.Value <= 0.002;
        if (!entering && i == _usageTipIndex) return false;
        _usagePlot = true;
        _usageTipIndex = i;
        if (entering || _options.ReduceMotion) _usageBucket.Snap(i);
        else _usageBucket.SetTarget(i);
        _usageTip.SetPreset(Motion.HoverIn);
        _usageTip.SetTarget(1);
        if (_options.ReduceMotion) _usageTip.Snap(1);
        return true;
    }

    /// <summary>WM_LBUTTONDOWN: recuerda el control pulsado.</summary>
    private void UsagePress(Point p)
    {
        var hit = UsageHitAt(p);
        if (hit.Control == UsageControl.Plot) hit = default;
        if (hit == _usageHeld) return;
        _usageHeld = hit;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la página Uso, sobre el MISMO control que se pulsó: tarjeta = destacar esa ventana; segmento = Gasto o
    /// Cuota; rango; serie de la leyenda = ocultarla o enseñarla; chip «Pro» = la tarjeta del Pro. Un clic en la gráfica no
    /// cierra el expandido (se está mirando). En una ventana que no es la interactiva, los controles del Pro piden serlo.
    /// <c>false</c> si el clic no era de la página Uso (clic normal).
    /// </summary>
    private bool UsageClick(Point p)
    {
        var held = _usageHeld;
        _usageHeld = default;
        if (!UsagePageShown || _usageMorph.To is not { } m) return false;
        var hit = UsageHitAt(p);
        if (hit.Control == UsageControl.None && !_interactive && !UsageLocked
            && UsageHitAt(p, interactive: true) is { Control: not UsageControl.None })
        {
            PromoteRequested?.Invoke(this);   // los rangos, la leyenda y el hover, en esta pantalla
            return true;
        }
        if (hit.Control == UsageControl.Plot) return true;
        if (hit.Control == UsageControl.None) return held.Control != UsageControl.None;   // soltar fuera cancela, sin cerrar
        if (hit != held) return true;
        switch (hit.Control)
        {
            case UsageControl.Tile:
                FocusUsage(hit.Index == 0 ? QuotaFocus.Five : QuotaFocus.Seven, animate: true);
                break;
            case UsageControl.Segment:
                if (!SetUsageMode((UsageChartMode)hit.Index)) return true;
                break;
            case UsageControl.Range:
                if (!SetUsageRange(UsageRanges.All[hit.Index])) return true;
                break;
            case UsageControl.Legend when hit.Index < m.Series.Count:
                SetUsageHidden(UsageSeriesToggle.Toggle(m.Series.Select(x => x.Key).ToList(), UsageHidden, m.Series[hit.Index].Key));
                break;
            case UsageControl.ProChip:
                OpenProCard();
                return true;
        }
        _usageTip.Snap(0);
        _usagePlot = false;
        Frame();
        Pump();
        return true;
    }

    /// <summary>Selector Gasto | Cuota: otro modo, sus datos y la gráfica nueva con su transición. false si ya era ese.</summary>
    internal bool SetUsageMode(UsageChartMode mode)
    {
        if (!_options.ShowSpend || mode == _usageMode) return false;   // sin el gasto no hay selector
        _usageMode = mode;
        SaveUsageView();
        RequestUsageSeries();
        RebuildUsageChart(animate: true);
        return true;
    }

    /// <summary>Botón de rango (Pro): sus datos y la gráfica nueva con su transición (sin datos aún, «Cargando…» sobre la
    /// anterior atenuada). false si ya era ese.</summary>
    internal bool SetUsageRange(ChartRange range)
    {
        if (range == _usageRange) return false;
        _usageRange = range;
        SaveUsageView();
        RequestUsageSeries();
        RebuildUsageChart(animate: true);
        return true;
    }

    /// <summary>Rueda sobre la gráfica (Pro, varias series): «todas → solo la 1.ª → solo la 2.ª → … → todas».</summary>
    private void UsageWheel(int delta)
    {
        if (!UsageInteractive || _usageMorph.To is not { Series.Count: >= 2 } m) return;
        _usageWheelAccum += delta;
        int steps = _usageWheelAccum / NotchNative.WHEEL_DELTA;
        if (steps == 0) return;
        _usageWheelAccum -= steps * NotchNative.WHEEL_DELTA;
        // Rueda hacia abajo (delta < 0) = la siguiente, como en el carrusel.
        SetUsageHidden(UsageSeriesToggle.Cycle(m.Series.Select(x => x.Key).ToList(), UsageHidden, -steps));
        Frame();
        Pump();
    }

    private void SetUsageHidden(HashSet<string> hidden)
    {
        if (UsageMode == UsageChartMode.Spend) _usageHiddenSpend = hidden;
        else _usageHiddenQuota = hidden;
        RebuildUsageChart(animate: true);
    }

    /// <summary>¿Está el ratón sobre la gráfica con varias series (para que la rueda las alterne en vez de cambiar de página)?</summary>
    private (bool OverPlot, bool Many) UsageWheelInfo(Point p) =>
        (UsageInteractive && UsagePageShown && UsageRects().Plot.Contains(p), _usageMorph.To?.Series.Count >= 2);

    /// <summary>
    /// Clic en un anillo que abre la página Uso con esa ventana: en la vista previa del reposo (los anillos) o en la columna de
    /// cuota de la página Claude, quietas y enteras. <c>false</c> si no era el caso (clic normal).
    /// </summary>
    private bool UsageRingClick(Point p)
    {
        if (!UsageEnabled || _proCardOpen || _anim.ContentAlpha < 1f) return false;
        var pt = new PointF(p.X + 0.5f, p.Y + 0.5f);
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        var focus = QuotaFocus.None;
        // Con el pomodoro sobre el reposo la vista previa es otra (FocusRingClick); con Claude trabajando, sus anillos siguen ahí.
        bool focusPeek = (_anim.ShownLayout & LayoutFocus) != 0 && !_hub.Sessions.Any(s => s.Phase.IsActive());
        if (_sm.Mode == NotchMode.Peek && _anim.ShownMode == NotchMode.Peek && (_anim.ShownLayout & LayoutIdleMusic) == 0 && !focusPeek
            && _sm.ActiveAlert is null)
            focus = _painter.PeekQuotaAt(pt, pill, _scale, (_anim.ShownLayout & IdleScene.LayoutMascot) != 0, _hub.Sessions, _hub.Quota);
        else if (_sm.Mode == NotchMode.Expanded && _anim.ShownMode == NotchMode.Expanded && _carousel.Current == NotchPage.Claude
            && _pageAnim.Current == NotchPage.Claude && !_pageAnim.IsAnimating)
            focus = NotchContentPainter.ClaudeQuotaAt(pt, pill, _scale, TopLineShown, _pages.Count > 1);
        if (focus == QuotaFocus.None) return false;
        if (_sm.Mode != NotchMode.Expanded) _sm.OnClick(DateTime.UtcNow);   // abre el expandido
        ShowUsagePage(focus);
        return true;
    }

    // ---------------------------------------------------------------- frame

    /// <summary>¿Se mueve algo de la página Uso (transición de la gráfica, raya del hover, tooltip o realces)?</summary>
    private bool UsageAnimating => _usageMorph.IsAnimating || _usageHover.IsAnimating || !_usageBucket.IsSettled
        || !_usageTip.IsSettled;

    /// <summary>Avanza las transiciones de la página Uso (desde <see cref="Frame"/>).</summary>
    private void StepUsage(TimeSpan now, double dt)
    {
        _usageMorph.Step(now);
        if (dt <= 0) return;
        _usageHover.Advance(dt);
        _usageBucket.Advance(dt);
        _usageTip.Advance(dt);
    }

    /// <summary>La página Uso del frame (solo en el expandido, con la página en la lista y sus números hechos).</summary>
    private UsageView? UsageFrame(NotchMode shown)
    {
        if (shown != NotchMode.Expanded || !UsageEnabled || _usageNumbers is null || _usageMorph.To is not { } chart) return null;
        float tip = (float)Math.Clamp(_usageTip.Value, 0, 1);
        return new UsageView(_usageNumbers, chart, UsageMode, UsageRange, _usageMorph.Kind, _usageMorph.Progress,
            _usageMorph.Current, _usageMorph.From, UsageInteractive, UsageLocked,
            _usageHover.Visible ? _usageHoverHit : default, (float)_usageHover.Intensity, _usageHeld,
            tip > 0.002f ? (float)_usageBucket.Value : -1f, _usageTipIndex, tip, UsageStatus, Modes: _options.ShowSpend);
    }

    /// <summary>Los datos y el idioma para una ventana nueva o al cambiar de idioma: números y gráfica, sin transición.</summary>
    private void ResetUsageView()
    {
        RebuildUsageNumbers();
        _usageChartKey = null;
        _usageMorph.Settle();
        RebuildUsageChart(animate: false);
    }

    private void DisposeUsage()
    {
        _shared.UsageChanged -= OnUsageChanged;
        _shared.UsageSeriesChanged -= OnUsageSeries;
        _shared.UsageSeriesError -= OnUsageSeriesError;
        _shared.UsageViewRestored -= OnUsageViewRestored;
    }
}
