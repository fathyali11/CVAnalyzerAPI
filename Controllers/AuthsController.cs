
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Services.AuthServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CVAnalyzerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthsController(IAuthService _authService) : ControllerBase
{

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return result.Match<IActionResult>(
            authResponse =>
            {
                PrepareRefreshTokenCookie(authResponse.RefreshToken, authResponse.RefreshTokenExpiration);
                return Ok(authResponse);
            },
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                ErrorCodes.Conflict => Conflict(error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }

        );
    }
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken = default)
    {
        Request.Cookies.TryGetValue("refreshToken", out var refreshTokenFromCookie);
        var result = await _authService.LoginAsync(request, refreshTokenFromCookie!, cancellationToken);
        return result.Match<IActionResult>(
            authResponse =>
            {
                PrepareRefreshTokenCookie(authResponse.RefreshToken, authResponse.RefreshTokenExpiration);
                return Ok(authResponse);
            },
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                ErrorCodes.UnAuthorized => StatusCode(StatusCodes.Status401Unauthorized, error.Message),
                ErrorCodes.NotFound => NotFound(error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }
        );
    }
    [HttpPost("refresh-token")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        Request.Cookies.TryGetValue("refreshToken", out var refreshTokenFromCookie);
        var result = await _authService.RefreshTokenAsync(request.Token, refreshTokenFromCookie!, cancellationToken);
        return result.Match<IActionResult>(
            authResponse =>
            {
                PrepareRefreshTokenCookie(authResponse.RefreshToken, authResponse.RefreshTokenExpiration);
                return Ok(authResponse);
            },
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                ErrorCodes.UnAuthorized => StatusCode(StatusCodes.Status401Unauthorized, error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }
        );
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("ForgotPasswordPolicy")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ForgotPasswordAsync(request, cancellationToken);
        return result.Match(
            success => Ok(new { Message = "If that email exists, a password reset link has been sent." }),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }
        );
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return result.Match(
            success => Ok(new { Message = "Password has been successfully reset." }),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                ErrorCodes.UnAuthorized => StatusCode(StatusCodes.Status401Unauthorized, error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }
        );
    }

    private void PrepareRefreshTokenCookie(string refreshToken, DateTime expiresAt)
    {
        var cookiesOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.None,
            Expires = expiresAt
        };
        Response.Cookies.Append("refreshToken", refreshToken, cookiesOptions);
    }
}


