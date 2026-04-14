namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public class CvAnalysisResponse
{
    public int Score { get; set; }
    public List<string> Strengths { get; set; } = [];
    public List<string> Weaknesses { get; set; } = [];
    public List<string> Suggestions { get; set; } = [];
    public int? JobMatchPercentage { get; set; }
}