namespace CVAnalyzerAPI.DTOs.AuthsDTOs;

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
