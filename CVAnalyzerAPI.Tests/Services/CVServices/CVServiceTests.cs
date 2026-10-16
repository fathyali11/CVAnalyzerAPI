using System.Text;
using CVAnalyzerAPI.Consts;
using CVAnalyzerAPI.Data;
using CVAnalyzerAPI.DTOs.AnalyzeDTOs;
using CVAnalyzerAPI.Models;
using CVAnalyzerAPI.Services.AnalyzeServices;
using CVAnalyzerAPI.Services.AuthServices;
using CVAnalyzerAPI.Services.CVServices;
using CVAnalyzerAPI.Services.FileServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Services.CVServices;

public class CVServiceTests : IDisposable
{
    private const string UserId = "user-1";

    private readonly IFileService _fileService;
    private readonly IAnalyzeService _analyzeService;
    private readonly IAuthService _authService;
    private readonly HybridCache _cache;
    private readonly SqliteConnection _database;
    private readonly ApplicationDbContext _context;
    private readonly CVService _sut;

    public CVServiceTests()
    {
        _fileService = Substitute.For<IFileService>();
        _analyzeService = Substitute.For<IAnalyzeService>();
        _authService = Substitute.For<IAuthService>();
        _cache = Substitute.For<HybridCache>();

        _database = new SqliteConnection("DataSource=:memory:");
        _database.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_database)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns(UserId);

        // Sensible defaults so only the behaviour under test needs to be arranged.
        _fileService.UploadFileAsync(Arg.Any<IFormFile>(), "cvs", Arg.Any<CancellationToken>())
            .Returns(("https://cdn.example.com/cv.txt", "cv-public-id"));
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessAnalysis());

        _sut = new CVService(
            _fileService,
            Substitute.For<ILogger<CVService>>(),
            _analyzeService,
            _context,
            _authService,
            _cache);
    }

    public void Dispose()
    {
        _context.Dispose();
        _database.Dispose();
    }

    // --- UploadAndAnalysisCVAsync ---

    [Fact]
    public async Task UploadAndAnalysisCVAsync_Unauthenticated_ReturnsUnauthorizedError()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")));

        ShouldBeError(result, ErrorCodes.UnAuthorized, "User must be authenticated to save CV record");
        await _fileService.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default);
        await _analyzeService.DidNotReceiveWithAnyArgs().AnalyzeCVAsync(default!, default, default);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_FileWithNoExtractableText_ReturnsBadRequestError()
    {
        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "   \n\t  ")));

        ShouldBeError(result, ErrorCodes.BadRequest,
            "No text could be extracted from the file. Please make sure it is not a scanned/image-only document.");
        await _analyzeService.DidNotReceiveWithAnyArgs().AnalyzeCVAsync(default!, default, default);
        await _fileService.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default);
        (await _context.CVs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_TextExceedsLimit_TruncatesBeforeAnalyzingAndSaving()
    {
        await SeedUserAsync();
        var oversized = new string('a', FileConstraints.MaxCvTextLength + 5_000);

        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("big.txt", oversized)));

        ShouldBeSuccess(result);

        await _analyzeService.Received(1).AnalyzeCVAsync(
            Arg.Is<string>(t => t.Length == FileConstraints.MaxCvTextLength),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        var savedCv = await _context.CVs.AsNoTracking().SingleAsync();
        savedCv.ExtractedText.Length.ShouldBe(FileConstraints.MaxCvTextLength);
        savedCv.ExtractedText.ShouldBe(new string('a', FileConstraints.MaxCvTextLength));
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_AnalyzeServiceFails_ReturnsAnalyzeErrorWithoutUploading()
    {
        var analyzeError = new Error(ErrorCodes.BadRequest, "The AI provider rejected the request");
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(OneOf<CvAnalysisResult, Error>.FromT1(analyzeError));

        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")));

        ShouldBeError(result, ErrorCodes.BadRequest, "The AI provider rejected the request");
        await _fileService.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default);
        (await _context.CVs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_ValidTxtFile_PersistsCvAndAnalysisAndReturnsIds()
    {
        await SeedUserAsync();
        const string content = "John Doe\nSenior Software Engineer\nC# and .NET";
        const string jobDescription = "Senior .NET Developer";

        var result = await _sut.UploadAndAnalysisCVAsync(
            Request(TextFile("resume.txt", content), jobDescription));

        var response = ShouldBeSuccess(result);
        response.CvId.ShouldBeGreaterThan(0);
        response.AnalysisId.ShouldBeGreaterThan(0);

        await _analyzeService.Received(1).AnalyzeCVAsync(content, jobDescription, Arg.Any<CancellationToken>());

        var savedCv = await _context.CVs.AsNoTracking().SingleAsync();
        savedCv.Id.ShouldBe(response.CvId);
        savedCv.FileName.ShouldBe("resume.txt");
        savedCv.FilePath.ShouldBe("https://cdn.example.com/cv.txt");
        savedCv.FilePublicId.ShouldBe("cv-public-id");
        savedCv.ExtractedText.ShouldBe(content);
        savedCv.UserId.ShouldBe(UserId);

        var savedAnalysis = await _context.Analyses.AsNoTracking().SingleAsync();
        savedAnalysis.Id.ShouldBe(response.AnalysisId);
        savedAnalysis.CVId.ShouldBe(savedCv.Id);
        savedAnalysis.Score.ShouldBe(85);
        savedAnalysis.JobDescription.ShouldBe(jobDescription);
        savedAnalysis.Strengths.ShouldHaveSingleItem().Heading.ShouldBe("Strong C#");
        savedAnalysis.Weaknesses.ShouldHaveSingleItem().ShouldBe("Limited cloud experience");
        savedAnalysis.Suggestions.ShouldHaveSingleItem().Heading.ShouldBe("Add metrics");

        await _fileService.Received(1).UploadFileAsync(
            Arg.Any<IFormFile>(), "cvs", Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync(CacheKeys.UserCvs(UserId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_FileNameWithPath_StripsPathDownToFileName()
    {
        await SeedUserAsync();

        var result = await _sut.UploadAndAnalysisCVAsync(
            Request(TextFile("/tmp/uploads/nested/resume.txt", "John Doe")));

        ShouldBeSuccess(result);

        var savedCv = await _context.CVs.AsNoTracking().SingleAsync();
        savedCv.FileName.ShouldBe("resume.txt");
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_UnreadablePdf_ReturnsInternalServerErrorWithoutUploading()
    {
        // Anything that is not ".txt" is treated as a PDF; garbage bytes make PdfPig throw.
        var result = await _sut.UploadAndAnalysisCVAsync(
            Request(TextFile("resume.pdf", "definitely not a real pdf")));

        ShouldBeError(result, ErrorCodes.InternalServerError,
            "An error occurred while processing your request. Please try again.");
        await _fileService.DidNotReceiveWithAnyArgs().UploadFileAsync(default!, default!, default);
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
        (await _context.CVs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_UploadToStorageFails_ReturnsInternalServerErrorWithoutDeleting()
    {
        await SeedUserAsync();
        _fileService.When(f => f.UploadFileAsync(Arg.Any<IFormFile>(), "cvs", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Cloudinary is down"));

        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")));

        ShouldBeError(result, ErrorCodes.InternalServerError,
            "An error occurred while processing your request. Please try again.");
        // Nothing was uploaded, so there is no public id to clean up.
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
        (await _context.CVs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_ErrorAfterUpload_DeletesUploadedFileAndReturnsInternalServerError()
    {
        await SeedUserAsync();
        // The upload succeeds (so a public id exists) and the cache invalidation then fails,
        // forcing the catch block to roll back the orphaned file.
        _cache.When(c => c.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Cache unavailable"));

        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")));

        ShouldBeError(result, ErrorCodes.InternalServerError,
            "An error occurred while processing your request. Please try again.");
        await _fileService.Received(1).DeleteFileAsync("cv-public-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_CancellationRequested_RethrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<OneOf<CvAnalysisResult, Error>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")), cts.Token));

        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
    }

    // --- Helpers ---

    private static UploadCVRequest Request(IFormFile file, string? jobDescription = null) =>
        new(file, jobDescription);

    private static IFormFile TextFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private static CvAnalysisResult SuccessAnalysis() => new(
        Score: 85,
        Strengths: [new StrengthsDto { Icon = "star", Heading = "Strong C#", Description = "Deep .NET knowledge" }],
        Weaknesses: ["Limited cloud experience"],
        Suggestions: [new SuggestionsDto { Heading = "Add metrics", Description = "Quantify your impact" }],
        JobMatchPercentage: 90,
        TechnicalAlignment: 88,
        SoftSkillsFit: 80,
        DomainExperience: 75);

    private async Task SeedUserAsync()
    {
        _context.Users.Add(new ApplicationUser { Id = UserId, UserName = "john", Email = "john@example.com" });
        await _context.SaveChangesAsync();
    }

    private static UploadCvResponse ShouldBeSuccess(OneOf<UploadCvResponse, Error> result)
    {
        result.TryPickT0(out var response, out _).ShouldBeTrue(
            "expected a success result but got an error result");
        return response!;
    }

    private static Error ShouldBeError(OneOf<UploadCvResponse, Error> result, string code, string message)
    {
        result.TryPickT1(out var error, out _).ShouldBeTrue(
            "expected an error result but got a success result");
        error!.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        return error;
    }
}
