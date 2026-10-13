using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CVAnalyzerAPI.Filters;

public class ValidationFilter(ILogger<ValidationFilter> _logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
                continue;

            var argumentType = argument.GetType();
            var validator = context.HttpContext.RequestServices
                .GetService(typeof(IValidator<>).MakeGenericType(argumentType)) as IValidator;

            if (validator is null || !validator.CanValidateInstancesOfType(argumentType))
                continue;

            var validationResult = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Validation failed for {ArgumentType} on {Method} {Path}: {Errors}",
                    argumentType.Name,
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path,
                    string.Join("; ", validationResult.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}")));

                context.Result = new BadRequestObjectResult(new
                {
                    message = "Validation failed",
                    errors = validationResult.Errors
                        .GroupBy(e => e.PropertyName)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
                });
                return;
            }

            _logger.LogDebug(
                "Validation passed for {ArgumentType} on {Method} {Path}",
                argumentType.Name,
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);
        }

        await next();
    }
}
