using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyPortfolio.Shared.Configuration;

namespace MyPortfolio.Web.Services;

/// <summary>
/// Simple in-memory rate limiting service for access code validation.
/// For production with multiple instances, consider using Redis or distributed cache.
/// </summary>
public class RateLimitService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<RateLimitService> _logger;
    private readonly AccessCodeOptions _options;

    public RateLimitService(
        IMemoryCache cache,
        ILogger<RateLimitService> logger,
        IOptions<AccessCodeOptions> options)
    {
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Checks if the IP address has exceeded the rate limit.
    /// </summary>
    public bool IsRateLimited(string ipAddress, out int remainingAttempts)
    {
        var cacheKey = $"ratelimit:accesscode:{ipAddress}";
        
        if (_cache.TryGetValue(cacheKey, out RateLimitInfo? info) && info != null)
        {
            if (info.Attempts >= _options.MaxValidationAttempts)
            {
                var timeRemaining = info.WindowEnd - DateTime.UtcNow;
                _logger.LogWarning(
                    "Rate limit exceeded for IP {IpAddress}. {TimeRemaining} remaining",
                    ipAddress,
                    timeRemaining);
                
                remainingAttempts = 0;
                return true;
            }

            remainingAttempts = _options.MaxValidationAttempts - info.Attempts;
            return false;
        }

        remainingAttempts = _options.MaxValidationAttempts;
        return false;
    }

    /// <summary>
    /// Records a validation attempt for the given IP address.
    /// </summary>
    public void RecordAttempt(string ipAddress)
    {
        var cacheKey = $"ratelimit:accesscode:{ipAddress}";
        var windowEnd = DateTime.UtcNow.AddMinutes(_options.RateLimitWindowMinutes);

        if (_cache.TryGetValue(cacheKey, out RateLimitInfo? info) && info != null)
        {
            info.Attempts++;
        }
        else
        {
            info = new RateLimitInfo
            {
                Attempts = 1,
                WindowEnd = windowEnd
            };
        }

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = windowEnd,
            SlidingExpiration = null
        };

        _cache.Set(cacheKey, info, cacheOptions);
    }

    /// <summary>
    /// Resets the rate limit for an IP address (e.g., after successful validation).
    /// </summary>
    public void ResetLimit(string ipAddress)
    {
        var cacheKey = $"ratelimit:accesscode:{ipAddress}";
        _cache.Remove(cacheKey);
        _logger.LogInformation("Rate limit reset for IP {IpAddress}", ipAddress);
    }

    private class RateLimitInfo
    {
        public int Attempts { get; set; }
        public DateTime WindowEnd { get; set; }
    }
}

