// Reads the BookingConfirmed message exactly as Booking publishes it (captured from a real booking), so a
// difference between the two services' shapes fails here and not in production.
namespace TicketGenerator.Features.Tickets.IssueTicket;

public sealed class BookingEventShapeTests
{
    private const string BookingConfirmedBody = """
        {"eventId":"0924d6c7-2eb2-44da-add5-b3650cae0c54","eventType":"BookingConfirmed","version":1,
        "occurredAt":"2026-11-01T05:31:06.660101+00:00","correlationId":"e52a10b6-bff6-4b30-b9aa-02961183b8de",
        "producer":"booking-service","data":{"bookingId":"a5c82b7b-3927-42a6-a699-92c7248f1be7",
        "customerId":"6a383f2a-2549-43f6-98e7-97e601a0dbaa","contact":{"name":"E2E Customer","email":"e2e-customer-1@example.com"},
        "operator":{"operatorId":"10f9cc56-9adf-4e6e-a622-e1d5a8164d20","name":"Vikas Travels","email":"singh.vikas.v.p@gmail.com"},
        "trip":{"tripId":"3f73d5e0-3dc9-4a5b-875d-ddff0a1c1207","serviceDate":"2026-11-02","source":"Mumbai","destination":"Pune",
        "departureUtc":"2026-11-02T01:30:00+00:00","arrivalUtc":"2026-11-02T05:00:00+00:00","busName":"Vikas Express AC Seater",
        "busType":"Seater","isAc":true},"seats":[{"seatNo":"3A","seatClass":"Seater","fare":450,"name":"Asha Rao","age":31,"gender":"Male"},
        {"seatNo":"3B","seatClass":"Seater","fare":450,"name":"Ravi Rao","age":34,"gender":"Female"}],"totalAmount":900,
        "currency":"INR","paymentId":"7525f6b2-c69e-421a-af84-94b1829cc6ad","confirmedAt":"2026-11-01T05:31:06.660101+00:00"}}
        """;

    [Fact]
    public void Deserialize_BookingConfirmedAsBookingPublishesIt_ReadsCitiesAndPassengers()
    {
        EventEnvelope<TicketSnapshotMessage>? envelope =
            JsonSerializer.Deserialize<EventEnvelope<TicketSnapshotMessage>>(BookingConfirmedBody, WireJson.Options);

        Assert.NotNull(envelope);
        Assert.Equal("Mumbai", envelope.Data.Trip.Source.Name);
        Assert.Equal("Pune", envelope.Data.Trip.Destination.Name);
        Assert.Equal(2, envelope.Data.Seats.Count);
        Assert.Equal("Asha Rao", envelope.Data.Seats[0].Passenger.Name);
        Assert.Equal(31, envelope.Data.Seats[0].Passenger.Age);
        Assert.Equal("Female", envelope.Data.Seats[1].Passenger.Gender);
    }

    [Fact]
    public void Validate_BookingConfirmedAsBookingPublishesIt_FindsEverythingATicketNeeds()
    {
        EventEnvelope<TicketSnapshotMessage>? envelope =
            JsonSerializer.Deserialize<EventEnvelope<TicketSnapshotMessage>>(BookingConfirmedBody, WireJson.Options);

        FluentValidation.Results.ValidationResult result = new TicketSnapshotValidator().Validate(envelope!.Data);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Deserialize_SeatWithPassengerObjectAndCityObjects_StillReadsThem()
    {
        string body = """
            {"seatNo":"1A","seatClass":"Seater","fare":450,"passenger":{"name":"Asha Rao","age":31,"gender":"Male"}}
            """;

        SeatMessage? seat = JsonSerializer.Deserialize<SeatMessage>(body, WireJson.Options);
        CityMessage? city = JsonSerializer.Deserialize<CityMessage>("{\"name\":\"Pune\"}", WireJson.Options);

        Assert.Equal("Asha Rao", seat!.Passenger.Name);
        Assert.Equal("Pune", city!.Name);
    }

    [Fact]
    public void Deserialize_SeatWithoutPassengerName_IsRefused()
    {
        string body = """
            {"seatNo":"1A","seatClass":"Seater","fare":450,"age":31,"gender":"Male"}
            """;

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SeatMessage>(body, WireJson.Options));
    }
}
