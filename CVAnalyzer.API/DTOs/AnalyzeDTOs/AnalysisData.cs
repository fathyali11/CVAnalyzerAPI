using CVAnalyzerAPI.Models;

namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

// EF materialization target for the analysis read queries. The entity stores Strengths/Suggestions
// as JSON-converted collections, so they can't be projected straight into the response DTO; this
// shape is mapped to GetCVAnalysisResponse by CVService.ToResponse.
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
    int DomainExperience);
