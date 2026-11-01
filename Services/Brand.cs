namespace ClaudeBarWin.Services;

/// <summary>
/// La marca que ve el usuario: <b>Muesca</b> (en español, «notch»), publicada por <b>Bekay</b>. Lo visible sale de aquí o de
/// los textos de <see cref="Strings"/>; lo interno (namespaces, ensamblado <c>ClaudeBarWin</c>, carpetas de datos, id del
/// paquete de Velopack, helper, variables <c>CLAUDEBAR_*</c>, hooks, clave de firma) se queda como estaba a propósito para
/// no romper instalaciones ni integraciones: ver docs/branding.md. Los colores son los de branding/muesca.svg.
/// </summary>
public static class Brand
{
    /// <summary>Nombre del producto en todo lo que se ve (ventanas, bandeja, globos, notch, instalador).</summary>
    public const string Name = "Muesca";

    /// <summary>Quien lo publica (Company del ensamblado, autores del paquete de Velopack).</summary>
    public const string Publisher = "Bekay";

    /// <summary>El repositorio público. GitHub redirige el nombre viejo (<c>Yovancas/claudebar-win</c>).</summary>
    public const string RepoUrl = "https://github.com/Yovancas/muesca";

    // ---- colores del logo (branding/muesca.svg y branding/README.md)

    /// <summary>Principio del degradado del fondo del logo, y el acento del tema (<see cref="Theme.Accent"/>).</summary>
    public static readonly Color Violet = Color.FromArgb(0x7B, 0x4D, 0xFF);

    /// <summary>Final del degradado del fondo del logo.</summary>
    public static readonly Color Blue = Color.FromArgb(0x1F, 0x6F, 0xFF);

    /// <summary>El negro del notch del logo.</summary>
    public static readonly Color Ink = Color.FromArgb(0x0A, 0x0A, 0x12);

    /// <summary>Pista de los dos anillos del logo.</summary>
    public static readonly Color RingTrack = Color.FromArgb(0x24, 0x24, 0x3A);

    /// <summary>Principio del degradado del anillo grande; también el acento como texto en oscuro (<see cref="Theme.AccentText"/>).</summary>
    public static readonly Color RingStart = Color.FromArgb(0xB4, 0x9B, 0xFF);

    /// <summary>Final del degradado del anillo grande.</summary>
    public static readonly Color RingEnd = Color.FromArgb(0x4C, 0x9B, 0xFF);
}
