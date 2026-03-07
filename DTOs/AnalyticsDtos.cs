namespace NewsIntel.API.DTOs;

public record KeywordFrequencyDto(string Keyword, int Count);
public record SourceDistributionDto(string Source, int Count, double Percentage);
public record SentimentTrendDto(string Bucket, int Positive, int Neutral, int Negative);
public record SpikeAlertDto(string Keyword, int PreviousCount, int CurrentCount, double IncreasePercent, DateTime DetectedAt);

public record AnalyticsSnapshot(
    List<KeywordFrequencyDto> KeywordFrequencies,
    List<SourceDistributionDto> SourceDistribution,
    List<SentimentTrendDto> SentimentTrend,
    int TotalArticles
);
