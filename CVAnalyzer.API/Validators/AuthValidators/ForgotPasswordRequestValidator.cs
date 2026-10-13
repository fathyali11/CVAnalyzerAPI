using CVAnalyzerAPI.DTOs.AuthsDTOs;
using FluentValidation;

namespace CVAnalyzerAPI.Validators.AuthValidators;

public class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");
    }
}