using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using Microsoft.Extensions.Options;
using OneOf;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CVAnalyzerAPI.Services.AnalyzeServices;

public class GroqService(HttpClient _httpClient, IOptions<GroqSettings> options) : IAnalyzeService
{
    private readonly GroqSettings _settings = options.Value;

    public async Task<OneOf<GetCVAnalysisResponse, Error>> AnalyzeCVAsync(string cvText, string? jobDescription = null)
    {
        var prompt = BuildPrompt(cvText, jobDescription);


        var requestBody = new
        {
            model = _settings.Model, 
            messages = new[]
            {
                new { role = "system", content = "You are an expert HR and Technical Recruiter. Analyze CVs and return structured JSON." },
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_object" }, 
            temperature = 0.5 
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

        var response = await _httpClient.PostAsync(_settings.Url, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return new Error(ErrorCodes.BadRequest, $"Groq API returned status code {response.StatusCode}: {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();

        try
        {
            using var jsonDocument = JsonDocument.Parse(responseString);

            var resultText = jsonDocument.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var analysisResult = JsonSerializer.Deserialize<GetCVAnalysisResponse>(resultText!, options);

            return analysisResult is not null ? analysisResult : new Error(ErrorCodes.BadRequest, "Failed to parse Groq API response");
        }
        catch (Exception ex)
        {
            return new Error(ErrorCodes.BadRequest, $"Error during parsing: {ex.Message}");
        }
    }

    private string BuildPrompt(string cvText, string? jobDescription)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert HR and Technical Recruiter. Analyze the provided CV.");
        sb.AppendLine("Return ONLY a valid JSON object matching this exact schema:");
        sb.AppendLine("{");
        sb.AppendLine("  \"score\": 0, // Integer between 0-100");
        sb.AppendLine("  \"strengths\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"icon\": \"string\", // MUST be a valid Google Material Symbols name in lowercase snake_case (e.g., 'psychology', 'bolt', 'groups', 'code', 'dns', 'trending_up')");
        sb.AppendLine("      \"heading\": \"string\", // Short title for the strength");
        sb.AppendLine("      \"description\": \"string\" // Detailed explanation of the strength");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"weaknesses\": [\"string\"], // List of short sentences highlighting areas for improvement");
        sb.AppendLine("  \"suggestions\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"heading\": \"string\", // Short title like 'Strategic Question' or 'Assessment Focus'");
        sb.AppendLine("      \"description\": \"string\" // Detailed advice or question for the interviewer");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"jobMatchPercentage\": 0, // Integer between 0-100 (null if no job description)");
        sb.AppendLine("  \"technicalAlignment\": 0, // Integer between 0-100");
        sb.AppendLine("  \"softSkillsFit\": 0, // Integer between 0-100");
        sb.AppendLine("  \"domainExperience\": 0 // Integer between 0-100");
        sb.AppendLine("}");

        if (!string.IsNullOrWhiteSpace(jobDescription))
        {
            sb.AppendLine($"\nEvaluate against this Job Description:\n{jobDescription}");
        }
        else
        {
            sb.AppendLine("\nNo job description provided. Evaluate based on general software engineering standards and set jobMatchPercentage to null.");
        }

        sb.AppendLine("\n--- CV TEXT ---");
        sb.AppendLine(cvText);

        return sb.ToString();
    }
}