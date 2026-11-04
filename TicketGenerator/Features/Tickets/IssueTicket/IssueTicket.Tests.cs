// Tests for Issue ticket.  Spec: CAP-TKT-001
// One test per acceptance criterion in SPEC.md, PLUS the idempotency test. The handler runs against
// the real PDF renderer with an in-memory store and announcer: the S3 store's conditional writes
// need a real S3 (LocalStack) and are covered by the deployment checks, not here.
namespace TicketGenerator.Features.Tickets.IssueTicket;

public sealed class IssueTicketTests
{
    private static IssueTicketInput ConfirmedInput(string bookingId, string eventId)
    {
        return new IssueTicketInput
        {
            EventId = eventId,
            EventType = TicketSamples.ConfirmedType,
            OccurredAt = TicketSamples.ConfirmedAt,
            CorrelationId = "corr-" + eventId,
            Ticket = TicketSamples.Snapshot(bookingId, "4C", "4D"),
        };
    }

    private static IssueTicketHandler HandlerFor(TicketHarness harness)
    {
        return new IssueTicketHandler(harness.Renderer, harness.Store, harness.Announcer, NullLogger.Instance);
    }

    private static IssueTicketFunction FunctionFor(TicketHarness harness)
    {
        return new IssueTicketFunction(
            harness.Renderer, harness.Store, harness.Announcer, NullLogger<IssueTicketFunction>.Instance);
    }

    [Fact]
    public async Task HandleAsync_ConfirmedBooking_StoresFirstIssueWithItsChangeTime()
    {
        using var harness = new TicketHarness();
        IssueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(ConfirmedInput("B-77", "E-1"), CancellationToken.None);

        StoreTicketRequest? stored = harness.Store.Get("B-77");
        Assert.NotNull(stored);
        Assert.Equal(TicketIssueKinds.FirstIssue, stored.IssueKind);
        Assert.Equal(TicketSamples.ConfirmedAt, stored.ChangeTime);
        Assert.Equal("E-1", stored.SourceEventId);
    }

    [Fact]
    public async Task HandleAsync_ConfirmedBooking_AnnouncesFirstIssueWithWhatTheEmailNeeds()
    {
        using var harness = new TicketHarness();
        IssueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(ConfirmedInput("B-77", "E-1"), CancellationToken.None);

        TicketReadyAnnouncement announcement = Assert.Single(harness.Announcer.Announcements);
        Assert.Equal("B-77", announcement.BookingId);
        Assert.Equal(TicketIssueKinds.FirstIssue, announcement.IssueKind);
        Assert.Equal(TicketSamples.ConfirmedAt, announcement.ChangeTime);
        Assert.Equal("Riya Sharma", announcement.Contact.Name);
        Assert.Equal("riya@example.com", announcement.Contact.Email);
        Assert.Equal("Mumbai", announcement.Trip.Source.Name);
        Assert.Equal("Pune", announcement.Trip.Destination.Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero), announcement.Trip.DepartureUtc);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_KeepsOneTicketAndAnnouncesItAgain()
    {
        using var harness = new TicketHarness();
        IssueTicketHandler handler = HandlerFor(harness);
        IssueTicketInput input = ConfirmedInput("B-77", "E-1");

        await handler.HandleAsync(input, CancellationToken.None);
        string firstText = TicketPdf.Text(harness.Store.Get("B-77")!.Document);
        await handler.HandleAsync(input, CancellationToken.None);
        string secondText = TicketPdf.Text(harness.Store.Get("B-77")!.Document);

        Assert.Equal(1, harness.Store.TicketCount);
        Assert.Equal(firstText, secondText);
        Assert.Equal(2, harness.Announcer.Announcements.Count);
        Assert.Equal(
            TicketReadyIdentity.For("B-77", harness.Announcer.Announcements[0].ChangeTime),
            TicketReadyIdentity.For("B-77", harness.Announcer.Announcements[1].ChangeTime));
    }

    [Fact]
    public async Task HandleAsync_NewerChangeAlreadyStored_SkipsTheLateConfirmation()
    {
        using var harness = new TicketHarness();
        var newerTicket = new StoreTicketRequest
        {
            BookingId = "B-77",
            ChangeTime = TicketSamples.SeatsChangedAt,
            IssueKind = TicketIssueKinds.Reissue,
            SourceEventId = "E-9",
            Document = new byte[] { 1 },
        };
        await harness.Store.StoreAsync(newerTicket, CancellationToken.None);
        IssueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(ConfirmedInput("B-77", "E-1"), CancellationToken.None);

        Assert.Equal("E-9", harness.Store.Get("B-77")!.SourceEventId);
        Assert.Empty(harness.Announcer.Announcements);
    }

    [Fact]
    public async Task ValidateAsync_ConfirmedBookingWithNoSeats_Fails()
    {
        var validator = new IssueTicketValidator();
        IssueTicketInput input = ConfirmedInput("B-95", "E-5") with { Ticket = TicketSamples.WithoutSeats("B-95") };

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(
            () => validator.ValidateAndThrowAsync(input, CancellationToken.None));

        Assert.Contains("no seats", exception.Message);
    }

    [Fact]
    public async Task HandleAsync_OneBadRecordInTheBatch_ReportsOnlyThatRecord()
    {
        using var harness = new TicketHarness();
        IssueTicketFunction function = FunctionFor(harness);
        SQSEvent batch = TicketSamples.Batch(
            Record("B-95", "E-95", TicketSamples.WithoutSeats("B-95")),
            Record("B-96", "E-96", TicketSamples.Snapshot("B-96", "4C", "4D")),
            Record("B-97", "E-97", TicketSamples.Snapshot("B-97", "4C", "4D")));

        SQSBatchResponse response = await function.HandleAsync(batch, TicketSamples.Context(), harness.Tracer);

        SQSBatchResponse.BatchItemFailure failure = Assert.Single(response.BatchItemFailures);
        Assert.Equal("M-E-95", failure.ItemIdentifier);
        Assert.Equal(2, harness.Announcer.Announcements.Count);
        Assert.Null(harness.Store.Get("B-95"));
    }

    [Fact]
    public async Task HandleAsync_StorageRefusesWrites_ReportsFailureAndAnnouncesNothing()
    {
        using var harness = new TicketHarness();
        harness.Store.FailWith = new InvalidOperationException("access denied");
        IssueTicketFunction function = FunctionFor(harness);
        SQSEvent batch = TicketSamples.Batch(Record("B-98", "E-98", TicketSamples.Snapshot("B-98", "4C", "4D")));

        SQSBatchResponse response = await function.HandleAsync(batch, TicketSamples.Context(), harness.Tracer);

        Assert.Single(response.BatchItemFailures);
        Assert.Empty(harness.Announcer.Announcements);
    }

    [Fact]
    public async Task HandleAsync_ParkedEventReplayedAfterTheFix_MakesTheTicket()
    {
        using var harness = new TicketHarness();
        harness.Store.FailWith = new InvalidOperationException("access denied");
        IssueTicketFunction function = FunctionFor(harness);
        SQSEvent batch = TicketSamples.Batch(Record("B-98", "E-98", TicketSamples.Snapshot("B-98", "4C", "4D")));
        await function.HandleAsync(batch, TicketSamples.Context(), harness.Tracer);
        harness.Store.FailWith = null;

        SQSBatchResponse response = await function.HandleAsync(batch, TicketSamples.Context(), harness.Tracer);

        Assert.Empty(response.BatchItemFailures);
        TicketReadyAnnouncement announcement = Assert.Single(harness.Announcer.Announcements);
        Assert.Equal(TicketIssueKinds.FirstIssue, announcement.IssueKind);
    }

    [Fact]
    public async Task HandleAsync_SeatChangeEventOnTheConfirmedQueue_IsReportedAsFailed()
    {
        using var harness = new TicketHarness();
        IssueTicketFunction function = FunctionFor(harness);
        EventEnvelope<TicketSnapshotMessage> wrongType = TicketSamples.Envelope(
            TicketSamples.SeatsChangedType, "E-3", TicketSamples.SeatsChangedAt, TicketSamples.Snapshot("B-77", "5C", "5D"));
        SQSEvent batch = TicketSamples.Batch(TicketSamples.Message(wrongType, "M-E-3"));

        SQSBatchResponse response = await function.HandleAsync(batch, TicketSamples.Context(), harness.Tracer);

        Assert.Single(response.BatchItemFailures);
        Assert.Equal(0, harness.Store.TicketCount);
    }

    private static SQSEvent.SQSMessage Record(string bookingId, string eventId, TicketSnapshotMessage snapshot)
    {
        EventEnvelope<TicketSnapshotMessage> envelope = TicketSamples.Envelope(
            TicketSamples.ConfirmedType, eventId, TicketSamples.ConfirmedAt, snapshot);
        return TicketSamples.Message(envelope, "M-" + eventId);
    }
}
