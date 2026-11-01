namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Cola de avisos del notch (sin ranura única: ningún aviso se pierde). PURA: el instante llega por
/// parámetro a <see cref="Current"/>, que es quien caduca y arranca relojes.
///
/// <list type="bullet">
/// <item><b>Prioridad</b>: el <see cref="NotchAlertKind"/> menor gana; a igualdad, el que llegó antes
/// (FIFO por orden de llegada).</item>
/// <item><b>Fusión</b> por (tipo, sesión): el nuevo sustituye al antiguo y ocupa su sitio al final.
/// Si el sustituido se estaba mostrando, su duración vuelve a empezar.</item>
/// <item><b>Caducidad</b>: <see cref="NotchAlert.Duration"/> cuenta desde que el aviso pasa a ser el
/// actual. Si otro más prioritario lo tapa, su reloj se reinicia: cuando vuelva tendrá su tiempo
/// entero (un «Terminado» tapado por un permiso no caduca sin haberse visto).</item>
/// </list>
/// </summary>
public sealed class NotchEventQueue
{
    private readonly List<NotchAlert> _items = new();   // en orden de llegada
    private NotchAlert? _current;
    private DateTime _currentSince;

    /// <summary>Avisos en cola (incluido el actual y los caducados aún no purgados por <see cref="Current"/>).</summary>
    public int Count => _items.Count;

    /// <summary>Cuándo caduca el aviso actual (<c>null</c> si no hay o es persistente). Lo usa la máquina
    /// de modos para despertarse justo a tiempo, sin timers en reposo.</summary>
    public DateTime? CurrentExpiresAtUtc =>
        _current?.Duration is { } d ? _currentSince + d : null;

    public void Enqueue(NotchAlert a)
    {
        ArgumentNullException.ThrowIfNull(a);
        int i = IndexOf(a.Kind, a.SessionId);
        if (i >= 0)
        {
            bool wasCurrent = ReferenceEquals(_items[i], _current);
            _items.RemoveAt(i);
            _items.Add(a);
            if (wasCurrent)
            {
                // Sigue en pantalla con el contenido nuevo; su reloj vuelve a empezar desde que llegó.
                _current = a;
                _currentSince = a.EnqueuedUtc;
            }
            return;
        }
        _items.Add(a);
    }

    public void Remove(NotchAlertKind k, string sessionId)
    {
        int i = IndexOf(k, sessionId);
        if (i < 0) return;
        if (ReferenceEquals(_items[i], _current)) _current = null;
        _items.RemoveAt(i);
    }

    /// <summary>El aviso en cola de ese tipo y sesión (o <c>null</c>), sin tocar relojes.</summary>
    public NotchAlert? Find(NotchAlertKind k, string sessionId)
    {
        int i = IndexOf(k, sessionId);
        return i < 0 ? null : _items[i];
    }

    /// <summary>Cambia el contenido de un aviso SIN moverlo de sitio ni reiniciar su reloj (p. ej. un Permiso que pierde
    /// sus botones). Mismo tipo y sesión obligatorios; si <paramref name="old"/> ya no está, no hace nada.</summary>
    public void ReplaceInPlace(NotchAlert old, NotchAlert updated)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(updated);
        if (old.Kind != updated.Kind || !string.Equals(old.SessionId, updated.SessionId, StringComparison.Ordinal))
            throw new ArgumentException("el aviso nuevo debe ser del mismo tipo y sesión", nameof(updated));
        int i = _items.FindIndex(x => ReferenceEquals(x, old));
        if (i < 0) return;
        _items[i] = updated;
        if (ReferenceEquals(_current, old)) _current = updated;
    }

    /// <summary>El aviso que toca mostrar en <paramref name="nowUtc"/> (o <c>null</c>). Purga el actual si
    /// ha caducado y arranca el reloj del siguiente.</summary>
    public NotchAlert? Current(DateTime nowUtc)
    {
        while (true)
        {
            var best = Best();
            if (best is null) { _current = null; return null; }

            if (!ReferenceEquals(best, _current))
            {
                _current = best;            // el anterior (si lo había) queda tapado: su reloj se reinicia
                _currentSince = nowUtc;
            }

            if (best.Duration is { } d && nowUtc - _currentSince >= d)
            {
                _items.Remove(best);
                _current = null;
                continue;                   // caducado: el siguiente empieza ahora
            }
            return best;
        }
    }

    private NotchAlert? Best()
    {
        NotchAlert? best = null;
        foreach (var a in _items)
            if (best is null || a.Kind < best.Kind) best = a;   // estricto: a igualdad gana el más antiguo
        return best;
    }

    private int IndexOf(NotchAlertKind k, string sessionId) =>
        _items.FindIndex(a => a.Kind == k && string.Equals(a.SessionId, sessionId, StringComparison.Ordinal));
}
