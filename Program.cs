using System.Text;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AuthsDTOs;
using CVAnalyzerAPI.Middlewares;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.EmailServices;
using CVAnalyzerAPI.Services.TokenServices;
using CVAnalyzerAPI.Validators.AuthValidators;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

using Microsoft.AspNetCore.Identity;
using CVAnalyzerAPI.Models;
using System.Threading.RateLimiting;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.FileServices;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Validators.CVValidators;
using CVAnalyzerAPI.Services.CVServices;
using Polly.Extensions.Http;
using Polly;
using Microsoft.Extensions.Caching.Hybrid;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => 
    configuration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.


builder.Services.AddControllers();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<ApplicationUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(nameof(JwtSettings)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GeminiSettings>()
    .Bind(builder.Configuration.GetSection(nameof(GeminiSettings)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<CloudinarySettings>()
    .Bind(builder.Configuration.GetSection(nameof(CloudinarySettings)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<GroqSettings>()
    .Bind(builder.Configuration.GetSection(nameof(GroqSettings)))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtSettings = builder.Configuration.GetSection(nameof(JwtSettings)).Get<JwtSettings>();

builder.Services.AddAuthentication(options =>
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


builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IValidator<RegisterRequest>, RegisterRequestValidator>();
builder.Services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
builder.Services.AddScoped<IValidator<ForgotPasswordRequest>, ForgotPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
builder.Services.AddScoped<IValidator<UploadCVRequest>, UploadCVRequestValidator>();

builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHttpClient<IAnalyzeService, GroqService>().AddPolicyHandler(GetRetryPolicy());
builder.Services.AddScoped<IFileService, CloudinaryService>();
builder.Services.AddScoped<ICVService, CVService>();

builder.Services.AddOptions<EmailSettings>()
    .Bind(builder.Configuration.GetSection(nameof(EmailSettings)));

builder.Services.AddCors(options =>
{
    options.AddPolicy("CVAnalyzerPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://cv-analyzer-ui.vercel.app")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
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
        context.HttpContext.Response.ContentType= "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests. Please try again later."
        }, cancellationToken);
    };
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromHours(1),
        LocalCacheExpiration = TimeSpan.FromMinutes(30),
    };
});



static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
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

var app = builder.Build();

app.UseExceptionHandler();

app.UseRouting();

app.UseCors("CVAnalyzerPolicy");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();