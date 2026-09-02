namespace SwLicenseWatcher.Agent.Worker;

public static class HeartbeatStatus
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Rejected = "Rejected";

    public static string Resolve(bool queueDrained, AgentPublishResult publishResult)
    {
        if (!queueDrained)
        {
            return Degraded;
        }

        return publishResult switch
        {
            AgentPublishResult.Succeeded => Healthy,
            AgentPublishResult.RetryableFailure => Degraded,
            AgentPublishResult.NonRetryableFailure => Rejected,
            _ => Degraded
        };
    }
}
