using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using Microsoft.Extensions.Options;
using OneOf;
using System.Text;
using System.Text.Json;

namespace CVAnalyzerAPI.Services.AnalyzeServices;


public class GeminiService(HttpClient _httpClient,IOptions<GeminiSettings> options) : IAnalyzeService
{
    private readonly GeminiSettings _settings = options.Value;
    public async Task<OneOf<CvAnalysisResponse, Error>> AnalyzeCVAsync(string cvText, string? jobDescription = null)
    {
        var prompt = BuildPrompt(cvText, jobDescription);

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json"
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_settings.Url}?key={_settings.ApiKey}", content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return new Error(ErrorCodes.BadRequest, $"Gemini API returned status code {response.StatusCode}: {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();

        using var jsonDocument = JsonDocument.Parse(responseString);
        var resultText = jsonDocument.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var analysisResult = JsonSerializer.Deserialize<CvAnalysisResponse>(resultText!, options);

        return analysisResult is not null ? analysisResult : new Error(ErrorCodes.BadRequest, "Failed to parse Gemini API response into CvAnalysisResult");
    }

    private string BuildPrompt(string cvText, string? jobDescription)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert HR and Technical Recruiter. Please analyze the following CV.");
        sb.AppendLine("Return ONLY a valid JSON object matching this schema:");
        sb.AppendLine("{ \"score\": 0-100, \"strengths\": [\"\"], \"weaknesses\": [\"\"], \"suggestions\": [\"\"], \"jobMatchPercentage\": 0-100 }");

        if (!string.IsNullOrWhiteSpace(jobDescription))
        {
            sb.AppendLine("Calculate the 'jobMatchPercentage' based on how well the CV fits the following Job Description.");
            sb.AppendLine("Job Description:");
            sb.AppendLine(jobDescription);
        }
        else
        {
            sb.AppendLine("No job description provided, so set 'jobMatchPercentage' to null.");
        }

        sb.AppendLine("--- CV TEXT ---");
        sb.AppendLine(cvText);

        return sb.ToString();
    }
}