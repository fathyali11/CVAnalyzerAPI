namespace CVAnalyzerAPI.Models;


public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime ?RevokedAt { get; set; }
    public bool IsActive=> RevokedAt == null && ExpiresAt > DateTime.UtcNow;
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
