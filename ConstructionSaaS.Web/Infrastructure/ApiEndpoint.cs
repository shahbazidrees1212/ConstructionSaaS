namespace ConstructionSaaS.Web.Infrastructure;

public static class ApiEndpoint
{
    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration["Api:BaseUrl"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http") ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException("Set Api:BaseUrl to your API origin and /api path.");
        if (uri.Scheme != "https" && !uri.IsLoopback)
            throw new InvalidOperationException("Non-local API connections must use HTTPS.");
        return uri.AbsoluteUri.TrimEnd('/');
    }
}
