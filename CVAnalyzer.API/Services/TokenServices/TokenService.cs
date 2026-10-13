using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CVAnalyzerAPI.Services.TokenServices;

public class TokenService(IOptions<JwtSettings> options) : ITokenService
{
    private readonly JwtSettings _jwtSettings = options.Value;
    public TokenCreationResult CreateToken(ApplicationUser user,string role)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Name,user.UserName!),
            new Claim(ClaimTypes.Role, role)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryInMinutes);
        var token = new JwtSecurityToken(
            issuer:_jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires:expires ,
            signingCredentials: creds
        );

        return new TokenCreationResult 
        { 
            Token= new JwtSecurityTokenHandler().WriteToken(token) ,
            ExpiresAt=expires,
            RefreshToken= GenerateRefreshToken(),
            RefreshTokenExpiresAt= DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryInDays)
        };
    }

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _jwtSettings?.Issuer,
            ValidAudience = _jwtSettings?.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings?.SecretKey ?? string.Empty))
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

        if (securityToken is not JwtSecurityToken jwtSecurityToken ||
            !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256))
            return null;

        return principal;
    }

    private string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
