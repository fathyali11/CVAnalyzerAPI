using Microsoft.AspNetCore.Identity;

namespace CVAnalyzerAPI.Models;

public class ApplicationUser:IdentityUser
{
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;

    public ICollection<CV> CVs { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
