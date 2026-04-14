namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record GetCVResponse(int Id, string FileName, string Url, DateTime UploadedAt, int Score);

public record GetCVAnalysisResponse(
    int Id,
    int Score, 
    string Strengths,
    string Weaknesses,
    string Suggestions,
    string userName);