namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record CvAnalysisResult(
    int Score,
    List<StrengthsDto>? Strengths,
    List<string>? Weaknesses,
    List<SuggestionsDto>? Suggestions,
    int? JobMatchPercentage,
    int TechnicalAlignment,
    int SoftSkillsFit,
    int DomainExperience,
    string? JobDescription);
