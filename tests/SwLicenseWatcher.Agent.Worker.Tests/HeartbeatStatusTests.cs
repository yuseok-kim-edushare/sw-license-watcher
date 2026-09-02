using SwLicenseWatcher.Agent.Worker;

namespace SwLicenseWatcher.Agent.Worker.Tests;

public class HeartbeatStatusTests
{
    [Fact]
    public void Resolve_reports_healthy_when_the_snapshot_was_delivered()
    {
        Assert.Equal("Healthy", HeartbeatStatus.Resolve(queueDrained: true, AgentPublishResult.Succeeded));
    }

    [Theory]
    [InlineData(AgentPublishResult.Succeeded)]
    [InlineData(AgentPublishResult.RetryableFailure)]
    [InlineData(AgentPublishResult.NonRetryableFailure)]
    public void Resolve_reports_degraded_when_the_queue_was_not_drained(AgentPublishResult publishResult)
    {
        Assert.Equal("Degraded", HeartbeatStatus.Resolve(queueDrained: false, publishResult));
    }

    [Fact]
    public void Resolve_reports_degraded_when_the_snapshot_must_be_retried()
    {
        Assert.Equal("Degraded", HeartbeatStatus.Resolve(queueDrained: true, AgentPublishResult.RetryableFailure));
    }

    [Fact]
    public void Resolve_reports_rejected_when_the_api_returns_a_non_retryable_failure()
    {
        Assert.Equal("Rejected", HeartbeatStatus.Resolve(queueDrained: true, AgentPublishResult.NonRetryableFailure));
    }

    [Theory]
    [InlineData(true, AgentPublishResult.Succeeded)]
    [InlineData(true, AgentPublishResult.RetryableFailure)]
    [InlineData(true, AgentPublishResult.NonRetryableFailure)]
    [InlineData(false, AgentPublishResult.Succeeded)]
    public void Resolve_status_fits_the_heartbeat_field_limit(bool queueDrained, AgentPublishResult publishResult)
    {
        var status = HeartbeatStatus.Resolve(queueDrained, publishResult);

        Assert.False(string.IsNullOrWhiteSpace(status));
        Assert.True(status.Length <= 32);
    }
}
