using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;

namespace TicketGenerator.Infrastructure;

/// <summary>Publishes TicketReady to the ticket events topic, with the eventType attribute subscribers filter on.</summary>
public sealed class SnsTicketAnnouncer : ITicketAnnouncer
{
    private const string EventType = "TicketReady";
    private const string Producer = "ticket-generator";
    private const int SchemaVersion = 1;
    private const string EventTypeAttribute = "eventType";
    private const string StringDataType = "String";

    private readonly IAmazonSimpleNotificationService _sns;
    private readonly IClock _clock;
    private readonly string _topicArn;

    public SnsTicketAnnouncer(IAmazonSimpleNotificationService sns, IClock clock, IConfiguration configuration)
    {
        _sns = sns;
        _clock = clock;
        _topicArn = configuration[ConfigKeys.TicketTopicArn]!;
    }

    public async Task AnnounceAsync(TicketReadyAnnouncement announcement, CancellationToken cancellationToken)
    {
        Guid eventId = TicketReadyIdentity.For(announcement.BookingId, announcement.ChangeTime);
        var envelope = new TicketReadyEnvelope
        {
            EventId = eventId.ToString(),
            EventType = EventType,
            Version = SchemaVersion,
            OccurredAt = _clock.UtcNow,
            CorrelationId = announcement.CorrelationId,
            Producer = Producer,
            Data = new TicketReadyData
            {
                BookingId = announcement.BookingId,
                IssueKind = announcement.IssueKind,
                TicketChangeAt = announcement.ChangeTime,
                Contact = announcement.Contact,
                Trip = announcement.Trip,
            },
        };

        var publish = new PublishRequest
        {
            TopicArn = _topicArn,
            Message = JsonSerializer.Serialize(envelope, WireJson.Options),
        };
        publish.MessageAttributes[EventTypeAttribute] = new MessageAttributeValue
        {
            DataType = StringDataType,
            StringValue = EventType,
        };

        await _sns.PublishAsync(publish, cancellationToken);
    }
}

/// <summary>The RouteGo envelope TicketReady is published in. Members are camelCase on the wire.</summary>
internal sealed record TicketReadyEnvelope
{
    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public required int Version { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public required string Producer { get; init; }

    public required TicketReadyData Data { get; init; }
}

/// <summary>
/// What the ticket email needs. Deliberately carries no storage location: the email links to the
/// Web Portal's ticket page, never to the file.
/// </summary>
internal sealed record TicketReadyData
{
    public required string BookingId { get; init; }

    public required string IssueKind { get; init; }

    public required DateTimeOffset TicketChangeAt { get; init; }

    public required ContactMessage Contact { get; init; }

    public required TicketReadyTrip Trip { get; init; }
}
