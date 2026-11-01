using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.UI.Notch;

/// <summary>Lo que se pide desde la tarjeta del Pro (solo en la ventana interactiva).</summary>
public enum NotchProAction
{
    /// <summary>«Comprar» (edición de la Store).</summary>
    Purchase,
    /// <summary>«Restaurar compra» (edición de la Store).</summary>
    Restore,
    /// <summary>«Conseguir Pro en Microsoft Store» (edición GitHub): abre la ficha de la Store.</summary>
    OpenStore,
}

/// <summary>Lo que dice la tarjeta bajo lo que incluye: nada, la compra en marcha o cómo acabó la última.</summary>
public enum ProMessage { None, Busy, Failed, Offline, Unavailable, NotOwned }

/// <summary>Botón de la tarjeta bajo el ratón o pulsado.</summary>
public enum ProCardButton { None, Primary, Secondary }

/// <summary>
/// El Pro tal como lo ve el notch (lo publica la licencia; la conecta la bandeja con <see cref="NotchHost.SetProOffer"/>):
/// estado, días de prueba, si se puede comprar aquí (Store) o solo enlazar la ficha (GitHub), precio y el mensaje de la
/// última acción (<see cref="ProMessage.Busy"/> mientras la ventana de compra está abierta: los botones no responden).
/// </summary>
public sealed record NotchProOffer(ProState State, int? TrialDaysLeft, bool CanPurchaseHere, string? Price,
    ProMessage Message = ProMessage.None)
{
    public bool Busy => Message == ProMessage.Busy;

    /// <summary>La oferta de la licencia de ahora con el mensaje de la última acción.</summary>
    public static NotchProOffer From(IProLicense license, ProMessage message = ProMessage.None)
    {
        ArgumentNullException.ThrowIfNull(license);
        return new(license.State, license.TrialDaysLeft, license.CanPurchaseHere, license.Price, message);
    }

    /// <summary>El mensaje de la tarjeta tras una compra o restauración. PURO. Comprado, ya suyo o cancelado: nada (el estado
    /// ya lo dice, o el usuario se echó atrás); lo demás, por qué no salió.</summary>
    public static ProMessage MessageFor(PurchaseResult result) => result switch
    {
        PurchaseResult.Purchased or PurchaseResult.AlreadyOwned or PurchaseResult.Cancelled => ProMessage.None,
        PurchaseResult.NotOwned => ProMessage.NotOwned,
        PurchaseResult.NotAvailable => ProMessage.Unavailable,
        PurchaseResult.NetworkError => ProMessage.Offline,
        _ => ProMessage.Failed,
    };
}

/// <summary>
/// Lo que pinta la tarjeta del Pro en este frame, con los textos YA formateados (<see cref="NotchProText.Card"/>, al cambiar
/// la oferta, el idioma o quién es la interactiva): título, la línea de la derecha (precio, días de prueba o «Activado»), lo
/// que incluye, los botones (<see cref="Primary"/> con el acento: «Comprar» o, con <see cref="Wide"/>, «Conseguir Pro en
/// Microsoft Store»; <see cref="Secondary"/>: «Restaurar compra») y la línea de estado. Sin botones en la ventana que no es
/// la interactiva ni con el Pro ya activo. <see cref="Hover"/>/<see cref="Pressed"/>: el botón bajo el ratón y pulsado.
/// </summary>
public sealed record NotchProCardView(string Title, string Subtitle, IReadOnlyList<string> Features, string? Primary,
    string? Secondary, bool Wide, string? Status, bool StatusIsError, bool Busy,
    ProCardButton Hover = ProCardButton.None, ProCardButton Pressed = ProCardButton.None)
{
    /// <summary>¿Lleva el botón <paramref name="b"/>?</summary>
    public bool Has(ProCardButton b) => b switch
    {
        ProCardButton.Primary => Primary is not null,
        ProCardButton.Secondary => Secondary is not null,
        _ => false,
    };
}

/// <summary>Los textos de la tarjeta del Pro y del menú de la bandeja. PURO.</summary>
public static class NotchProText
{
    /// <summary>
    /// La tarjeta: con el Pro, «Activado. ¡Gracias!» y sin botones. Sin él, en la Store, el precio (y los días de prueba) con
    /// «Comprar» y «Restaurar compra»; en la edición GitHub, «Pago único» con «Conseguir Pro en Microsoft Store». En una
    /// ventana que no es la interactiva (<paramref name="interactive"/>), sin botones y con «Haz clic para comprar desde esta
    /// pantalla», como el botón «Actualizar» y los del Permiso.
    /// </summary>
    public static NotchProCardView Card(NotchProOffer offer, Strings s, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(s);
        var features = s.NotchProFeatures ?? Array.Empty<string>();
        if (offer.State == ProState.Pro)
            return new NotchProCardView(s.NotchProTitle, s.NotchProActive, features, null, null, false, null, false, false);

        var parts = new List<string>(2);
        if (offer.State == ProState.Trial && offer.TrialDaysLeft is { } days) parts.Add(TrialText(s, days));
        parts.Add(!string.IsNullOrWhiteSpace(offer.Price) ? string.Format(s.Culture, s.NotchProPriceFmt, offer.Price) : s.NotchProOneTime);
        string subtitle = string.Join(" · ", parts);

        string? status = offer.Message switch
        {
            ProMessage.Busy => s.NotchProBusy,
            ProMessage.Failed => s.NotchProFailed,
            ProMessage.Offline => s.NotchProOffline,
            ProMessage.Unavailable => s.NotchProUnavailable,
            ProMessage.NotOwned => s.NotchProNotOwned,
            _ => null,
        };
        bool error = offer.Message is not (ProMessage.None or ProMessage.Busy);
        if (!interactive)
            return new NotchProCardView(s.NotchProTitle, subtitle, features, null, null, false, s.NotchProElsewhere, false, offer.Busy);
        return offer.CanPurchaseHere
            ? new NotchProCardView(s.NotchProTitle, subtitle, features, s.NotchProBuy, s.NotchProRestore, false, status, error, offer.Busy)
            : new NotchProCardView(s.NotchProTitle, subtitle, features, s.NotchProGetInStore, null, true, status, error, offer.Busy);
    }

    /// <summary>«Prueba · quedan 5 días» / «Prueba · último día».</summary>
    public static string TrialText(Strings s, int days) =>
        days <= 1 ? s.NotchProTrialOne : string.Format(s.Culture, s.NotchProTrialFmt, days);

    /// <summary>
    /// El globo de «ClaudeBar Pro…» sin notch a la vista (no se compra a ciegas): con el Pro, «¡Gracias!»; si no, el estado
    /// («Prueba · quedan 5 días» / «Gratis») y que se compra desde la tarjeta Pro del notch.
    /// </summary>
    public static string Balloon(ProSummary pro, Strings s)
    {
        ArgumentNullException.ThrowIfNull(pro);
        ArgumentNullException.ThrowIfNull(s);
        if (pro.State == ProState.Pro) return s.ProThanks;
        string state = pro.State == ProState.Trial ? TrialText(s, pro.TrialDaysLeft ?? ProTrial.Days) : s.ProStateFree;
        return state + "\n" + s.OnbProBuyNote;
    }
    /// <summary>La entrada del menú de la bandeja con el estado: «ClaudeBar Pro… (Gratis)», «… (Prueba: 5 días)», «… (Pro)».</summary>
    public static string Menu(ProSummary pro, Strings s)
    {
        ArgumentNullException.ThrowIfNull(pro);
        ArgumentNullException.ThrowIfNull(s);
        string state = pro.State switch
        {
            ProState.Pro => s.ProStatePro,
            ProState.Trial when pro.TrialDaysLeft is { } d => d <= 1 ? s.ProStateTrialOne : string.Format(s.Culture, s.ProStateTrialFmt, d),
            ProState.Trial => string.Format(s.Culture, s.ProStateTrialFmt, ProTrial.Days),
            _ => s.ProStateFree,
        };
        return string.Format(s.Culture, s.MenuProFmt, state);
    }
}

/// <summary>
/// Geometría del candado del Pro en el notch. PURA: la usan el painter para pintar y <c>NotchForm</c> para acertar el clic,
/// así salen de la misma cuenta. Página con candado: la cabecera de la página (su nombre y, a la derecha, el chip «Pro»), y
/// debajo qué trae (hasta dos líneas) y la pista «Haz clic para ver qué incluye Pro»; un clic en cualquier punto de la
/// página abre la tarjeta.
/// </summary>
public static class NotchProLayout
{
    /// <summary>Chip «Pro» (px lógicos).</summary>
    public const float ChipWidth = 30f, ChipHeight = 16f;

    /// <summary>Líneas del cuerpo de la página con candado (px lógicos): qué trae (dos, con ajuste) y la pista.</summary>
    public const float LockedLine = 17f, LockedGap = 6f;

    /// <summary>Alto de la página con candado sin aviso integrado ni puntos.</summary>
    public const float LockedPageHeight = NotchLayout.ExpandedPadTop + NotchLayout.ExpandedLine + 3 * LockedLine + LockedGap
        + NotchLayout.ExpandedPadBottom;

    /// <summary>Tamaño del expandido en una página con candado (con su aviso integrado y la fila de puntos, como las demás).</summary>
    public static SizeF LockedPageSize(bool withAlert, bool withDots, float scale)
    {
        float h = LockedPageHeight + (withAlert ? NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap : 0f)
            + (withDots ? NotchLayout.PageDotsRow : 0f);
        return new SizeF(NotchLayout.ExpandedWidth * scale, Math.Min(h, NotchLayout.ExpandedMaxHeight) * scale);
    }

    /// <summary>Cabecera de la página con candado: la primera línea de su zona de contenido.</summary>
    public static RectangleF LockedHeader(RectangleF inner, float s) =>
        new(inner.Left, inner.Top, inner.Width, NotchLayout.ExpandedLine * s);

    /// <summary>Chip «Pro» al final de la cabecera, centrado en alto.</summary>
    public static RectangleF Chip(RectangleF inner, float s)
    {
        var head = LockedHeader(inner, s);
        float w = ChipWidth * s, h = ChipHeight * s;
        return new RectangleF(head.Right - w, head.Top + (head.Height - h) / 2f, w, h);
    }

    /// <summary>Qué trae (dos líneas con ajuste) y la pista, centrados en lo que queda bajo la cabecera.</summary>
    public static (RectangleF Pitch, RectangleF Hint) LockedBody(RectangleF inner, float s)
    {
        float top = inner.Top + NotchLayout.ExpandedLine * s, line = LockedLine * s;
        float block = 3 * line + LockedGap * s;
        float y = top + Math.Max(0f, (inner.Bottom - top - block) / 2f);
        return (new RectangleF(inner.Left, y, inner.Width, 2 * line), new RectangleF(inner.Left, y + 2 * line + LockedGap * s, inner.Width, line));
    }

    /// <summary>¿Cae <paramref name="p"/> en la página con candado? (toda su zona de contenido abre la tarjeta).</summary>
    public static bool HitLockedPage(Point p, RectangleF inner) => inner.Contains(p.X + 0.5f, p.Y + 0.5f);

    // ---------------------------------------------------------------- tarjeta

    /// <summary>Tarjeta (px lógicos): título, lo que incluye en dos columnas de tres filas y la fila de los botones.</summary>
    public const float CardTitleRow = 22f, CardFeatureRow = 18f, CardGap = 4f, CardButtonRow = 24f,
        CardPrimaryWidth = 96f, CardSecondaryWidth = 150f, CardWideWidth = 240f, CardButtonGap = 8f;

    /// <summary>Filas de lo que incluye (seis cosas, dos columnas).</summary>
    public const int CardFeatureRows = 3;

    /// <summary>Alto de la tarjeta: ocupa el expandido entero (sin aviso integrado ni puntos).</summary>
    public const float CardHeight = NotchLayout.ExpandedPadTop + CardTitleRow + CardGap + CardFeatureRows * CardFeatureRow
        + 2 * CardGap + CardButtonRow + NotchLayout.ExpandedPadBottom;

    public static SizeF CardSize(float scale) => new(NotchLayout.ExpandedWidth * scale, CardHeight * scale);

    /// <summary>Zona de contenido de la tarjeta (los márgenes del expandido).</summary>
    public static RectangleF CardInner(RectangleF pill, float s) => NotchContentPainter.PageInner(pill, s, false, false);

    /// <summary>Fila del título (y, a la derecha, el precio o los días de prueba).</summary>
    public static RectangleF CardTitle(RectangleF inner, float s) => new(inner.Left, inner.Top, inner.Width, CardTitleRow * s);

    /// <summary>La cosa <paramref name="i"/> de lo que incluye: de arriba abajo, primero la columna izquierda.</summary>
    public static RectangleF CardFeature(RectangleF inner, float s, int i)
    {
        int col = i / CardFeatureRows, row = i % CardFeatureRows;
        float w = inner.Width / 2f;
        return new RectangleF(inner.Left + col * w, inner.Top + (CardTitleRow + CardGap + row * CardFeatureRow) * s, w,
            CardFeatureRow * s);
    }

    /// <summary>Fila de los botones, abajo del todo.</summary>
    public static RectangleF CardButtonsRow(RectangleF inner, float s) =>
        new(inner.Left, inner.Bottom - CardButtonRow * s, inner.Width, CardButtonRow * s);

    /// <summary>Botón principal («Comprar» o, ancho, «Conseguir Pro en Microsoft Store») a la derecha, a píxel entero.</summary>
    public static Rectangle CardPrimary(RectangleF inner, float s, bool wide)
    {
        var row = CardButtonsRow(inner, s);
        float w = (wide ? CardWideWidth : CardPrimaryWidth) * s;
        return Snap(row.Right - w, row.Top, w, row.Height, row.Width);
    }

    /// <summary>Botón secundario («Restaurar compra») a la izquierda del principal.</summary>
    public static Rectangle CardSecondary(RectangleF inner, float s)
    {
        var row = CardButtonsRow(inner, s);
        float w = CardSecondaryWidth * s, right = row.Right - (CardPrimaryWidth + CardButtonGap) * s;
        return Snap(right - w, row.Top, w, row.Height, row.Width);
    }

    /// <summary>Línea de estado: desde la izquierda hasta el primer botón (o toda la fila sin botones).</summary>
    public static RectangleF CardStatus(RectangleF inner, float s, NotchProCardView v)
    {
        var row = CardButtonsRow(inner, s);
        float right = row.Right;
        if (v.Has(ProCardButton.Secondary)) right = CardSecondary(inner, s).Left - CardButtonGap * s;
        else if (v.Has(ProCardButton.Primary)) right = CardPrimary(inner, s, v.Wide).Left - CardButtonGap * s;
        return RectangleF.FromLTRB(row.Left, row.Top, Math.Max(row.Left, right), row.Bottom);
    }

    /// <summary>Botón de la tarjeta bajo <paramref name="p"/> (solo los que lleva); <c>None</c> si ninguno.</summary>
    public static ProCardButton HitCard(Point p, RectangleF pill, float s, NotchProCardView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var inner = CardInner(pill, s);
        if (v.Has(ProCardButton.Primary) && CardPrimary(inner, s, v.Wide).Contains(p)) return ProCardButton.Primary;
        if (v.Has(ProCardButton.Secondary) && CardSecondary(inner, s).Contains(p)) return ProCardButton.Secondary;
        return ProCardButton.None;
    }

    private static Rectangle Snap(float l, float t, float w, float h, float rowWidth)
    {
        if (w <= 0 || h <= 0 || rowWidth < w) return Rectangle.Empty;
        return Rectangle.FromLTRB((int)Math.Ceiling(l - 1e-3f), (int)Math.Ceiling(t - 1e-3f),
            (int)Math.Floor(l + w + 1e-3f), (int)Math.Floor(t + h + 1e-3f));
    }
}
