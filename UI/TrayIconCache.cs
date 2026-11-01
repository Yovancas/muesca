namespace ClaudeBarWin.UI;

/// <summary>
/// Caché de UNA ranura del icono de bandeja (F3 Task 7): cada refresco recreaba el badge (y su HICON)
/// aunque no hubiera cambiado nada. Con la misma <see cref="BadgeSpec"/> devuelve el icono vigente; con
/// otra pinta uno nuevo y entrega el anterior en <c>retired</c> para que el llamador lo libere DESPUÉS de
/// colocar el nuevo en la bandeja (liberarlo antes dejaría a la shell con un HICON destruido).
/// </summary>
internal sealed class TrayIconCache<T> : IDisposable where T : class, IDisposable
{
    private BadgeSpec? _key;
    private T? _current;

    public T Get(BadgeSpec key, Func<T> render, out T? retired)
    {
        retired = null;
        if (_current is not null && _key == key) return _current;
        retired = _current;
        _current = render();
        _key = key;
        return _current;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
        _key = null;
    }
}
