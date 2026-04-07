using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Models;

namespace CVAnalyzerAPI.Services.TokenServices;

public interface ITokenService
{
    TokenCreationResult CreateToken(ApplicationUser user, string role);
}
