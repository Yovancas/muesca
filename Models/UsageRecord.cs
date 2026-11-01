namespace ClaudeBarWin.Models;

/// <summary>
/// One assistant turn parsed from a Claude Code transcript line, with its token usage.
/// </summary>
public sealed record UsageRecord(
    DateTime TimestampUtc,
    string Model,
    long InputTokens,
    long OutputTokens,
    long CacheCreate5mTokens,
    long CacheCreate1hTokens,
    long CacheReadTokens)
{
    public long TotalTokens =>
        InputTokens + OutputTokens + CacheCreate5mTokens + CacheCreate1hTokens + CacheReadTokens;
}
