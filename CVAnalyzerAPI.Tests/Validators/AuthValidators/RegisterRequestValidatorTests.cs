using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Validators.AuthValidators;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Validators.AuthValidators;

public class RegisterRequestValidatorTests
{
    private const string ValidName = "John Doe";
    private const string ValidEmail = "user@example.com";
    private const string ValidPassword = "Str0ng@Pass";

    private readonly RegisterRequestValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_ShouldSucceed()
    {
        var result = _validator.Validate(new RegisterRequest(ValidName, ValidEmail, ValidPassword));

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyName_ShouldFailWithRequired(string name)
    {
        var result = _validator.Validate(new RegisterRequest(name, ValidEmail, ValidPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe([(nameof(RegisterRequest.Name), ValidationCodes.Required)]);
    }

    [Fact]
    public void Validate_NameExceeding100Chars_ShouldFailWithTooLong()
    {
        var name = new string('a', 101);

        var result = _validator.Validate(new RegisterRequest(name, ValidEmail, ValidPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe([(nameof(RegisterRequest.Name), ValidationCodes.TooLong)]);
    }

    [Fact]
    public void Validate_NameWithExactly100Chars_ShouldSucceed()
    {
        var name = new string('a', 100);

        var result = _validator.Validate(new RegisterRequest(name, ValidEmail, ValidPassword));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.InvalidFormat })]
    [InlineData("not-an-email", new[] { ValidationCodes.InvalidFormat })]
    public void Validate_InvalidEmail_ShouldFail(string email, string[] expectedCodes)
    {
        var result = _validator.Validate(new RegisterRequest(ValidName, email, ValidPassword));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(RegisterRequest.Email), expectedCodes), ignoreOrder: true);
    }

    [Theory]
    [InlineData("", new[] { ValidationCodes.Required, ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("Ab1@xy", new[] { ValidationCodes.TooShort, ValidationCodes.TooWeak })]
    [InlineData("alllowercase", new[] { ValidationCodes.TooWeak })]
    public void Validate_InvalidPassword_ShouldFail(string password, string[] expectedCodes)
    {
        var result = _validator.Validate(new RegisterRequest(ValidName, ValidEmail, password));

        result.IsValid.ShouldBeFalse();
        result.Errors
            .Select(e => (e.PropertyName, e.ErrorCode))
            .ShouldBe(ErrorsFor(nameof(RegisterRequest.Password), expectedCodes), ignoreOrder: true);
    }

    [Fact]
    public void Validate_EmptyNameEmailAndPassword_ShouldReportAllErrors()
    {
        var result = _validator.Validate(new RegisterRequest(string.Empty, string.Empty, string.Empty));

        result.Errors.Count.ShouldBe(6);
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Name));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Email));
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RegisterRequest.Password));
    }

    private static IEnumerable<(string PropertyName, string ErrorCode)> ErrorsFor(
        string propertyName, IEnumerable<string> codes) =>
        codes.Select(code => (propertyName, code));
}
