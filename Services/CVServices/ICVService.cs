using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using OneOf;

namespace CVAnalyzerAPI.Services.CVServices;

public interface ICVService
{
    Task<OneOf<UploadCvResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default);

    Task<OneOf<List<GetCVResponse>, Error>> GetCVsAsync(CancellationToken cancellationToken);
    Task<OneOf<GetCVAnalysisResponse, Error>> GetCVAnalysisAsync(int cvId, CancellationToken cancellationToken);
    Task<OneOf<GetCVAnalysisResponse, Error>> AnalyzeExtractedCVAsync(int id, CancellationToken cancellationToken);
    Task<Error> DeleteCvAsync(int id, CancellationToken cancellationToken = default);
    Task<OneOf<GetCVAnalysisResponse, Error>> GetByShareTokenAsync(Guid token);
}
