using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Lo que suena ahora (instantánea inmutable que publica el servicio de música). La posición NO se sondea:
/// <paramref name="Position"/> es la que dio el reproductor en <paramref name="PositionAtUtc"/> y
/// <see cref="PositionAt"/> la interpola mientras suena (velocidad 1×).
/// </summary>
/// <param name="ArtPng">Carátula ya reducida a PNG 64×64, o null si el reproductor no da imagen.</param>
/// <param name="Accent">Color de acento sacado de la carátula (<see cref="AlbumAccent"/>); gris claro sin carátula.</param>
/// <param name="SourceAppId">Id de la app que reproduce (p. ej. «Spotify.exe» o el AUMID del navegador).</param>
public sealed record NowPlaying(
    string Title,
    string Artist,
    string Album,
    bool Playing,
    TimeSpan Position,
    TimeSpan Duration,
    DateTime PositionAtUtc,
    byte[]? ArtPng,
    Color Accent,
    string SourceAppId)
{
    /// <summary>
    /// Posición estimada en <paramref name="utcNow"/>: en pausa, la del ancla; sonando, ancla + tiempo
    /// transcurrido (un reloj que va hacia atrás no rebobina). Siempre ≥ 0 y, si hay duración, ≤ duración.
    /// </summary>
    public TimeSpan PositionAt(DateTime utcNow)
    {
        var pos = Position;
        if (Playing)
        {
            var elapsed = utcNow - PositionAtUtc;
            if (elapsed > TimeSpan.Zero) pos += elapsed;
        }
        if (pos < TimeSpan.Zero) return TimeSpan.Zero;
        return Duration > TimeSpan.Zero && pos > Duration ? Duration : pos;
    }
}
