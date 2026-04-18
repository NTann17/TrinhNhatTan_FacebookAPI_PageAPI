namespace FacebookPageApi.Configuration;

public class FacebookOptions
{
    public const string SectionName = "Facebook";

    public string GraphApiBaseUrl { get; set; } = "https://graph.facebook.com";

    public string GraphApiVersion { get; set; } = "v22.0";

    public string PageAccessToken { get; set; } = string.Empty;

    public string UserAccessToken { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    public List<string> InsightsMetrics { get; set; } =
    [
        "page_impressions",
        "page_engaged_users"
    ];
}
