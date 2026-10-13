namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record GetCVAnalysisResponse(
    int Id,
    int Score, 
    List<StrengthsDto> Strengths,
    List<string> Weaknesses,
    List<SuggestionsDto> Suggestions,
    string ShareToken,
    string UserName,
    int? JobMatchPercentage,
    int TechnicalAlignment,
    int SoftSkillsFit,
    int DomainExperience);

public class StrengthsDto
{
    public string Icon { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public class SuggestionsDto
{
    public string Heading { get; set; } = null!;
    public string Description { get; set; } = null!;
}