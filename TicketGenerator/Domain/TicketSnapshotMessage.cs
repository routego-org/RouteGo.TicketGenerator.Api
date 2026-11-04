using System.Text.Json.Serialization;

namespace TicketGenerator.Domain;

// The ticket snapshot Booking carries on BookingConfirmed and BookingSeatsChanged. Booking is the
// authority; only the fields a ticket needs are read, the rest are ignored. Value types are
// nullable so that a missing value reaches the validator instead of silently becoming zero.

/// <summary>The RouteGo event envelope around an event's data.</summary>
public sealed record EventEnvelope<TData>
{
    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public required TData Data { get; init; }
}

/// <summary>Everything a ticket shows: the booking, its contact, operator, trip, seats and the total paid.</summary>
public sealed record TicketSnapshotMessage
{
    public required string BookingId { get; init; }

    public required ContactMessage Contact { get; init; }

    public required OperatorMessage Operator { get; init; }

    public required TripMessage Trip { get; init; }

    public required IReadOnlyList<SeatMessage> Seats { get; init; }

    public required long? TotalAmount { get; init; }

    public required string Currency { get; init; }
}

public sealed record ContactMessage(string Name, string Email);

public sealed record OperatorMessage(string Name);

[JsonConverter(typeof(CityMessageConverter))]
public sealed record CityMessage(string Name);

public sealed record TripMessage
{
    public required CityMessage Source { get; init; }

    public required CityMessage Destination { get; init; }

    public required DateTimeOffset? DepartureUtc { get; init; }

    public required DateTimeOffset? ArrivalUtc { get; init; }

    public required string BusName { get; init; }

    public required string BusType { get; init; }

    public required bool? IsAc { get; init; }
}

[JsonConverter(typeof(SeatMessageConverter))]
public sealed record SeatMessage
{
    public required string SeatNo { get; init; }

    public required string SeatClass { get; init; }

    public required long? Fare { get; init; }

    public required PassengerMessage Passenger { get; init; }
}

public sealed record PassengerMessage(string Name, int? Age, string Gender);
