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

        var upperBoundInMilliseconds = Math.Max(1, (int)Math.Ceiling(maxJitter.TotalMilliseconds));
        var jitter = TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, upperBoundInMilliseconds + 1));
        return baseInterval + jitter;
    }
}
