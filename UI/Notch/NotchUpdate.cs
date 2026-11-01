using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// La oferta de actualización tal como la ve el notch (la publica <c>IAppUpdates</c>; la conecta la bandeja con
/// <see cref="NotchHost.SetUpdateOffer"/>). <paramref name="Version"/>: la nueva («0.6»); <paramref name="Progress"/>: la
/// descarga en [0, 1]; <paramref name="Ready"/>: descargada y lista para instalar (entonces sale el botón «Actualizar»).
/// </summary>
public sealed record UpdateOfferView(string Version, double Progress, bool Ready)
{
    /// <summary>
    /// La oferta según el estado de <c>IAppUpdates</c>. PURO. Solo hay oferta con una versión nueva bajando
    /// (<see cref="UpdateStage.Downloading"/>, con su progreso) o ya lista (<see cref="UpdateStage.ReadyToApply"/>, con el
    /// botón). Al día, comprobando o con la descarga fallida, null: el notch no promete nada que no pueda dar (el reintento
    /// tras un fallo sigue en el menú de la bandeja).
    /// </summary>
    public static UpdateOfferView? From(UpdateStage stage, string? version, double progress)
    {
        if (string.IsNullOrEmpty(version)) return null;
        return stage switch
        {
            UpdateStage.Downloading => new UpdateOfferView(version, Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1), false),
            UpdateStage.ReadyToApply => new UpdateOfferView(version, 1, true),
            _ => null,
        };
    }
}

/// <summary>
/// Lo que pinta el notch de la oferta en este frame, con el texto YA formateado (<see cref="NotchUpdateText"/>, al cambiar la
/// oferta, el idioma o quién es la interactiva): «Nueva versión 0.6» (<see cref="Label"/>) y, a la derecha, el anillo de la
/// descarga con «Descargando… 42 %» (<see cref="Status"/>) o el botón (<see cref="Action"/>, solo en la ventana interactiva;
/// en las demás, «Lista para instalar»). <see cref="Hover"/>/<see cref="Pressed"/>: el botón bajo el ratón y pulsado.
/// </summary>
public sealed record NotchUpdateView(string Label, string Status, string? Action, float Progress, bool Ready,
    bool Hover = false, bool Pressed = false)
{
    /// <summary>¿Lleva botón? (lista y en la ventana interactiva).</summary>
    public bool Button => Ready && Action is not null;
}

/// <summary>Los textos del aviso de actualización. PURO.</summary>
public static class NotchUpdateText
{
    /// <summary>
    /// «Nueva versión 0.6» y, según cómo va: «Descargando… 42 %» con el anillo; lista, el botón «Actualizar» en la ventana
    /// interactiva (<paramref name="interactive"/>; con un notch por monitor, solo una lo lleva, como los botones del
    /// Permiso) y «Lista para instalar» en las demás.
    /// </summary>
    public static NotchUpdateView Build(UpdateOfferView offer, Strings s, bool interactive)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(s);
        string label = string.Format(s.Culture, s.NotchUpdateFmt, offer.Version);
        float progress = (float)Math.Clamp(double.IsFinite(offer.Progress) ? offer.Progress : 0, 0, 1);
        if (offer.Ready) return new NotchUpdateView(label, interactive ? "" : s.NotchUpdateElsewhere,
            interactive ? s.NotchUpdateAction : null, 1f, true);
        string status = $"{s.NotchUpdateDownloading} {UsageFormat.Percent(Math.Floor(progress * 100), s.Culture)}";
        return new NotchUpdateView(label, status, null, progress, false);
    }
}

/// <summary>
/// Geometría del aviso de actualización. PURA: la usan el painter para pintar y <c>NotchForm</c> para acertar el clic, así
/// salen de la misma cuenta. En el reposo y la vista previa, un punto de acento pegado al borde derecho; en el expandido, la
/// línea de arriba (la del aviso integrado, que manda si lo hay) con el botón o el anillo a la derecha.
/// </summary>
public static class NotchUpdateLayout
{
    /// <summary>Punto del reposo (px lógicos): diámetro y separación del borde derecho.</summary>
    public const float DotDiameter = 5f, DotInset = 7f;

    /// <summary>Botón «Actualizar» y anillo de la descarga (px lógicos).</summary>
    public const float ButtonWidth = 92f, ButtonHeight = 20f, RingDiameter = 14f;

    /// <summary>Punto de acento del reposo: centrado en alto, a <see cref="DotInset"/> del borde derecho de la pastilla.</summary>
    public static RectangleF Dot(RectangleF pill, float s)
    {
        float d = DotDiameter * s, cx = pill.Right - DotInset * s, cy = pill.Top + pill.Height / 2f;
        return new RectangleF(cx - d / 2f, cy - d / 2f, d, d);
    }

    /// <summary>La línea del aviso en el expandido: la primera del contenido (la que usaría el aviso integrado).</summary>
    public static RectangleF Row(RectangleF pill, float s)
    {
        var inner = NotchContentPainter.PageInner(pill, s, withAlert: false, withDots: false);
        return new RectangleF(inner.Left, inner.Top, inner.Width, NotchLayout.ExpandedLine * s);
    }

    /// <summary>Botón «Actualizar» al final de la línea, centrado en alto y redondeado a píxel entero hacia dentro.</summary>
    public static Rectangle Button(RectangleF row, float s)
    {
        float w = ButtonWidth * s, h = ButtonHeight * s;
        float l = row.Right - w, t = row.Top + (row.Height - h) / 2f;
        if (s <= 0 || row.Width < w) return Rectangle.Empty;
        return Rectangle.FromLTRB((int)Math.Ceiling(l - 1e-3f), (int)Math.Ceiling(t - 1e-3f),
            (int)Math.Floor(l + w + 1e-3f), (int)Math.Floor(t + h + 1e-3f));
    }

    /// <summary>Anillo de la descarga al final de la línea.</summary>
    public static RectangleF Ring(RectangleF row, float s)
    {
        float d = RingDiameter * s;
        return new RectangleF(row.Right - d, row.Top + (row.Height - d) / 2f, d, d);
    }

    /// <summary>¿Cae <paramref name="p"/> en el botón? (con la misma cuenta con que se pinta).</summary>
    public static bool HitButton(Point p, RectangleF pill, float s) => Button(Row(pill, s), s).Contains(p);

    /// <summary>¿Cae <paramref name="p"/> en la línea del aviso? (en una ventana que no es la interactiva, pide serlo).</summary>
    public static bool HitRow(Point p, RectangleF pill, float s) => Row(pill, s).Contains(p.X + 0.5f, p.Y + 0.5f);
}
