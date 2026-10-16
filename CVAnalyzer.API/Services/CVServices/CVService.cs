
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.FileServices;
using Microsoft.EntityFrameworkCore;
using OneOf;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Microsoft.Extensions.Caching.Hybrid;

namespace CVAnalyzerAPI.Services.CVServices;

public class CVService(IFileService _fileService,
    ILogger<CVService> _logger,
    IAnalyzeService _analyzeService,
    ApplicationDbContext _context,
    IAuthService _authService,
    HybridCache _cache
    ) :ICVService
{
    public async Task<OneOf<UploadCvResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Received request to upload and analyze CV: {FileName}", request.File.FileName);
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to upload CV.");
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to save CV record");
        }

        var fileName = Path.GetFileName(request.File.FileName);
        string? uploadedFilePublicId = null;
        try
        {
            string text;
            await using (var stream = request.File.OpenReadStream())
            {
                text = await ExtractTextAsync(stream, fileName, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("No text could be extracted from {FileName}.", fileName);
                return new Error(ErrorCodes.BadRequest, "No text could be extracted from the file. Please make sure it is not a scanned/image-only document.");
            }

            if (text.Length > FileConstraints.MaxCvTextLength)
            {
                _logger.LogInformation("Extracted text length {Length} exceeds limit, truncating to {Limit} characters.", text.Length, FileConstraints.MaxCvTextLength);
                text = text[..FileConstraints.MaxCvTextLength];
            }
            _logger.LogInformation("Successfully extracted {Length} characters from CV.", text.Length);

            var analysisResultOrError = await _analyzeService.AnalyzeCVAsync(text, request.JobDescription, cancellationToken);
            if (analysisResultOrError.IsT1)
            {
                _logger.LogError("Error analyzing CV: {Error}", analysisResultOrError.AsT1.Message);
                return analysisResultOrError.AsT1;
            }

            var analysisResult = analysisResultOrError.AsT0;
            _logger.LogInformation("CV analysis completed with score {Score} and job match {JobMatchPercentage}",
                analysisResult.Score, analysisResult.JobMatchPercentage);

            var (url, publicId) = await _fileService.UploadFileAsync(request.File, "cvs", cancellationToken);
            uploadedFilePublicId = publicId;
            _logger.LogInformation("CV uploaded successfully to {Url} with public ID {PublicId}", url, publicId);

            var cvRecord = new CV
            {
                FileName = fileName,
                FilePath = url,
                FilePublicId = publicId,
                UploadedAt = DateTime.UtcNow,
                UserId = currentUserId,
                ExtractedText = text
            };
            var analysisRecord = BuildAnalysis(analysisResult, request.JobDescription);
            analysisRecord.CV = cvRecord;
            _context.CVs.Add(cvRecord);
            _context.Analyses.Add(analysisRecord);
            await _context.SaveChangesAsync(cancellationToken);

            await _cache.RemoveAsync(CacheKeys.UserCvs(currentUserId), cancellationToken);
            _logger.LogInformation("CV record and analysis saved successfully for CV ID {CvId}.", cvRecord.Id);
            return new UploadCvResponse(cvRecord.Id, analysisRecord.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while processing CV {FileName}.", fileName);

            await TryDeleteFileAsync(uploadedFilePublicId, cancellationToken);

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

        var cacheKey = CacheKeys.UserCvs(currentUserId);
        var cachedCVs = await _cache.GetOrCreateAsync(cacheKey,
            async ct =>
            {
                var cvs = await _context.CVs
                .AsNoTracking()
                .Where(cv => cv.UserId == currentUserId)
                .Select(cv => new GetCVResponse(
                    cv.Id,
                    cv.FileName,
                    cv.FilePath,
                    cv.UploadedAt,
                    cv.Analyses.OrderByDescending(a => a.Id).Select(a => (int?)a.Score).FirstOrDefault() ?? 0
                    ))
                .ToListAsync(ct);
                return cvs;
            }, cancellationToken: cancellationToken);

        return cachedCVs;
    }

    public async Task<OneOf<GetCVAnalysisResponse,Error>> GetCVAnalysisAsync(int cvId, CancellationToken cancellationToken)
    {
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to retrieve CV analysis for CV ID {CvId}.", cvId);
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to retrieve CV analysis");
        }


        var cacheKey =CacheKeys.CvAnalysis(cvId, currentUserId);
        var analysis = await _cache.GetOrCreateAsync(cacheKey,
            async ct =>
            {
                var a = await _context.Analyses
                        .AsNoTracking()
                        .Where(x => x.CV.UserId == currentUserId && x.CVId == cvId)
                        .OrderByDescending(x => x.Id)
                        .Select(x => new AnalysisData(
                            x.Id,
                            x.Score,
                            x.Strengths,
                            x.Weaknesses,
                            x.Suggestions,
                            x.CV.ShareToken,
                            x.CV.User.UserName,
                            x.JobMatchPercentage,
                            x.TechnicalAlignment,
                            x.SoftSkillsFit,
                            x.DomainExperience))
                        .FirstOrDefaultAsync(ct);

                return a is null ? null : ToResponse(a);
            },cancellationToken:cancellationToken);

        if (analysis is null)
        {
            _logger.LogWarning("No analysis found for CV ID {CvId} and user ID {UserId}.", cvId, currentUserId);
            return new Error(ErrorCodes.NotFound, "No analysis found for the specified CV");
        }
        _logger.LogInformation("Successfully retrieved analysis for CV ID {CvId} and user ID {UserId}.", cvId, currentUserId);
        return analysis;
    }
    
    public async Task<OneOf<GetCVAnalysisResponse, Error>> AnalyzeExtractedCVAsync(int id,CancellationToken cancellationToken)
    {
        var currentUserId = await _authService.GetCurrentUserIdAsync(cancellationToken);
        if (currentUserId is null)
        {
            _logger.LogWarning("Unauthenticated attempt to analyze CV with ID {CvId}.", id);
            return new Error(ErrorCodes.UnAuthorized, "User must be authenticated to analyze CV");
        }
        
        try
        {
            var ownsCv = await _context.CVs
                .AnyAsync(cv => cv.Id == id && cv.UserId == currentUserId, cancellationToken);
            if (!ownsCv)
            {
                _logger.LogWarning("Attempt to analyze non-existent or not-owned CV with ID {CvId} by user ID {UserId}.", id, currentUserId);
                return new Error(ErrorCodes.NotFound, "CV not found");
            }

            var cacheKey = CacheKeys.CvReAnalysis(id, currentUserId);
            var cvResponseFromDb = await _cache.GetOrCreateAsync(cacheKey,
                async ct => await _context.CVs
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ReAnalysisSource(
                    x.Id,
                    x.ExtractedText,
                    x.Analyses.OrderByDescending(a => a.Id).Select(a => a.JobDescription).FirstOrDefault(),
                    x.User.UserName,
                    x.ShareToken))
                .FirstOrDefaultAsync(ct),
                cancellationToken: cancellationToken);
            if (cvResponseFromDb is null)
            {
                _logger.LogWarning("Attempt to analyze non-existent CV with ID {CvId}.", id);
                return new Error(ErrorCodes.NotFound, "CV not found");
            }

            if (string.IsNullOrWhiteSpace(cvResponseFromDb.ExtractedText))
            {
                _logger.LogWarning("CV with ID {CvId} has no extracted text to analyze.", id);
                return new Error(ErrorCodes.BadRequest, "No extracted text is available for this CV.");
            }

            var cvText = cvResponseFromDb.ExtractedText.Length > FileConstraints.MaxCvTextLength
                ? cvResponseFromDb.ExtractedText[..FileConstraints.MaxCvTextLength]
                : cvResponseFromDb.ExtractedText;

            var analysisResultOrError = await _analyzeService.AnalyzeCVAsync(cvText, cvResponseFromDb.JobDescription, cancellationToken);
            if (analysisResultOrError.IsT1)
            {
                _logger.LogError("Error analyzing extracted CV with ID {CvId}: {Error}", id, analysisResultOrError.AsT1.Message);
                return analysisResultOrError.AsT1;
            }
            var analysisResult = analysisResultOrError.AsT0;
            var analysisRecord = BuildAnalysis(analysisResult, cvResponseFromDb.JobDescription);
            analysisRecord.CVId = cvResponseFromDb.CvId;
            await _context.Analyses.AddAsync(analysisRecord, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await _cache.RemoveAsync(cacheKey, cancellationToken);
            await _cache.RemoveAsync(CacheKeys.CvAnalysis(cvResponseFromDb.CvId, currentUserId), cancellationToken);
            await _cache.RemoveAsync(CacheKeys.UserCvs(currentUserId), cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SharedCv(cvResponseFromDb.ShareToken), cancellationToken);
            _logger.LogInformation("Successfully analyzed extracted CV with ID {CvId}. Score: {Score}, Job Match: {JobMatchPercentage}",
                id, analysisResult.Score, analysisResult.JobMatchPercentage);

            return ToResponse(analysisResult, analysisRecord.Id, cvResponseFromDb.ShareToken, cvResponseFromDb.UserName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while re-analyzing CV with ID {CvId}.", id);
            return new Error(ErrorCodes.InternalServerError, "An error occurred while processing your request. Please try again.");
        }
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
            return new Error(ErrorCodes.NotFound, "CV not found");
        }
        _context.CVs.Remove(cv);
        await _context.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CacheKeys.UserCvs(currentUserId), cancellationToken);
        await _cache.RemoveAsync(CacheKeys.CvAnalysis(cv.Id, currentUserId), cancellationToken);
        await _cache.RemoveAsync(CacheKeys.SharedCv(cv.ShareToken), cancellationToken);
        await _cache.RemoveAsync(CacheKeys.CvReAnalysis(id, currentUserId), cancellationToken);

        await TryDeleteFileAsync(cv.FilePublicId, cancellationToken);

        _logger.LogInformation("Successfully deleted CV with ID {CvId} for user ID {UserId}.", id, currentUserId);
        return new Error(ErrorCodes.None, "CV deleted successfully");
    }
   
    public async Task<OneOf<GetCVAnalysisResponse, Error>> GetByShareTokenAsync(Guid token,CancellationToken cancellationToken=default)
    {
        var cacheKey =CacheKeys.SharedCv(token);

        var analysis = await _cache.GetOrCreateAsync(cacheKey,
           async ct =>
           {
               var a = await _context.Analyses
                       .AsNoTracking()
                       .Where(x => x.CV.ShareToken == token)
                       .OrderByDescending(x => x.Id)
                       .Select(x => new AnalysisData(
                           x.Id,
                           x.Score,
                           x.Strengths,
                           x.Weaknesses,
                           x.Suggestions,
                           x.CV.ShareToken,
                           x.CV.User.UserName,
                           x.JobMatchPercentage,
                           x.TechnicalAlignment,
                           x.SoftSkillsFit,
                           x.DomainExperience))
                       .FirstOrDefaultAsync(ct);

               return a is null ? null : ToResponse(a);
           }, cancellationToken: cancellationToken);

        if (analysis is null)
        {
            _logger.LogWarning("No analysis found for share token {Token}.", token);
            return new Error(ErrorCodes.NotFound, "No analysis found for the specified share token");
        }
        return analysis;
    }

    private async Task<string> ExtractTextAsync(Stream stream, string fileName, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => await ExtractTextFromTxtAsync(stream, cancellationToken),
            _ => await ExtractTextFromPdfAsync(stream, cancellationToken)
        };
    }

    private static async Task<string> ExtractTextFromTxtAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.Trim();
    }

    private async Task<string> ExtractTextFromPdfAsync(Stream pdfStream, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var textBuilder = new StringBuilder();

            using (var document = PdfDocument.Open(pdfStream))
            {
                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var text = ContentOrderTextExtractor.GetText(page);
                    textBuilder.AppendLine(text);
                }
            }

            return textBuilder.ToString().Trim();
        }, cancellationToken);
    }

    private async Task TryDeleteFileAsync(string? publicId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicId))
            return;

        try
        {
            await _fileService.DeleteFileAsync(publicId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file with public ID {PublicId} from storage.", publicId);
        }
    }

    private static Analysis BuildAnalysis(CvAnalysisResult result, string? jobDescription) => new()
    {
        Score = Math.Clamp(result.Score, 1, 100),
        Strengths = [.. (result.Strengths ?? []).Select(s => new AnalysisStrength
        {
            Icon = s.Icon,
            Heading = s.Heading,
            Description = s.Description
        })],
        Weaknesses = [.. (result.Weaknesses ?? [])],
        Suggestions = [.. (result.Suggestions ?? []).Select(s => new AnalysisSuggestion
        {
            Heading = s.Heading,
            Description = s.Description
        })],
        JobMatchPercentage = result.JobMatchPercentage,
        TechnicalAlignment = result.TechnicalAlignment,
        SoftSkillsFit = result.SoftSkillsFit,
        DomainExperience = result.DomainExperience,
        JobDescription = jobDescription
    };

    private sealed record ReAnalysisSource(
        int CvId,
        string ExtractedText,
        string? JobDescription,
        string? UserName,
        Guid ShareToken);

    private static GetCVAnalysisResponse ToResponse(CvAnalysisResult r, int id, Guid shareToken, string? userName) => new(
        id,
        Math.Clamp(r.Score, 1, 100),
        [.. (r.Strengths ?? [])],
        [.. (r.Weaknesses ?? [])],
        [.. (r.Suggestions ?? [])],
        shareToken.ToString(),
        userName ?? "Unknown",
        r.JobMatchPercentage,
        r.TechnicalAlignment,
        r.SoftSkillsFit,
        r.DomainExperience);

    private static GetCVAnalysisResponse ToResponse(AnalysisData a) => new(
        a.Id,
        Math.Clamp(a.Score, 1, 100),
        [.. (a.Strengths ?? []).Select(s => new StrengthsDto
        {
            Icon = s.Icon,
            Heading = s.Heading,
            Description = s.Description
        })],
        [.. (a.Weaknesses ?? [])],
        [.. (a.Suggestions ?? []).Select(s => new SuggestionsDto
        {
            Heading = s.Heading,
            Description = s.Description
        })],
        a.ShareToken.ToString(),
        a.UserName ?? "Unknown",
        a.JobMatchPercentage,
        a.TechnicalAlignment,
        a.SoftSkillsFit,
        a.DomainExperience);
}
