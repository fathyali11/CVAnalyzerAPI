using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
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
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(new { error.Message }),
                ErrorCodes.UnAuthorized => Unauthorized(new { error.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error.Message })
            });
    }
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var result = await _cVService.GetCVsAsync(cancellationToken);
        return result.Match<IActionResult>(
            cvs => Ok(cvs),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(new { error.Message }),
                ErrorCodes.UnAuthorized => Unauthorized(new { error.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error.Message })
            });
    }
    [HttpGet("{id}/analysis")]
    public async Task<IActionResult> GetCVAnalysis([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.GetCVAnalysisAsync(id, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(new { error.Message }),
                ErrorCodes.UnAuthorized => Unauthorized(new { error.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error.Message })
            });
    }
    [EnableRateLimiting("Analyze")]
    [HttpPost("{id}/reanalyze")]
    public async Task<IActionResult> ReanalyzeCV([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.AnalyzeExtractedCVAsync(id, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(new { error.Message }),
                ErrorCodes.UnAuthorized => Unauthorized(new { error.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error.Message })
            });
    }
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCV([FromRoute] int id, CancellationToken cancellationToken)
    {
        var result = await _cVService.DeleteCvAsync(id, cancellationToken);
        return result.Code switch
        {
            ErrorCodes.None => NoContent(),
            ErrorCodes.BadRequest => BadRequest(new { result.Message }),
            ErrorCodes.UnAuthorized => StatusCode(StatusCodes.Status401Unauthorized, new { result.Message }),
            _ => StatusCode(StatusCodes.Status500InternalServerError, new { result.Message })
        };
    }

    [EnableRateLimiting("public-link")]
    [AllowAnonymous]
    [HttpGet("share-analysis/{token}")]
    public async Task<IActionResult> GetSharedAnalysis(Guid token, CancellationToken cancellationToken = default)
    {

        var result = await _cVService.GetByShareTokenAsync(token, cancellationToken);
        return result.Match<IActionResult>(
            analysis => Ok(analysis),
            error => error.Code switch
            {
                ErrorCodes.NotFound => NotFound(new { error.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { error.Message })
            });
    }

}

