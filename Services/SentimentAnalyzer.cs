using NewsIntel.API.Services.Interfaces;

namespace NewsIntel.API.Services;

public class SentimentAnalyzer : ISentimentAnalyzer
{
    private static readonly HashSet<string> PositiveWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "breakthrough", "surge", "rally", "growth", "wins", "win", "positive", "record high", "recovery",
        "approve", "approved", "success", "successful", "rises", "rise", "gains", "gain", "advances",
        "advance", "boosts", "boost", "improves", "improve", "profit", "profits", "innovation",
        "agreement", "deal", "accord", "progress", "expanding", "expand", "thrives", "thrive"
    };

    private static readonly HashSet<string> NegativeWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "crash", "crisis", "fail", "failed", "decline", "collapse", "protest", "loss", "recession",
        "scandal", "warning", "drops", "drop", "falls", "fall", "loses", "lose", "cuts", "cut",
        "fears", "fear", "concerns", "concern", "slumps", "slump", "tumbles", "tumble", "crisis",
        "war", "conflict", "attack", "attacks", "death", "deaths", "killed", "shooting", "disaster"
    };

    public string Analyze(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "neutral";
        var lower = text.ToLower();
        int pos = PositiveWords.Count(w => lower.Contains(w));
        int neg = NegativeWords.Count(w => lower.Contains(w));
        if (pos > neg) return "positive";
        if (neg > pos) return "negative";
        return "neutral";
    }
}
