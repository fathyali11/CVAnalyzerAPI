namespace CVAnalyzerAPI.Consts;

public static class FileConstraints
{
    public const long MaxUploadFileSizeBytes = 10 * 1024 * 1024;

    public const int MaxCvTextLength = 15_000;

    public const int MaxPdfPages = 20;

    public const int PdfExtractionTimeoutSeconds = 30;
}
