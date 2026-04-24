namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record UploadCVRequest(IFormFile File,string? JobDescription);
