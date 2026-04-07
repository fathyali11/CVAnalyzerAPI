namespace CVAnalyzerAPI.DTOs.AuthsDTOs;

public class AuthResponse
{
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Token { get; set; } = null!;
    public DateTime Expiration { get; set; }
}
