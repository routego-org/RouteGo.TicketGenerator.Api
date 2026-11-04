namespace TicketGenerator.Common.Ports;

/// <summary>A finished ticket, ready to be kept as the booking's one stored ticket.</summary>
public sealed record StoreTicketRequest
{
    public required string BookingId { get; init; }

    public required DateTimeOffset ChangeTime { get; init; }

    public required string IssueKind { get; init; }

    public required string SourceEventId { get; init; }

    public required byte[] Document { get; init; }
}

/// <summary>What happened to a ticket offered to the store.</summary>
public enum TicketStoreOutcome
{
    /// <summary>The ticket is now the booking's stored ticket.</summary>
    Stored,

    /// <summary>The stored ticket shows a later change; nothing was written.</summary>
    SkippedOlder,
}

/// <summary>
/// Keeps one private ticket per booking. A ticket replaces the stored one only when its change
/// time is the same or later, and that check and the write happen as one step.
/// </summary>
public interface ITicketStore
{
    Task<TicketStoreOutcome> StoreAsync(StoreTicketRequest request, CancellationToken cancellationToken);
}
