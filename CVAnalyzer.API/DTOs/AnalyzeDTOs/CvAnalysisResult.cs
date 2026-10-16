namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

// Contract for the LLM's JSON output. Deliberately separate from GetCVAnalysisResponse:
// the model only produces the analysis fields (no Id/ShareToken/UserName), and its collection
// fields are nullable because STJ leaves missing positional record params null.
public record CvAnalysisResult(
    int Score,
    List<StrengthsDto>? Strengths,
    List<string>? Weaknesses,
    List<SuggestionsDto>? Suggestions,
    int? JobMatchPercentage,
    int TechnicalAlignment,
    int SoftSkillsFit,
    int DomainExperience);
