using CVAnalyzerAPI.Models;

namespace CVAnalyzerAPI.Services.TokenServices;

public interface ITokenService
{
    string CreateToken(ApplicationUser user, string role);
}
