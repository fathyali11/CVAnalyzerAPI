using System.ComponentModel.DataAnnotations;

namespace CVAnalyzerAPI.Consts;

public class JwtSettings
{
    [Range(1, 30)]
    public int MaxExpiredAccessTokenAgeInDays { get; set; } = 7 ;
    [Required, MinLength(32)]
    public string SecretKey { get; set; } = null!;

    [Required]
    public string Issuer { get; set; } = null!;

    [Required]
    public string Audience { get; set; } = null!;

    [Range(1, 1440)]
    public int ExpiryInMinutes { get; set; }

    [Range(1, 365)]
    public int RefreshTokenExpiryInDays { get; set; }
}
