using CloudinaryDotNet.Actions;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Services.CVServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.VisualBasic;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Utilities.IO;
using System.ComponentModel;
using System.Reflection;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.SpecialGraphicsState;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
    [HttpPost("{id}/reanalyze")]
    public async Task<IActionResult> ReanalyzeCV([FromRoute] int id, [FromBody] ReanalyzeCVRequest request, CancellationToken cancellationToken)
    {
        var result = await _cVService.AnalyzeExtractedCVAsync(id, request.JobDescription, cancellationToken);
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

