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
    public async Task<OneOf<UploadCvResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default)
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
                Strengths = analysisResult.Strengths.Select(s => new AnalysisStrength
                {
                    Icon = s.Icon,
                    Heading = s.Heading,
                    Description = s.Description
                }).ToList(),
                Weaknesses = analysisResult.Weaknesses,
                Suggestions = analysisResult.Suggestions.Select(s => new AnalysisSuggestion
                {
                    Heading = s.Heading,
                    Description = s.Description
                }).ToList(),
                JobMatchPercentage = analysisResult.JobMatchPercentage,
                TechnicalAlignment = analysisResult.TechnicalAlignment,
                SoftSkillsFit = analysisResult.SoftSkillsFit,
                DomainExperience = analysisResult.DomainExperience,
                JobDescription = request.JobDescription
            };
            await _context.Analyses.AddAsync(analysisRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("CV record and analysis saved successfully for CV ID {CvId}.", cvRecord.Id);
            return new UploadCvResponse(cvRecord.Id, analysisRecord.Id);
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
            .OrderByDescending(a => a.Id)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.CV.UserId == currentUserId && a.CVId == cvId, cancellationToken);
        if (analysis is null)
        {
            _logger.LogWarning("No analysis found for CV ID {CvId} and user ID {UserId}.", cvId, currentUserId);
            return new Error(ErrorCodes.BadRequest, "No analysis found for the specified CV");
        }
        var response = new GetCVAnalysisResponse(
            analysis.Id,
            analysis.Score,
            analysis.Strengths.Select(s => new StrengthsDto 
            {
                Icon = s.Icon,
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            analysis.Weaknesses.Select(w => w).ToList(),
            analysis.Suggestions.Select(s => new SuggestionsDto
            {
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            analysis.CV.ShareToken.ToString(),
            analysis.CV.User.UserName!,
            analysis.JobMatchPercentage,
            analysis.TechnicalAlignment,
            analysis.SoftSkillsFit,
            analysis.DomainExperience
        );
        return response;
    }
    
    public async Task<OneOf<GetCVAnalysisResponse, Error>> AnalyzeExtractedCVAsync(int id,CancellationToken cancellationToken)
    {
        var cvResponseFromDb = await _context.CVs
            .Select(x=>new
            {
                CvId = x.Id,
                ExtractedText=x.ExtractedText,
                JobDescription= x.Analyses.OrderByDescending(a=>a.Id).Select(a=>a.JobDescription).FirstOrDefault(),
                UserName = x.User.UserName,
                ShareToken=x.ShareToken
            })
            .FirstOrDefaultAsync(x => x.CvId == id, cancellationToken);
        if (cvResponseFromDb is null)
        {
            _logger.LogWarning("Attempt to analyze non-existent CV with ID {CvId}.", id);
            return new Error(ErrorCodes.BadRequest, "CV not found");
        }
        var analysisResultOrError = await _analyzeService.AnalyzeCVAsync(cvResponseFromDb.ExtractedText, cvResponseFromDb.JobDescription);
        if (analysisResultOrError.IsT1)
        {
            _logger.LogError("Error analyzing extracted CV with ID {CvId}: {Error}", id, analysisResultOrError.AsT1.Message);
            return analysisResultOrError.AsT1;
        }
        var analysisResult = analysisResultOrError.AsT0;
        var analysisRecord = new Analysis
        {
            CVId = cvResponseFromDb.CvId,
            Score = analysisResult.Score,
            Strengths = analysisResult.Strengths.Select(s => new AnalysisStrength
            {
                Icon = s.Icon,
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            Weaknesses = analysisResult.Weaknesses.Select(w => w).ToList(),
            Suggestions = analysisResult.Suggestions.Select(s => new AnalysisSuggestion
            {
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            JobMatchPercentage = analysisResult.JobMatchPercentage,
            TechnicalAlignment = analysisResult.TechnicalAlignment,
            SoftSkillsFit = analysisResult.SoftSkillsFit,
            DomainExperience = analysisResult.DomainExperience,
            JobDescription = cvResponseFromDb.JobDescription
        };
        await _context.Analyses.AddAsync(analysisRecord);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Successfully analyzed extracted CV with ID {CvId}. Score: {Score}, Job Match: {JobMatchPercentage}",
            id, analysisResult.Score, analysisResult.JobMatchPercentage);
        return new GetCVAnalysisResponse(
            analysisRecord.Id,
            analysisRecord.Score,
            analysisResult.Strengths,
            analysisResult.Weaknesses,
            analysisResult.Suggestions,
            cvResponseFromDb.ShareToken.ToString(),
            cvResponseFromDb.UserName ?? "Unknown",
            analysisResult.JobMatchPercentage,
            analysisResult.TechnicalAlignment,
            analysisResult.SoftSkillsFit,
            analysisResult.DomainExperience
        );
    }
    
    public async Task<Error> DeleteCvAsync(int id, CancellationToken cancellationToken = default)
    {
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to delete CV with ID {CvId}.", id);
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to delete CV");
        }
        var cv = await _context.CVs.FirstOrDefaultAsync(cv => cv.Id == id && cv.UserId == currentUserId, cancellationToken);
        if (cv is null)
        {
            _logger.LogWarning("Attempt to delete non-existent CV with ID {CvId} for user ID {UserId}.", id, currentUserId);
            return new Error(ErrorCodes.BadRequest, "CV not found");
        }
        _context.CVs.Remove(cv);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Successfully deleted CV with ID {CvId} for user ID {UserId}.", id, currentUserId);
        return new Error(ErrorCodes.None, "CV deleted successfully");
    }
   
    public async Task<OneOf<GetCVAnalysisResponse, Error>> GetByShareTokenAsync(Guid token)
    {
        var analysis = await _context.Analyses
            .Include(a => a.CV)
            .ThenInclude(cv => cv.User)
            .OrderByDescending(a => a.Id)
            .AsSplitQuery()
            .FirstOrDefaultAsync(a => a.CV.ShareToken == token);
        if (analysis is null)
        {
            _logger.LogWarning("No analysis found for share token {Token}.", token);
            return new Error(ErrorCodes.BadRequest, "No analysis found for the specified share token");
        }
        var response = new GetCVAnalysisResponse(
            analysis.Id,
            analysis.Score,
            analysis.Strengths.Select(s => new StrengthsDto
            {
                Icon = s.Icon,
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            analysis.Weaknesses.Select(w => w).ToList(),
            analysis.Suggestions.Select(s => new SuggestionsDto
            {
                Heading = s.Heading,
                Description = s.Description
            }).ToList(),
            analysis.CV.ShareToken.ToString(),
            analysis.CV.User.UserName!,
            analysis.JobMatchPercentage,
            analysis.TechnicalAlignment,
            analysis.SoftSkillsFit,
            analysis.DomainExperience
        );
        return response;
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
