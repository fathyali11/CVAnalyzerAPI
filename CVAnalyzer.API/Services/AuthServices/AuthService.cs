using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.EmailServices;
using CVAnalyzerAPI.Services.TokenServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OneOf;
using System.Runtime.Intrinsics.Arm;
using System.Security.Claims;
using System.Text;

namespace CVAnalyzerAPI.Services.AuthServices;

public class AuthService(
    UserManager<ApplicationUser> _userManager,
    ILogger<AuthService> _logger,
    ITokenService _tokenService,
    IEmailService _emailService,
    ApplicationDbContext _context,
    IHttpContextAccessor _httpContextAccessor,
    IWebHostEnvironment _webHostEnvironment,
    IOptions<FrontendSettings> _frontendSettings) : IAuthService
{
    public async Task<OneOf<AuthResponse,Error>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
       if(await _userManager.Users
            .AnyAsync(u => u.Email == request.Email || u.UserName == request.Name, cancellationToken))
        {
            _logger.LogWarning("User with email {Email} or username {Name} already exists", request.Email, request.Name);
            return new Error(ErrorCodes.Conflict, "User with the same email or username already exists");
        }

        var user = new ApplicationUser
        {
            UserName = request.Name,
            Email = request.Email,
            EmailConfirmed = true,
            LockoutEnabled = true,
        };


        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var creationResult = await _userManager.CreateAsync(user, request.Password);
            if(!creationResult.Succeeded)
            {
                _logger.LogError("Failed to create user: {Errors}", creationResult.Errors);
                return new Error(ErrorCodes.BadRequest, string.Join("; ", creationResult.Errors.Select(e => e.Description)));
            }

            _logger.LogInformation("User {Email} registered successfully", request.Email);

            var setInRoleResult = await _userManager.AddToRoleAsync(user, UserRoles.User);
            if(!setInRoleResult.Succeeded)
            {
                _logger.LogError("Failed to set role for user {Email}: {Errors}", request.Email, setInRoleResult.Errors);
                return new Error(ErrorCodes.BadRequest, string.Join("; ", setInRoleResult.Errors.Select(e => e.Description)));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Registration conflict for email {Email} or username {Name}", request.Email, request.Name);
            return new Error(ErrorCodes.Conflict, "User with the same email or username already exists");
        }

        _logger.LogInformation("Role {Role} assigned to user {Email} successfully", UserRoles.User, request.Email);
        
        var tokenCreationResult=_tokenService.CreateToken(user, UserRoles.User);
        user.RefreshTokens.Add(new RefreshToken
        {
            Token = RefreshTokenHasher.Hash(tokenCreationResult.RefreshToken),
            ExpiresAt = tokenCreationResult.RefreshTokenExpiresAt
        });
        await _context.SaveChangesAsync(cancellationToken);


        return new AuthResponse
        {
            Name = user.UserName!,
            Email = user.Email!,
            Role = UserRoles.User,
            Token = tokenCreationResult.Token,
            Expiration = tokenCreationResult.ExpiresAt,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt
        };

    }

    public async Task<OneOf<AuthResponse, Error>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if(user is null)
        {
            _logger.LogWarning("Login attempt for unknown email {Email}", request.Email);
            return new Error(ErrorCodes.UnAuthorized, "Invalid credentials");
        }

        if(await _userManager.IsLockedOutAsync(user))
        {
            _logger.LogWarning("Login blocked: account {UserId} is locked out", user.Id);
            return new Error(ErrorCodes.UnAuthorized, "Invalid credentials");
        }

        var hasValidCreds= await _userManager.CheckPasswordAsync(user, request.Password);
        if(!hasValidCreds)
        {
            _logger.LogWarning("Invalid credentials for user with email {Email}", request.Email);
            var accessFailedResult = await _userManager.AccessFailedAsync(user);
            if(accessFailedResult is { Succeeded: false })
            {
                _logger.LogError("Failed to record failed access attempt for user {UserId}", user.Id);
            }
            return new Error(ErrorCodes.UnAuthorized, "Invalid credentials");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        _logger.LogInformation("User {Email} logged in successfully", request.Email);
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles?.FirstOrDefault();
        if(role is null)
        {
            _logger.LogWarning("No roles found for user with email {Email}", request.Email);
            return new Error(ErrorCodes.NotFound, "User has no roles assigned");
        }

        var tokenCreationResult = _tokenService.CreateToken(user, role);
        if(tokenCreationResult is null)
        {
            _logger.LogError("Failed to create token for user with email {Email}", request.Email);
            return new Error(ErrorCodes.BadRequest, "Failed to create token");
        }
        user.RefreshTokens.Add(new RefreshToken
        {
            Token = RefreshTokenHasher.Hash(tokenCreationResult.RefreshToken),
            ExpiresAt = tokenCreationResult.RefreshTokenExpiresAt
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Token created successfully for user with email {Email}", request.Email);
        return new AuthResponse
        {
            Name = user.UserName!,
            Email = user.Email!,
            Role = role,
            Token = tokenCreationResult.Token,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt,
            Expiration = tokenCreationResult.ExpiresAt
        };
    }
    
    public async Task<OneOf<AuthResponse, Error>> RefreshTokenAsync(string token, string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(refreshToken))
        {
            _logger.LogWarning("Refresh failed: access token or refresh token is missing or empty.");
            return new Error(ErrorCodes.UnAuthorized, "Access Token and Refresh Token are required.");
        }

        ClaimsPrincipal? principal;
        try
        {
            principal = _tokenService.GetPrincipalFromExpiredToken(token);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Malformed or invalid access token presented for refresh");
            return new Error(ErrorCodes.BadRequest, "Invalid token");
        }

        if (principal is null)
        {
            _logger.LogWarning("Invalid token provided for refresh");
            return new Error(ErrorCodes.BadRequest, "Invalid token");
        }
        var email = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
        if (email is null)
        {
            _logger.LogWarning("Email claim not found in token for refresh");
            return new Error(ErrorCodes.BadRequest, "Invalid token claims");
        }
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            _logger.LogWarning("User with email {Email} not found for token refresh", email);
            return new Error(ErrorCodes.NotFound, "User not found");
        }
        var refreshTokenFromDb= await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == RefreshTokenHasher.Hash(refreshToken) && rt.UserId == user.Id, cancellationToken);
        if (refreshTokenFromDb is null)
        {
            _logger.LogWarning("Refresh token not found in database for user with email {Email}", email);
            return new Error(ErrorCodes.NotFound, "Refresh token not found");
        }
        if (!refreshTokenFromDb.IsActive)
        {
            _logger.LogWarning("Refresh token is not active for user with email {Email}", email);
            await _context.RefreshTokens.Where(x => x.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
            return new Error(ErrorCodes.UnAuthorized, "Refresh token is not active");
        }

        var revokedCount = await _context.RefreshTokens
            .Where(rt => rt.Id == refreshTokenFromDb.Id && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(rt => rt.RevokedAt, DateTime.UtcNow),
                cancellationToken);
        if (revokedCount == 0)
        {
            _logger.LogWarning("Refresh token was revoked or expired concurrently for user {UserId}", user.Id);
            await _context.RefreshTokens.Where(x => x.UserId == user.Id)
                .ExecuteDeleteAsync(cancellationToken);
            return new Error(ErrorCodes.UnAuthorized, "Refresh token is not active");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles?.FirstOrDefault();
        if (role is null)
        {
            _logger.LogError("No roles found for user {UserId} during token refresh", user.Id);
            return new Error(ErrorCodes.UnAuthorized, "User has no roles assigned");
        }

        var tokenCreationResult = _tokenService.CreateToken(user, role);
        user.RefreshTokens.Add(new RefreshToken
        {
            Token = RefreshTokenHasher.Hash(tokenCreationResult.RefreshToken),
            ExpiresAt = tokenCreationResult.RefreshTokenExpiresAt
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new AuthResponse
        {
            Name = user.UserName!,
            Email = user.Email!,
            Role = role,
            Token = tokenCreationResult.Token,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt, 
            Expiration = tokenCreationResult.ExpiresAt
        };
    }

    public async Task<Error> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("User with email {Email} not found for forgot password request", request.Email);
            return GenericForgotPasswordResponse();
        }

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var resetLink = QueryHelpers.AddQueryString(
                $"{_frontendSettings.Value.BaseUrl.TrimEnd('/')}/auth/reset-password",
                new Dictionary<string, string?>
                {
                    ["email"] = request.Email,
                    ["token"] = encodedToken,
                });

            var templatePath = Path.Combine(_webHostEnvironment.ContentRootPath, "Templates", "ResetPassword.html");
            var htmlTemplate = await File.ReadAllTextAsync(templatePath, cancellationToken);
            var body= htmlTemplate.
                Replace("{{UserName}}", user.UserName ?? string.Empty).
                Replace("{{ResetLink}}", resetLink).
                Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());
            await _emailService.SendEmailAsync(user.Email!, "Reset Password", body, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to user {UserId}", user.Id);
        }

        return GenericForgotPasswordResponse();
    }

    public async Task<Error> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("User with email {Email} not found for reset password request", request.Email);
            return new Error(ErrorCodes.BadRequest, "Invalid request");
        }

        string decodedToken;
        try
        {
            decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            _logger.LogWarning(ex, "Malformed reset token for user with email {Email}", request.Email);
            return new Error(ErrorCodes.BadRequest, "Invalid request");
        }

        var resetResult = await _userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            _logger.LogError("Failed to reset password for user with email {Email}: {Errors}", request.Email, resetResult.Errors);
            return new Error(ErrorCodes.BadRequest, string.Join("; ", resetResult.Errors.Select(e => e.Description)));
        }
        
        await _context.RefreshTokens
            .Where(rt => rt.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Password reset and all sessions revoked for user {UserId}", user.Id);

        return new Error(ErrorCodes.None, "Password reset successfully");
    }

    public async Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            _logger.LogWarning("HttpContext is null when trying to get current user ID");
            return null;
        }
        var userIdClaim = httpContext.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier);
        if (userIdClaim is null)
        {
            _logger.LogWarning("NameIdentifier claim not found in HttpContext when trying to get current user ID");
            return null;
        }
        return userIdClaim.Value;
    }

    private static Error GenericForgotPasswordResponse() =>
        new(ErrorCodes.None, "If the email exists, a reset link will be sent.");
}
