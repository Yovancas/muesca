using System.Text;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;

namespace ClaudeBarWin.Tests.Platform;

/// <summary>
/// <see cref="ShelfDataTransfer"/>: traduce el <c>IDataObject</c> de un arrastre (Explorer, navegadores, editores) a lo que
/// entra en la bandeja, y la bandeja a un <c>DataObject</c> para arrastrar fuera. Con el <c>DataObject</c> gestionado de
/// WinForms (en memoria, sin OLE ni portapapeles). <see cref="ShellLauncher"/>: argumentos de Explorer.
/// </summary>
public class ShelfDataTransferTests
{
    [Fact]
    public void Reads_files_from_Explorer()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { @"C:\a.txt", @"C:\docs" });
        var d = ShelfDataTransfer.Read(data);
        Assert.Equal(new[] { @"C:\a.txt", @"C:\docs" }, d.Paths);
    }

    [Fact]
    public void Reads_the_browser_url_in_UTF16_with_its_terminator()
    {
        var data = new DataObject();
        data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://ex.com/ñ\0\0")));
        data.SetData(DataFormats.UnicodeText, "Título de la página");
        var d = ShelfDataTransfer.Read(data);
        Assert.Equal("https://ex.com/ñ", d.Link);
        Assert.Null(d.Text);
    }

    [Fact]
    public void Reads_the_url_as_string_or_without_terminator_and_never_a_non_http_one()
    {
        var asString = new DataObject();
        asString.SetData(ShelfDataTransfer.UrlFormat, "https://ex.com/a");
        Assert.Equal("https://ex.com/a", ShelfDataTransfer.Read(asString).Link);

        var noNul = new DataObject();
        noNul.SetData(ShelfDataTransfer.UrlFormat, new MemoryStream(Encoding.Unicode.GetBytes("https://ex.com/b")));
        Assert.Equal("https://ex.com/b", ShelfDataTransfer.Read(noNul).Link);

        var js = new DataObject();
        js.SetData(ShelfDataTransfer.UrlFormat, new MemoryStream(Encoding.Unicode.GetBytes("javascript:alert(1)\0")));
        js.SetData(DataFormats.UnicodeText, "javascript:alert(1)");
        var d = ShelfDataTransfer.Read(js);
        Assert.Null(d.Link);
        Assert.Equal("javascript:alert(1)", d.Text);   // entra como texto: nunca se abre
    }

    [Fact]
    public void Reads_plain_text_and_refuses_nothing_usable()
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, "una nota\ncon dos líneas");
        Assert.Equal("una nota\ncon dos líneas", ShelfDataTransfer.Read(data).Text);

        var bitmapOnly = new DataObject();
        bitmapOnly.SetData(DataFormats.Bitmap, new Bitmap(2, 2));
        Assert.True(ShelfDataTransfer.Read(bitmapOnly).IsEmpty);
        Assert.True(ShelfDataTransfer.Read(null).IsEmpty);
    }

    /// <summary>Un origen ajeno que falla al entregar un formato no tumba el arrastre: ese formato cuenta como ausente.</summary>
    [Fact]
    public void A_source_that_throws_counts_as_empty()
    {
        Assert.True(ShelfDataTransfer.Read(new ThrowingData()).IsEmpty);
    }

    [Fact]
    public void Creates_file_drop_and_text_for_dragging_out()
    {
        var data = ShelfDataTransfer.Create(new ShelfDragPayload(new[] { @"C:\a.txt" }, "https://ex.com"));
        Assert.Equal(new[] { @"C:\a.txt" }, (string[])data.GetData(DataFormats.FileDrop)!);
        Assert.Equal("https://ex.com", data.GetData(DataFormats.UnicodeText));

        var textOnly = ShelfDataTransfer.Create(new ShelfDragPayload(Array.Empty<string>(), "hola"));
        Assert.False(textOnly.GetDataPresent(DataFormats.FileDrop));
        Assert.Equal("hola", textOnly.GetData(DataFormats.UnicodeText));
    }

    [Fact]
    public void Reveal_selects_the_file_in_Explorer()
    {
        Assert.Equal("/select,\"C:\\docs\\setup.exe\"", ShellLauncher.RevealArguments(@"C:\docs\setup.exe"));
        Assert.EndsWith(@"\explorer.exe", ShellLauncher.ExplorerPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(Path.IsPathFullyQualified(ShellLauncher.ExplorerPath), "ruta absoluta: nada de buscar en el PATH");
    }

    private sealed class ThrowingData : IDataObject
    {
        public object? GetData(string format, bool autoConvert) => throw new System.Runtime.InteropServices.COMException("x");
        public object? GetData(string format) => throw new System.Runtime.InteropServices.COMException("x");
        public object? GetData(Type format) => throw new System.Runtime.InteropServices.COMException("x");
        public bool GetDataPresent(string format, bool autoConvert) => true;
        public bool GetDataPresent(string format) => true;
        public bool GetDataPresent(Type format) => true;
        public string[] GetFormats(bool autoConvert) => new[] { DataFormats.FileDrop };
        public string[] GetFormats() => new[] { DataFormats.FileDrop };
        public void SetData(string format, bool autoConvert, object? data) { }
        public void SetData(string format, object? data) { }
        public void SetData(Type format, object? data) { }
        public void SetData(object? data) { }
    }
}
