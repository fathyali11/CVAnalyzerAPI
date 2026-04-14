using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.FileServices;
using Microsoft.EntityFrameworkCore;
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
                UserId = currentUserId,
                ExtractedText = text
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

    public async Task<OneOf<List<GetCVResponse>, Error>> GetCVsAsync(CancellationToken cancellationToken)
    {
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to retrieve CVs.");
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to retrieve CVs");
        }

        var cvs = await _context.CVs
            .Include(cv=>cv.Analyses)
            .Where(cv => cv.UserId == currentUserId)
            .Select(cv=>new GetCVResponse(
                cv.Id,
                cv.FileName,
                cv.FilePath,
                cv.UploadedAt,
                cv.Analyses.OrderByDescending(a=>a.Id).FirstOrDefault()!.Score
                ))
            .ToListAsync(cancellationToken);

        return cvs;
    }

    public async Task<OneOf<GetCVAnalysisResponse,Error>> GetCVAnalysisAsync(int cvId, CancellationToken cancellationToken)
    {
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to retrieve CV analysis for CV ID {CvId}.", cvId);
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to retrieve CV analysis");
        }
        var analysis = await _context.Analyses
            .Include(a => a.CV)
            .ThenInclude(cv=>cv.User)
            .Where(a => a.CV.UserId == currentUserId && a.CVId == cvId)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (analysis is null)
        {
            _logger.LogWarning("No analysis found for CV ID {CvId} and user ID {UserId}.", cvId, currentUserId);
            return new Error(ErrorCodes.BadRequest, "No analysis found for the specified CV");
        }
        var response = new GetCVAnalysisResponse(
            analysis.Id,
            analysis.Score,
            analysis.Strengths,
            analysis.Weaknesses,
            analysis.Suggestions,
            analysis.CV.User.UserName!
        );
        return response;
    }
    
    public async Task<OneOf<CvAnalysisResponse, Error>> AnalyzeExtractedCVAsync(int id, string? jobDescription, CancellationToken cancellationToken)
    {
        var cv = await _context.CVs.FindAsync(id);
        if (cv is null)
        {
            _logger.LogWarning("Attempt to analyze non-existent CV with ID {CvId}.", id);
            return new Error(ErrorCodes.BadRequest, "CV not found");
        }
        var analysisResultOrError = await _analyzeService.AnalyzeCVAsync(cv.ExtractedText, jobDescription);
        if (analysisResultOrError.IsT1)
        {
            _logger.LogError("Error analyzing extracted CV with ID {CvId}: {Error}", id, analysisResultOrError.AsT1.Message);
            return analysisResultOrError.AsT1;
        }
        var analysisResult = analysisResultOrError.AsT0;
        var analysisRecord = new Analysis
        {
            CVId = cv.Id,
            Score = analysisResult.Score,
            Strengths = string.Join(";", analysisResult.Strengths),
            Weaknesses = string.Join(";", analysisResult.Weaknesses),
            Suggestions = string.Join(";", analysisResult.Suggestions),
            JobMatchPercentage = analysisResult.JobMatchPercentage
        };
        await _context.Analyses.AddAsync(analysisRecord);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Successfully analyzed extracted CV with ID {CvId}. Score: {Score}, Job Match: {JobMatchPercentage}",
            id, analysisResult.Score, analysisResult.JobMatchPercentage);
        return analysisResult;
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
