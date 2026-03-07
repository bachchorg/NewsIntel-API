namespace NewsIntel.API.Services.Interfaces;

public interface ISentimentAnalyzer
{
    string Analyze(string text);
}
