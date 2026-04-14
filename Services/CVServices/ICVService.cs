using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using OneOf;

namespace CVAnalyzerAPI.Services.CVServices;

public interface ICVService
{
    Task<OneOf<CvAnalysisResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default);

    Task<OneOf<List<GetCVResponse>, Error>> GetCVsAsync(CancellationToken cancellationToken);
    Task<OneOf<GetCVAnalysisResponse, Error>> GetCVAnalysisAsync(int cvId, CancellationToken cancellationToken);
    Task<OneOf<CvAnalysisResponse, Error>> AnalyzeExtractedCVAsync(int id, string? jobDescription, CancellationToken cancellationToken);
}
