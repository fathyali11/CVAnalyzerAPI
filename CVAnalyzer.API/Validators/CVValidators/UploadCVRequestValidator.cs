using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Extensions;
using FluentValidation;

namespace CVAnalyzerAPI.Validators.CVValidators;

public class UploadCVRequestValidator:AbstractValidator<UploadCVRequest>
{
    public UploadCVRequestValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithErrorCode(ValidationCodes.Required)
            .Must(f => f.Length > 0).WithErrorCode(ValidationCodes.EmptyFile)
            .Must(f => f.Length <= FileConstraints.MaxUploadFileSizeBytes).WithErrorCode(ValidationCodes.FileTooLarge)
            .Must(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
                       f.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode(ValidationCodes.UnsupportedFileType)
            .Must(f => f.IsValidDocumentSignature()).WithErrorCode(ValidationCodes.InvalidFileFormat);
    }
}
