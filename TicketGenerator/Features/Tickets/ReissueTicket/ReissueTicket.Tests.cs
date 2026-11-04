// Tests for Re-issue ticket.  Spec: CAP-TKT-002
// One test per acceptance criterion in SPEC.md, PLUS the idempotency test. The handler runs against
// the real PDF renderer with an in-memory store and announcer: the S3 store's conditional writes
// need a real S3 (LocalStack) and are covered by the deployment checks, not here.
namespace TicketGenerator.Features.Tickets.ReissueTicket;

public sealed class ReissueTicketTests
{
    private static ReissueTicketInput SeatChangeInput(string bookingId, DateTimeOffset occurredAt, string firstSeat, string secondSeat)
    {
        return new ReissueTicketInput
        {
            EventId = "E-seat-" + firstSeat,
            EventType = TicketSamples.SeatsChangedType,
            OccurredAt = occurredAt,
            CorrelationId = "corr-seat",
            Ticket = TicketSamples.Snapshot(bookingId, firstSeat, secondSeat),
        };
    }

    private static StoreTicketRequest StoredTicket(string bookingId, DateTimeOffset changeTime, string kind)
    {
        return new StoreTicketRequest
        {
            BookingId = bookingId,
            ChangeTime = changeTime,
            IssueKind = kind,
            SourceEventId = "E-stored",
            Document = new byte[] { 1 },
        };
    }

    private static ReissueTicketHandler HandlerFor(TicketHarness harness)
    {
        return new ReissueTicketHandler(harness.Renderer, harness.Store, harness.Announcer, NullLogger.Instance);
    }

    [Fact]
    public async Task HandleAsync_SeatChange_ReplacesTheStoredTicketWithTheNewSeats()
    {
        using var harness = new TicketHarness();
        await harness.Store.StoreAsync(
            StoredTicket("B-77", TicketSamples.ConfirmedAt, TicketIssueKinds.FirstIssue), CancellationToken.None);
        ReissueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(SeatChangeInput("B-77", TicketSamples.SeatsChangedAt, "5C", "5D"), CancellationToken.None);

        StoreTicketRequest stored = harness.Store.Get("B-77")!;
        string text = TicketPdf.Text(stored.Document);
        Assert.Equal(1, harness.Store.TicketCount);
        Assert.Equal(TicketSamples.SeatsChangedAt, stored.ChangeTime);
        Assert.Contains("5C", text);
        Assert.DoesNotContain("4C", text);
        Assert.Contains("Re-issued after seat change", text);
    }

    [Fact]
    public async Task HandleAsync_SeatChange_AnnouncesAReissueWithItsChangeTime()
    {
        using var harness = new TicketHarness();
        ReissueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(SeatChangeInput("B-77", TicketSamples.SeatsChangedAt, "5C", "5D"), CancellationToken.None);

        TicketReadyAnnouncement announcement = Assert.Single(harness.Announcer.Announcements);
        Assert.Equal(TicketIssueKinds.Reissue, announcement.IssueKind);
        Assert.Equal(TicketSamples.SeatsChangedAt, announcement.ChangeTime);
    }

    [Fact]
    public async Task HandleAsync_OlderSeatChange_KeepsTheNewerTicket()
    {
        using var harness = new TicketHarness();
        DateTimeOffset newerChange = TicketSamples.SeatsChangedAt.AddDays(1);
        await harness.Store.StoreAsync(
            StoredTicket("B-77", newerChange, TicketIssueKinds.Reissue), CancellationToken.None);
        ReissueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(SeatChangeInput("B-77", TicketSamples.SeatsChangedAt, "5C", "5D"), CancellationToken.None);

        Assert.Equal(newerChange, harness.Store.Get("B-77")!.ChangeTime);
        Assert.Empty(harness.Announcer.Announcements);
    }

    [Fact]
    public async Task HandleAsync_SeatChangeBeforeAnyTicket_MakesTheTicketAsAReissue()
    {
        using var harness = new TicketHarness();
        ReissueTicketHandler handler = HandlerFor(harness);

        await handler.HandleAsync(SeatChangeInput("B-91", TicketSamples.SeatsChangedAt, "5C", "5D"), CancellationToken.None);

        StoreTicketRequest stored = harness.Store.Get("B-91")!;
        Assert.Equal(TicketIssueKinds.Reissue, stored.IssueKind);
        Assert.Contains("Re-issued after seat change", TicketPdf.Text(stored.Document));
        Assert.Equal(TicketIssueKinds.Reissue, Assert.Single(harness.Announcer.Announcements).IssueKind);
    }

    [Fact]
    public async Task HandleAsync_SameSeatChangeTwice_KeepsOneTicket()
    {
        using var harness = new TicketHarness();
        ReissueTicketHandler handler = HandlerFor(harness);
        ReissueTicketInput input = SeatChangeInput("B-77", TicketSamples.SeatsChangedAt, "5C", "5D");

        await handler.HandleAsync(input, CancellationToken.None);
        await handler.HandleAsync(input, CancellationToken.None);

        Assert.Equal(1, harness.Store.TicketCount);
        Assert.Equal(2, harness.Announcer.Announcements.Count);
    }

    [Fact]
    public async Task HandleAsync_StorageRefusesTheReissue_ReportsFailureAndLeavesTheCurrentTicket()
    {
        using var harness = new TicketHarness();
        await harness.Store.StoreAsync(
            StoredTicket("B-77", TicketSamples.ConfirmedAt, TicketIssueKinds.FirstIssue), CancellationToken.None);
        harness.Store.FailWith = new InvalidOperationException("access denied");
        var function = new ReissueTicketFunction(
            harness.Renderer, harness.Store, harness.Announcer, NullLogger<ReissueTicketFunction>.Instance);
        EventEnvelope<TicketSnapshotMessage> envelope = TicketSamples.Envelope(
            TicketSamples.SeatsChangedType, "E-3", TicketSamples.SeatsChangedAt, TicketSamples.Snapshot("B-77", "5C", "5D"));

        SQSBatchResponse response = await function.HandleAsync(
            TicketSamples.Batch(TicketSamples.Message(envelope, "M-E-3")), TicketSamples.Context(), harness.Tracer);

        Assert.Single(response.BatchItemFailures);
        Assert.Equal(TicketSamples.ConfirmedAt, harness.Store.Get("B-77")!.ChangeTime);
        Assert.Empty(harness.Announcer.Announcements);
    }
}
