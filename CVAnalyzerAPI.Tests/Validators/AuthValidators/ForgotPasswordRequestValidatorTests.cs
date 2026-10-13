using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Validators.AuthValidators;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Validators.AuthValidators;

public class ForgotPasswordRequestValidatorTests
{
    private const string ValidEmail = "user@example.com";

    private readonly ForgotPasswordRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidEmail_ShouldSucceed()
    {
        var result = _validator.Validate(new ForgotPasswordRequest(ValidEmail));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.InvalidFormat })]
    [InlineData("not-an-email", new[] { ValidationCodes.InvalidFormat })]
    public void Validate_InvalidEmail_ShouldFail(string email, string[] expectedCodes)
    {
        var result = _validator.Validate(new ForgotPasswordRequest(email));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(ForgotPasswordRequest.Email), expectedCodes), ignoreOrder: true);
    }

    private static IEnumerable<(string PropertyName, string ErrorCode)> ErrorsFor(
        string propertyName, IEnumerable<string> codes) =>
        codes.Select(code => (propertyName, code));
}
