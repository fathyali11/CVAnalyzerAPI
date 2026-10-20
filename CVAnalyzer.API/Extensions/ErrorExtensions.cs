using CVAnalyzerAPI.Consts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CVAnalyzerAPI.Extensions;


public static class ErrorExtensions{

    
    public static IActionResult ToEnvelopeResult(this Error error)
    {
        if (error.Code == ErrorCodes.None)
            return new NoContentResult();

        var statusCode = error.ToHttpStatusCode();
        var message = statusCode == StatusCodes.Status500InternalServerError
            ? "An unexpected error occurred"
            : error.Message;

        return new ObjectResult(new { message }) { StatusCode = statusCode };
    }

    private static int ToHttpStatusCode(this Error error) => error.Code switch
    {
        ErrorCodes.BadRequest => StatusCodes.Status400BadRequest,
        ErrorCodes.UnAuthorized => StatusCodes.Status401Unauthorized,
        ErrorCodes.NotFound => StatusCodes.Status404NotFound,
        ErrorCodes.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };
}
