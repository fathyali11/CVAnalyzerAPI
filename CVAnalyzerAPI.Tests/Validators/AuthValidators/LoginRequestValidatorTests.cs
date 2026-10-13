using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Validators.AuthValidators;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Validators.AuthValidators;

public class LoginRequestValidatorTests
{
    private const string ValidEmail = "user@example.com";
    private const string ValidPassword = "Str0ng@Pass";

    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidCredentials_ShouldSucceed()
    {
        var result = _validator.Validate(new LoginRequest(ValidEmail, ValidPassword));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.InvalidFormat })]
    [InlineData("not-an-email", new[] { ValidationCodes.InvalidFormat })]
    public void Validate_InvalidEmail_ShouldFail(string email, string[] expectedCodes)
    {
        var result = _validator.Validate(new LoginRequest(email, ValidPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(LoginRequest.Email), expectedCodes), ignoreOrder: true);
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("Ab1@xy", new[] { ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("alllowercase", new[] { ValidationCodes.TooWeak })]
    public void Validate_InvalidPassword_ShouldFail(string password, string[] expectedCodes)
    {
        var result = _validator.Validate(new LoginRequest(ValidEmail, password));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(LoginRequest.Password), expectedCodes), ignoreOrder: true);
    }

    [Fact]
    public void Validate_EmptyEmailAndPassword_ShouldReportAllErrors()
    {
        var result = _validator.Validate(new LoginRequest(string.Empty, string.Empty));

        result.Errors.Count.ShouldBe(5);
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Email));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Password));
    }

    private static IEnumerable<(string PropertyName, string ErrorCode)> ErrorsFor(
        string propertyName, IEnumerable<string> codes) =>
        codes.Select(code => (propertyName, code));
}
