using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.FileServices;
using FluentValidation;
using OneOf;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace CVAnalyzerAPI.Services.CVServices;

public class CVService(IFileService _fileService,
    ILogger<CVService> _logger,
    IAnalyzeService _analyzeService,
    ApplicationDbContext _context,
    IAuthService _authService,
    IValidator<UploadCVRequest> _validator
    ) :ICVService
{
    public async Task<OneOf<CvAnalysisResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request to upload and analyze CV: {FileName}", request.File.FileName);
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            _logger.LogWarning("Validation failed for CV upload: {Errors}", errors);
            return new Error(ErrorCodes.BadRequest, $"Validation failed: {errors}");
        }
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to upload CV.");
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to save CV record");
        }
        var (url, publicId) = await _fileService.UploadFileAsync(request.File, "cvs", cancellationToken);
        _logger.LogInformation("CV uploaded successfully to {Url} with public ID {PublicId}", url, publicId);

        try
        {
            using var stream = request.File.OpenReadStream();
            var text = await ExtractTextFromPDFAsync(stream);
            _logger.LogInformation("Successfully extracted {Length} characters from CV.", text.Length);

            var analysisResultOrError = await _analyzeService.AnalyzeCVAsync(text, request.JobDescription);
            if (analysisResultOrError.IsT1)
            {
                _logger.LogError("Error analyzing CV: {Error}", analysisResultOrError.AsT1.Message);
                await _fileService.DeleteFileAsync(publicId);
                return analysisResultOrError.AsT1;
            }

            var analysisResult = analysisResultOrError.AsT0;
            _logger.LogInformation("CV analysis completed with score {Score} and job match {JobMatchPercentage}",
                analysisResult.Score, analysisResult.JobMatchPercentage);

            using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

            var cvRecord = new CV
            {
                FileName = request.File.FileName,
                FilePath = url,
                UploadedAt = DateTime.UtcNow,
                UserId = currentUserId
            };
            await _context.CVs.AddAsync(cvRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            var analysisRecord = new Analysis
            {
                CVId = cvRecord.Id,
                Score = analysisResult.Score,
                Strengths = string.Join(";", analysisResult.Strengths),
                Weaknesses = string.Join(";", analysisResult.Weaknesses),
                Suggestions = string.Join(";", analysisResult.Suggestions),
                JobMatchPercentage = analysisResult.JobMatchPercentage
            };
            await _context.Analyses.AddAsync(analysisRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return analysisResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while processing CV {FileName}. Rolling back...", request.File.FileName);

            if (!string.IsNullOrEmpty(publicId))
            {
                await _fileService.DeleteFileAsync(publicId);
            }
            
            return new Error(ErrorCodes.InternalServerError, "An error occurred while processing your request. Please try again.");
        }
    }


    private async Task<string> ExtractTextFromPDFAsync(Stream pdfStream)
    {
        return await Task.Run(() =>
        {
            var textBuilder = new StringBuilder();

            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    var text = ContentOrderTextExtractor.GetText(page);
                    textBuilder.AppendLine(text);
                }
            }

            return textBuilder.ToString().Trim();
        });
    }
}
