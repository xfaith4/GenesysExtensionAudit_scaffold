// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Clients/GenesysCloudApiClient.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Genesys.Json;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

/// <summary>
/// Base HTTP client for Genesys Cloud API with resilience:
/// - JSON (System.Text.Json)
/// - Retries: 429 (Retry-After), 408, 5xx, network faults
/// - 401: forces one token refresh and retries once
/// Designed to be used with HttpClient configured with DelegatingHandlers:
/// OAuthBearerHandler, RateLimitHandler, HttpLoggingHandler.
/// </summary>
public abstract class GenesysCloudApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            // Genesys Cloud API sometimes returns "" instead of null for date fields.
            // These converters treat empty/whitespace strings as null rather than throwing.
            new NullableDateTimeOffsetConverter(),
            new NullableDateTimeConverter()
        }
    };

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly ITokenProvider _tokenProvider;
    private readonly GenesysRegionOptions _regionOptions;

    protected GenesysCloudApiClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _regionOptions = regionOptions?.Value ?? throw new ArgumentNullException(nameof(regionOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected Uri ApiUri(string relativePathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(relativePathAndQuery))
            throw new ArgumentException("Value cannot be null/empty.", nameof(relativePathAndQuery));

        var baseUri = new Uri($"https://api.{_regionOptions.Region}".TrimEnd('/') + "/");
        return new Uri(baseUri, relativePathAndQuery.TrimStart('/'));
    }

    protected async Task<T> GetJsonAsync<T>(
        string relativePathAndQuery,
        CancellationToken ct)
    {
        var uri = ApiUri(relativePathAndQuery);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await SendJsonAsync<T>(request, ct).ConfigureAwait(false);
    }

    protected async Task<T> SendJsonAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        return await SendWithRetryAsync<T>(
            request,
            async (req, token) =>
            {
                using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await SafeReadBodyAsync(response, token).ConfigureAwait(false);
                    throw new GenesysApiException(
                        statusCode: response.StatusCode,
                        reasonPhrase: response.ReasonPhrase,
                        correlationId: TryGetCorrelationId(response),
                        responseBody: body,
                        retryAfter: TryGetRetryAfter(response));
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, token).ConfigureAwait(false);
                if (result is null)
                    throw new InvalidOperationException("Genesys Cloud response JSON deserialized to null.");

                return result;
            },
            ct).ConfigureAwait(false);
    }

    private async Task<T> SendWithRetryAsync<T>(
        HttpRequestMessage originalRequest,
        Func<HttpRequestMessage, CancellationToken, Task<T>> sender,
        CancellationToken ct)
    {
        const int maxAttempts = 6;
        var attempt = 0;
        var tokenRefreshAttempted = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;

            using var request = await CloneHttpRequestMessageAsync(originalRequest, ct).ConfigureAwait(false);

            try
            {
                var token = await _tokenProvider.GetAccessTokenAsync(ct).ConfigureAwait(false);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                return await sender(request, ct).ConfigureAwait(false);
            }
            catch (GenesysApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized && !tokenRefreshAttempted)
            {
                tokenRefreshAttempted = true;

                _logger.LogWarning(
                    "Received 401 Unauthorized from Genesys Cloud. Forcing token refresh and retrying once. CorrelationId={CorrelationId}",
                    ex.CorrelationId);

                await _tokenProvider.ForceRefreshAsync(ct).ConfigureAwait(false);

                if (attempt >= maxAttempts)
                    throw;

                continue;
            }
            catch (GenesysApiException ex) when (IsRetryableStatus(ex.StatusCode) && attempt < maxAttempts)
            {
                var delay = ComputeDelay(ex, attempt);
                _logger.LogWarning(
                    "Transient Genesys Cloud HTTP error {StatusCode}. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms. CorrelationId={CorrelationId}",
                    (int)ex.StatusCode, attempt, maxAttempts, (int)delay.TotalMilliseconds, ex.CorrelationId);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                var delay = ComputeBackoffDelay(attempt, retryAfter: null);
                _logger.LogWarning(
                    ex,
                    "Network error calling Genesys Cloud. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms.",
                    attempt, maxAttempts, (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                var delay = ComputeBackoffDelay(attempt, retryAfter: null);
                _logger.LogWarning(
                    ex,
                    "Timeout calling Genesys Cloud. Attempt {Attempt}/{MaxAttempts}. Delay {DelayMs}ms.",
                    attempt, maxAttempts, (int)delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }
        }
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
        => statusCode == (HttpStatusCode)429
           || statusCode == HttpStatusCode.RequestTimeout
           || (int)statusCode >= 500;

    private static TimeSpan ComputeDelay(GenesysApiException ex, int attempt)
    {
        TimeSpan? retryAfter = null;

        if (ex.StatusCode == (HttpStatusCode)429)
        {
            if (ex.RetryAfter is { } headerRetryAfter && headerRetryAfter > TimeSpan.Zero)
            {
                retryAfter = headerRetryAfter;
            }
            else if (int.TryParse(ex.ResponseBody?.Trim(), out var seconds) && seconds > 0)
            {
                retryAfter = TimeSpan.FromSeconds(seconds);
            }
        }

        return ComputeBackoffDelay(attempt, retryAfter);
    }

    private static TimeSpan ComputeBackoffDelay(int attempt, TimeSpan? retryAfter)
    {
        var baseDelay = TimeSpan.FromSeconds(1);
        var maxDelay = TimeSpan.FromSeconds(30);

        TimeSpan delay;
        if (retryAfter is { } ra && ra > TimeSpan.Zero)
        {
            delay = ra;
        }
        else
        {
            var pow = Math.Pow(2, Math.Max(0, attempt - 1));
            delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * pow);
        }

        if (delay > maxDelay) delay = maxDelay;

        var jitterMs = Random.Shared.Next(0, 251);
        delay += TimeSpan.FromMilliseconds(jitterMs);

        return delay;
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private static string? TryGetCorrelationId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("ININ-Correlation-Id", out var inin))
            return inin.FirstOrDefault();

        if (response.Headers.TryGetValues("X-Correlation-Id", out var xcorr))
            return xcorr.FirstOrDefault();

        return null;
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry is null)
            return null;

        if (retry.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;

        if (retry.Date is { } at)
        {
            var wait = at - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                return wait;
        }

        return null;
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;

            const int max = 8_192;
            return body.Length <= max ? body : body[..max];
        }
        catch
        {
            return null;
        }
    }
}

public sealed class GenesysApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ReasonPhrase { get; }
    public string? CorrelationId { get; }
    public string? ResponseBody { get; }
    public TimeSpan? RetryAfter { get; }

    public GenesysApiException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string? correlationId,
        string? responseBody,
        TimeSpan? retryAfter = null)
        : base(BuildMessage(statusCode, reasonPhrase, correlationId, responseBody, retryAfter))
    {
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        CorrelationId = correlationId;
        ResponseBody = responseBody;
        RetryAfter = retryAfter;
    }

    private static string BuildMessage(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string? correlationId,
        string? responseBody,
        TimeSpan? retryAfter)
    {
        var sb = new StringBuilder();
        sb.Append("Genesys Cloud API request failed: ");
        sb.Append((int)statusCode).Append(' ').Append(reasonPhrase ?? statusCode.ToString());

        if (retryAfter is { } ra && ra > TimeSpan.Zero)
            sb.Append(" RetryAfter=").Append((int)Math.Ceiling(ra.TotalSeconds)).Append('s');

        if (!string.IsNullOrWhiteSpace(correlationId))
            sb.Append(" CorrelationId=").Append(correlationId);

        if (!string.IsNullOrWhiteSpace(responseBody))
            sb.Append(" Body=").Append(responseBody);

        return sb.ToString();
    }
}
