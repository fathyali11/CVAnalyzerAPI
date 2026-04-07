using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.TokenServices;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace CVAnalyzerAPI.Services.AuthServices;

public class AuthService(
    UserManager<ApplicationUser> _userManager,
    ILogger<AuthService> _logger,
    IValidator<RegisterRequest> _registerRequestValidator,
    ITokenService _tokenService) : IAuthService
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
            Expiration = tokenCreationResult.ExpiresAt
        };

    }
}
