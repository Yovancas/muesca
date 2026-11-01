using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// ClaudeBar Pro en la ventana del notch (docs/superpowers/specs/2026-10-30-gratis-y-pro-design.md). Archivo aparte para no
/// mezclarse con el resto de la ventana.
/// <list type="bullet">
/// <item><b>Página con candado</b> (<see cref="NotchOptions.Locked"/>): un clic en ella, ya quieta y entera, abre la tarjeta.</item>
/// <item><b>Tarjeta</b>: ocupa el expandido (otra maquetación del mismo modo: el animador funde el contenido). Botones al
/// SOLTAR sobre el mismo que se pulsó, como «Actualizar»; un clic en el resto la cierra y vuelve a la página. Se cierra sola
/// al contraerse el expandido. Solo la ventana <see cref="Interactive"/> lleva los botones (con un notch por monitor nunca
/// hay dos compras a la vez); un clic en otra pide ser la interactiva (<see cref="PromoteRequested"/>).</item>
/// <item><b>Aviso del fin de la prueba</b>: un aviso de la app en la cola de la máquina (el último en prioridad, sin sonido);
/// pulsarlo abre la tarjeta.</item>
/// </list>
/// El texto de la tarjeta se rehace al cambiar la oferta, el idioma o quién es la interactiva, nunca por frame.
/// </summary>
public sealed partial class NotchForm
{
    /// <summary>Maquetación del expandido con la tarjeta del Pro (para <see cref="NotchAnimator.SetTarget"/>).</summary>
    private const int LayoutProCard = 32;

    /// <summary>Id del aviso «tu prueba de Pro ha terminado» en la cola.</summary>
    internal const string ProTrialNoticeId = "pro:trial-ended";

    private NotchProOffer? _proOffer;
    private NotchProCardView? _proCard;
    private bool _proCardOpen;
    private ProCardButton _proHover, _proPressed;
    private bool _proNoticeReported;

    /// <summary>El aviso del fin de la prueba SE VE por fin (es el actual y la ventana no está oculta): la bandeja lo apunta
    /// en config.json entonces, no al meterlo en la cola (si la ventana se rehace antes, se perdería).</summary>
    internal event Action<NotchForm>? ProNoticeShown;

    /// <summary>Clic en un botón de la tarjeta del Pro (solo en la ventana interactiva y sin una compra en marcha).</summary>
    internal event Action<NotchForm, NotchProAction>? ProActionRequested;

    /// <summary>La oferta del Pro que tiene (la de <see cref="NotchHost.SetProOffer"/>); null sin ella.</summary>
    internal NotchProOffer? ProOffer => _proOffer;

    /// <summary>Lo que pinta la tarjeta (textos ya formateados y botones).</summary>
    internal NotchProCardView? ProCard => _proCard;

    /// <summary>¿Está abierta la tarjeta del Pro?</summary>
    internal bool ProCardOpen => _proCardOpen;

    /// <summary>El Pro de ahora (estado, precio, la compra en marcha…). Lo mismo otra vez no repinta.</summary>
    internal void SetProOffer(NotchProOffer? offer)
    {
        if (Equals(offer, _proOffer)) return;
        _proOffer = offer;
        if (offer is null || offer.Busy) _proPressed = ProCardButton.None;
        RebuildProCard();
        if (offer is null) _proCardOpen = false;
        if (Alive) Sync();
    }

    private void RebuildProCard() =>
        _proCard = _proOffer is { } o ? NotchProText.Card(o, _painter.Strings, _interactive) : null;

    /// <summary>
    /// Abre la tarjeta (clic en una página con candado, el aviso del fin de la prueba o «ClaudeBar Pro…» de la bandeja). Si el
    /// expandido no estaba abierto se abre; con <paramref name="linger"/> (desde la bandeja, sin el puntero encima) se queda
    /// <see cref="NotchTiming.ProCardLinger"/>. false si no hay oferta, la ventana está oculta o se está mudando.
    /// </summary>
    internal bool OpenProCard(bool linger = false)
    {
        if (!Alive || _proCard is null || _hiddenForFullscreen || _migrateTo is not null) return false;
        EndTaskEdit(save: true, restoreFocus: true);
        _proCardOpen = true;
        _proHover = _proPressed = ProCardButton.None;
        ResetShelfPointer();
        ResetTasksPointer();
        if (_sm.Mode != NotchMode.Expanded) _sm.OpenExpanded(DateTime.UtcNow, linger ? NotchTiming.ProCardLinger : null);
        Sync();
        return _proCardOpen;
    }

    /// <summary>Cierra la tarjeta y vuelve a la página que había debajo.</summary>
    internal void CloseProCard()
    {
        if (!_proCardOpen) return;
        _proCardOpen = false;
        _proHover = _proPressed = ProCardButton.None;
        if (Alive) Sync();
    }

    /// <summary>Saca el aviso «tu prueba de Pro ha terminado» (una vez: lo decide la bandeja).</summary>
    internal void ShowProNotice()
    {
        if (!Alive) return;
        _sm.OnNotice(ProTrialNoticeId, DateTime.UtcNow);
        Sync();
    }

    /// <summary>Avisa (una vez por ventana) cuando el aviso del fin de la prueba pasa a ser el que se ve. Desde <c>Sync</c>.</summary>
    private void ReportProNoticeIfShown()
    {
        if (_proNoticeReported || _hiddenForFullscreen
            || _sm.ActiveAlert is not { Kind: NotchAlertKind.Notice, SessionId: ProTrialNoticeId }) return;
        _proNoticeReported = true;
        ProNoticeShown?.Invoke(this);
    }

    /// <summary>Un botón de la tarjeta (lo llama el clic al soltar). Solo en la interactiva, con ese botón y sin una compra en
    /// marcha.</summary>
    internal bool RequestProAction(ProCardButton button)
    {
        if (!_interactive || !_proCardOpen || _proCard is not { Busy: false } card || !card.Has(button)) return false;
        var action = button == ProCardButton.Secondary ? NotchProAction.Restore
            : card.Wide ? NotchProAction.OpenStore : NotchProAction.Purchase;
        ProActionRequested?.Invoke(this, action);
        return true;
    }

    /// <summary>¿Se ve la tarjeta quieta y entera? Solo entonces aceptan ratón sus botones (lo que se pinta es lo que se acciona).</summary>
    private bool ProCardShown => _proCardOpen && _sm.Mode == NotchMode.Expanded && _anim.ShownMode == NotchMode.Expanded
        && (_anim.ShownLayout & LayoutProCard) != 0 && _anim.ContentAlpha >= 1f;

    private ProCardButton ProCardButtonAt(Point p) => ProCardShown && _proCard is { } card
        ? NotchProLayout.HitCard(p, NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale, card)
        : ProCardButton.None;

    private void ProCardHover(Point p)
    {
        var over = ProCardButtonAt(p);
        if (over == _proHover) return;
        _proHover = over;
        if (over == ProCardButton.None) _proPressed = ProCardButton.None;
        RenderFrame();
    }

    private void ProCardPress(Point p)
    {
        var over = ProCardButtonAt(p);
        if (over == _proPressed) return;
        _proPressed = over;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR con la tarjeta abierta: el botón pulsado y soltado encima hace lo suyo; en el resto de la tarjeta, la
    /// cierra. En una ventana que no es la interactiva, pide serlo. Mientras se funde, el clic no cuenta. <c>false</c> si la
    /// tarjeta no está abierta (clic normal).
    /// </summary>
    private bool ProCardClick(Point p)
    {
        var pressed = _proPressed;
        _proPressed = ProCardButton.None;
        if (!_proCardOpen || _sm.Mode != NotchMode.Expanded) return false;
        if (!ProCardShown) return true;
        if (!_interactive)
        {
            PromoteRequested?.Invoke(this);
            return true;
        }
        var over = ProCardButtonAt(p);
        if (over == ProCardButton.None)
        {
            if (pressed == ProCardButton.None) CloseProCard();   // soltar fuera tras pulsar un botón solo lo cancela
            return true;
        }
        if (over == pressed) RequestProAction(over);
        return true;
    }

    /// <summary>Clic en una página con candado (quieta y entera): abre la tarjeta. <c>false</c> si no era el caso.</summary>
    private bool ProLockClick(Point p)
    {
        if (_proCardOpen || _sm.Mode != NotchMode.Expanded || _anim.ShownMode != NotchMode.Expanded || _anim.ContentAlpha < 1f
            || _pageAnim.IsAnimating || _pageAnim.Current != _carousel.Current || !PageLocked(_carousel.Current)) return false;
        if (!NotchProLayout.HitLockedPage(p, MusicInner())) return false;
        OpenProCard();
        return true;
    }

    /// <summary>Clic en el aviso del fin de la prueba: fuera el aviso y, en su lugar, la tarjeta. <c>false</c> si no era él.</summary>
    private bool ProNoticeClick()
    {
        if (_sm.Mode != NotchMode.Completed || _sm.ActiveAlert is not { Kind: NotchAlertKind.Notice } notice) return false;
        _sm.ClearNotice(notice.SessionId, DateTime.UtcNow);
        if (!OpenProCard()) _sm.OnClick(DateTime.UtcNow);   // sin oferta: el clic de siempre (abre el expandido)
        return true;
    }

    /// <summary>Lo que se pinta de la tarjeta en este frame (si la maquetación que SE PINTA es la suya), con el realce.</summary>
    private NotchProCardView? ProCardFrame(NotchMode shown) =>
        shown == NotchMode.Expanded && (_anim.ShownLayout & LayoutProCard) != 0 && _proCard is { } card
            ? card with { Hover = _proHover, Pressed = _proPressed }
            : null;

    private void ResetProPointer() => _proHover = _proPressed = ProCardButton.None;
}
