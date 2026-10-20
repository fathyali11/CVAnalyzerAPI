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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OneOf;
using Shouldly;

namespace CVAnalyzerAPI.Tests.Services.CVServices;

public class CVServiceTests : IDisposable
{
    private const string UserId = "user-1";
    private const string OtherUserId = "user-2";
    private const string InternalServerErrorMessage = "An error occurred while processing your request. Please try again.";

    private readonly IFileService _fileService;
    private readonly IAnalyzeService _analyzeService;
    private readonly IAuthService _authService;
    private readonly ServiceProvider _provider;
    private readonly HybridCache _cache;
    private readonly SqliteConnection _database;
    private readonly ApplicationDbContext _context;
    private readonly CVService _sut;

    public CVServiceTests()
    {
        _fileService = Substitute.For<IFileService>();
        _analyzeService = Substitute.For<IAnalyzeService>();
        _authService = Substitute.For<IAuthService>();

        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<HybridCache>();

        _database = new SqliteConnection("DataSource=:memory:");
        _database.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_database)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();

        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns(UserId);

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
        _provider.Dispose();
        _context.Dispose();
        _database.Dispose();
    }

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
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_NewCv_InvalidatesUserCvsCache()
    {
        await SeedUserAsync();

        ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None)).ShouldBeEmpty();

        ShouldBeSuccess(await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe"))));

        ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None))
            .ShouldHaveSingleItem()
            .FileName.ShouldBe("resume.txt");
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
        var result = await _sut.UploadAndAnalysisCVAsync(
            Request(TextFile("resume.pdf", "definitely not a real pdf")));

        ShouldBeError(result, ErrorCodes.InternalServerError, InternalServerErrorMessage);
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

        ShouldBeError(result, ErrorCodes.InternalServerError, InternalServerErrorMessage);
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
        (await _context.CVs.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task UploadAndAnalysisCVAsync_SaveFailsAfterUpload_DeletesUploadedFileAndReturnsInternalServerError()
    {
        var result = await _sut.UploadAndAnalysisCVAsync(Request(TextFile("resume.txt", "John Doe")));

        ShouldBeError(result, ErrorCodes.InternalServerError, InternalServerErrorMessage);
        await _fileService.Received(1).DeleteFileAsync("cv-public-id", Arg.Any<CancellationToken>());
        (await _context.CVs.CountAsync()).ShouldBe(0);
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

    [Fact]
    public async Task GetCVsAsync_Unauthenticated_ReturnsUnauthorizedError()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.GetCVsAsync(CancellationToken.None);

        ShouldBeError(result, ErrorCodes.UnAuthorized, "User must be authenticated to retrieve CVs");
    }

    [Fact]
    public async Task GetCVsAsync_NoCvs_ReturnsEmptyList()
    {
        await SeedUserAsync();

        var result = await _sut.GetCVsAsync(CancellationToken.None);

        ShouldBeSuccess(result).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCVsAsync_ReturnsOnlyCurrentUsersCvsWithLatestAnalysisScore()
    {
        await SeedUserAsync(UserId, "john");
        await SeedUserAsync(OtherUserId, "jane");

        var withAnalyses = await SeedCvAsync(UserId, "resume.txt");
        await SeedAnalysisAsync(withAnalyses, score: 60);
        await SeedAnalysisAsync(withAnalyses, score: 92);

        var withoutAnalyses = await SeedCvAsync(UserId, "cover-letter.txt");
        await SeedCvAsync(OtherUserId, "other.txt");

        var result = await _sut.GetCVsAsync(CancellationToken.None);

        var cvs = ShouldBeSuccess(result);
        cvs.Count.ShouldBe(2);

        var scored = cvs.Single(x => x.Id == withAnalyses.Id);
        scored.FileName.ShouldBe("resume.txt");
        scored.Url.ShouldBe("https://cdn.example.com/cv.txt");
        scored.Score.ShouldBe(92);

        cvs.Single(x => x.Id == withoutAnalyses.Id).Score.ShouldBe(0);
    }

    [Fact]
    public async Task GetCVsAsync_PopulatesCache_SubsequentCallsServedFromCache()
    {
        await SeedUserAsync();
        await SeedCvAsync(UserId, "resume.txt");

        var first = ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None));
        first.Count.ShouldBe(1);

        await SeedCvAsync(UserId, "second.txt");

        var second = ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None));
        second.Count.ShouldBe(1);
        second.Single().FileName.ShouldBe("resume.txt");
    }

    [Fact]
    public async Task GetCVAnalysisAsync_Unauthenticated_ReturnsUnauthorizedError()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.GetCVAnalysisAsync(1, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.UnAuthorized, "User must be authenticated to retrieve CV analysis");
    }

    [Fact]
    public async Task GetCVAnalysisAsync_NoAnalysisFound_ReturnsNotFoundError()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");

        var result = await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "No analysis found for the specified CV");
    }

    [Fact]
    public async Task GetCVAnalysisAsync_CvOwnedByAnotherUser_ReturnsNotFoundError()
    {
        await SeedUserAsync(UserId, "john");
        await SeedUserAsync(OtherUserId, "jane");
        var otherCv = await SeedCvAsync(OtherUserId, "other.txt");
        await SeedAnalysisAsync(otherCv, score: 80);

        var result = await _sut.GetCVAnalysisAsync(otherCv.Id, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "No analysis found for the specified CV");
    }

    [Fact]
    public async Task GetCVAnalysisAsync_ReturnsLatestAnalysisMappedToResponse()
    {
        await SeedUserAsync(UserId, "john");
        var cv = await SeedCvAsync(UserId, "resume.txt");
        await SeedAnalysisAsync(cv, score: 50);
        var latest = await SeedAnalysisAsync(cv, score: 88, jobMatchPercentage: 91,
            technicalAlignment: 84, softSkillsFit: 79, domainExperience: 73, jobDescription: "Backend JD");

        var result = await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None);

        var response = ShouldBeSuccess(result);
        response.Id.ShouldBe(latest.Id);
        response.Score.ShouldBe(88);
        response.ShareToken.ShouldBe(cv.ShareToken.ToString());
        response.UserName.ShouldBe("john");
        response.JobMatchPercentage.ShouldBe(91);
        response.TechnicalAlignment.ShouldBe(84);
        response.SoftSkillsFit.ShouldBe(79);
        response.DomainExperience.ShouldBe(73);
        response.JobDescription.ShouldBe("Backend JD");
        response.Strengths.ShouldHaveSingleItem().Heading.ShouldBe("Strong C#");
        response.Weaknesses.ShouldHaveSingleItem().ShouldBe("Limited cloud experience");
        response.Suggestions.ShouldHaveSingleItem().Heading.ShouldBe("Add metrics");
    }

    [Fact]
    public async Task GetCVAnalysisAsync_PopulatesCache_SubsequentCallsServedFromCache()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");
        var original = await SeedAnalysisAsync(cv, score: 70);

        var first = ShouldBeSuccess(await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None));
        first.Id.ShouldBe(original.Id);

        await SeedAnalysisAsync(cv, score: 99);

        var second = ShouldBeSuccess(await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None));
        second.Id.ShouldBe(original.Id);
        second.Score.ShouldBe(70);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_Unauthenticated_ReturnsUnauthorizedError()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.AnalyzeExtractedCVAsync(1, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.UnAuthorized, "User must be authenticated to analyze CV");
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_CvNotFound_ReturnsNotFoundError()
    {
        await SeedUserAsync();

        var result = await _sut.AnalyzeExtractedCVAsync(999, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "CV not found");
        await _analyzeService.DidNotReceiveWithAnyArgs().AnalyzeCVAsync(default!, default, default);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_CvOwnedByAnotherUser_ReturnsNotFoundError()
    {
        await SeedUserAsync(UserId, "john");
        await SeedUserAsync(OtherUserId, "jane");
        var otherCv = await SeedCvAsync(OtherUserId, "other.txt");

        var result = await _sut.AnalyzeExtractedCVAsync(otherCv.Id, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "CV not found");
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_NoExtractedText_ReturnsBadRequestError()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, extractedText: "");

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.BadRequest, "No extracted text is available for this CV.");
        await _analyzeService.DidNotReceiveWithAnyArgs().AnalyzeCVAsync(default!, default, default);
        (await _context.Analyses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_AnalyzeServiceFails_ReturnsAnalyzeError()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(OneOf<CvAnalysisResult, Error>.FromT1(
                new Error(ErrorCodes.BadRequest, "The AI provider rejected the request")));

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.BadRequest, "The AI provider rejected the request");
        (await _context.Analyses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_WhenAnalyzerThrows_ReturnsInternalServerError()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");
        _analyzeService.When(a => a.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Provider down"));

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.InternalServerError, InternalServerErrorMessage);
        (await _context.Analyses.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_ExtractedTextExceedsLimit_TruncatesBeforeAnalyzing()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, extractedText: new string('a', FileConstraints.MaxCvTextLength + 1_000));

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        ShouldBeSuccess(result);
        await _analyzeService.Received(1).AnalyzeCVAsync(
            Arg.Is<string>(t => t.Length == FileConstraints.MaxCvTextLength),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(150, 100)]
    public async Task AnalyzeExtractedCVAsync_OutOfRangeScore_IsClampedWhenPersistingAndReturning(int rawScore, int expected)
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessAnalysis(score: rawScore));

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        ShouldBeSuccess(result).Score.ShouldBe(expected);
        var persisted = await _context.Analyses.AsNoTracking().SingleAsync();
        persisted.Score.ShouldBe(expected);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_ValidCv_SavesNewAnalysisAndReturnsMappedResponse()
    {
        await SeedUserAsync(UserId, "john");
        var cv = await SeedCvAsync(UserId, "resume.txt", extractedText: "John Doe\nC#");
        var priorAnalysis = await SeedAnalysisAsync(cv, score: 60, jobDescription: "Prior JD");

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None);

        var response = ShouldBeSuccess(result);
        response.Id.ShouldBeGreaterThan(priorAnalysis.Id);
        response.Score.ShouldBe(85);
        response.ShareToken.ShouldBe(cv.ShareToken.ToString());
        response.UserName.ShouldBe("john");
        response.Strengths.ShouldHaveSingleItem().Heading.ShouldBe("Strong C#");
        response.Weaknesses.ShouldHaveSingleItem().ShouldBe("Limited cloud experience");
        response.Suggestions.ShouldHaveSingleItem().Heading.ShouldBe("Add metrics");
        response.JobDescription.ShouldBe("Prior JD");

        await _analyzeService.Received(1).AnalyzeCVAsync("John Doe\nC#", "Prior JD", Arg.Any<CancellationToken>());

        var newAnalysis = await _context.Analyses.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync();
        newAnalysis.Id.ShouldBe(response.Id);
        newAnalysis.CVId.ShouldBe(cv.Id);
        newAnalysis.Score.ShouldBe(85);
        newAnalysis.JobDescription.ShouldBe("Prior JD");
        (await _context.Analyses.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_ProvidedJobDescription_OverridesStoredJobDescription()
    {
        await SeedUserAsync(UserId, "john");
        var cv = await SeedCvAsync(UserId, "resume.txt", extractedText: "John Doe\nC#");
        await SeedAnalysisAsync(cv, score: 60, jobDescription: "Stored JD");

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, "  New JD  ", CancellationToken.None);

        var response = ShouldBeSuccess(result);
        response.JobDescription.ShouldBe("New JD");
        await _analyzeService.Received(1).AnalyzeCVAsync("John Doe\nC#", "New JD", Arg.Any<CancellationToken>());

        var newAnalysis = await _context.Analyses.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync();
        newAnalysis.JobDescription.ShouldBe("New JD");
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_BlankJobDescription_ClearsStoredJobDescription()
    {
        await SeedUserAsync(UserId, "john");
        var cv = await SeedCvAsync(UserId, "resume.txt", extractedText: "John Doe\nC#");
        await SeedAnalysisAsync(cv, score: 60, jobDescription: "Stored JD");

        var result = await _sut.AnalyzeExtractedCVAsync(cv.Id, "   ", CancellationToken.None);

        var response = ShouldBeSuccess(result);
        response.JobDescription.ShouldBeNull();
        await _analyzeService.Received(1).AnalyzeCVAsync("John Doe\nC#", null, Arg.Any<CancellationToken>());

        var newAnalysis = await _context.Analyses.AsNoTracking().OrderByDescending(a => a.Id).FirstAsync();
        newAnalysis.JobDescription.ShouldBeNull();
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_ReAnalysis_InvalidatesAnalysisUserCvsAndShareTokenCaches()
    {
        await SeedUserAsync(UserId, "john");
        var token = Guid.NewGuid();
        var cv = await SeedCvAsync(UserId, "resume.txt", shareToken: token);
        var original = await SeedAnalysisAsync(cv, score: 60);

        ShouldBeSuccess(await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None)).Id.ShouldBe(original.Id);
        ShouldBeSuccess(await _sut.GetByShareTokenAsync(token)).Id.ShouldBe(original.Id);
        ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None)).Single().Score.ShouldBe(60);

        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(SuccessAnalysis(score: 92));

        var reanalysis = ShouldBeSuccess(await _sut.AnalyzeExtractedCVAsync(cv.Id, null, CancellationToken.None));
        reanalysis.Score.ShouldBe(92);

        ShouldBeSuccess(await _sut.GetCVAnalysisAsync(cv.Id, CancellationToken.None)).Id.ShouldBe(reanalysis.Id);
        ShouldBeSuccess(await _sut.GetByShareTokenAsync(token)).Score.ShouldBe(92);
        ShouldBeSuccess(await _sut.GetCVsAsync(CancellationToken.None)).Single().Score.ShouldBe(92);
    }

    [Fact]
    public async Task AnalyzeExtractedCVAsync_CancellationRequested_RethrowsOperationCanceled()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _analyzeService.AnalyzeCVAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<OneOf<CvAnalysisResult, Error>>(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _sut.AnalyzeExtractedCVAsync(cv.Id, null, cts.Token));
    }

    [Fact]
    public async Task DeleteCvAsync_Unauthenticated_ReturnsUnauthorizedError()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await _sut.DeleteCvAsync(1, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.UnAuthorized, "User must be authenticated to delete CV");
    }

    [Fact]
    public async Task DeleteCvAsync_CvNotFound_ReturnsNotFoundError()
    {
        await SeedUserAsync();

        var result = await _sut.DeleteCvAsync(999, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "CV not found");
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
    }

    [Fact]
    public async Task DeleteCvAsync_CvOwnedByAnotherUser_ReturnsNotFoundErrorAndKeepsCv()
    {
        await SeedUserAsync(UserId, "john");
        await SeedUserAsync(OtherUserId, "jane");
        var otherCv = await SeedCvAsync(OtherUserId, "other.txt");

        var result = await _sut.DeleteCvAsync(otherCv.Id, CancellationToken.None);

        ShouldBeError(result, ErrorCodes.NotFound, "CV not found");
        (await _context.CVs.CountAsync()).ShouldBe(1);
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
    }

    [Fact]
    public async Task DeleteCvAsync_ExistingCv_RemovesCvAndDeletesStoredFile()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt", filePublicId: "cv-public-id");

        var result = await _sut.DeleteCvAsync(cv.Id, CancellationToken.None);

        ShouldBeNoError(result);
        result.Message.ShouldBe("CV deleted successfully");
        (await _context.CVs.CountAsync()).ShouldBe(0);
        await _fileService.Received(1).DeleteFileAsync("cv-public-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteCvAsync_CvWithoutStoredFile_DoesNotCallFileService()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt", filePublicId: null);

        var result = await _sut.DeleteCvAsync(cv.Id, CancellationToken.None);

        ShouldBeNoError(result);
        (await _context.CVs.CountAsync()).ShouldBe(0);
        await _fileService.DidNotReceiveWithAnyArgs().DeleteFileAsync(default!, default);
    }

    [Fact]
    public async Task DeleteCvAsync_FileDeletionFails_StillReportsSuccess()
    {
        await SeedUserAsync();
        var cv = await SeedCvAsync(UserId, "resume.txt", filePublicId: "cv-public-id");
        _fileService.When(f => f.DeleteFileAsync("cv-public-id", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Storage unavailable"));

        var result = await _sut.DeleteCvAsync(cv.Id, CancellationToken.None);

        ShouldBeNoError(result);
        (await _context.CVs.CountAsync()).ShouldBe(0);
        await _fileService.Received(1).DeleteFileAsync("cv-public-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetByShareTokenAsync_UnknownToken_ReturnsNotFoundError()
    {
        await SeedUserAsync();

        var result = await _sut.GetByShareTokenAsync(Guid.NewGuid());

        ShouldBeError(result, ErrorCodes.NotFound, "No analysis found for the specified share token");
    }

    [Fact]
    public async Task GetByShareTokenAsync_DoesNotRequireAuthentication()
    {
        _authService.GetCurrentUserIdAsync(Arg.Any<CancellationToken>()).Returns((string?)null);
        await SeedUserAsync(UserId, "john");
        var token = Guid.NewGuid();
        var cv = await SeedCvAsync(UserId, "resume.txt", shareToken: token);
        await SeedAnalysisAsync(cv, score: 77);

        var result = await _sut.GetByShareTokenAsync(token);

        ShouldBeSuccess(result).Score.ShouldBe(77);
    }

    [Fact]
    public async Task GetByShareTokenAsync_ReturnsLatestAnalysisMappedToResponse()
    {
        await SeedUserAsync(UserId, "john");
        var token = Guid.NewGuid();
        var cv = await SeedCvAsync(UserId, "resume.txt", shareToken: token);
        await SeedAnalysisAsync(cv, score: 40);
        var latest = await SeedAnalysisAsync(cv, score: 90, jobMatchPercentage: 88,
            technicalAlignment: 86, softSkillsFit: 81, domainExperience: 74);

        var result = await _sut.GetByShareTokenAsync(token);

        var response = ShouldBeSuccess(result);
        response.Id.ShouldBe(latest.Id);
        response.Score.ShouldBe(90);
        response.ShareToken.ShouldBe(token.ToString());
        response.UserName.ShouldBe("john");
        response.JobMatchPercentage.ShouldBe(88);
        response.TechnicalAlignment.ShouldBe(86);
        response.SoftSkillsFit.ShouldBe(81);
        response.DomainExperience.ShouldBe(74);
        response.Strengths.ShouldHaveSingleItem().Heading.ShouldBe("Strong C#");
        response.Weaknesses.ShouldHaveSingleItem().ShouldBe("Limited cloud experience");
        response.Suggestions.ShouldHaveSingleItem().Heading.ShouldBe("Add metrics");
    }

    [Fact]
    public async Task GetByShareTokenAsync_PopulatesCache_SubsequentCallsServedFromCache()
    {
        await SeedUserAsync();
        var token = Guid.NewGuid();
        var cv = await SeedCvAsync(UserId, "resume.txt", shareToken: token);
        var original = await SeedAnalysisAsync(cv, score: 70);

        var first = ShouldBeSuccess(await _sut.GetByShareTokenAsync(token));
        first.Id.ShouldBe(original.Id);

        await SeedAnalysisAsync(cv, score: 99);

        var second = ShouldBeSuccess(await _sut.GetByShareTokenAsync(token));
        second.Id.ShouldBe(original.Id);
        second.Score.ShouldBe(70);
    }

    private static UploadCVRequest Request(IFormFile file, string? jobDescription = null) =>
        new(file, jobDescription);

    private static IFormFile TextFile(string fileName, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName);
    }

    private static CvAnalysisResult SuccessAnalysis(int score = 85) => new(
        Score: score,
        Strengths: [new StrengthsDto { Icon = "star", Heading = "Strong C#", Description = "Deep .NET knowledge" }],
        Weaknesses: ["Limited cloud experience"],
        Suggestions: [new SuggestionsDto { Heading = "Add metrics", Description = "Quantify your impact" }],
        JobMatchPercentage: 90,
        TechnicalAlignment: 88,
        SoftSkillsFit: 80,
        DomainExperience: 75,
        JobDescription: null);

    private async Task<ApplicationUser> SeedUserAsync(string id = UserId, string userName = "john")
    {
        var user = new ApplicationUser { Id = id, UserName = userName, Email = $"{id}@example.com" };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    private async Task<CV> SeedCvAsync(
        string userId = UserId,
        string fileName = "resume.txt",
        string filePath = "https://cdn.example.com/cv.txt",
        string? filePublicId = "cv-public-id",
        string extractedText = "John Doe\nSoftware Engineer",
        Guid? shareToken = null)
    {
        var cv = new CV
        {
            UserId = userId,
            FileName = fileName,
            FilePath = filePath,
            FilePublicId = filePublicId,
            ExtractedText = extractedText,
            UploadedAt = DateTime.UtcNow,
            ShareToken = shareToken ?? Guid.NewGuid(),
        };
        _context.CVs.Add(cv);
        await _context.SaveChangesAsync();
        return cv;
    }

    private async Task<Analysis> SeedAnalysisAsync(
        CV cv,
        int score = 80,
        string? jobDescription = null,
        int? jobMatchPercentage = 70,
        int technicalAlignment = 75,
        int softSkillsFit = 65,
        int domainExperience = 60)
    {
        var analysis = new Analysis
        {
            CVId = cv.Id,
            CV = cv,
            Score = score,
            JobDescription = jobDescription,
            Strengths =
            [
                new AnalysisStrength { Icon = "star", Heading = "Strong C#", Description = "Deep .NET knowledge" }
            ],
            Weaknesses = ["Limited cloud experience"],
            Suggestions = [new AnalysisSuggestion { Heading = "Add metrics", Description = "Quantify your impact" }],
            JobMatchPercentage = jobMatchPercentage,
            TechnicalAlignment = technicalAlignment,
            SoftSkillsFit = softSkillsFit,
            DomainExperience = domainExperience,
        };
        _context.Analyses.Add(analysis);
        await _context.SaveChangesAsync();
        return analysis;
    }

    private static T ShouldBeSuccess<T>(OneOf<T, Error> result)
    {
        result.TryPickT0(out var value, out _).ShouldBeTrue(
            "expected a success result but got an error result");
        return value!;
    }

    private static Error ShouldBeError<T>(OneOf<T, Error> result, string code, string message)
    {
        result.TryPickT1(out var error, out _).ShouldBeTrue(
            "expected an error result but got a success result");
        error!.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        return error;
    }

    private static Error ShouldBeError(Error error, string code, string message)
    {
        error.Code.ShouldBe(code);
        error.Message.ShouldBe(message);
        return error;
    }

    private static Error ShouldBeNoError(Error result)
    {
        result.Code.ShouldBe(
            ErrorCodes.None,
            $"expected success but got error {result.Code}: {result.Message}");
        return result;
    }
}
