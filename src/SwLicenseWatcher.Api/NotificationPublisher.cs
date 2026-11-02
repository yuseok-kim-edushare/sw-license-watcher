using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Api;

internal interface IUnsignedPackageNotifier
{
    void NotifyUnsigned(string target, string version);
}

internal sealed class UnsignedPackageNotifier(NotificationPublisher publisher) : IUnsignedPackageNotifier
{
    public void NotifyUnsigned(string target, string version) =>
        publisher.EnqueueUnsignedPackageIfNeeded(target, version);
}

public sealed class NotificationPublisher
{
    private const int MaxListedSoftware = 50;
    private readonly Channel<NotificationMessage> _channel;
    private readonly IOptions<NotificationOptions> _options;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(IOptions<NotificationOptions> options, ILogger<NotificationPublisher> logger)
    {
        _options = options;
        _logger = logger;
        _channel = Channel.CreateUnbounded<NotificationMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<NotificationMessage> Reader => _channel.Reader;

    public void EnqueueNewSoftwareIfNeeded(InventoryIngestionRequest snapshot, SnapshotSaveResult saveResult)
    {
        try
        {
            var options = _options.Value;
            if (!saveResult.Applied || !options.Events.NewSoftware || !options.HasEnabledChannel)
            {
                return;
            }

            var added = FindNewlyInstalled(saveResult.PreviousSoftware, snapshot.InstalledSoftware);
            if (added.Count == 0)
            {
                return;
            }

            Enqueue(ForNewSoftware(snapshot.Pc, added));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue new-software notification for {DeviceCode}.", snapshot.Pc.DeviceCode);
        }
    }

    public void EnqueueBlacklistViolationsIfNeeded(InventoryIngestionRequest snapshot, SnapshotSaveResult saveResult)
    {
        try
        {
            var options = _options.Value;
            if (!saveResult.Applied || saveResult.NewViolations.Count == 0 || !options.Events.BlacklistViolation || !options.HasEnabledChannel)
            {
                return;
            }

            Enqueue(ForBlacklistViolations(snapshot.Pc, saveResult.NewViolations));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue blacklist-violation notification for {DeviceCode}.", snapshot.Pc.DeviceCode);
        }
    }

    public void EnqueueUnsignedPackageIfNeeded(string target, string version)
    {
        try
        {
            var options = _options.Value;
            if (!options.Events.UnsignedPackage || !options.HasEnabledChannel ||
                string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(version))
            {
                return;
            }

            Enqueue(ForUnsignedPackage(target, version));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue unsigned-package notification for {Target} {Version}.", target, version);
        }
    }

    public void EnqueueStaleHeartbeatsIfNeeded(
        IReadOnlyList<StalePcHeartbeat> stalePcs,
        TimeSpan threshold)
    {
        try
        {
            var options = _options.Value;
            if (stalePcs.Count == 0 || !options.Events.StaleHeartbeat || !options.HasEnabledChannel)
            {
                return;
            }

            Enqueue(ForStaleHeartbeats(stalePcs, threshold));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue stale-heartbeat notification.");
        }
    }

    private void Enqueue(NotificationMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            _logger.LogWarning("Notification queue rejected '{Subject}'.", message.Subject);
        }
    }

    internal static IReadOnlyList<InstalledSoftwareEntry> FindNewlyInstalled(
        IReadOnlyList<InstalledSoftwareEntry> previous,
        IReadOnlyCollection<InstalledSoftwareEntry> current)
    {
        if (previous.Count == 0)
        {
            return [];
        }

        var existing = new HashSet<(string Name, string Version)>(previous.Select(ToKey));
        var added = new List<InstalledSoftwareEntry>();
        var seen = new HashSet<(string Name, string Version)>();
        foreach (var entry in current)
        {
            var key = ToKey(entry);
            if (seen.Add(key) && !existing.Contains(key))
            {
                added.Add(entry);
            }
        }

        return added;
    }

    private static (string Name, string Version) ToKey(InstalledSoftwareEntry entry) =>
        (
            (Truncate(entry.Name, 256) ?? string.Empty).Trim().ToUpperInvariant(),
            (Truncate(entry.Version, 64) ?? string.Empty).Trim().ToUpperInvariant());

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private static NotificationMessage ForNewSoftware(PcIdentity pc, IReadOnlyList<InstalledSoftwareEntry> added)
    {
        var subject = $"신규 소프트웨어 설치 감지 — {pc.HostName} ({pc.DeviceCode})";
        var body = new StringBuilder();
        body.Append("PC ").Append(pc.HostName).Append(" (").Append(pc.DeviceCode)
            .AppendLine(")에서 이전에 없던 소프트웨어가 감지되었습니다.");
        body.AppendLine();
        var listed = 0;
        foreach (var entry in added)
        {
            if (listed >= MaxListedSoftware)
            {
                break;
            }

            body.Append("- ").Append(entry.Name);
            if (!string.IsNullOrWhiteSpace(entry.Version))
            {
                body.Append(' ').Append(entry.Version);
            }

            body.AppendLine();
            listed++;
        }

        if (added.Count > MaxListedSoftware)
        {
            body.Append("- … 외 ").Append(added.Count - MaxListedSoftware).AppendLine("개");
        }

        return new NotificationMessage(subject, body.ToString().TrimEnd());
    }

    internal static NotificationMessage ForBlacklistViolations(PcIdentity pc, IReadOnlyList<NewBlacklistViolation> violations)
    {
        var subject = $"블랙리스트 정책 위반 감지 — {pc.HostName} ({pc.DeviceCode})";
        var body = new StringBuilder();
        body.Append("PC ").Append(pc.HostName).Append(" (").Append(pc.DeviceCode)
            .AppendLine(")에서 블랙리스트 정책에 해당하는 소프트웨어가 새로 감지되었습니다.");
        body.AppendLine();
        var listed = 0;
        foreach (var violation in violations)
        {
            if (listed >= MaxListedSoftware)
            {
                break;
            }

            body.Append("- ").Append(violation.Software.Name);
            if (!string.IsNullOrWhiteSpace(violation.Software.Version))
            {
                body.Append(' ').Append(violation.Software.Version);
            }

            body.Append(" — 정책: ").Append(FormatPolicyPattern(violation.Policy)).AppendLine();
            listed++;
        }

        if (violations.Count > MaxListedSoftware)
        {
            body.Append("- … 외 ").Append(violations.Count - MaxListedSoftware).AppendLine("개");
        }

        return new NotificationMessage(subject, body.ToString().TrimEnd());
    }

    internal static string FormatPolicyPattern(SoftwarePolicyEntry policy)
    {
        var builder = new StringBuilder(policy.ProductName);
        if (!string.IsNullOrWhiteSpace(policy.Publisher))
        {
            builder.Append(" / ").Append(policy.Publisher);
        }

        if (!string.IsNullOrWhiteSpace(policy.VersionPattern))
        {
            builder.Append(" / ").Append(policy.VersionPattern);
        }

        return builder.ToString();
    }

    internal static NotificationMessage ForUnsignedPackage(string target, string version)
    {
        var server = string.Equals(target, "server", StringComparison.OrdinalIgnoreCase);
        var kind = server ? "서버" : "클라이언트";
        var subject = $"{kind} 패키지 서명 실패 — {version}";
        var body = server
            ? $"{version} 서버 패키지 서명이 켜져 있으나 서명에 실패했습니다. 패키지는 적용 준비로 넘기지 않았거나 서명되지 않은 채 남았습니다."
            : $"{version} 클라이언트 패키지 서명이 켜져 있으나 서명에 실패했습니다. 핀은 바뀌지 않았습니다.";
        return new NotificationMessage(subject, body);
    }

    private static NotificationMessage ForStaleHeartbeats(IReadOnlyList<StalePcHeartbeat> stalePcs, TimeSpan threshold)
    {
        var subject = stalePcs.Count == 1
            ? $"PC 하트비트 두절 — {stalePcs[0].HostName} ({stalePcs[0].DeviceCode})"
            : $"PC 하트비트 두절 — {stalePcs.Count}대";
        var body = new StringBuilder();
        body.Append("마지막 heartbeat가 ").Append(FormatThreshold(threshold))
            .AppendLine(" 이상 지난 PC가 있습니다.");
        body.AppendLine();
        foreach (var pc in stalePcs)
        {
            body.Append("- ").Append(pc.HostName).Append(" (").Append(pc.DeviceCode)
                .Append(") — last: ").Append(pc.LastHeartbeatUtc.UtcDateTime.ToString("u"))
                .AppendLine();
        }

        return new NotificationMessage(subject, body.ToString().TrimEnd());
    }

    private static string FormatThreshold(TimeSpan threshold)
    {
        if (threshold.TotalHours >= 1 && threshold.TotalHours == Math.Floor(threshold.TotalHours))
        {
            return $"{threshold.TotalHours:0}시간";
        }

        if (threshold.TotalMinutes >= 1 && threshold.TotalMinutes == Math.Floor(threshold.TotalMinutes))
        {
            return $"{threshold.TotalMinutes:0}분";
        }

        return threshold.ToString();
    }
}
