# CVAnalyzer API

A .NET Core backend service for recruiting and talent assessment that analyzes resumes (CVs) and generates structured insights for hiring teams.

## Overview

CVAnalyzer is a modern API built with ASP.NET Core that takes resumes, extracts key information, and uses large language models to produce detailed analysis reports. The system supports user authentication, secure CV uploads, and automated analysis of candidate profiles.

## Key Features

- **Resume Analysis**: Upload CVs and get instant analysis of skills, strengths, weaknesses, and job match scores
- **Structured Reporting**: Generates JSON responses with scores, strengths, weaknesses, and recommendations
- **Secure Authentication**: JWT-based authentication with role-based access control
- **LLM-Powered Insights**: Integrates with Groq and Gemini APIs to analyze CV content and extract meaningful insights
- **Task Management**: Track CVs, analyses, and re-analyses with caching for performance
- **Shareable Links**: Generate shareable links for team collaboration

## Tech Stack

- **Language**: C# (.NET 8)
- **Framework**: ASP.NET Core 8.x
- **Database**: PostgreSQL (via Entity Framework Core)
- **Authentication**: JWT Bearer Tokens
- **AI Integration**: Groq and Gemini LLM APIs
- **Caching**: Hybrid Cache
- **Documentation**: Swagger/OpenAPI

## API Endpoints

### Authentication
- `POST /api/auth/register` — Register a new user account
- `POST /api/auth/login` — Authenticate and obtain access token
- `POST /api/auth/refresh-token` — Refresh expired access token
- `POST /api/auth/forgot-password` — Request password reset
- `POST /api/auth/login` — Log in and get authentication response

### CV Management
- `POST /api/cvs/upload` — Upload a CV file and trigger automatic analysis
- `GET /api/cvs/{id}/analysis` — Retrieve analysis for a specific CV
- `DELETE /api/cvs/{id}` — Delete a CV (requires authentication)

### Analysis Results
- `GET /api/cvs/{id}/analysis` — Get detailed analysis report (score, strengths, weaknesses, suggestions)
- `GET /api/cvs/{id}/reanalyze` — Re-run analysis on previously uploaded CV

## How It Works

1. **Upload**: A user uploads a CV file through the `/api/cvs/upload` endpoint
2. **Extraction**: The system extracts text from the PDF using OCR/parsing
3. **Analysis**: The extracted text is sent to an LLM (Groq or Gemini) with a system prompt instructing it to act as an "expert HR and Technical Recruiter"
4. **Storage**: The analysis result is saved to the database alongside the original CV
5. **Retrieval**: Clients can fetch the analysis using the CV ID

## Getting Started

### Prerequisites
- .NET 8 SDK installed
- PostgreSQL database
- JWT secret key (defined in `appsettings.json`)

### Installation

```bash
# Clone the repository
cd CVAnalyzer/backend/CVAnalyzerAPI

# Restore and build
dotnet restore
dotnet build

# Run the application
cd CVAnalyzer.API
dotnet run
```

### Configuration

The application uses several configuration sections in `appsettings.json`:
- **Database connection** (DefaultConnection) – connects to PostgreSQL
- **JWT settings** – issuer, audience, secret key for token validation
- **Rate limits** – per-endpoint throttling (login, upload, analyze)
- **LLM settings** – API keys and endpoints for Groq and Gemini

### Running Tests

```bash
# Run unit tests
dotnet test CVAnalyzerAPI.Tests/
```

## Project Structure

```
CVAnalyzerAPI/
├── CVAnalyzer.API/        # Web API project
│   ├── Controllers/       # REST endpoints
│   ├── Services/          # Business logic (Auth, CV, Analysis)
│   ├── Models/            # Database entities (CV, Analysis, etc.)
│   ├── DTOs/              # Data transfer objects
│   ├── Migrations/        # Database schema migrations
│   └── ...
└── CVAnalyzerAPI.Tests/   # Unit tests (xUnit)
```

## License

This project is licensed under the MIT License.

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
