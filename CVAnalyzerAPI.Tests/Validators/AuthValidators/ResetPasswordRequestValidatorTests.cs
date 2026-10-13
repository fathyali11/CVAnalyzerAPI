using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Validators.AuthValidators;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Validators.AuthValidators;

public class ResetPasswordRequestValidatorTests
{
    private const string ValidEmail = "user@example.com";
    private const string ValidToken = "reset-token";
    private const string ValidNewPassword = "Str0ng@Pass";

    private readonly ResetPasswordRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ShouldSucceed()
    {
        var result = _validator.Validate(new ResetPasswordRequest(ValidEmail, ValidToken, ValidNewPassword));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.InvalidFormat })]
    [InlineData("not-an-email", new[] { ValidationCodes.InvalidFormat })]
    public void Validate_InvalidEmail_ShouldFail(string email, string[] expectedCodes)
    {
        var result = _validator.Validate(new ResetPasswordRequest(email, ValidToken, ValidNewPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(ResetPasswordRequest.Email), expectedCodes), ignoreOrder: true);
    }

    [Fact]
    public void Validate_EmptyToken_ShouldFailWithRequired()
    {
        var result = _validator.Validate(new ResetPasswordRequest(ValidEmail, string.Empty, ValidNewPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe([(nameof(ResetPasswordRequest.Token), ValidationCodes.Required)]);
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("Ab1@xy", new[] { ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("alllowercase", new[] { ValidationCodes.TooWeak })]
    public void Validate_InvalidNewPassword_ShouldFail(string newPassword, string[] expectedCodes)
    {
        var result = _validator.Validate(new ResetPasswordRequest(ValidEmail, ValidToken, newPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(ResetPasswordRequest.NewPassword), expectedCodes), ignoreOrder: true);
    }

    [Fact]
    public void Validate_EmptyEmailTokenAndPassword_ShouldReportAllErrors()
    {
        var result = _validator.Validate(new ResetPasswordRequest(string.Empty, string.Empty, string.Empty));

        result.Errors.Count.ShouldBe(6);
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ResetPasswordRequest.Email));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ResetPasswordRequest.Token));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ResetPasswordRequest.NewPassword));
    }

    private static IEnumerable<(string PropertyName, string ErrorCode)> ErrorsFor(
        string propertyName, IEnumerable<string> codes) =>
        codes.Select(code => (propertyName, code));
}
