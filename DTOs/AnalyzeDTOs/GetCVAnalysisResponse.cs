namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record GetCVAnalysisResponse(
    int Id,
    int Score, 
    string Strengths,
    string Weaknesses,
    string Suggestions,
    string userName);
