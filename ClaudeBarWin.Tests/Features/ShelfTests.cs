using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="ShelfStore"/>: la bandeja guarda rutas (no copia archivos), enlaces y texto suelto en
/// <c>shelf.json</c>. Existencia, tipo y reloj inyectados: nada toca el disco salvo el propio JSON.
/// </summary>
public sealed class ShelfTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 28, 9, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-shelf-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _folders = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _now = T0;

    private string PathJson => Path.Combine(_dir, "shelf.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private ShelfStore New() => new(PathJson, p => _existing.Contains(p), p => _folders.Contains(p), () => _now);

    [Fact]
    public void Adds_files_and_folders_without_duplicates()
    {
        _existing.UnionWith(new[] { @"C:\docs\informe.pdf", @"C:\docs\fotos" });
        _folders.Add(@"C:\docs\fotos");
        var s = New();
        int changed = 0;
        s.Changed += () => changed++;

        var added = s.AddPaths(new[] { @"C:\docs\informe.pdf", @"C:\docs\fotos\", "  ", @"c:\DOCS\informe.pdf" });
        Assert.Equal(2, added.Count);
        Assert.Equal(ShelfKind.File, added[0].Kind);
        Assert.Equal("informe.pdf", added[0].Display);
        Assert.Equal(ShelfKind.Folder, added[1].Kind);
        Assert.Equal(@"C:\docs\fotos", added[1].Value);
        Assert.Equal("fotos", added[1].Display);
        Assert.Equal(T0, added[0].AddedAt);
        Assert.Equal(1, changed);

        Assert.Empty(s.AddPaths(new[] { @"C:\docs\informe.pdf" }));
        Assert.Equal(1, changed); // nada nuevo → sin aviso ni escritura
        Assert.Equal(2, s.Items.Count);
    }

    [Fact]
    public void Caps_at_30_dropping_oldest()
    {
        var s = New();
        for (int i = 0; i < 25; i++)
        {
            _now = T0.AddMinutes(i);
            s.AddPaths(new[] { $@"C:\f\{i}.txt" });
        }
        var batch = s.AddPaths(Enumerable.Range(25, 10).Select(i => $@"C:\f\{i}.txt"));
        Assert.Equal(10, batch.Count);
        Assert.Equal(30, s.Items.Count);
        Assert.Equal(@"C:\f\5.txt", s.Items[0].Value);
        Assert.Equal(@"C:\f\34.txt", s.Items[^1].Value);

        // Un lote mayor que el tope solo deja (y devuelve) los 30 últimos.
        var big = s.AddPaths(Enumerable.Range(100, 40).Select(i => $@"C:\g\{i}.txt"));
        Assert.Equal(30, big.Count);
        Assert.Equal(@"C:\g\110.txt", s.Items[0].Value);
    }

    [Fact]
    public void Missing_file_is_unavailable()
    {
        _existing.Add(@"C:\a.txt");
        var s = New();
        var item = s.AddPaths(new[] { @"C:\a.txt" })[0];
        Assert.True(s.IsAvailable(item));

        _existing.Clear(); // borrado o movido después de soltarlo
        Assert.False(s.IsAvailable(item));
        Assert.Single(s.Items); // no se quita solo: la UI lo marca como no disponible

        var throwing = new ShelfStore(PathJson, _ => throw new UnauthorizedAccessException(), _ => false, () => _now);
        Assert.False(throwing.IsAvailable(item));

        Assert.True(s.IsAvailable(s.AddText("hola")));
        Assert.True(s.IsAvailable(s.AddLink("https://example.com")));
    }

    [Fact]
    public void Only_http_links()
    {
        var s = New();
        var l = s.AddLink(" https://example.com/ruta?x=1 ");
        Assert.Equal(ShelfKind.Link, l.Kind);
        Assert.Equal("https://example.com/ruta?x=1", l.Value);
        Assert.Equal("example.com/ruta", l.Display);
        Assert.Equal(ShelfKind.Link, s.AddLink("http://localhost:8080").Kind);

        Assert.Throws<ArgumentException>(() => s.AddLink("file:///C:/Windows/system32/calc.exe"));
        Assert.Throws<ArgumentException>(() => s.AddLink("javascript:alert(1)"));
        Assert.Throws<ArgumentException>(() => s.AddLink("ftp://example.com"));
        Assert.Throws<ArgumentException>(() => s.AddLink("no es una url"));
        Assert.Same(l, s.AddLink("https://example.com/ruta?x=1")); // duplicado → el mismo
        Assert.Equal(2, s.Items.Count);
    }

    [Fact]
    public void Text_is_capped()
    {
        var s = New();
        var t = s.AddText(new string('a', 9999) + "😀" + "cola");
        Assert.Equal(ShelfKind.Text, t.Kind);
        Assert.Equal(9999, t.Value.Length); // sin partir el emoji
        Assert.Equal(10_000, s.AddText(new string('b', 20_000)).Value.Length);

        var multi = s.AddText("\n\n  Primera línea útil  \nsegunda");
        Assert.Equal("Primera línea útil", multi.Display);
        Assert.Equal(60, s.AddText(new string('c', 100)).Display.Length);
        Assert.Throws<ArgumentException>(() => s.AddText("   "));
    }

    [Fact]
    public void Remove_and_Clear()
    {
        var s = New();
        var a = s.AddText("a");
        var b = s.AddText("b");
        var c = s.AddText("c");
        s.Remove(new[] { a.Id, c.Id });
        Assert.Equal(new[] { b }, s.Items);
        s.Clear();
        Assert.Empty(s.Items);
        Assert.Empty(New().Items);
    }

    [Fact]
    public void Persists_and_reloads()
    {
        _folders.Add(@"D:\proyectos");
        var s = New();
        s.AddPaths(new[] { @"D:\proyectos", @"D:\nota.md" });
        s.AddLink("https://lrclib.net");
        s.AddText("texto suelto");

        var again = New();
        Assert.Equal(s.Items, again.Items);
        Assert.Equal(new[] { ShelfKind.Folder, ShelfKind.File, ShelfKind.Link, ShelfKind.Text }, again.Items.Select(i => i.Kind));
    }

    [Fact]
    public void Corrupt_file_starts_empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathJson, "[[[");
        Assert.Empty(New().Items);
    }
}
