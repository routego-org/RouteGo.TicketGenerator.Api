// Tests for the PDF e-ticket: what a person reading it would see.
namespace TicketGenerator.Infrastructure;

public sealed class QuestPdfTicketRendererTests
{
    private static string Render(string issueKind, DateTimeOffset changeTime)
    {
        var renderer = new QuestPdfTicketRenderer();
        TicketSnapshotMessage snapshot = TicketSamples.Snapshot("B-77", "4C", "4D");
        byte[] document = renderer.Render(new TicketRenderRequest(snapshot, issueKind, changeTime));
        return TicketPdf.Text(document);
    }

    [Fact]
    public void Render_FirstIssue_PrintsTheBookingAndTripInIndianTimeAndRupees()
    {
        string text = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);

        Assert.Contains("RouteGo e-ticket", text);
        Assert.Contains("Booking ID B-77", text);
        Assert.Contains("Issued 25 Oct 2026", text);
        Assert.Contains("Mumbai → Pune", text);
        Assert.Contains("Mon 2 Nov 2026", text);
        Assert.Contains("Departs 22:30 IST", text);
        Assert.Contains("Arrives 03:15 IST (3 Nov)", text);
        Assert.Contains("Total paid ₹1,700", text);
        Assert.Contains("Operator Neeta Travels", text);
        Assert.Contains("Shivneri Sleeper (AC)", text);
    }

    [Fact]
    public void Render_FirstIssue_ListsEachSeatWithItsPassengerAndHasNoReissueMarker()
    {
        string text = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);

        Assert.Contains("4C", text);
        Assert.Contains("Riya Sharma", text);
        Assert.Contains("Amit Sharma", text);
        Assert.DoesNotContain("Re-issued after seat change", text);
    }

    [Fact]
    public void Render_Reissue_PrintsTheReissueMarker()
    {
        string text = Render(TicketIssueKinds.Reissue, TicketSamples.SeatsChangedAt);

        Assert.Contains("Re-issued after seat change", text);
        Assert.Contains("Issued 26 Oct 2026", text);
    }

    [Fact]
    public void Render_AnyTicket_NeverPrintsTheContactEmail()
    {
        string text = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);

        Assert.DoesNotContain("riya@example.com", text);
        Assert.DoesNotContain("@", text);
    }

    [Fact]
    public void Render_AnyTicket_PrintsTheFourBoardingNotes()
    {
        string text = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);

        Assert.Contains("Boarding notes", text);
        Assert.Contains("Carry a valid government photo ID for every passenger.", text);
    }

    [Fact]
    public void Render_SameRequestTwice_PrintsTheSameDocument()
    {
        string first = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);
        string second = Render(TicketIssueKinds.FirstIssue, TicketSamples.ConfirmedAt);

        Assert.Equal(first, second);
    }
}
