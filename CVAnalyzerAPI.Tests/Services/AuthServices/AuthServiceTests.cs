using System.Security.Claims;
using System.Text;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.EmailServices;
using CVAnalyzerAPI.Services.TokenServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Services.AuthServices;

public class AuthServiceTests : IDisposable
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly SqliteConnection _database;
    private readonly ApplicationDbContext _context;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            Substitute.For<IUserStore<ApplicationUser>>(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
        _tokenService = Substitute.For<ITokenService>();
        _emailService = Substitute.For<IEmailService>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

        // SQLite in-memory keeps the database alive only while the connection is open,
        // and unlike the EF InMemory provider it supports relational features used by
        // the service (e.g. ExecuteDeleteAsync).
        _database = new SqliteConnection("DataSource=:memory:");
        _database.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_database)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        // AnyAsync requires an IAsyncQueryProvider, so back the substitute
        // with the real (InMemory) Users set instead of a LINQ-to-objects queryable.
        _userManager.Users.Returns(_context.Users);

        _sut = new AuthService(
            _userManager,
            Substitute.For<ILogger<AuthService>>(),
            _tokenService,
            _emailService,
            _context,
            _httpContextAccessor);
    }

    public void Dispose()
    {
        _context.Dispose();
        _database.Dispose();
    }

    // --- RegisterAsync ---

    [Fact]
    public async Task RegisterAsync_ExistingEmailOrUsername_ReturnsConflictError()
    {
        await SeedUserAsync(email: "taken@example.com", userName: "taken");

        var result = await _sut.RegisterAsync(new RegisterRequest("john", "taken@example.com", "Str0ng@Pass"));

        ShouldBeError(result, ErrorCodes.Conflict, "User with the same email or username already exists");
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RegisterAsync_WhenUserCreationFails_ReturnsBadRequestError()
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Password is too weak" }));

        var result = await _sut.RegisterAsync(new RegisterRequest("john", "john@example.com", "weak"));

        ShouldBeError(result, ErrorCodes.BadRequest, "Password is too weak");
        await _userManager.DidNotReceive().AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        _tokenService.DidNotReceive().CreateToken(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RegisterAsync_WhenRoleAssignmentFails_ReturnsBadRequestError()
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Role store unavailable" }));

        var result = await _sut.RegisterAsync(new RegisterRequest("john", "john@example.com", "Str0ng@Pass"));

        ShouldBeError(result, ErrorCodes.BadRequest, "Role store unavailable");
        _tokenService.DidNotReceive().CreateToken(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_ReturnsAuthResponseWithTokens()
    {
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var tokenResult = new TokenCreationResult
        {
            Token = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = "refresh-token",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        _tokenService.CreateToken(Arg.Any<ApplicationUser>(), UserRoles.User).Returns(tokenResult);

        var result = await _sut.RegisterAsync(new RegisterRequest("john", "john@example.com", "Str0ng@Pass"));

        var response = ShouldBeSuccess(result);
        response.Name.ShouldBe("john");
        response.Email.ShouldBe("john@example.com");
        response.Role.ShouldBe(UserRoles.User);
        response.Token.ShouldBe("access-token");
        response.Expiration.ShouldBe(tokenResult.ExpiresAt);
        response.RefreshToken.ShouldBe("refresh-token");
        response.RefreshTokenExpiration.ShouldBe(tokenResult.RefreshTokenExpiresAt);

        await _userManager.Received(1).AddToRoleAsync(
            Arg.Is<ApplicationUser>(u => u.UserName == "john" && u.Email == "john@example.com"),
            UserRoles.User);
    }

    // --- LoginAsync ---

    [Fact]
    public async Task LoginAsync_UnknownEmail_ReturnsNotFoundError()
    {
        _userManager.FindByEmailAsync("ghost@example.com").Returns((ApplicationUser?)null);

        var result = await _sut.LoginAsync(new LoginRequest("ghost@example.com", "Str0ng@Pass"));

        ShouldBeError(result, ErrorCodes.NotFound, "User not found");
        await _userManager.DidNotReceive().CheckPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
        _tokenService.DidNotReceive().CreateToken(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsUnauthorizedError()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, "wrong-password").Returns(false);

        var result = await _sut.LoginAsync(new LoginRequest("john@example.com", "wrong-password"));

        ShouldBeError(result, ErrorCodes.UnAuthorized, "Invalid credentials");
        _tokenService.DidNotReceive().CreateToken(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_UserWithoutRoles_ReturnsNotFoundError()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, "Str0ng@Pass").Returns(true);
        _userManager.GetRolesAsync(user).Returns((IList<string>)null!);

        var result = await _sut.LoginAsync(new LoginRequest("john@example.com", "Str0ng@Pass"));

        ShouldBeError(result, ErrorCodes.NotFound, "User has no roles assigned");
        _tokenService.DidNotReceive().CreateToken(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_WhenTokenCreationFails_ReturnsBadRequestError()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, "Str0ng@Pass").Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string> { UserRoles.User });
        _tokenService.CreateToken(user, UserRoles.User).Returns((TokenCreationResult)null!);

        var result = await _sut.LoginAsync(new LoginRequest("john@example.com", "Str0ng@Pass"));

        ShouldBeError(result, ErrorCodes.BadRequest, "Failed to create token");
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResponseWithTokens()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.CheckPasswordAsync(user, "Str0ng@Pass").Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string> { UserRoles.User });

        var tokenResult = new TokenCreationResult
        {
            Token = "access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = "refresh-token",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        _tokenService.CreateToken(user, UserRoles.User).Returns(tokenResult);

        var result = await _sut.LoginAsync(new LoginRequest("john@example.com", "Str0ng@Pass"));

        var response = ShouldBeSuccess(result);
        response.Name.ShouldBe("john");
        response.Email.ShouldBe("john@example.com");
        response.Role.ShouldBe(UserRoles.User);
        response.Token.ShouldBe("access-token");
        response.RefreshToken.ShouldBe("refresh-token");
        user.RefreshTokens.ShouldHaveSingleItem()
            .Token.ShouldBe("refresh-token");
    }

    // --- RefreshTokenAsync ---

    [Theory]
    [InlineData("", "refresh-token")]
    [InlineData("access-token", "")]
    [InlineData("", "")]
    public async Task RefreshTokenAsync_MissingTokens_ReturnsUnauthorizedError(string token, string refreshToken)
    {
        var result = await _sut.RefreshTokenAsync(token, refreshToken);

        ShouldBeError(result, ErrorCodes.UnAuthorized, "Access Token and Refresh Token are required.");
    }

    [Fact]
    public async Task RefreshTokenAsync_InvalidAccessToken_ReturnsBadRequestError()
    {
        _tokenService.GetPrincipalFromExpiredToken("bad-token").Returns((ClaimsPrincipal?)null);

        var result = await _sut.RefreshTokenAsync("bad-token", "refresh-token");

        ShouldBeError(result, ErrorCodes.BadRequest, "Invalid token");
    }

    [Fact]
    public async Task RefreshTokenAsync_AccessTokenWithoutEmailClaim_ReturnsBadRequestError()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "john") }));
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);

        var result = await _sut.RefreshTokenAsync("access-token", "refresh-token");

        ShouldBeError(result, ErrorCodes.BadRequest, "Invalid token claims");
    }

    [Fact]
    public async Task RefreshTokenAsync_UnknownUser_ReturnsNotFoundError()
    {
        var principal = PrincipalWithEmail("ghost@example.com");
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);
        _userManager.FindByEmailAsync("ghost@example.com").Returns((ApplicationUser?)null);

        var result = await _sut.RefreshTokenAsync("access-token", "refresh-token");

        ShouldBeError(result, ErrorCodes.NotFound, "User not found");
    }

    [Fact]
    public async Task RefreshTokenAsync_UnknownRefreshToken_ReturnsNotFoundError()
    {
        var user = await SeedUserAsync();
        var principal = PrincipalWithEmail(user.Email!);
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);
        _userManager.FindByEmailAsync(user.Email!).Returns(user);

        var result = await _sut.RefreshTokenAsync("access-token", "unknown-refresh-token");

        ShouldBeError(result, ErrorCodes.NotFound, "Refresh token not found");
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedRefreshToken_ReturnsUnauthorizedErrorAndDeletesUserTokens()
    {
        var user = await SeedUserAsync();
        await SeedRefreshTokenAsync(user, "revoked-refresh-token", revokedAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedRefreshTokenAsync(user, "another-refresh-token");

        var principal = PrincipalWithEmail(user.Email!);
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);
        _userManager.FindByEmailAsync(user.Email!).Returns(user);

        var result = await _sut.RefreshTokenAsync("access-token", "revoked-refresh-token");

        ShouldBeError(result, ErrorCodes.UnAuthorized, "Refresh token is not active");
        _context.RefreshTokens.Count(rt => rt.UserId == user.Id).ShouldBe(0);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredRefreshToken_ReturnsUnauthorizedErrorAndDeletesUserTokens()
    {
        var user = await SeedUserAsync();
        await SeedRefreshTokenAsync(user, "expired-refresh-token", expiresAt: DateTime.UtcNow.AddMinutes(-1));
        await SeedRefreshTokenAsync(user, "another-refresh-token");

        var principal = PrincipalWithEmail(user.Email!);
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);
        _userManager.FindByEmailAsync(user.Email!).Returns(user);

        var result = await _sut.RefreshTokenAsync("access-token", "expired-refresh-token");

        ShouldBeError(result, ErrorCodes.UnAuthorized, "Refresh token is not active");
        _context.RefreshTokens.Count(rt => rt.UserId == user.Id).ShouldBe(0);
    }

    [Fact]
    public async Task RefreshTokenAsync_ActiveRefreshToken_ReturnsAuthResponseAndRotatesTokens()
    {
        var user = await SeedUserAsync();
        await SeedRefreshTokenAsync(user, "active-refresh-token");

        var principal = PrincipalWithEmail(user.Email!);
        _tokenService.GetPrincipalFromExpiredToken("access-token").Returns(principal);
        _userManager.FindByEmailAsync(user.Email!).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new List<string> { UserRoles.User });
        _tokenService.CreateToken(user, UserRoles.User).Returns(new TokenCreationResult
        {
            Token = "new-access-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RefreshToken = "new-refresh-token",
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
        });

        var result = await _sut.RefreshTokenAsync("access-token", "active-refresh-token");

        var response = ShouldBeSuccess(result);
        response.Token.ShouldBe("new-access-token");
        response.RefreshToken.ShouldBe("new-refresh-token");
        response.Role.ShouldBe(UserRoles.User);

        var oldToken = await _context.RefreshTokens.AsNoTracking()
            .SingleAsync(rt => rt.Token == "active-refresh-token");
        oldToken.RevokedAt.ShouldNotBeNull();

        var newToken = await _context.RefreshTokens.AsNoTracking()
            .SingleAsync(rt => rt.Token == "new-refresh-token");
        newToken.UserId.ShouldBe(user.Id);
        newToken.IsActive.ShouldBeTrue();
    }

    // --- ForgotPasswordAsync ---

    [Fact]
    public async Task ForgotPasswordAsync_UnknownEmail_ReturnsTrueWithoutSendingEmail()
    {
        _userManager.FindByEmailAsync("ghost@example.com").Returns((ApplicationUser?)null);

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("ghost@example.com"));

        ShouldBeTrueWithoutError(result);
        await _emailService.DidNotReceiveWithAnyArgs()
            .SendEmailAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task ForgotPasswordAsync_KnownEmail_SendsResetEmailAndReturnsTrue()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.GeneratePasswordResetTokenAsync(user).Returns("reset-token");

        var result = await _sut.ForgotPasswordAsync(new ForgotPasswordRequest("john@example.com"));

        ShouldBeTrueWithoutError(result);

        var expectedEncodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes("reset-token"));
        await _emailService.Received(1).SendEmailAsync(
            "john@example.com",
            "Reset Password",
            Arg.Is<string>(body =>
                body.Contains($"http://localhost:4200/auth/reset-password?email={user.Email}&token={expectedEncodedToken}")
                && body.Contains("john")
                && !body.Contains("{{")),
            Arg.Any<CancellationToken>());
    }

    // --- ResetPasswordAsync ---

    [Fact]
    public async Task ResetPasswordAsync_UnknownEmail_ReturnsBadRequestError()
    {
        _userManager.FindByEmailAsync("ghost@example.com").Returns((ApplicationUser?)null);

        var result = await _sut.ResetPasswordAsync(
            new ResetPasswordRequest("ghost@example.com", EncodeToken("reset-token"), "NewStr0ng@Pass"));

        ShouldBeError(result, ErrorCodes.BadRequest, "Invalid request");
        await _userManager.DidNotReceiveWithAnyArgs()
            .ResetPasswordAsync(default!, default!, default!);
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenIdentityResetFails_ReturnsBadRequestError()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.ResetPasswordAsync(user, "reset-token", "NewStr0ng@Pass")
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Token is invalid" }));

        var result = await _sut.ResetPasswordAsync(
            new ResetPasswordRequest("john@example.com", EncodeToken("reset-token"), "NewStr0ng@Pass"));

        ShouldBeError(result, ErrorCodes.BadRequest, "Token is invalid");
    }

    [Fact]
    public async Task ResetPasswordAsync_ValidRequest_ReturnsTrue()
    {
        var user = new ApplicationUser { Email = "john@example.com", UserName = "john" };
        _userManager.FindByEmailAsync(user.Email).Returns(user);
        _userManager.ResetPasswordAsync(user, "reset-token", "NewStr0ng@Pass")
            .Returns(IdentityResult.Success);

        var result = await _sut.ResetPasswordAsync(
            new ResetPasswordRequest("john@example.com", EncodeToken("reset-token"), "NewStr0ng@Pass"));

        ShouldBeTrueWithoutError(result);
        await _userManager.Received(1).ResetPasswordAsync(user, "reset-token", "NewStr0ng@Pass");
    }

    // --- GetCurrentUserIdAsync ---

    [Fact]
    public async Task GetCurrentUserIdAsync_WithoutHttpContext_ReturnsNull()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var userId = await _sut.GetCurrentUserIdAsync();

        userId.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_WithoutNameIdentifierClaim_ReturnsNull()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "john") })),
        });

        var userId = await _sut.GetCurrentUserIdAsync();

        userId.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentUserIdAsync_WithUserIdClaim_ReturnsClaimValue()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "user-42") })),
        });

        var userId = await _sut.GetCurrentUserIdAsync();

        userId.ShouldBe("user-42");
    }

    // --- Helpers ---

    private async Task<ApplicationUser> SeedUserAsync(
        string id = "user-1", string email = "john@example.com", string userName = "john")
    {
        var user = new ApplicationUser { Id = id, Email = email, UserName = userName };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<RefreshToken> SeedRefreshTokenAsync(
        ApplicationUser user,
        string token,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null)
    {
        var refreshToken = new RefreshToken
        {
            Token = token,
            UserId = user.Id,
            User = user,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            RevokedAt = revokedAt,
        };
        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();
        return refreshToken;
    }

    private static ClaimsPrincipal PrincipalWithEmail(string email) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }));

    private static string EncodeToken(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static AuthResponse ShouldBeSuccess(OneOf<AuthResponse, Error> result)
    {
        result.TryPickT0(out var response, out _).ShouldBeTrue(DescribeFailure(result));
        return response!;
    }

    private static Error ShouldBeError(OneOf<AuthResponse, Error> result, string code, string message)
    {
        result.TryPickT1(out var error, out _).ShouldBeTrue("expected an error result but got a success result");
        error!.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        return error;
    }

    private static Error ShouldBeError(OneOf<bool, Error> result, string code, string message)
    {
        result.TryPickT1(out var error, out _).ShouldBeTrue("expected an error result but got a success result");
        error!.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        return error;
    }

    private static void ShouldBeTrueWithoutError(OneOf<bool, Error> result)
    {
        var failureMessage = result.TryPickT1(out var failure, out _)
            ? $"expected true but got error {failure.Code}: {failure.Message}"
            : "expected a boolean result";
        result.TryPickT0(out var value, out _).ShouldBeTrue(failureMessage);
        value.ShouldBeTrue();
    }

    private static string DescribeFailure(OneOf<AuthResponse, Error> result) =>
        result.TryPickT1(out var error, out _)
            ? $"expected success but got error {error.Code}: {error.Message}"
            : "expected an error result but got a success result";
}
