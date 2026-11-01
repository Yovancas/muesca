using System.Text;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch.Platform;

/// <summary>
/// Arrastrar y soltar de la bandeja (F5 · B4), lado Windows: traduce el <see cref="IDataObject"/> de un arrastre
/// (Explorador, navegadores, editores) a un <see cref="ShelfDrop"/> y un <see cref="ShelfDragPayload"/> a un
/// <see cref="DataObject"/> para <c>DoDragDrop</c>. Lo que se decide (qué gana, qué es un enlace) está en
/// <see cref="ShelfDrop.Parse"/>; aquí solo se leen los formatos <c>FileDrop</c>, <c>UniformResourceLocatorW</c> y
/// <c>UnicodeText</c>.
/// </summary>
public static class ShelfDataTransfer
{
    /// <summary>URL que ponen los navegadores al arrastrar un enlace o la pestaña (UTF-16 terminado en NUL).</summary>
    public const string UrlFormat = "UniformResourceLocatorW";

    /// <summary>
    /// Lee el arrastre. Un origen ajeno puede fallar al entregar un formato (COM, datos con retraso que no llegan…):
    /// ese formato cuenta como ausente y el resto sigue valiendo; nunca lanza.
    /// </summary>
    public static ShelfDrop Read(IDataObject? data)
    {
        if (data is null) return ShelfDrop.None;
        var files = Get(data, DataFormats.FileDrop) as string[];
        string? url = Get(data, UrlFormat) switch
        {
            string s => s,
            MemoryStream ms => Decode(ms),
            _ => null,
        };
        var text = Get(data, DataFormats.UnicodeText) as string;
        return ShelfDrop.Parse(files, url, text);
    }

    /// <summary>Lo que se arrastra fuera: rutas como <c>FileDrop</c> (solo si hay) y texto como <c>UnicodeText</c>.</summary>
    public static DataObject Create(ShelfDragPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var data = new DataObject();
        if (payload.Files.Count > 0) data.SetData(DataFormats.FileDrop, payload.Files.ToArray());
        if (payload.Text is { Length: > 0 } t) data.SetData(DataFormats.UnicodeText, t);
        return data;
    }

    private static object? Get(IDataObject data, string format)
    {
        try { return data.GetDataPresent(format) ? data.GetData(format) : null; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException
            or ArgumentException or InvalidCastException or NotSupportedException or OutOfMemoryException)
        {
            AppLog.Warn($"notch: arrastre: el origen no entregó {format}: {ex.Message}");
            return null;
        }
    }

    /// <summary>UTF-16 hasta el primer NUL (y sin leer más de 8 KB: una URL no es un libro).</summary>
    private static string? Decode(MemoryStream ms)
    {
        const int MaxBytes = 8 * 1024;
        var bytes = new byte[Math.Min(MaxBytes, Math.Max(0, ms.Length - ms.Position))];
        int n = ms.Read(bytes, 0, bytes.Length);
        var s = Encoding.Unicode.GetString(bytes, 0, n - n % 2);
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }
}
