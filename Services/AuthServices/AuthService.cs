using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.EmailServices;
using CVAnalyzerAPI.Services.TokenServices;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OneOf;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace CVAnalyzerAPI.Services.AuthServices;

public class AuthService(
    UserManager<ApplicationUser> _userManager,
    ILogger<AuthService> _logger,
    IValidator<RegisterRequest> _registerRequestValidator,
    IValidator<LoginRequest> _loginRequestValidator,
    IValidator<ForgotPasswordRequest> _forgotPasswordRequestValidator,
    IValidator<ResetPasswordRequest> _resetPasswordRequestValidator,
    ITokenService _tokenService,
    IEmailService _emailService,
    ApplicationDbContext _context) : IAuthService
{
    public async Task<OneOf<AuthResponse,Error>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Registering user with email: {Email}", request.Email);
        var validationResult = await _registerRequestValidator.ValidateAsync(request, cancellationToken);
        if(!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for registration request: {Errors}", validationResult.Errors);
            return new Error(ErrorCodes.BadRequest,string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

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
        };
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

        _logger.LogInformation("Role {Role} assigned to user {Email} successfully", UserRoles.User, request.Email);
        var roles = await _userManager.GetRolesAsync(user);
        var tokenCreationResult=_tokenService.CreateToken(user,roles.First());

        return new AuthResponse
        {
            Name = user.UserName,
            Email = user.Email,
            Role = roles.First(),
            Token = tokenCreationResult.Token,
            Expiration = tokenCreationResult.ExpiresAt,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt
        };

    }

    public async Task<OneOf<AuthResponse, Error>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Login user with email: {Email}", request.Email);
        var validationResult = await _loginRequestValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for login request: {Errors}", validationResult.Errors);
            return new Error(ErrorCodes.BadRequest, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }
        var user= await _userManager.FindByEmailAsync(request.Email);
        if(user is null)
        {
            _logger.LogWarning("User with email {Email} not found", request.Email);
            return new Error(ErrorCodes.NotFound, "User not found");
        }
        var hasValidCreds= await _userManager.CheckPasswordAsync(user, request.Password);
        if(!hasValidCreds)
        {
            _logger.LogWarning("Invalid credentials for user with email {Email}", request.Email);
            return new Error(ErrorCodes.UnAuthorized, "Invalid credentials");
        }
        _logger.LogInformation("User {Email} logged in successfully", request.Email);
        var roles = await _userManager.GetRolesAsync(user);
        if(roles is null)
        {
            _logger.LogWarning("No roles found for user with email {Email}", request.Email);
            return new Error(ErrorCodes.NotFound, "User has no roles assigned");
        }

        var tokenCreationResult = _tokenService.CreateToken(user, roles.First());
        if(tokenCreationResult is null)
        {
            _logger.LogError("Failed to create token for user with email {Email}", request.Email);
            return new Error(ErrorCodes.BadRequest, "Failed to create token");
        }

        _logger.LogInformation("Token created successfully for user with email {Email}", request.Email);
        return new AuthResponse
        {
            Name = user.UserName!,
            Email = user.Email!,
            Role = roles.First(),
            Token = tokenCreationResult.Token,
            Expiration = tokenCreationResult.ExpiresAt,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt
        };
    }
    
    public async Task<OneOf<AuthResponse, Error>> RefreshTokenAsync(string token, string refreshToken, CancellationToken cancellationToken = default)
    {
        var principal = _tokenService.GetPrincipalFromExpiredToken(token);
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
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && rt.UserId == user.Id, cancellationToken);
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
        refreshTokenFromDb.RevokedAt = DateTime.UtcNow;

        var roles = await _userManager.GetRolesAsync(user);
        var tokenCreationResult = _tokenService.CreateToken(user, roles.First());
        user.RefreshTokens.Add(new RefreshToken
        {
            Token = tokenCreationResult.RefreshToken,
            ExpiresAt = tokenCreationResult.RefreshTokenExpiresAt
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new AuthResponse
        {
            Name = user.UserName!,
            Email = user.Email!,
            Role = roles.First(),
            Token = tokenCreationResult.Token,
            Expiration = tokenCreationResult.ExpiresAt,
            RefreshToken = tokenCreationResult.RefreshToken,
            RefreshTokenExpiration = tokenCreationResult.RefreshTokenExpiresAt
        };
    }

    public async Task<OneOf<bool, Error>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _forgotPasswordRequestValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for forgot password request: {Errors}", validationResult.Errors);
            return new Error(ErrorCodes.BadRequest, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("User with email {Email} not found for forgot password request", request.Email);
            return true;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedToken=WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var resetLink = $"http://localhost:3000/reset-password?email={request.Email}&token={encodedToken}";
        var htmlTemplate = await File.ReadAllTextAsync("Templates/ResetPassword.html", cancellationToken);
        var body= htmlTemplate.
            Replace("{{UserName}}", user.UserName!).
            Replace("{{ResetLink}}", resetLink).
            Replace("{{CurrentYear}}", DateTime.UtcNow.Year.ToString());
        await _emailService.SendEmailAsync(user.Email!, "Reset Password", body, cancellationToken);

        return true;
    }

    public async Task<OneOf<bool, Error>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var validationResult = await _resetPasswordRequestValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed for reset password request: {Errors}", validationResult.Errors);
            return new Error(ErrorCodes.BadRequest, string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));
        }

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("User with email {Email} not found for reset password request", request.Email);
            return new Error(ErrorCodes.BadRequest, "Invalid request");
        }
        var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));

        var resetResult = await _userManager.ResetPasswordAsync(user, decodedToken, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            _logger.LogError("Failed to reset password for user with email {Email}: {Errors}", request.Email, resetResult.Errors);
            return new Error(ErrorCodes.BadRequest, string.Join("; ", resetResult.Errors.Select(e => e.Description)));
        }

        return true;
    }
}
