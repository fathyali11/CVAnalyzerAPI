using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using OneOf;

namespace CVAnalyzerAPI.Services.AnalyzeServices;

public interface IAnalyzeService
{
    Task<OneOf<CvAnalysisResponse, Error>> AnalyzeCVAsync(string cvText, string? jobDescription = null);
}
