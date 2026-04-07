
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Services.AuthServices;
using Microsoft.AspNetCore.Mvc;

namespace CVAnalyzerAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthsController(IAuthService _authService) : ControllerBase
{

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody]RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _authService.RegisterAsync(request, cancellationToken);
        return result.Match<IActionResult>(
            authResponse => Ok(authResponse),
            error => error.Code switch
            {
                ErrorCodes.BadRequest => BadRequest(error.Message),
                ErrorCodes.Conflict => Conflict(error.Message),
                _ => StatusCode(500, "An unexpected error occurred")
            }

        );
    }
}


