using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.TokenServices;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Services.TokenServices;

public class TokenServiceTests
{
    private const string Issuer = "test-issuer";
    private const string Audience = "test-audience";
    private const string SecretKey = "unit-test-secret-key-with-at-least-32-characters";

    private static JwtSettings CreateSettings(
        int expiryInMinutes = 60,
        int refreshTokenExpiryInDays = 7,
        int maxExpiredAccessTokenAgeInDays = 7,
        string issuer = Issuer,
        string audience = Audience,
        string secretKey = SecretKey) => new()
        {
            SecretKey = secretKey,
            Issuer = issuer,
            Audience = audience,
            ExpiryInMinutes = expiryInMinutes,
            RefreshTokenExpiryInDays = refreshTokenExpiryInDays,
            MaxExpiredAccessTokenAgeInDays = maxExpiredAccessTokenAgeInDays
        };

    private static TokenService CreateSut(JwtSettings settings) => new(Options.Create(settings));

    private static ApplicationUser CreateUser(
        string id = "user-1",
        string email = "john@example.com",
        string userName = "john") => new()
        {
            Id = id,
            Email = email,
            UserName = userName
        };

    // --- CreateToken ---

    [Fact]
    public void CreateToken_ReturnsAccessTokenWithIssuerAudienceAndAlgorithm()
    {
        var sut = CreateSut(CreateSettings());

        var result = sut.CreateToken(CreateUser(), UserRoles.User);

        result.Token.ShouldNotBeNullOrWhiteSpace();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        jwt.Issuer.ShouldBe(Issuer);
        jwt.Audiences.ShouldHaveSingleItem().ShouldBe(Audience);
        jwt.Header.Alg.ShouldBe(SecurityAlgorithms.HmacSha256);
    }

    [Fact]
    public void CreateToken_SetsAccessTokenExpiryFromSettings()
    {
        var sut = CreateSut(CreateSettings(expiryInMinutes: 30));

        var before = DateTime.UtcNow.AddMinutes(30);
        var result = sut.CreateToken(CreateUser(), UserRoles.User);
        var after = DateTime.UtcNow.AddMinutes(30);

        result.ExpiresAt.ShouldBeInRange(before.AddSeconds(-5), after.AddSeconds(5));
    }

    [Fact]
    public void CreateToken_SetsRefreshTokenExpiryFromSettings()
    {
        var sut = CreateSut(CreateSettings(refreshTokenExpiryInDays: 14));

        var before = DateTime.UtcNow.AddDays(14);
        var result = sut.CreateToken(CreateUser(), UserRoles.User);
        var after = DateTime.UtcNow.AddDays(14);

        result.RefreshTokenExpiresAt.ShouldBeInRange(before.AddSeconds(-5), after.AddSeconds(5));
    }

    [Fact]
    public void CreateToken_GeneratesRefreshTokenOfSixtyFourRandomBytes()
    {
        var sut = CreateSut(CreateSettings());

        var result = sut.CreateToken(CreateUser(), UserRoles.User);

        Convert.FromBase64String(result.RefreshToken).Length.ShouldBe(64);
    }

    [Fact]
    public void CreateToken_ProducesDifferentRefreshTokensAndJtiPerCall()
    {
        var sut = CreateSut(CreateSettings());
        var handler = new JwtSecurityTokenHandler();
        var user = CreateUser();

        var first = sut.CreateToken(user, UserRoles.User);
        var second = sut.CreateToken(user, UserRoles.User);

        first.RefreshToken.ShouldNotBe(second.RefreshToken);
        handler.ReadJwtToken(first.Token).Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value
            .ShouldNotBe(handler.ReadJwtToken(second.Token).Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value);
    }

    // --- GetPrincipalFromExpiredToken ---

    [Fact]
    public void GetPrincipalFromExpiredToken_ExpiredToken_ReturnsMappedClaimsUsedByAuthService()
    {
        var sut = CreateSut(CreateSettings(expiryInMinutes: -1));
        var user = CreateUser();

        var token = sut.CreateToken(user, UserRoles.User).Token;
        var principal = sut.GetPrincipalFromExpiredToken(token);

        principal.ShouldNotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.ShouldBe(user.Id);
        principal.FindFirst(ClaimTypes.Email)?.Value.ShouldBe(user.Email);
        principal.FindFirst(ClaimTypes.Name)?.Value.ShouldBe(user.UserName);
        principal.FindFirst(ClaimTypes.Role)?.Value.ShouldBe(UserRoles.User);
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenExpiredWithinMaxAgeWindow_ReturnsPrincipal()
    {
        var sut = CreateSut(CreateSettings(expiryInMinutes: -60, maxExpiredAccessTokenAgeInDays: 7));

        var token = sut.CreateToken(CreateUser(), UserRoles.User).Token;

        sut.GetPrincipalFromExpiredToken(token).ShouldNotBeNull();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenExpiredBeyondMaxAgeWindow_ReturnsNull()
    {
        const int maxAgeDays = 7;
        const int expiredMinutes = -(maxAgeDays * 24 * 60) - 120; // 7 days + 2 hours ago
        var sut = CreateSut(CreateSettings(expiryInMinutes: expiredMinutes, maxExpiredAccessTokenAgeInDays: maxAgeDays));

        var token = sut.CreateToken(CreateUser(), UserRoles.User).Token;

        sut.GetPrincipalFromExpiredToken(token).ShouldBeNull();
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenSignedWithDifferentKey_Throws()
    {
        var token = CreateSut(CreateSettings()).CreateToken(CreateUser(), UserRoles.User).Token;
        var sut = CreateSut(CreateSettings(secretKey: "a-completely-different-secret-key-of-32+-chars"));

        Should.Throw<SecurityTokenException>(() => sut.GetPrincipalFromExpiredToken(token));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenFromDifferentIssuer_Throws()
    {
        var token = CreateSut(CreateSettings()).CreateToken(CreateUser(), UserRoles.User).Token;
        var sut = CreateSut(CreateSettings(issuer: "some-other-issuer"));

        Should.Throw<SecurityTokenException>(() => sut.GetPrincipalFromExpiredToken(token));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenForDifferentAudience_Throws()
    {
        var token = CreateSut(CreateSettings()).CreateToken(CreateUser(), UserRoles.User).Token;
        var sut = CreateSut(CreateSettings(audience: "some-other-audience"));

        Should.Throw<SecurityTokenException>(() => sut.GetPrincipalFromExpiredToken(token));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_TokenSignedWithUnsupportedAlgorithm_Throws()
    {
        var settings = CreateSettings(secretKey: new string('a', 64));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);
        var forged = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: [new Claim(ClaimTypes.NameIdentifier, "user-1")],
            expires: DateTime.UtcNow.AddMinutes(-1),
            signingCredentials: credentials);
        var token = new JwtSecurityTokenHandler().WriteToken(forged);
        var sut = CreateSut(settings);

        Should.Throw<SecurityTokenException>(() => sut.GetPrincipalFromExpiredToken(token));
    }

    [Fact]
    public void GetPrincipalFromExpiredToken_MalformedToken_Throws()
    {
        var sut = CreateSut(CreateSettings());

        Should.Throw<SecurityTokenMalformedException>(() => sut.GetPrincipalFromExpiredToken("garbage-token"));
    }
}
