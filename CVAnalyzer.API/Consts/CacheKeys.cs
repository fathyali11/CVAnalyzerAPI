namespace CVAnalyzerAPI.Consts;

public static class CacheKeys
{
    public static string UserCvs(string userId) => $"user_cvs_{userId}";
    public static string CvAnalysis(int cvId, string userId) => $"cv_analysis_{cvId}_user_{userId}";
    public static string CvReAnalysis(int cvId) => $"cv_analysis_{cvId}";
    public static string SharedCv(Guid token) => $"shared_cv_{token}";
}
