using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CVAnalyzerAPI.Services.TokenServices;

public class TokenService : ITokenService
{
    private readonly JwtSettings _jwtSettings;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _refreshValidationParameters;

    public TokenService(IOptions<JwtSettings> options)
    {
        _jwtSettings = options.Value;

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));

        _signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        _refreshValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = false, 
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidAudience = _jwtSettings.Audience,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }

    public TokenCreationResult CreateToken(ApplicationUser user, string role)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_jwtSettings.ExpiryInMinutes);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email!),
            new Claim(ClaimTypes.Name, user.UserName!),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: _signingCredentials
        );

        return new TokenCreationResult
        {
            Token = _tokenHandler.WriteToken(token),
            ExpiresAt = expires,
            RefreshToken = GenerateRefreshToken(),
            RefreshTokenExpiresAt = now.AddDays(_jwtSettings.RefreshTokenExpiryInDays)
        };
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var principal = _tokenHandler.ValidateToken(token, _refreshValidationParameters, out var securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
        {
           
            return null;
        }

         if(DateTime.UtcNow - jwtSecurityToken.ValidTo > TimeSpan.FromDays(_jwtSettings.MaxExpiredAccessTokenAgeInDays))
        {
            return null;
        }
        return principal;
    }

    private static string GenerateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
}
