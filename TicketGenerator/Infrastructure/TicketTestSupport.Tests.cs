// Shared test support: sample Booking events, in-memory ports and a PDF text reader.
// Lives beside the adapters as a *.Tests.cs file so it compiles into the test project only.
using OpenTelemetry.Trace;
using OpenTelemetry;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig;

namespace TicketGenerator.Tests.Support;

/// <summary>Booking events as the ticket queues receive them, for tests.</summary>
public static class TicketSamples
{
    public const string ConfirmedType = "BookingConfirmed";

    public const string SeatsChangedType = "BookingSeatsChanged";

    public static readonly DateTimeOffset ConfirmedAt = new DateTimeOffset(2026, 10, 25, 10, 15, 30, TimeSpan.Zero);

    public static readonly DateTimeOffset SeatsChangedAt = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    public static TicketSnapshotMessage Snapshot(string bookingId, string firstSeat, string secondSeat)
    {
        return new TicketSnapshotMessage
        {
            BookingId = bookingId,
            Contact = new ContactMessage("Riya Sharma", "riya@example.com"),
            Operator = new OperatorMessage("Neeta Travels"),
            Trip = new TripMessage
            {
                Source = new CityMessage("Mumbai"),
                Destination = new CityMessage("Pune"),
                DepartureUtc = new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero),
                ArrivalUtc = new DateTimeOffset(2026, 11, 2, 21, 45, 0, TimeSpan.Zero),
                BusName = "Shivneri",
                BusType = "Sleeper",
                IsAc = true,
            },
            Seats = new List<SeatMessage>
            {
                Seat(firstSeat, "Riya Sharma", 34, "Female"),
                Seat(secondSeat, "Amit Sharma", 36, "Male"),
            },
            TotalAmount = 1700,
            Currency = "INR",
        };
    }

    public static TicketSnapshotMessage WithoutSeats(string bookingId)
    {
        TicketSnapshotMessage snapshot = Snapshot(bookingId, "4C", "4D");
        return snapshot with { Seats = new List<SeatMessage>() };
    }

    public static EventEnvelope<TicketSnapshotMessage> Envelope(
        string eventType,
        string eventId,
        DateTimeOffset occurredAt,
        TicketSnapshotMessage snapshot)
    {
        return new EventEnvelope<TicketSnapshotMessage>
        {
            EventId = eventId,
            EventType = eventType,
            OccurredAt = occurredAt,
            CorrelationId = "corr-" + eventId,
            Data = snapshot,
        };
    }

    public static SQSEvent.SQSMessage Message(EventEnvelope<TicketSnapshotMessage> envelope, string messageId)
    {
        return new SQSEvent.SQSMessage
        {
            MessageId = messageId,
            Body = JsonSerializer.Serialize(envelope, WireJson.Options),
            Attributes = new Dictionary<string, string> { ["ApproximateReceiveCount"] = "1" },
        };
    }

    public static SQSEvent Batch(params SQSEvent.SQSMessage[] messages)
    {
        return new SQSEvent { Records = messages.ToList() };
    }

    public static TestLambdaContext Context()
    {
        return new TestLambdaContext { RemainingTime = TimeSpan.FromSeconds(30) };
    }

    private static SeatMessage Seat(string seatNo, string passengerName, int age, string gender)
    {
        return new SeatMessage
        {
            SeatNo = seatNo,
            SeatClass = "Sleeper",
            Fare = 850,
            Passenger = new PassengerMessage(passengerName, age, gender),
        };
    }
}

/// <summary>An in-memory ticket store that applies the same newest-change-wins rule as the real one.</summary>
public sealed class InMemoryTicketStore : ITicketStore
{
    private readonly Dictionary<string, StoreTicketRequest> _tickets = new Dictionary<string, StoreTicketRequest>();

    /// <summary>When set, every write fails with it, like a storage permission that was removed.</summary>
    public Exception? FailWith { get; set; }

    public int TicketCount
    {
        get { return _tickets.Count; }
    }

    public StoreTicketRequest? Get(string bookingId)
    {
        _tickets.TryGetValue(bookingId, out StoreTicketRequest? ticket);
        return ticket;
    }

    public Task<TicketStoreOutcome> StoreAsync(StoreTicketRequest request, CancellationToken cancellationToken)
    {
        if (FailWith != null)
        {
            throw FailWith;
        }

        bool hasStored = _tickets.TryGetValue(request.BookingId, out StoreTicketRequest? stored);
        if (hasStored && TicketRules.CompareChange(stored!.ChangeTime, request.ChangeTime) == TicketChangeOrder.Older)
        {
            return Task.FromResult(TicketStoreOutcome.SkippedOlder);
        }

        _tickets[request.BookingId] = request;
        return Task.FromResult(TicketStoreOutcome.Stored);
    }
}

/// <summary>Remembers every announcement it is asked to publish.</summary>
public sealed class RecordingAnnouncer : ITicketAnnouncer
{
    public List<TicketReadyAnnouncement> Announcements { get; } = new List<TicketReadyAnnouncement>();

    public Task AnnounceAsync(TicketReadyAnnouncement announcement, CancellationToken cancellationToken)
    {
        Announcements.Add(announcement);
        return Task.CompletedTask;
    }
}

/// <summary>The ports a ticket slice needs, wired to the real renderer and in-memory store and announcer.</summary>
public sealed class TicketHarness : IDisposable
{
    private readonly TracerProvider _tracer = Sdk.CreateTracerProviderBuilder().Build();

    public ITicketRenderer Renderer { get; } = new QuestPdfTicketRenderer();

    public InMemoryTicketStore Store { get; } = new InMemoryTicketStore();

    public RecordingAnnouncer Announcer { get; } = new RecordingAnnouncer();

    /// <summary>The telemetry provider a function flushes at the end of every invocation.</summary>
    public TracerProvider Tracer
    {
        get { return _tracer; }
    }

    public void Dispose()
    {
        _tracer.Dispose();
    }
}

/// <summary>Reads the printed words back out of a ticket PDF so tests can check what a person would see.</summary>
public static class TicketPdf
{
    public static string Text(byte[] document)
    {
        using var pdf = PdfDocument.Open(document);
        var words = new List<string>();
        foreach (Page page in pdf.GetPages())
        {
            foreach (Word word in page.GetWords())
            {
                words.Add(word.Text);
            }
        }

        return string.Join(" ", words);
    }
}
