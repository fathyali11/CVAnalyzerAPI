namespace CVAnalyzerAPI.Services.FileServices;

public class FileServiceException(string message, Exception? innerException = null)
    : Exception(message, innerException);
