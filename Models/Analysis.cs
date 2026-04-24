namespace CVAnalyzerAPI.Models;

public class Analysis
{
    public int Id { get; set; }
    public int CVId { get; set; }
    public string? JobDescription { get; set; }
    public int Score { get; set; }
    public List<AnalysisStrength> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<AnalysisSuggestion> Suggestions { get; set; } = [];
    public int? JobMatchPercentage { get; set; }
    public int TechnicalAlignment { get; set; }
    public int SoftSkillsFit { get; set; }
    public int DomainExperience { get; set; }
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;

    public CV CV { get; set; }=default!;
}

public class AnalysisStrength
{
    public string Icon { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public string Description { get; set; } = null!;
}

public class AnalysisSuggestion
{
    public string Heading { get; set; } = null!;
    public string Description { get; set; } = null!;
}