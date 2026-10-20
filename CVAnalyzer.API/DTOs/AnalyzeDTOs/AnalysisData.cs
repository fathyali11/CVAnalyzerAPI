using CVAnalyzerAPI.Models;

namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public sealed record AnalysisData(
    int Id,
    int Score,
    List<AnalysisStrength> Strengths,
    List<string> Weaknesses,
    List<AnalysisSuggestion> Suggestions,
    Guid ShareToken,
    string? UserName,
    int? JobMatchPercentage,
    int TechnicalAlignment,
    int SoftSkillsFit,
    int DomainExperience,
    string? JobDescription);
