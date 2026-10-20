using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Extensions;
using CVAnalyzerAPI.Services.CVServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CVAnalyzerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CVsController(ICVService _cVService):ControllerBase
{
    [EnableRateLimiting("UploadCV")]
    [HttpPost("upload")]
    public async Task<IActionResult> UploadCV([FromForm] UploadCVRequest request, CancellationToken cancellationToken)
    {
        var result = await _cVService.UploadAndAnalysisCVAsync(request, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.ToEnvelopeResult());
    }
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _cVService.GetCVsAsync(cancellationToken);
        return result.Match<IActionResult>(
            cvs => Ok(cvs),
            error => error.ToEnvelopeResult());
    }
    [HttpGet("{id}/analysis")]
    public async Task<IActionResult> GetCVAnalysis([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.GetCVAnalysisAsync(id, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.ToEnvelopeResult());
    }
    [EnableRateLimiting("Analyze")]
    [HttpPost("{id}/reanalyze")]
    public async Task<IActionResult> ReanalyzeCV([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.AnalyzeExtractedCVAsync(id, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.ToEnvelopeResult());
    }
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCV([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.DeleteCvAsync(id, cancellationToken);
        return result.ToEnvelopeResult();
    }

    [EnableRateLimiting("public-link")]
    [AllowAnonymous]
    [HttpGet("share-analysis/{token}")]
    public async Task<IActionResult> GetSharedAnalysis(Guid token, CancellationToken cancellationToken = default)
    {

        var result = await _cVService.GetByShareTokenAsync(token, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.ToEnvelopeResult());
    }

}

