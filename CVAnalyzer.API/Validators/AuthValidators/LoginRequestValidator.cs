using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using FluentValidation;

namespace CVAnalyzerAPI.Validators.AuthValidators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.InvalidFormat);

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MinimumLength(8).WithErrorCode(ValidationCodes.TooShort)
            .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$")
            .WithErrorCode(ValidationCodes.TooWeak);
    }
}
