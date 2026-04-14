namespace CVAnalyzerAPI.Models;

public class Analysis
{
    public int Id { get; set; }
    public int CVId { get; set; }
    public string? JobDescription { get; set; }
    public int Score { get; set; }
    public string Strengths { get; set; } = null!;
    public string Weaknesses { get; set; } = null!;
    public string Suggestions { get; set; } = null!;
    public int? JobMatchPercentage { get; set; }
    public DateTime CreatedAt { get; set; }=DateTime.UtcNow;

    public CV CV { get; set; }=default!;
}