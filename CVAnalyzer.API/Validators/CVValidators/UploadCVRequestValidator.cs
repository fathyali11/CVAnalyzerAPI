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
            .Must(f => f.Length <= 10 * 1024 * 1024).WithMessage("File size must not exceed 10 MB.")
            .Must(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || 
                       f.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || 
                       f.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            .WithMessage("File extension must be .pdf, .docx, or .txt")
            .Must(f => f.IsValidDocumentSignature()).WithMessage("Invalid file format. The file is not a genuine document.");
    }
}
