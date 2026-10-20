# CVAnalyzer API

A .NET backend service for recruiting and talent assessment that analyzes resumes (CVs) and generates structured insights for hiring teams.

## Overview

CVAnalyzer is an API built with ASP.NET Core that takes resumes, extracts their text, and uses a large language model to produce detailed analysis reports. The system supports user authentication, secure CV uploads, optional job-description matching, and shareable read-only analysis links.

## Key Features

- **Resume Analysis**: Upload CVs and get instant analysis of skills, strengths, weaknesses, and an overall score
- **Job Description Matching**: Optionally compare a CV against a job description to get a match percentage plus technical, soft-skill, and domain-fit scores
- **Structured Reporting**: Returns typed JSON with scores, strengths, weaknesses, and interview suggestions
- **Re-analysis**: Re-run analysis on a stored CV, with or without a new job description
- **Shareable Links**: Every CV gets a share token that exposes a public, read-only analysis endpoint
- **Secure Authentication**: JWT access tokens with rotating, hashed refresh tokens stored in an HttpOnly cookie
- **Password Reset**: Token-based reset flow delivered by email
- **Caching**: `HybridCache` for CV lists, analyses, and shared reports (in-process, no distributed cache configured)
- **Rate Limiting**: Per-IP/user sliding-window limits on login, upload, analysis, and public links
- **Resilience**: Polly retry policy with exponential backoff around the LLM calls
- **Observability**: Serilog console and rolling-file logging, plus request logging

## Tech Stack

- **Language**: C# (.NET 10)
- **Framework**: ASP.NET Core 10
- **Database**: PostgreSQL (via Entity Framework Core 10 / Npgsql)
- **Authentication**: ASP.NET Core Identity + JWT Bearer tokens
- **AI Integration**: Groq chat-completions API (a Gemini client exists in the codebase but is not currently registered in DI)
- **File Storage**: Cloudinary
- **PDF Text Extraction**: PdfPig
- **Email**: MailKit / MimeKit over SMTP
- **Caching**: HybridCache
- **Validation**: FluentValidation
- **Logging**: Serilog (console + rolling files)
- **API Docs**: OpenAPI + Scalar UI (development only)
- **Resilience**: Polly (`Microsoft.Extensions.Http.Polly`)
- **Testing**: xUnit, Shouldly, NSubstitute

## API Endpoints

All CV endpoints except the shared-analysis link require a valid JWT (`Authorization: Bearer <token>`).

### Authentication — `/api/auths`

- `POST /api/auths/register` — Register a new user account
- `POST /api/auths/login` — Authenticate and obtain an access token (refresh token is set as an HttpOnly cookie)
- `POST /api/auths/refresh-token` — Rotate an expired access token using the refresh token
- `POST /api/auths/forgot-password` — Request a password-reset email
- `POST /api/auths/reset-password` — Complete the reset using the emailed token

### CV Management — `/api/cvs`

- `POST /api/cvs/upload` — Upload a CV (`multipart/form-data`: `file` + optional `jobDescription`) and analyze it immediately
- `GET /api/cvs` — List the current user's CVs with their latest score
- `GET /api/cvs/{id}/analysis` — Retrieve the latest analysis for a CV
- `POST /api/cvs/{id}/reanalyze` — Re-run analysis; an empty body reuses the previous job description, and `{ "jobDescription": "..." }` overrides it
- `DELETE /api/cvs/{id}` — Delete a CV, its analyses, and its stored file
- `GET /api/cvs/share-analysis/{token}` — Anonymous, read-only access to an analysis by share token

Share tokens are generated automatically for each CV and returned as `shareToken` in analysis responses; there is no separate endpoint to create or revoke them.

## How It Works

1. **Upload**: The user uploads a PDF or TXT file through `POST /api/cvs/upload`
2. **Validation**: The file size, extension, and magic-byte signature are checked before processing
3. **Extraction**: Text is extracted with PdfPig (PDF) or read as UTF-8 (TXT), limited to the first 20 pages and 15,000 characters
4. **Analysis**: The text, plus an optional job description, is sent to the Groq API with a system prompt that casts the model as an "expert HR and Technical Recruiter" and requests a strict JSON schema
5. **Storage**: The file is uploaded to Cloudinary; the CV record and analysis are saved to PostgreSQL
6. **Cache Invalidation**: Cached CV lists and analyses for that user are cleared
7. **Retrieval**: Clients fetch the analysis by CV ID or via the share token

> **Note:** There is no OCR step. Scanned/image-only PDFs contain no extractable text and are rejected with a `400` response rather than being analyzed.

### Upload Limits

| Constraint | Value |
|---|---|
| Max file size | 10 MB |
| Allowed formats | `.pdf`, `.txt` |
| Max PDF pages processed | 20 |
| Max extracted characters sent to the LLM | 15,000 |
| PDF extraction timeout | 30 seconds |

## Getting Started

### Prerequisites

- .NET 10 SDK
- PostgreSQL
- A Cloudinary account (cloud name, API key, API secret)
- A Groq API key
- SMTP credentials for password-reset emails

### Installation

```bash
# Clone the repository
git clone https://github.com/fathyali11/CVAnalyzerAPI.git
cd CVAnalyzerAPI

# Restore, build, and run
dotnet restore CVAnalyzerAPI.slnx
dotnet build CVAnalyzerAPI.slnx
dotnet run --project CVAnalyzer.API
```

The API listens on the URLs configured in `CVAnalyzer.API/Properties/launchSettings.json`. In development, interactive API docs are available at `/scalar/v1` (backed by the OpenAPI document at `/openapi/v1.json`).

### Configuration

Settings live in `appsettings.json` and are bound to strongly typed options. `JwtSettings` uses data annotations and is validated at startup, so missing or invalid JWT values fail fast. Keep secrets out of source control — the API project has a `UserSecretsId`, so use user secrets locally:

```bash
cd CVAnalyzer.API
dotnet user-secrets set "JwtSettings:SecretKey" "<at-least-32-characters>"
dotnet user-secrets set "GroqSettings:ApiKey" "<key>"
dotnet user-secrets set "CloudinarySettings:ApiSecret" "<secret>"
dotnet user-secrets set "EmailSettings:Password" "<smtp-password>"
```

Required sections:

- **`ConnectionStrings:DefaultConnection`** — PostgreSQL connection string
- **`JwtSettings`** — `SecretKey` (min. 32 chars), `Issuer`, `Audience`, `ExpiryInMinutes`, `RefreshTokenExpiryInDays`, `MaxExpiredAccessTokenAgeInDays`
- **`GroqSettings`** — `Url`, `ApiKey`, `Model`
- **`CloudinarySettings`** — `CloudName`, `ApiKey`, `ApiSecret`
- **`EmailSettings`** — `Host`, `Port`, `Username`, `Password`, `SenderName`, `SenderEmail`
- **`GeminiSettings`** — `ApiKey`, `Url` (currently unused; the Gemini service is not registered)
- **`FrontendSettings:BaseUrl`** — base URL used to build password-reset links
- **`Serilog`** — log levels and sinks (console + rolling file under `../private/logs`)

Rate-limit values and the CORS allow-list are defined in code in `Extensions/DependencyInjection.cs`, not in `appsettings.json`.

### Database Setup

Apply migrations before the first run:

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update --project CVAnalyzer.API --startup-project CVAnalyzer.API
```

### Running Tests

```bash
dotnet test CVAnalyzerAPI.slnx
```

The test project covers auth services, CV services, token services, and auth validators.

## Project Structure

```
CVAnalyzerAPI/
├── CVAnalyzer.API/               # Web API project
│   ├── Consts/                   # Settings, error codes, cache keys, limits
│   ├── Controllers/              # REST endpoints (Auths, CVs)
│   ├── Data/                     # DbContext and entity configuration
│   ├── DTOs/                     # Request/response contracts
│   ├── Extensions/               # DI setup, error mapping, file validation
│   ├── Filters/                  # FluentValidation action filter
│   ├── Middlewares/              # Global exception handler
│   ├── Migrations/               # EF Core migrations
│   ├── Models/                   # Entities (CV, Analysis, ApplicationUser, RefreshToken)
│   ├── OpenApi/                  # Bearer security scheme for OpenAPI
│   ├── Services/                 # Auth, CV, analysis, email, file, token services
│   ├── Templates/                # HTML email templates
│   ├── Validators/               # FluentValidation validators
│   └── Program.cs
├── CVAnalyzerAPI.Tests/          # Unit tests (xUnit)
└── CVAnalyzerAPI.slnx            # Solution file
```

## CI/CD

The GitHub Actions workflow (`.github/workflows/ci-cd.yml`) runs on pushes and pull requests to `master`:

1. Restores, builds, and tests with coverage on .NET 10
2. Publishes the API and uploads the artifact
3. On pushes to `master`, deploys to MonsterASP.NET over FTP/FTPS/SFTP using repository secrets

## License

No license file is currently included in this repository. Add a `LICENSE` file (for example, MIT) before distributing or accepting external contributions under a specific license.

## Contributing

Contributions are welcome! Please:
- Fork the repository
- Create a feature branch
- Submit a pull request with clear descriptions
- Follow the existing code style and patterns

## Support

For questions about the API or development, feel free to reach out to me.

---

*CVAnalyzer – Intelligent CV Analysis Platform*
