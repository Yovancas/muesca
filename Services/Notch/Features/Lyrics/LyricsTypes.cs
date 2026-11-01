namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// La pista tal como la da el reproductor (SMTC): en un vídeo del navegador, <paramref name="Title"/> es el título del
/// vídeo y <paramref name="Artist"/> el canal. <paramref name="SourceAppId"/> es el AUMID o ejecutable de la app.
/// </summary>
public sealed record TrackQuery(string Title, string Artist, string? Album, TimeSpan Duration, string SourceAppId);

/// <summary>Un (título, artista) a buscar en las fuentes de letras. Menor <paramref name="Rank"/> = más probable.
/// <paramref name="Artist"/> vacío = buscar solo por título.</summary>
public sealed record SearchCandidate(string Title, string Artist, int Rank);
