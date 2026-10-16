using System.ComponentModel.DataAnnotations;

namespace CVAnalyzerAPI.Consts;

public class FrontendSettings
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;
}
