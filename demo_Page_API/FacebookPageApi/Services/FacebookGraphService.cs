using System.Net.Http.Headers;
using System.Text.Json;
using FacebookPageApi.Configuration;
using Microsoft.Extensions.Options;

namespace FacebookPageApi.Services;

public class FacebookGraphService : IFacebookGraphService
{
    private readonly HttpClient _httpClient;
    private readonly FacebookOptions _options;

    public FacebookGraphService(HttpClient httpClient, IOptions<FacebookOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<string> GetPageAsync(string pageId, CancellationToken cancellationToken = default)
        => SendGetAsync($"/{GetBasePath()}/{pageId}?fields=id,name,fan_count,followers_count", cancellationToken);

    public Task<string> GetPostsAsync(string pageId, CancellationToken cancellationToken = default)
        => SendGetAsync($"/{GetBasePath()}/{pageId}/posts?fields=id,message,created_time,permalink_url", cancellationToken);

    public Task<string> CreatePostAsync(string pageId, string message, CancellationToken cancellationToken = default)
    {
        EnsurePostingIsConfigured();

        var body = new Dictionary<string, string>
        {
            ["message"] = message
        };

        return SendPostWithFallbackAsync(pageId, $"/{GetBasePath()}/{pageId}/feed", body, cancellationToken);
    }

    public Task<string> DeletePostAsync(string postId, CancellationToken cancellationToken = default)
        => SendDeleteAsync($"/{GetBasePath()}/{postId}", cancellationToken);

    public Task<string> GetCommentsAsync(string postId, CancellationToken cancellationToken = default)
        => SendGetAsync($"/{GetBasePath()}/{postId}/comments?fields=id,from,message,created_time", cancellationToken);

    public Task<string> GetLikesAsync(string postId, CancellationToken cancellationToken = default)
        => SendGetAsync($"/{GetBasePath()}/{postId}/likes?summary=true", cancellationToken);

    public async Task<string> GetInsightsAsync(string pageId, CancellationToken cancellationToken = default)
    {
        HttpRequestException? lastInsightsError = null;

        foreach (var metrics in BuildInsightsMetricCandidates())
        {
            var encodedMetrics = Uri.EscapeDataString(metrics);
            var path = $"/{GetBasePath()}/{pageId}/insights?metric={encodedMetrics}";

            try
            {
                return await SendGetAsync(path, cancellationToken);
            }
            catch (HttpRequestException ex) when (IsInvalidInsightsMetricError(ex.Message))
            {
                lastInsightsError = ex;
            }
        }

        if (lastInsightsError is not null)
        {
            return await BuildFallbackInsightsResponseAsync(pageId, lastInsightsError.Message, cancellationToken);
        }

        throw new HttpRequestException("Khong lay duoc du lieu insights.");
    }

    private string GetBasePath() => _options.GraphApiVersion.Trim('/');

    private string BuildUriWithToken(string relativePath, string accessToken)
    {
        var separator = relativePath.Contains('?') ? "&" : "?";
        return $"{_options.GraphApiBaseUrl.TrimEnd('/')}{relativePath}{separator}access_token={Uri.EscapeDataString(accessToken)}";
    }

    private async Task<string> SendGetAsync(string relativePath, CancellationToken cancellationToken)
    {
        var requestUri = BuildUriWithToken(relativePath, GetConfiguredPageAccessToken());
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        return await HandleResponse(response, GetConfiguredPageAccessToken(), cancellationToken);
    }

    private async Task<string> SendPostAsync(string relativePath, Dictionary<string, string> body, CancellationToken cancellationToken)
    {
        var accessToken = GetConfiguredPageAccessToken();

        var requestUri = $"{_options.GraphApiBaseUrl.TrimEnd('/')}{relativePath}";
        body["access_token"] = accessToken;

        using var content = new FormUrlEncodedContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
        return await HandleResponse(response, accessToken, cancellationToken);
    }

    private async Task<string> SendPostWithFallbackAsync(string pageId, string relativePath, Dictionary<string, string> body, CancellationToken cancellationToken)
    {
        try
        {
            return await SendPostAsync(relativePath, body, cancellationToken);
        }
        catch (HttpRequestException ex) when (IsFacebookPermissionError(ex.Message) && HasConfiguredToken(_options.UserAccessToken))
        {
            var pageAccessToken = await ResolvePageAccessTokenFromUserTokenAsync(pageId, cancellationToken);

            var retryBody = new Dictionary<string, string>(body)
            {
                ["access_token"] = pageAccessToken
            };

            var requestUri = $"{_options.GraphApiBaseUrl.TrimEnd('/')}{relativePath}";
            using var content = new FormUrlEncodedContent(retryBody);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            using var response = await _httpClient.PostAsync(requestUri, content, cancellationToken);
            return await HandleResponse(response, pageAccessToken, cancellationToken);
        }
    }

    private async Task<string> SendDeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var accessToken = GetConfiguredPageAccessToken();
        var requestUri = BuildUriWithToken(relativePath, accessToken);
        using var response = await _httpClient.DeleteAsync(requestUri, cancellationToken);
        return await HandleResponse(response, accessToken, cancellationToken);
    }

    private async Task<string> HandleResponse(HttpResponseMessage response, string accessToken, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        var statusCode = (int)response.StatusCode;
        if (statusCode == StatusCodes.Status403Forbidden && IsFacebookPermissionError(payload))
        {
            var diagnostics = await TryBuildTokenDiagnosticsAsync(accessToken, cancellationToken);
            throw new HttpRequestException($"Facebook Graph API rejected the request because the token does not have the required permissions. Make sure the token belongs to an admin of the Page and has pages_read_engagement and pages_manage_posts. {diagnostics} Raw response: {payload}");
        }

        throw new HttpRequestException($"Facebook Graph API returned {statusCode}: {payload}");
    }

    private string GetConfiguredPageAccessToken()
    {
        if (HasConfiguredToken(_options.PageAccessToken))
        {
            return _options.PageAccessToken;
        }

        throw new InvalidOperationException("Facebook.PageAccessToken is missing. Please update appsettings.json with a valid Page Access Token or provide Facebook.UserAccessToken so the API can resolve a Page token.");
    }

    private void EnsurePostingIsConfigured()
    {
        if (HasConfiguredToken(_options.PageAccessToken))
        {
            return;
        }

        if (HasConfiguredToken(_options.UserAccessToken))
        {
            return;
        }

        throw new InvalidOperationException("Facebook post is not configured. Set Facebook.PageAccessToken to a real Page token, or set Facebook.UserAccessToken with pages_show_list so the API can resolve the Page token before posting.");
    }

    private async Task<string> ResolvePageAccessTokenFromUserTokenAsync(string pageId, CancellationToken cancellationToken)
    {
        if (!HasConfiguredToken(_options.UserAccessToken))
        {
            throw new InvalidOperationException("Facebook.UserAccessToken is missing. Provide a valid User Access Token with pages_show_list permission so the API can resolve a Page token.");
        }

        var requestUri = BuildUriWithToken("/me/accounts?fields=id,name,access_token", _options.UserAccessToken);
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        var payload = await HandleResponse(response, _options.UserAccessToken, cancellationToken);

        using var document = JsonDocument.Parse(payload);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Facebook returned an unexpected response while resolving the Page token.");
        }

        foreach (var page in data.EnumerateArray())
        {
            if (!page.TryGetProperty("id", out var idElement) || !string.Equals(idElement.GetString(), pageId, StringComparison.Ordinal))
            {
                continue;
            }

            if (page.TryGetProperty("access_token", out var tokenElement))
            {
                var accessToken = tokenElement.GetString();
                if (HasConfiguredToken(accessToken))
                {
                    return accessToken!;
                }
            }
        }

        throw new InvalidOperationException($"Facebook.UserAccessToken is valid, but no Page access token was returned for page '{pageId}'. Make sure the user is an admin of that Page and has granted pages_show_list, pages_read_engagement, and pages_manage_posts.");
    }

    private static bool HasConfiguredToken(string? accessToken)
        => !string.IsNullOrWhiteSpace(accessToken) && accessToken != "YOUR_PAGE_ACCESS_TOKEN" && accessToken != "YOUR_USER_ACCESS_TOKEN";

    private static bool IsFacebookPermissionError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var codeElement) && codeElement.GetInt32() == 200)
                {
                    return true;
                }

                if (error.TryGetProperty("type", out var typeElement) && string.Equals(typeElement.GetString(), "OAuthException", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
        }

        return payload.Contains("(#200)", StringComparison.OrdinalIgnoreCase) || payload.Contains("OAuthException", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInvalidInsightsMetricError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("code", out var codeElement) && codeElement.GetInt32() == 100)
                {
                    return true;
                }

                if (error.TryGetProperty("message", out var messageElement))
                {
                    var message = messageElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message) && message.Contains("valid insights metric", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return payload.Contains("valid insights metric", StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> BuildInsightsMetricCandidates()
    {
        var configured = (_options.InsightsMetrics ?? [])
            .Where(metric => !string.IsNullOrWhiteSpace(metric))
            .Select(metric => metric.Trim())
            .ToArray();

        if (configured.Length > 0)
        {
            yield return string.Join(',', configured);
        }

        yield return "page_impressions,page_engaged_users";
        yield return "page_impressions_unique,page_engaged_users";
    }

    private async Task<string> BuildFallbackInsightsResponseAsync(string pageId, string metricErrorDetails, CancellationToken cancellationToken)
    {
        try
        {
            var pageSummaryPayload = await SendGetAsync(
                $"/{GetBasePath()}/{pageId}?fields=id,name,fan_count,followers_count",
                cancellationToken);

            using var pageSummaryJson = JsonDocument.Parse(pageSummaryPayload);

            var fallbackResponse = new
            {
                warning = "Khong lay duoc insights metrics hop le. API da tra ve thong tin tong quan cua Page.",
                details = metricErrorDetails,
                source = "fallback-page-summary",
                page = pageSummaryJson.RootElement.Clone()
            };

            return JsonSerializer.Serialize(fallbackResponse);
        }
        catch (HttpRequestException)
        {
            throw new HttpRequestException(
                "Khong lay duoc du lieu insights va cung khong lay duoc du lieu tong quan Page de fallback. " +
                "Hay kiem tra lai token va Facebook.InsightsMetrics trong appsettings.json.");
        }
    }

    private async Task<string> TryBuildTokenDiagnosticsAsync(string accessToken, CancellationToken cancellationToken)
    {
        if (!HasConfiguredToken(_options.AppId) || !HasConfiguredToken(_options.AppSecret))
        {
            return string.Empty;
        }

        var appAccessToken = $"{_options.AppId}|{_options.AppSecret}";
        var requestUri = $"{_options.GraphApiBaseUrl.TrimEnd('/')}/debug_token?input_token={Uri.EscapeDataString(accessToken)}&access_token={Uri.EscapeDataString(appAccessToken)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("data", out var data))
            {
                return string.Empty;
            }

            var tokenType = data.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            var isValid = data.TryGetProperty("is_valid", out var validElement) && validElement.GetBoolean();
            var scopes = new List<string>();

            if (data.TryGetProperty("scopes", out var scopesElement) && scopesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var scope in scopesElement.EnumerateArray())
                {
                    var scopeName = scope.GetString();
                    if (!string.IsNullOrWhiteSpace(scopeName))
                    {
                        scopes.Add(scopeName);
                    }
                }
            }

            var missingScopes = new[] { "pages_manage_posts", "pages_read_engagement" }
                .Where(scope => !scopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var details = new List<string>();
            details.Add($"Token type: {tokenType ?? "unknown"}");
            details.Add($"Valid: {isValid}");

            if (scopes.Count > 0)
            {
                details.Add($"Scopes: {string.Join(", ", scopes)}");
            }

            if (missingScopes.Length > 0)
            {
                details.Add($"Missing scopes: {string.Join(", ", missingScopes)}");
            }

            return $"Token diagnostics - {string.Join("; ", details)}. ";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
