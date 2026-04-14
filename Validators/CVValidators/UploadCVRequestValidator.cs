using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Extensions;
using FluentValidation;

namespace CVAnalyzerAPI.Validators.CVValidators;

public class UploadCVRequestValidator:AbstractValidator<UploadCVRequest>
{
    public UploadCVRequestValidator()
    {
        RuleFor(x => x.File)
            .NotNull().WithMessage("Please upload a file.")
            .Must(f => f.Length > 0).WithMessage("The uploaded file is empty.")
            .Must(f => f.Length <= 5 * 1024 * 1024).WithMessage("File size must not exceed 5 MB.")
            .Must(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)).WithMessage("File extension must be .pdf")
            .Must(f => f.IsValidPdfSignature()).WithMessage("Invalid file format. The file is not a genuine PDF."); 
    }
}
