namespace TicketGenerator.Common.Ports;

/// <summary>The trip summary a TicketReady carries: the two cities and when the bus leaves.</summary>
public sealed record TicketReadyTrip(CityMessage Source, CityMessage Destination, DateTimeOffset DepartureUtc);

/// <summary>What a TicketReady announcement is built from.</summary>
public sealed record TicketReadyAnnouncement
{
    public required string BookingId { get; init; }

    public required string IssueKind { get; init; }

    public required DateTimeOffset ChangeTime { get; init; }

    public required string CorrelationId { get; init; }

    public required ContactMessage Contact { get; init; }

    public required TicketReadyTrip Trip { get; init; }
}

/// <summary>Publishes TicketReady once a ticket is stored.</summary>
public interface ITicketAnnouncer
{
    Task AnnounceAsync(TicketReadyAnnouncement announcement, CancellationToken cancellationToken);
}
