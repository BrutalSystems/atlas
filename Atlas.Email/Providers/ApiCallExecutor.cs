using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Atlas.Email.Providers;

public sealed class ApiCallExecutor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConcurrencyLimiters = new(StringComparer.Ordinal);

    private readonly ILogger<ApiCallExecutor> _logger;

    public ApiCallExecutor(ILogger<ApiCallExecutor> logger)
    {
        _logger = logger;
    }

    public int MaxAttempts { get; init; } = 3;

    public TimeSpan MaxTotalDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<T> ExecuteHttpAsync<T>(
        string provider,
        string operation,
        string limiterGroup,
        string accountKey,
        int maxConcurrency,
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        Func<HttpResponseMessage, CancellationToken, Task<T>> parseAsync,
        bool ensureSuccess,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("provider is required", nameof(provider));
        if (string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("operation is required", nameof(operation));
        if (string.IsNullOrWhiteSpace(limiterGroup)) throw new ArgumentException("limiterGroup is required", nameof(limiterGroup));
        if (string.IsNullOrWhiteSpace(accountKey)) throw new ArgumentException("accountKey is required", nameof(accountKey));
        if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

        var limiterKey = $"{provider}:{accountKey}:{limiterGroup}";
        var limiter = ConcurrencyLimiters.GetOrAdd(limiterKey, _ => new SemaphoreSlim(maxConcurrency, maxConcurrency));

        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        var totalDelay = TimeSpan.Zero;

        Exception? lastException = null;

        while (attempt < MaxAttempts)
        {
            attempt++;

            await limiter.WaitAsync(cancellationToken);
            try
            {
                _logger.LogDebug("{Provider} {Operation} start (attempt {Attempt}/{MaxAttempts}) for {AccountKey}", provider, operation, attempt, MaxAttempts, accountKey);

                using var response = await sendAsync(cancellationToken);

                var isRetryableStatus = IsRetryableStatusCode(response.StatusCode);
                string? throttleBodySnippet = null;

                if (!isRetryableStatus && response.StatusCode == HttpStatusCode.Forbidden)
                {
                    throttleBodySnippet = await SafeReadBodySnippetAsync(response, cancellationToken);
                    if (throttleBodySnippet.Contains("rateLimitExceeded", StringComparison.OrdinalIgnoreCase)
                        || throttleBodySnippet.Contains("userRateLimitExceeded", StringComparison.OrdinalIgnoreCase))
                    {
                        isRetryableStatus = true;
                    }
                }

                if (isRetryableStatus)
                {
                    throttleBodySnippet ??= await SafeReadBodySnippetAsync(response, cancellationToken);
                    var retryAfter = TryGetRetryAfter(response);
                    var delay = GetRetryDelay(response, attempt, totalDelay);
                    if (delay.HasValue)
                    {
                        _logger.LogWarning(
                            "********** {Provider} {Operation} throttled/transient ({StatusCode}); retrying in {DelayMs}ms (retry-after: {RetryAfterMs}ms) (attempt {Attempt}/{MaxAttempts}) for {AccountKey}. Body: {BodySnippet}",
                            provider, operation, (int)response.StatusCode,
                            (int)delay.Value.TotalMilliseconds,
                            retryAfter.HasValue ? (int)retryAfter.Value.TotalMilliseconds : (int?)null,
                            attempt, MaxAttempts, accountKey, throttleBodySnippet);

                        totalDelay += delay.Value;
                        await Task.Delay(delay.Value, cancellationToken);
                        continue;
                    }
                }

                if (ensureSuccess && !response.IsSuccessStatusCode)
                {
                    var bodySnippet = await SafeReadBodySnippetAsync(response, cancellationToken);
                    throw new HttpRequestException(
                        $"{provider} {operation} failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {bodySnippet}",
                        inner: null,
                        statusCode: response.StatusCode);
                }

                var parsed = await parseAsync(response, cancellationToken);

                _logger.LogDebug(
                    "{Provider} {Operation} success in {ElapsedMs}ms (attempt {Attempt}/{MaxAttempts}) for {AccountKey}",
                    provider, operation, stopwatch.ElapsedMilliseconds, attempt, MaxAttempts, accountKey);

                return parsed;
            }
            catch (Exception ex) when (IsRetryableException(ex) && attempt < MaxAttempts)
            {
                lastException = ex;

                var delay = GetExceptionRetryDelay(attempt, totalDelay);
                if (delay.HasValue)
                {
                    _logger.LogWarning(
                        ex,
                        "************************* {Provider} {Operation} exception; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}) for {AccountKey}",
                        provider, operation, (int)delay.Value.TotalMilliseconds, attempt, MaxAttempts, accountKey);

                    totalDelay += delay.Value;
                    await Task.Delay(delay.Value, cancellationToken);
                    continue;
                }

                throw;
            }
            finally
            {
                limiter.Release();
            }
        }

        _logger.LogError(
            lastException,
            "***************************** {Provider} {Operation} failed after {Attempts} attempts in {ElapsedMs}ms for {AccountKey}",
            provider, operation, attempt, stopwatch.ElapsedMilliseconds, accountKey);

        throw lastException ?? new InvalidOperationException($"{provider} {operation} failed");
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == (HttpStatusCode)429
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout;
    }

    private bool IsRetryableException(Exception ex)
    {
        if (ex is OperationCanceledException) return false;

        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue)
            {
                return IsRetryableStatusCode(httpEx.StatusCode.Value);
            }

            return true;
        }
        if (ex is TaskCanceledException) return true;

        return false;
    }

    private TimeSpan? GetRetryDelay(HttpResponseMessage response, int attempt, TimeSpan totalDelaySoFar)
    {
        var retryAfter = TryGetRetryAfter(response);
        var computed = retryAfter ?? ComputeExponentialBackoff(attempt);

        if (totalDelaySoFar + computed > MaxTotalDelay)
        {
            return null;
        }

        return computed;
    }

    private TimeSpan? GetExceptionRetryDelay(int attempt, TimeSpan totalDelaySoFar)
    {
        var computed = ComputeExponentialBackoff(attempt);

        if (totalDelaySoFar + computed > MaxTotalDelay)
        {
            return null;
        }

        return computed;
    }

    private TimeSpan ComputeExponentialBackoff(int attempt)
    {
        var exponent = Math.Max(0, attempt - 1);
        var multiplier = Math.Pow(2, exponent);
        var delayMs = BaseDelay.TotalMilliseconds * multiplier;

        var jitteredMs = Random.Shared.NextDouble() * delayMs;
        return TimeSpan.FromMilliseconds(Math.Max(0, jitteredMs));
    }

    private TimeSpan? TryGetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter == null) return null;

        if (response.Headers.RetryAfter.Delta.HasValue)
        {
            var delta = response.Headers.RetryAfter.Delta.Value;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (response.Headers.RetryAfter.Date.HasValue)
        {
            var date = response.Headers.RetryAfter.Date.Value;
            var delay = date - DateTimeOffset.UtcNow;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static async Task<string> SafeReadBodySnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrEmpty(body)) return "";
            return body.Length <= 1000 ? body : body.Substring(0, 1000);
        }
        catch
        {
            return "";
        }
    }
}
