namespace CVAnalyzerAPI.Models;

public class CV
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; }=null!;
    public string FilePath { get; set; }=null!;
    public DateTime UploadedAt { get; set; }

    public ApplicationUser User { get; set; }=default!;
    public ICollection<Analysis> Analyses { get; set; } = [];
}
