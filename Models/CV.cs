namespace CVAnalyzerAPI.Models;

public class CV
{
    public int Id { get; set; }
    public string UserId { get; set; }=null!;
    public string FileName { get; set; }=null!;
    public string FilePath { get; set; }=null!;
    public string ExtractedText { get; set; }=null!;
    public DateTime UploadedAt { get; set; }=DateTime.UtcNow;

    public ApplicationUser User { get; set; }=default!;
    public ICollection<Analysis> Analyses { get; set; } = [];
}
