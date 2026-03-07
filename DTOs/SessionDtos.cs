namespace NewsIntel.API.DTOs;

public record KeywordEntry(string Term, string Logic);

public record CreateSessionRequest(
    string Name,
    List<KeywordEntry> Keywords,
    List<string> Sources,
    DateTime? DateRangeFrom,
    DateTime? DateRangeTo,
    int PollIntervalSeconds = 60
);

public record UpdateSessionRequest(
    string? Name,
    List<KeywordEntry>? Keywords,
    List<string>? Sources,
    DateTime? DateRangeFrom,
    DateTime? DateRangeTo,
    int? PollIntervalSeconds
);

public record SessionKeywordDto(int Id, string Term, string Logic);

public record SessionSourceDto(int Id, string SourceName, bool IsEnabled);

public record SessionDto(
    Guid Id,
    string Name,
    string State,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? StoppedAt,
    DateTime? DateRangeFrom,
    DateTime? DateRangeTo,
    int PollIntervalSeconds,
    List<SessionKeywordDto> Keywords,
    List<SessionSourceDto> Sources
);
