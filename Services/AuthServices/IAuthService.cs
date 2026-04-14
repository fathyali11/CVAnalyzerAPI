using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using OneOf;

namespace CVAnalyzerAPI.Services.AuthServices;

public interface IAuthService
{
    Task<OneOf<AuthResponse, Error>> RegisterAsync(RegisterRequest request,CancellationToken cancellationToken=default);
    Task<OneOf<AuthResponse, Error>> LoginAsync(LoginRequest request,CancellationToken cancellationToken=default);
    Task<OneOf<AuthResponse, Error>> RefreshTokenAsync(string token, string refreshToken, CancellationToken cancellationToken = default);
    Task<OneOf<bool, Error>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<OneOf<bool, Error>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<string?> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);
}
