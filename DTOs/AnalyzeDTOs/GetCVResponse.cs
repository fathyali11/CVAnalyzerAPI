namespace CVAnalyzerAPI.DTOs.AnalyzeDTOs;

public record GetCVResponse(int Id, string FileName, string Url, DateTime UploadedAt, int Score);
