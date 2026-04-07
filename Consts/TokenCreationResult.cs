namespace CVAnalyzerAPI.Consts;

public class TokenCreationResult
{
    public string Token { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
}
