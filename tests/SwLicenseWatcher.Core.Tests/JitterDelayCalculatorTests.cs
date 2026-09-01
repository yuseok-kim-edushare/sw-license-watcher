using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class JitterDelayCalculatorTests
{
    [Fact]
    public void NextDelay_returns_base_interval_when_jitter_is_zero()
    {
        var baseInterval = TimeSpan.FromMinutes(30);

        var delay = JitterDelayCalculator.NextDelay(baseInterval, TimeSpan.Zero);

        Assert.Equal(baseInterval, delay);
    }

    [Fact]
    public void NextDelay_returns_base_interval_when_jitter_is_negative()
    {
        var baseInterval = TimeSpan.FromMinutes(30);

        var delay = JitterDelayCalculator.NextDelay(baseInterval, TimeSpan.FromMilliseconds(-1));

        Assert.Equal(baseInterval, delay);
    }

    [Fact]
    public void NextDelay_stays_within_the_computed_inclusive_jitter_range()
    {
        var baseInterval = TimeSpan.FromSeconds(10);
        var maxJitter = TimeSpan.FromMilliseconds(250);
        var upperBoundInMilliseconds = Math.Max(1, (int)Math.Ceiling(maxJitter.TotalMilliseconds));
        var maxDelay = baseInterval + TimeSpan.FromMilliseconds(upperBoundInMilliseconds);

        for (var i = 0; i < 200; i++)
        {
            var delay = JitterDelayCalculator.NextDelay(baseInterval, maxJitter);

            Assert.InRange(delay, baseInterval, maxDelay);
        }
    }

    [Fact]
    public void NextDelay_uses_at_least_one_millisecond_when_jitter_is_sub_millisecond()
    {
        var baseInterval = TimeSpan.FromSeconds(1);
        var maxDelay = baseInterval + TimeSpan.FromMilliseconds(1);

        for (var i = 0; i < 50; i++)
        {
            var delay = JitterDelayCalculator.NextDelay(baseInterval, TimeSpan.FromTicks(1));

            Assert.InRange(delay, baseInterval, maxDelay);
        }
    }
}
