using System.Security.Cryptography;

namespace SwLicenseWatcher.Core;

public static class JitterDelayCalculator
{
    public static TimeSpan NextDelay(TimeSpan baseInterval, TimeSpan maxJitter)
    {
        if (maxJitter <= TimeSpan.Zero)
        {
            return baseInterval;
        }

        var upperBoundInSeconds = Math.Max(1, (int)Math.Ceiling(maxJitter.TotalSeconds));
        var jitter = TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(0, upperBoundInSeconds + 1));
        return baseInterval + jitter;
    }
}
