using System.Text;
using System.Threading.RateLimiting;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Filters;
using CVAnalyzerAPI.Middlewares;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.OpenApi;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.CVServices;
using CVAnalyzerAPI.Services.EmailServices;
using CVAnalyzerAPI.Services.FileServices;
using CVAnalyzerAPI.Services.TokenServices;
using CVAnalyzerAPI.Validators.AuthValidators;
using CVAnalyzerAPI.Validators.CVValidators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Extensions.Http;

namespace CVAnalyzerAPI.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddApiDependencies(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers(options =>
        {
            options.Filters.Add<ValidationFilter>();
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        AddOpenApiDocument(services);
        AddDatabase(services, configuration);
        AddIdentityCore(services);
        AddSettings(services, configuration);
        AddJwtAuthentication(services, configuration);
        AddApplicationServices(services);
        AddCorsPolicy(services);
        AddRateLimiterPolicies(services);
        AddCaching(services);

        return services;
    }

    private static void AddOpenApiDocument(IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        });
    }

    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();
    }

    private static void AddSettings(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(nameof(JwtSettings)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GeminiSettings>()
            .Bind(configuration.GetSection(nameof(GeminiSettings)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<CloudinarySettings>()
            .Bind(configuration.GetSection(nameof(CloudinarySettings)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GroqSettings>()
            .Bind(configuration.GetSection(nameof(GroqSettings)))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EmailSettings>()
            .Bind(configuration.GetSection(nameof(EmailSettings)))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtSettings = configuration.GetSection(nameof(JwtSettings)).Get<JwtSettings>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings?.Issuer,
                ValidAudience = jwtSettings?.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings?.SecretKey ?? string.Empty))
            };
        });
    }

    private static void AddApplicationServices(IServiceCollection services)
    {
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        services.AddScoped<IValidator<UploadCVRequest>, UploadCVRequestValidator>();

        services.AddScoped<IEmailService, EmailService>();
        services.AddHttpClient<IAnalyzeService, GroqService>().AddPolicyHandler(GetRetryPolicy());
        services.AddScoped<IFileService, CloudinaryService>();
        services.AddScoped<ICVService, CVService>();

        services.AddScoped<ValidationFilter>();
    }

    private static void AddCorsPolicy(IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("CVAnalyzerPolicy", policy =>
            {
                policy.WithOrigins("http://localhost:4200", "https://cv-analyzer-ui.vercel.app")
                      .AllowAnyMethod()
                      .AllowAnyHeader()
                      .AllowCredentials();
            });
        });
    }

    private static void AddRateLimiterPolicies(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("ForgotPassword", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 3,
                        Window = TimeSpan.FromHours(1),
                        SegmentsPerWindow = 4,
                        QueueLimit = 0
                    }));
            options.AddPolicy("UploadCV", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"{httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown"}_{httpContext.User?.Identity?.Name ?? "anonymous"}",
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromHours(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));

            options.AddPolicy("Analyze", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"{httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown"}_{httpContext.User?.Identity?.Name ?? "anonymous"}",
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 15,
                        Window = TimeSpan.FromHours(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0
                    }));
            options.AddPolicy("public-link", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 3,
                        QueueLimit = 0
                    }));

            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}_{httpContext.User?.Identity?.Name ?? "anonymous"}",
                    factory: partition => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5,
                        QueueLimit = 0
                    }));
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                context.HttpContext.Response.ContentType = "application/json";
                await context.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Please try again later."
                }, cancellationToken);
            };
        });
    }

    private static void AddCaching(IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddHybridCache(options =>
        {
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromHours(1),
                LocalCacheExpiration = TimeSpan.FromMinutes(30),
            };
        });
    }

    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Console.WriteLine($"[Polly Warning] Gemini API is busy. Delaying for {timespan.TotalSeconds} seconds, then making retry {retryAttempt}...");
                });
    }
}
