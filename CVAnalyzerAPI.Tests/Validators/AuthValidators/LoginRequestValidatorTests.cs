using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Validators.AuthValidators;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Validators.AuthValidators;

public class LoginRequestValidatorTests
{
    private const string ValidEmail = "user@example.com";
    private const string ValidPassword = "Str0ng@Pass";

    private const string EmailRequired = "Email is required.";
    private const string EmailInvalidFormat = "Invalid email format.";
    private const string PasswordRequired = "Password is required.";
    private const string PasswordTooShort = "Password must be at least 8 characters long.";
    private const string PasswordTooWeak = "Password must contain at least one uppercase letter, one lowercase letter, one number and one special character.";

    private readonly LoginRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidCredentials_ShouldSucceed()
    {
        var result = _validator.Validate(new LoginRequest(ValidEmail, ValidPassword));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", new[] { EmailRequired, EmailInvalidFormat })]
    [InlineData("not-an-email", new[] { EmailInvalidFormat })]
    public void Validate_InvalidEmail_ShouldFail(string email, string[] expectedMessages)
    {
        var result = _validator.Validate(new LoginRequest(email, ValidPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorMessage))
            .ShouldBe(ErrorsFor(nameof(LoginRequest.Email), expectedMessages), ignoreOrder: true);
    }

    [Theory]
    [InlineData("", new[] { PasswordRequired, PasswordTooShort, PasswordTooWeak })]
    [InlineData("Ab1@xy", new[] { PasswordTooShort, PasswordTooWeak })]
    [InlineData("alllowercase", new[] { PasswordTooWeak })]
    public void Validate_InvalidPassword_ShouldFail(string password, string[] expectedMessages)
    {
        var result = _validator.Validate(new LoginRequest(ValidEmail, password));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorMessage))
            .ShouldBe(ErrorsFor(nameof(LoginRequest.Password), expectedMessages), ignoreOrder: true);
    }

    [Fact]
    public void Validate_EmptyEmailAndPassword_ShouldReportAllErrors()
    {
        var result = _validator.Validate(new LoginRequest(string.Empty, string.Empty));

        result.Errors.Count.ShouldBe(5);
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Email));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(LoginRequest.Password));
    }

    private static IEnumerable<(string PropertyName, string ErrorMessage)> ErrorsFor(
        string propertyName, IEnumerable<string> messages) =>
        messages.Select(message => (propertyName, message));
}
