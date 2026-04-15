using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using MailKit;
using MailKit.Net.Imap;

namespace Atlas.Email.Providers;

public sealed class ImapCallExecutor
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ConcurrencyLimiters = new(StringComparer.Ordinal);

    // Allows re-entrant execution for the same limiter key within a single async flow.
    private static readonly AsyncLocal<Dictionary<string, int>?> HeldLimiterKeys = new();

    private readonly ILogger<ImapCallExecutor> _logger;

    public ImapCallExecutor(ILogger<ImapCallExecutor> logger)
    {
        _logger = logger;
    }

    public int MaxAttempts { get; init; } = 3;

    public TimeSpan MaxTotalDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public async Task<T> ExecuteAsync<T>(
        string provider,
        string operation,
        string limiterGroup,
        string accountKey,
        int maxConcurrency,
        Func<CancellationToken, Task<T>> opAsync,
        Func<CancellationToken, Task>? reconnectAsync,
        bool allowRetry,
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

            var held = HeldLimiterKeys.Value ??= new Dictionary<string, int>(StringComparer.Ordinal);
            var isReentrant = held.TryGetValue(limiterKey, out var heldCount) && heldCount > 0;
            var acquired = false;

            if (isReentrant)
            {
                held[limiterKey] = heldCount + 1;
            }
            else
            {
                await limiter.WaitAsync(cancellationToken);
                acquired = true;
                held[limiterKey] = 1;
            }
            try
            {
                _logger.LogDebug("{Provider} {Operation} start (attempt {Attempt}/{MaxAttempts}) for {AccountKey}", provider, operation, attempt, MaxAttempts, accountKey);

                var result = await opAsync(cancellationToken);

                _logger.LogDebug(
                    "{Provider} {Operation} success in {ElapsedMs}ms (attempt {Attempt}/{MaxAttempts}) for {AccountKey}",
                    provider, operation, stopwatch.ElapsedMilliseconds, attempt, MaxAttempts, accountKey);

                return result;
            }
            catch (Exception ex) when (allowRetry && IsRetryableException(ex) && attempt < MaxAttempts)
            {
                lastException = ex;

                if (reconnectAsync != null && ShouldReconnect(ex))
                {
                    try
                    {
                        await reconnectAsync(cancellationToken);
                    }
                    catch
                    {
                        // Ignore reconnect failures; we will still backoff and retry.
                    }
                }

                var delay = GetRetryDelay(attempt, totalDelay);
                if (delay.HasValue)
                {
                    _logger.LogWarning(
                        ex,
                        "{Provider} {Operation} transient IMAP error; retrying in {DelayMs}ms (attempt {Attempt}/{MaxAttempts}) for {AccountKey}",
                        provider, operation, (int)delay.Value.TotalMilliseconds, attempt, MaxAttempts, accountKey);

                    totalDelay += delay.Value;
                    await Task.Delay(delay.Value, cancellationToken);
                    continue;
                }

                throw;
            }
            finally
            {
                if (held.TryGetValue(limiterKey, out var currentCount) && currentCount > 0)
                {
                    if (currentCount == 1)
                    {
                        held.Remove(limiterKey);
                        if (acquired)
                        {
                            limiter.Release();
                        }
                    }
                    else
                    {
                        held[limiterKey] = currentCount - 1;

                        if (acquired)
                        {
                            limiter.Release();
                        }
                    }
                }
                else if (acquired)
                {
                    limiter.Release();
                }
            }
        }

        _logger.LogError(
            lastException,
            "{Provider} {Operation} failed after {Attempts} attempts in {ElapsedMs}ms for {AccountKey}",
            provider, operation, attempt, stopwatch.ElapsedMilliseconds, accountKey);

        throw lastException ?? new InvalidOperationException($"{provider} {operation} failed");
    }

    private static bool ShouldReconnect(Exception ex)
    {
        return ex is ServiceNotConnectedException
            || ex is ServiceNotAuthenticatedException
            || ex is ImapProtocolException;
    }

    private static bool IsRetryableException(Exception ex)
    {
        if (ex is OperationCanceledException) return false;

        if (ex is SocketException) return true;
        if (ex is IOException) return true;

        if (ex is ServiceNotConnectedException) return true;
        if (ex is ServiceNotAuthenticatedException) return true;

        if (ex is ImapProtocolException) return true;

        return false;
    }

    private TimeSpan? GetRetryDelay(int attempt, TimeSpan totalDelaySoFar)
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
}
