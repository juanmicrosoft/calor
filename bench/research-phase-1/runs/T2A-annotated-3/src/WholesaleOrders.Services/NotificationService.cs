using WholesaleOrders.Infra.Logging;

namespace WholesaleOrders.Services;

public interface INotificationService
{
    Task NotifyAsync(string topic, string message, object? payload = null, CancellationToken ct = default);
    IReadOnlyList<Notification> Sent { get; }
}

public sealed record Notification(DateTimeOffset Timestamp, string Topic, string Message, object? Payload);

public class NotificationService : INotificationService
{
    private readonly IStructuredLogger _logger;
    private readonly List<Notification> _sent = new();
    private readonly object _lock = new();

    public NotificationService(IStructuredLogger logger) => _logger = logger;

    public IReadOnlyList<Notification> Sent
    {
        get { lock (_lock) return _sent.ToList(); }
    }

    // EFFECTS: mem:w, log. POSTCONDITION: Sent contains a Notification with the given topic and message.
    public Task NotifyAsync(string topic, string message, object? payload = null, CancellationToken ct = default)
    {
        var notif = new Notification(DateTimeOffset.UtcNow, topic, message, payload);
        lock (_lock) _sent.Add(notif);
        _logger.Info($"notification: {topic}", new { message });
        return Task.CompletedTask;
    }
}
