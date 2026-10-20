
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Extensions;
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
            error => error.ToEnvelopeResult()

        );
    }
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _authService.LoginAsync(request, cancellationToken);
        return result.Match<IActionResult>(
            authResponse =>
            {
                PrepareRefreshTokenCookie(authResponse.RefreshToken, authResponse.RefreshTokenExpiration);
                return Ok(authResponse);
            },
            error => error.ToEnvelopeResult()
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
            error => error.ToEnvelopeResult()
        );
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("ForgotPassword")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ForgotPasswordAsync(request, cancellationToken);
        return result.ToEnvelopeResult();
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await _authService.ResetPasswordAsync(request, cancellationToken);
        return result.ToEnvelopeResult();
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


