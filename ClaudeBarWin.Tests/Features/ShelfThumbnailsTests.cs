using System.Drawing;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Miniaturas de la bandeja (F5 · B4), lógica pura: la clave de la caché (<see cref="ShelfThumbKey"/>), el LRU que libera
/// lo que saca (<see cref="LruCache{TKey,TValue}"/>), qué entradas se miran y piden su imagen al shell
/// (<see cref="ShelfThumbPolicy"/>) y el coordinador (<see cref="ShelfThumbnails"/>) con una fuente falsa
/// y dos colas manuales: lo de fondo y lo que vuelve al hilo de UI se ejecutan cuando el test quiere, y un reloj falso para
/// la caducidad de lo que va en vuelo (deterministas, sin hilos ni shell).
/// </summary>
public sealed class ShelfThumbnailsTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 9, 0, 0, DateTimeKind.Utc);

    private readonly Queue<Action> _background = new();
    private readonly Queue<Action> _ui = new();
    private readonly FakeSource _src = new();
    private readonly List<ShelfThumbnails> _made = new();
    private readonly List<string> _changedPaths = new();
    private int _changed;
    private DateTime _now = T0;

    public void Dispose()
    {
        foreach (var t in _made) t.Dispose();
        foreach (var b in _src.Made) b.Dispose();
    }

    /// <summary>Disco y shell falsos: lo que hay en <see cref="Disk"/> existe; cada imagen es un bitmap nuevo.</summary>
    private sealed class FakeSource : IShelfThumbnailSource
    {
        public readonly Dictionary<string, ShelfFileProbe> Disk = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Probed = new();
        public readonly List<string> Extracted = new();
        public readonly List<Bitmap> Made = new();
        public bool Fail;

        public ShelfFileProbe Probe(string path)
        {
            Probed.Add(path);
            return Disk.TryGetValue(path, out var p) ? p : ShelfFileProbe.Missing;
        }

        public Bitmap? Extract(string path, int sizePx)
        {
            Extracted.Add($"{path}@{sizePx}");
            if (Fail) return null;
            var b = new Bitmap(sizePx, sizePx);
            Made.Add(b);
            return b;
        }
    }

    private ShelfThumbnails Thumbs(int capacity = ShelfThumbnails.Capacity, Action<Action>? post = null)
    {
        var t = new ShelfThumbnails(_src, _background.Enqueue, post ?? _ui.Enqueue, capacity, () => _now);
        t.Changed += path =>
        {
            _changed++;
            _changedPaths.Add(path);
        };
        _made.Add(t);
        return t;
    }

    private void RunBackground()
    {
        while (_background.Count > 0) _background.Dequeue()();
    }

    private void RunUi()
    {
        while (_ui.Count > 0) _ui.Dequeue()();
    }

    private void RunAll()
    {
        RunBackground();
        RunUi();
    }

    private void OnDisk(string path, DateTime modified, long length = 1000) =>
        _src.Disk[path] = new ShelfFileProbe(ShelfProbeState.File, length, modified);

    private static ShelfItem File(string path) => new(Guid.NewGuid(), ShelfKind.File, path, Path.GetFileName(path), T0);

    private static ShelfItem Item(ShelfKind kind, string value) => new(Guid.NewGuid(), kind, value, value, T0);

    /// <summary>GDI+ da <see cref="ArgumentException"/> al tocar un bitmap ya liberado.</summary>
    private static bool Disposed(Bitmap b)
    {
        try { _ = b.Width; return false; }
        catch (ArgumentException) { return true; }
    }

    // ---------------------------------------------------------------- clave

    [Fact]
    public void Key_ignores_path_case_but_not_size_nor_date()
    {
        var a = ShelfThumbKey.For(@"C:\Fotos\Playa.JPG", 48, T0);
        Assert.Equal(a, ShelfThumbKey.For(@"c:\fotos\playa.jpg", 48, T0));
        Assert.Equal(a.GetHashCode(), ShelfThumbKey.For(@"c:\fotos\playa.jpg", 48, T0).GetHashCode());
        Assert.NotEqual(a, ShelfThumbKey.For(@"C:\Fotos\Playa.JPG", 72, T0));               // otra escala
        Assert.NotEqual(a, ShelfThumbKey.For(@"C:\Fotos\Playa.JPG", 48, T0.AddSeconds(1)));  // modificado
        Assert.NotEqual(a, ShelfThumbKey.For(@"C:\Fotos\Otra.JPG", 48, T0));
        Assert.Equal(a, ShelfThumbKey.For(@"C:\Fotos\Playa.JPG", 48, T0.ToLocalTime()));     // la misma hora, en local
    }

    // ---------------------------------------------------------------- LRU

    private sealed class Res : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    [Fact]
    public void Lru_evicts_and_disposes_the_least_recently_used()
    {
        using var lru = new LruCache<int, Res>(3);
        var r = Enumerable.Range(0, 6).Select(_ => new Res()).ToArray();
        lru.Add(1, r[1]);
        lru.Add(2, r[2]);
        lru.Add(3, r[3]);
        Assert.True(lru.TryGet(1, out var one));                 // 1 pasa a ser la más reciente
        Assert.Same(r[1], one);
        lru.Add(4, r[4]);                                        // sale la 2, la menos usada
        Assert.False(lru.Contains(2));
        Assert.Equal(1, r[2].Disposals);
        Assert.Equal(new[] { 4, 1, 3 }, lru.Keys);

        Assert.Same(r[3], lru.Peek(3));                          // mirar sin usar no la salva
        lru.Add(5, r[5]);
        Assert.False(lru.Contains(3));
        Assert.Equal(1, r[3].Disposals);
        Assert.Equal(3, lru.Count);

        var replacement = new Res();
        lru.Add(4, replacement);                                 // sustituir libera la vieja
        Assert.Equal(1, r[4].Disposals);
        lru.Add(4, replacement);                                 // el mismo objeto no se libera
        Assert.Equal(0, replacement.Disposals);

        Assert.True(lru.Remove(1));
        Assert.Equal(1, r[1].Disposals);
        Assert.False(lru.Remove(1));
        lru.Clear();
        Assert.Equal(0, lru.Count);
        Assert.Equal(1, r[5].Disposals);
        Assert.Equal(1, replacement.Disposals);
        Assert.Equal(0, r[0].Disposals);                         // nunca entró
    }

    [Fact]
    public void Default_capacity_is_64_and_keeps_the_most_recent()
    {
        Assert.Equal(64, ShelfThumbnails.Capacity);
        var t = Thumbs(capacity: 4);
        var items = Enumerable.Range(0, 6).Select(i => File($@"C:\f\{i}.jpg")).ToArray();
        foreach (var i in items) OnDisk(i.Value, T0);
        t.Request(items, 48);
        RunAll();
        Assert.Equal(4, t.Cached);
        Assert.Null(t.Get(items[0].Value, 48));                  // las primeras en llegar salen
        Assert.Null(t.Get(items[1].Value, 48));
        Assert.NotNull(t.Get(items[5].Value, 48));
        Assert.Equal(2, _src.Made.Count(Disposed));
    }

    // ---------------------------------------------------------------- coordinador

    [Fact]
    public void Thumbnails_come_from_the_background_and_repaint_on_the_ui_thread()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0, 2_516_582);

        t.Request(new[] { photo }, 48);
        Assert.Empty(_src.Probed);                               // en el hilo de UI no se toca el disco
        Assert.Empty(_src.Extracted);
        Assert.Equal(1, t.Pending);
        Assert.Null(t.Get(photo.Value, 48));

        RunBackground();
        Assert.Equal(new[] { @"C:\Fotos\playa.jpg@48" }, _src.Extracted);
        Assert.Null(t.Get(photo.Value, 48));                     // aún no volvió al hilo de UI
        Assert.Equal(0, _changed);

        RunUi();
        Assert.Same(_src.Made.Single(), t.Get(photo.Value, 48));
        Assert.Same(_src.Made.Single(), t.Get(@"c:\fotos\PLAYA.jpg", 48));
        Assert.Equal(1, _changed);
        Assert.Equal(new[] { photo.Value }, _changedPaths);      // avisa de QUÉ ruta se acaba de mirar
        Assert.Equal(0, t.Pending);
        Assert.Equal(new ShelfFileProbe(ShelfProbeState.File, 2_516_582, T0), t.Probe(photo.Value));
        Assert.Null(t.Get(photo.Value, 72));                     // a otra escala, otra imagen
    }

    [Fact]
    public void An_unchanged_file_is_not_extracted_again()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunAll();
        var first = t.Get(photo.Value, 48);

        t.Request(new[] { photo }, 48);
        RunAll();
        Assert.Equal(2, _src.Probed.Count);                      // se vuelve a mirar (barato)…
        Assert.Single(_src.Extracted);                           // …pero la imagen no se vuelve a pedir
        Assert.Same(first, t.Get(photo.Value, 48));
        Assert.False(Disposed(first!));
        Assert.Equal(2, _changed);
    }

    [Fact]
    public void A_modified_file_gets_a_new_thumbnail_and_frees_the_old_one()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunAll();
        var old = t.Get(photo.Value, 48)!;

        OnDisk(photo.Value, T0.AddMinutes(5));
        t.Request(new[] { photo }, 48);
        RunAll();
        var fresh = t.Get(photo.Value, 48);
        Assert.NotNull(fresh);
        Assert.NotSame(old, fresh);
        Assert.True(Disposed(old));
        Assert.Equal(1, t.Cached);
    }

    [Fact]
    public void Another_scale_is_another_thumbnail_and_both_stay_cached()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunAll();
        t.Request(new[] { photo }, 72);
        RunAll();
        Assert.Equal(new[] { @"C:\Fotos\playa.jpg@48", @"C:\Fotos\playa.jpg@72" }, _src.Extracted);
        Assert.Equal(48, t.Get(photo.Value, 48)!.Width);
        Assert.Equal(72, t.Get(photo.Value, 72)!.Width);
    }

    [Fact]
    public void A_file_that_no_longer_exists_has_no_thumbnail_and_reports_missing()
    {
        var t = Thumbs();
        var gone = File(@"C:\docs\borrado.pdf");                 // nunca estuvo en el disco
        t.Request(new[] { gone }, 48);
        RunAll();
        Assert.Empty(_src.Extracted);                            // sin archivo no se pide imagen
        Assert.Null(t.Get(gone.Value, 48));
        Assert.Equal(ShelfProbeState.Missing, t.Probe(gone.Value)!.Value.State);
        Assert.Equal(1, _changed);                               // la página se repinta: «No disponible»

        // Estaba, se pintó su miniatura y luego se borró: la miniatura se va (y se libera).
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunAll();
        var shown = t.Get(photo.Value, 48)!;
        _src.Disk.Remove(photo.Value);
        t.Request(new[] { photo }, 48);
        RunAll();
        Assert.Null(t.Get(photo.Value, 48));
        Assert.True(Disposed(shown));
        Assert.Equal(ShelfProbeState.Missing, t.Probe(photo.Value)!.Value.State);
    }

    [Fact]
    public void A_failed_extraction_keeps_the_size_and_falls_back_to_the_drawn_icon()
    {
        var t = Thumbs();
        var doc = File(@"C:\docs\raro.xyz");
        OnDisk(doc.Value, T0, 4096);
        _src.Fail = true;
        t.Request(new[] { doc }, 48);
        RunAll();
        Assert.Single(_src.Extracted);
        Assert.Null(t.Get(doc.Value, 48));
        Assert.Equal(4096, t.Probe(doc.Value)!.Value.Length);
        Assert.Equal(1, _changed);
    }

    [Fact]
    public void Links_texts_shortcuts_and_network_paths_never_reach_the_shell()
    {
        var t = Thumbs();
        var url = File(@"C:\docs\web.url");
        var lnk = File(@"C:\docs\acceso.lnk");
        OnDisk(url.Value, T0);
        OnDisk(lnk.Value, T0);
        t.Request(new[]
        {
            Item(ShelfKind.Link, "https://example.com/a.png"),
            Item(ShelfKind.Text, @"C:\no\es\una\ruta.jpg"),
            url, lnk,
            File(@"\\servidor\c$\foto.jpg"),
            File(@"\\?\C:\foto.jpg"),
            File(@"relativo\foto.jpg"),
            File(@"C:\docs\informe.pdf:oculto"),
        }, 48);
        RunAll();
        Assert.Empty(_src.Extracted);                            // ningún icono que pueda apuntar a \\host\…
        Assert.Equal(new[] { url.Value, lnk.Value }, _src.Probed);   // los accesos directos sí se miran (tamaño)
        Assert.Equal(ShelfProbeState.File, t.Probe(url.Value)!.Value.State);
    }

    [Fact]
    public void A_request_in_flight_is_not_repeated()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        t.Request(new[] { photo }, 48);
        Assert.Single(_background);
        RunAll();
        Assert.Single(_src.Extracted);
    }

    [Fact]
    public void Entries_that_left_the_shelf_are_forgotten_and_freed()
    {
        var t = Thumbs();
        var a = File(@"C:\Fotos\a.jpg");
        var b = File(@"C:\Fotos\b.jpg");
        OnDisk(a.Value, T0);
        OnDisk(b.Value, T0);
        t.Request(new[] { a, b }, 48);
        RunAll();
        var imageA = t.Get(a.Value, 48)!;

        t.Request(new[] { b }, 48);                              // «a» se quitó de la bandeja
        Assert.Null(t.Get(a.Value, 48));
        Assert.Null(t.Probe(a.Value));
        Assert.True(Disposed(imageA));
        Assert.NotNull(t.Get(b.Value, 48));
    }

    [Fact]
    public void Results_after_dispose_are_released_and_do_not_repaint()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunBackground();
        t.Dispose();
        RunUi();
        Assert.True(Disposed(_src.Made.Single()));
        Assert.Null(t.Get(photo.Value, 48));
        Assert.Equal(0, _changed);

        t.Request(new[] { photo }, 48);                          // cerrada: no pide nada
        Assert.Empty(_background);
    }

    [Fact]
    public void If_the_window_is_closing_the_image_is_released()
    {
        var t = Thumbs(post: _ => throw new ObjectDisposedException("NotchForm"));
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunBackground();
        Assert.True(Disposed(_src.Made.Single()));
    }

    // ---------------------------------------------------------------- en vuelo: caducidad y generaciones

    [Fact]
    public void A_hung_request_expires_and_its_late_result_is_discarded()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        var hung = _background.Dequeue();                        // el trabajo se cuelga: de momento no vuelve

        _now += TimeSpan.FromSeconds(5);
        t.Request(new[] { photo }, 48);
        Assert.Empty(_background);                               // aún va de camino
        _now += ShelfThumbnails.PendingTimeout;
        t.Request(new[] { photo }, 48);
        Assert.Single(_background);                              // caducó: se repite
        RunAll();
        var fresh = t.Get(photo.Value, 48);
        Assert.NotNull(fresh);
        Assert.Equal(1, _changed);

        // El colgado vuelve al fin (en su hilo abandonado): su resultado ya no cuenta y su imagen se libera.
        hung();
        RunUi();
        Assert.Same(fresh, t.Get(photo.Value, 48));
        Assert.True(Disposed(_src.Made.Last()));
        Assert.Equal(1, _changed);
        Assert.Equal(0, t.Pending);
    }

    [Fact]
    public void A_path_lost_twice_is_not_asked_again_until_it_is_dropped_again()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        _background.Clear();                                     // se pierde
        _now += ShelfThumbnails.PendingTimeout;
        t.Request(new[] { photo }, 48);
        Assert.Single(_background);
        _background.Clear();                                     // se pierde otra vez
        _now += ShelfThumbnails.PendingTimeout;
        t.Request(new[] { photo }, 48);
        Assert.Empty(_background);                               // no se insiste: queda el glifo
        Assert.Equal(1, t.Pending);

        // Quitarla de la bandeja la olvida; si se vuelve a soltar, se pide otra vez.
        t.Request(Array.Empty<ShelfItem>(), 48);
        Assert.Equal(0, t.Pending);
        t.Request(new[] { photo }, 48);
        Assert.Single(_background);
    }

    [Fact]
    public void A_result_that_cannot_reach_the_ui_thread_is_retried_when_it_expires()
    {
        bool fail = true;
        var t = Thumbs(post: work =>
        {
            if (fail) throw new InvalidOperationException("la ventana aún no tiene asa");
            _ui.Enqueue(work);
        });
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        RunBackground();
        Assert.True(Disposed(_src.Made.Single()));
        Assert.Equal(1, t.Pending);                              // la ranura no se queda bloqueada para siempre…

        fail = false;
        _now += ShelfThumbnails.PendingTimeout;
        t.Request(new[] { photo }, 48);                          // …caduca y se repite
        RunAll();
        Assert.NotNull(t.Get(photo.Value, 48));
        Assert.Equal(0, t.Pending);
    }

    [Fact]
    public void A_result_for_an_entry_that_left_the_shelf_is_discarded()
    {
        var t = Thumbs();
        var photo = File(@"C:\Fotos\playa.jpg");
        OnDisk(photo.Value, T0);
        t.Request(new[] { photo }, 48);
        t.Request(Array.Empty<ShelfItem>(), 48);                 // se quitó antes de que volviera
        RunAll();
        Assert.Null(t.Get(photo.Value, 48));
        Assert.Null(t.Probe(photo.Value));
        Assert.True(Disposed(_src.Made.Single()));
        Assert.Equal(0, _changed);
    }

    // ---------------------------------------------------------------- qué pide imagen al shell

    [Theory]
    [InlineData(ShelfKind.File, @"C:\Fotos\playa.jpg", ShelfGlyph.File, true, true)]
    [InlineData(ShelfKind.File, @"C:\docs\informe.pdf", ShelfGlyph.File, true, true)]
    [InlineData(ShelfKind.Folder, @"C:\Fotos", ShelfGlyph.Folder, true, true)]
    [InlineData(ShelfKind.Folder, @"C:\raro.lnk", ShelfGlyph.Folder, true, true)]        // una carpeta no es un acceso directo
    [InlineData(ShelfKind.File, @"C:\docs\web.url", ShelfGlyph.Link, true, false)]       // .url: glifo de enlace
    [InlineData(ShelfKind.File, @"C:\docs\WEB.URL", ShelfGlyph.Link, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\sitio.website", ShelfGlyph.Link, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\acceso.lnk", ShelfGlyph.File, true, false)]    // su icono puede apuntar a \\host
    [InlineData(ShelfKind.File, @"C:\docs\acceso.lnk. ", ShelfGlyph.File, true, false)]  // disfrazado: igual
    [InlineData(ShelfKind.File, @"C:\docs\b.library-ms", ShelfGlyph.File, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\c.searchConnector-ms", ShelfGlyph.File, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\d.scf", ShelfGlyph.File, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\azul.theme", ShelfGlyph.File, true, false)]          // CVE-2024-21320
    [InlineData(ShelfKind.File, @"C:\docs\azul.themepack", ShelfGlyph.File, true, false)]
    [InlineData(ShelfKind.File, @"C:\docs\azul.deskthemepack", ShelfGlyph.File, true, false)]
    [InlineData(ShelfKind.File, @"\\servidor\c$\foto.jpg", ShelfGlyph.File, false, false)]
    [InlineData(ShelfKind.File, @"\\?\C:\foto.jpg", ShelfGlyph.File, false, false)]
    [InlineData(ShelfKind.File, @"relativo\foto.jpg", ShelfGlyph.File, false, false)]
    [InlineData(ShelfKind.File, @"C:\docs\informe.pdf:oculto", ShelfGlyph.File, false, false)]
    [InlineData(ShelfKind.Link, "https://example.com/foto.jpg", ShelfGlyph.Link, false, false)]
    [InlineData(ShelfKind.Text, @"C:\Fotos\playa.jpg", ShelfGlyph.Text, false, false)]
    public void Only_local_files_and_folders_ask_the_shell(ShelfKind kind, string value, ShelfGlyph glyph, bool probes,
        bool image)
    {
        var item = Item(kind, value);
        Assert.Equal(glyph, ShelfThumbPolicy.GlyphFor(item));
        Assert.Equal(probes, ShelfThumbPolicy.Probes(item));
        // Lo que hay en el disco coincide aquí con el tipo con que entró.
        var state = kind == ShelfKind.Folder ? ShelfProbeState.Folder : ShelfProbeState.File;
        Assert.Equal(image, probes && ShelfThumbPolicy.WantsShellImage(value, state));
    }

    [Fact]
    public void The_shell_is_asked_by_what_is_on_disk_not_by_how_the_entry_came_in()
    {
        var t = Thumbs();
        // Entró como carpeta, pero ahora en esa ruta hay un ARCHIVO .url (con IconFile=\\host\…): ni se pide.
        var wasFolder = Item(ShelfKind.Folder, @"C:\docs\era-carpeta.url");
        OnDisk(wasFolder.Value, T0);
        // Entró como archivo «.lnk», pero ahora es una carpeta: se pide (su desktop.ini lo mira la fuente).
        var nowFolder = Item(ShelfKind.File, @"C:\docs\ahora-carpeta.lnk");
        _src.Disk[nowFolder.Value] = new ShelfFileProbe(ShelfProbeState.Folder, 0, T0);
        t.Request(new[] { wasFolder, nowFolder }, 48);
        RunAll();
        Assert.Equal(new[] { @"C:\docs\ahora-carpeta.lnk@48" }, _src.Extracted);
        Assert.Equal(ShelfProbeState.File, t.Probe(wasFolder.Value)!.Value.State);
    }

    [Theory]
    [InlineData(@"C:\docs\web.url", ShelfProbeState.File, false)]
    [InlineData(@"C:\docs\web.url", ShelfProbeState.Folder, true)]
    [InlineData(@"C:\docs\foto.jpg", ShelfProbeState.File, true)]
    [InlineData(@"C:\docs\foto.jpg", ShelfProbeState.Missing, false)]
    [InlineData(@"Z:\docs\foto.jpg", ShelfProbeState.Opaque, false)]
    public void Shell_images_follow_the_probed_state(string path, ShelfProbeState state, bool image) =>
        Assert.Equal(image, ShelfThumbPolicy.WantsShellImage(path, state));

    [Theory]
    [InlineData(DriveType.Fixed, true)]
    [InlineData(DriveType.Removable, true)]
    [InlineData(DriveType.CDRom, true)]
    [InlineData(DriveType.Ram, true)]
    [InlineData(DriveType.Unknown, true)]
    [InlineData(DriveType.Network, false)]                       // SMB: NTLM y cuelgues largos
    [InlineData(DriveType.NoRootDirectory, false)]
    public void The_background_thread_never_touches_network_drives(DriveType type, bool touches) =>
        Assert.Equal(touches, ShelfThumbPolicy.Touches(type));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("[.ShellClassInfo]\r\nIconResource=%SystemRoot%\\system32\\imageres.dll,-112\r\n", false)]   // carpeta especial
    [InlineData("[.ShellClassInfo]\r\nIconFile=C:\\iconos\\a.ico\r\nIconIndex=0\r\n", false)]
    [InlineData("[.ShellClassInfo]\r\nInfoTip=\\\\host\\no-es-un-icono\r\n", false)]
    [InlineData("[.ShellClassInfo]\r\nIconResource=\\\\host\\share\\a.ico,0\r\n", true)]
    [InlineData("[.ShellClassInfo]\nIconFile = \"//host/share/a.ico\"\n", true)]
    [InlineData("[.ShellClassInfo]\r\niconresource=\\\\?\\UNC\\host\\a.ico\r\n", true)]
    [InlineData("[ExtShellFolderViews]\r\nLogo=file://host/share/logo.png\r\n", true)]
    [InlineData("[{BE098140-A513-11D0-A3A4-00C04FD706EC}]\r\nIconArea_Image=\\\\host\\fondo.jpg\r\n", true)]
    [InlineData("[.ShellClassInfo]\r\nCLSID={645FF040-5081-101B-9F08-00AA002F954E}\r\n", true)]          // controlador COM
    [InlineData("[.ShellClassInfo]\r\nCLSID2={0AFACED1-E828-11D1-9187-B532F1E9575D}\r\n", true)]
    [InlineData("[.ShellClassInfo]\r\nUICLSID={7BD29E00-76C1-11CF-9DD0-00A0C9034933}\r\n", true)]
    public void A_folder_whose_icon_lives_on_another_machine_keeps_the_glyph(string? desktopIni, bool remote) =>
        Assert.Equal(remote, ShelfThumbPolicy.UnsafeFolderIcon(desktopIni, NoEnvironment));

    /// <summary>Expansión falsa (determinista: no depende del equipo que ejecuta los tests).</summary>
    private static string NoEnvironment(string s) => s;

    [Theory]
    [InlineData("IconResource=%LOGONSERVER%\\iconos\\a.ico,0", true)]      // %LOGONSERVER% = \\DC01
    [InlineData("IconFile=\"%REMOTO%/a.ico\"", true)]                         // %REMOTO% = //host/share
    [InlineData("IconResource=%SystemRoot%\\system32\\imageres.dll,-112", false)]
    [InlineData("IconResource=%NO_DEFINIDA%\\a.ico,0", false)]              // sin expandir: ruta relativa, local
    public void Environment_variables_are_expanded_before_deciding(string line, bool unsafeIcon)
    {
        static string Expand(string s) => s.Replace("%LOGONSERVER%", @"\\DC01").Replace("%REMOTO%", "//host/share")
            .Replace("%SystemRoot%", @"C:\Windows");
        Assert.Equal(unsafeIcon, ShelfThumbPolicy.UnsafeFolderIcon("[.ShellClassInfo]\r\n" + line + "\r\n", Expand));
        Assert.False(ShelfThumbPolicy.UnsafeFolderIcon("[.ShellClassInfo]\r\nIconResource=%X%\\a.ico\r\n",
            _ => throw new ArgumentException("variable rota")));   // si expandir falla, cuenta lo escrito
    }
}
