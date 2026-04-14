using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Services.CVServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CVAnalyzerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CVsController(ICVService _cVService):ControllerBase
{
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
}
