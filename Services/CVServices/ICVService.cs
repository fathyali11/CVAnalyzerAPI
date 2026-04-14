using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using OneOf;

namespace CVAnalyzerAPI.Services.CVServices;

public interface ICVService
{
    Task<OneOf<CvAnalysisResponse, Error>> UploadAndAnalysisCVAsync(UploadCVRequest request, CancellationToken cancellationToken = default);
}
