using System.Drawing;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Geometría del notch por modo (spec §4.5). PURA: todo en píxeles lógicos × <c>scale</c>
/// (<c>scale = DeviceDpi/96 × factor de tamaño</c>; el notch no usa <c>Dpi.Apply</c>).
/// </summary>
public static class NotchLayout
{
    /// <summary>Ancho extra máximo (px lógicos) que el texto de «Trabajando» puede añadir a los 300 base.</summary>
    public const float WorkingExtraMax = 200f;

    /// <summary>Ancho mínimo de Working: con un texto corto la pastilla encoge hasta aquí (sin hueco negro entre el
    /// texto y el %).</summary>
    public const float WorkingMinWidth = 160f;

    /// <summary>Expandido: ancho fijo; el alto lo marca lo más alto entre la columna de cuota (5h + 7d) y la
    /// lista de sesiones (hasta <see cref="ExpandedMaxRows"/> filas; la última dice «+N más»).</summary>
    public const float ExpandedWidth = 480f, ExpandedLine = 22f, ExpandedPadTop = 12f, ExpandedPadBottom = 14f,
        ExpandedQuotaBlock = 98f, ExpandedAlertGap = 4f;
    public const int ExpandedMaxRows = 6;

    /// <summary>Tamaño del expandido para <paramref name="sessions"/> sesiones abiertas (con o sin aviso integrado).</summary>
    public static SizeF ExpandedSize(int sessions, bool withAlert, float scale)
    {
        int rows = Math.Clamp(sessions, 1, ExpandedMaxRows);
        float h = ExpandedPadTop + Math.Max(ExpandedQuotaBlock, rows * ExpandedLine) + ExpandedPadBottom
            + (withAlert ? ExpandedLine + ExpandedAlertGap : 0f);
        return new SizeF(ExpandedWidth * scale, h * scale);
    }

    /// <summary>Páginas del expandido (F5): fila de los puntos abajo, alto de la página Música (con puntos) y tope de alto.
    /// El tope deja bajo la pastilla sitio para la sombra (blur 12 + desplazamiento 3) dentro de 200 de superficie.</summary>
    public const float PageDotsRow = 16f, MusicPageHeight = 140f, ExpandedMaxHeight = 184f;

    /// <summary>Alto máximo de la página Uso (contenido fijo, con el aviso integrado y los puntos): la única más alta que
    /// <see cref="ExpandedMaxHeight"/>; la superficie del notch (<c>NotchPlacement.SurfaceHeight</c>) le deja sitio a su sombra.</summary>
    public const float UsagePageMaxHeight = ExpandedPadTop + UsageLayout.ContentHeight + ExpandedPadBottom + ExpandedLine
        + ExpandedAlertGap + PageDotsRow;

    /// <summary>
    /// Tamaño del expandido en la página <paramref name="page"/>. Claude = <see cref="ExpandedSize"/>; Música = 140; Foco = su
    /// contenido fijo (<see cref="FocusLayout.ContentHeight"/>: con el aviso y los puntos, justo el tope);
    /// Tareas = cabecera + una línea por fila (vacía = una, la del texto de vacío); Bandeja = cabecera + una fila de
    /// tarjetas, tenga las entradas que tenga (vacía = la línea del texto de vacío; <see cref="ShelfLayout.ContentHeight"/>).
    /// El aviso integrado suma su línea y, con varias páginas (<paramref name="withDots"/>), la fila de puntos. Nunca más
    /// alto que <see cref="ExpandedMaxHeight"/>: lo que no cabe lo resume la propia página («+N más», flechas). Uso es la
    /// excepción: más ancha (<see cref="UsageLayout.PageWidth"/>) y con su alto fijo (<see cref="UsageLayout.ContentHeight"/>).
    /// </summary>
    public static SizeF PageSize(NotchPage page, PageRows rows, bool withAlert, bool withDots, float scale)
    {
        float alert = withAlert ? ExpandedLine + ExpandedAlertGap : 0f;
        float dots = withDots ? PageDotsRow : 0f;
        float h = page switch
        {
            NotchPage.Music => MusicPageHeight - (withDots ? 0f : PageDotsRow) + alert,
            NotchPage.Tasks => ListPage(rows.Tasks) + alert + dots,
            NotchPage.Shelf => ExpandedPadTop + ShelfLayout.ContentHeight(rows.Shelf) + ExpandedPadBottom + alert + dots,
            NotchPage.Usage => ExpandedPadTop + UsageLayout.ContentHeight + ExpandedPadBottom + alert + dots,
            NotchPage.Focus => ExpandedPadTop + FocusLayout.ContentHeight + ExpandedPadBottom + alert + dots,
            _ => ExpandedSize(rows.Sessions, withAlert, 1f).Height + dots,
        };
        if (page == NotchPage.Usage) return new SizeF(UsageLayout.PageWidth * scale, Math.Min(h, UsagePageMaxHeight) * scale);
        return new SizeF(ExpandedWidth * scale, Math.Min(h, ExpandedMaxHeight) * scale);

        static float ListPage(int n) =>
            ExpandedPadTop + ExpandedLine + Math.Clamp(n, 1, 64) * ExpandedLine + ExpandedPadBottom;
    }

    /// <summary>Reposo con música (F5 · B3): carátula mini + título en vez de los anillos; mismo alto que el reposo.</summary>
    public const float IdleMusicWidth = 200f;

    /// <summary>Tamaño del reposo con música.</summary>
    public static SizeF IdleMusicSize(float scale) =>
        new(IdleMusicWidth * scale, TargetSize(NotchMode.Idle, 1f, 0f).Height * scale);

    /// <summary>Tamaño objetivo del modo <paramref name="m"/>. <paramref name="workingTextExtra"/> solo
    /// cuenta en Working (recortado a <see cref="WorkingMinWidth"/> − 300 .. <see cref="WorkingExtraMax"/>: negativo = encoger). Hidden = 0×0.</summary>
    public static SizeF TargetSize(NotchMode m, float scale, float workingTextExtra)
    {
        var (w, h) = m switch
        {
            NotchMode.Hidden => (0f, 0f),
            NotchMode.Minimal => (44f, 4f),
            NotchMode.Idle => (112f, 30f),
            NotchMode.Working => (300f + Math.Clamp(workingTextExtra, WorkingMinWidth - 300f, WorkingExtraMax), 34f),
            NotchMode.Peek => (184f, 34f),
            NotchMode.Expanded => (ExpandedWidth, ExpandedPadTop + ExpandedQuotaBlock + ExpandedPadBottom),
            NotchMode.Permission => (PermissionWidth, 64f),
            NotchMode.Completed => (310f, 60f),
            NotchMode.QuotaHud => (320f, 36f),
            NotchMode.QuotaAlert => (310f, 60f),
            NotchMode.Reminder => (360f, PermissionButtonsHeight),   // como el Permiso con botones (F5 · B5)
            _ => (150f, 30f),
        };
        return new SizeF(w * scale, h * scale);
    }

    /// <summary>Ancho del Permiso (con y sin botones).</summary>
    public const float PermissionWidth = 360f;

    /// <summary>Permiso con botones (F2): mismo ancho que el de solo lectura y más alto para la fila de botones
    /// (título + detalle arriba, Denegar / Permitir abajo a la derecha; ver <c>NotchGeometry.PermissionButtons</c>).
    /// Alto con UNA línea de detalle; cada línea más suma <see cref="PermissionDetailLine"/>.</summary>
    public const float PermissionButtonsHeight = 96f;

    /// <summary>Alto de cada línea del detalle («herramienta: comando», con ajuste de línea) del Permiso con botones, y
    /// máximo de líneas: lo que no cabe entero en ellas no lleva botones (solo lectura, «Responde en la terminal»).</summary>
    public const float PermissionDetailLine = 16f;
    public const int PermissionDetailMaxLines = 4;

    /// <summary>Un Permiso o Recordatorio con botones enseñado en SOLO LECTURA (un notch por monitor: los botones están en
    /// otro): como el Permiso de solo lectura, título + detalle + la pista de una línea.</summary>
    public static SizeF ReadOnlyAlertSize(float scale) => TargetSize(NotchMode.Permission, scale, 0f);

    /// <summary>Tamaño del Permiso con botones con <paramref name="detailLines"/> líneas de detalle (1..4, se acota).</summary>
    public static SizeF PermissionButtonsSize(float scale, int detailLines = 1) =>
        new(PermissionWidth * scale,
            (PermissionButtonsHeight + (Math.Clamp(detailLines, 1, PermissionDetailMaxLines) - 1) * PermissionDetailLine) * scale);

    /// <summary>Radio de las esquinas inferiores: <c>(t·18+14)·s</c> con <c>t = clamp((w−30s)/110s, 0, 1)</c>.
    /// Crece con el ancho para que la pastilla estrecha no quede redonda y la ancha no quede cuadrada.
    /// (El renderer lo limita a la mitad del alto en los modos bajos, p. ej. Minimal.)</summary>
    public static float CornerRadius(float width, float scale)
    {
        if (scale <= 0) return 0;
        float t = Math.Clamp((width - 30f * scale) / (110f * scale), 0f, 1f);
        return (t * 18f + 14f) * scale;
    }

    /// <summary>Factor de tamaño «Auto» según el ancho lógico del monitor: ≥3000 → 1,3; ≥2400 → 1,15; si no 1.</summary>
    public static float AutoScale(int monitorLogicalWidth) =>
        monitorLogicalWidth >= 3000 ? 1.3f : monitorLogicalWidth >= 2400 ? 1.15f : 1.0f;

    /// <summary>Muelle para animar el tamaño al pasar de <paramref name="from"/> a <paramref name="to"/>:
    /// aparecer/desaparecer = blando; abrir/cerrar el expandido, la vista previa y los avisos = suave;
    /// cambios de contenido entre modos de reposo (Idle↔Working…) = crítico, sin pasarse.</summary>
    public static SpringPreset PresetFor(NotchMode from, NotchMode to)
    {
        if (from == NotchMode.Hidden || to == NotchMode.Hidden) return Motion.Motion.SpringGentle;
        if (IsOpenish(from) || IsOpenish(to)) return Motion.Motion.SpringSoft;
        return Motion.Motion.SpringCritical;
    }

    private static bool IsOpenish(NotchMode m) => m is NotchMode.Expanded or NotchMode.Peek
        or NotchMode.Permission or NotchMode.Completed or NotchMode.QuotaHud or NotchMode.QuotaAlert or NotchMode.Reminder;
}
