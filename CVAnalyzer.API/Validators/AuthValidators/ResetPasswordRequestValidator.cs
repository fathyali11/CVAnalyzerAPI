using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using FluentValidation;

namespace CVAnalyzerAPI.Validators.AuthValidators;

public class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode(ValidationCodes.Required)
            .EmailAddress().WithErrorCode(ValidationCodes.InvalidFormat);

        RuleFor(x => x.Token)
            .NotEmpty().WithErrorCode(ValidationCodes.Required);

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithErrorCode(ValidationCodes.Required)
            .MinimumLength(8).WithErrorCode(ValidationCodes.TooShort)
            .Matches(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$")
            .WithErrorCode(ValidationCodes.TooWeak);
    }
}
