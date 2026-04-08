using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Models;
using System.Security.Claims;

namespace CVAnalyzerAPI.Services.TokenServices;

public interface ITokenService
{
    TokenCreationResult CreateToken(ApplicationUser user, string role);

    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
