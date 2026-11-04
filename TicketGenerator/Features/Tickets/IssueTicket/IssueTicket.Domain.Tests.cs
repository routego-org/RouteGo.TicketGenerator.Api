// Tests for the Domain rules both ticket slices rely on (formatting, newest-wins, announcement identity).
namespace TicketGenerator.Features.Tickets.IssueTicket;

public sealed class TicketDomainRulesTests
{
    [Theory]
    [InlineData(850L, "₹850")]
    [InlineData(1700L, "₹1,700")]
    [InlineData(125000L, "₹1,25,000")]
    [InlineData(0L, "₹0")]
    [InlineData(10000000L, "₹1,00,00,000")]
    public void Rupees_WholeAmount_UsesIndianGrouping(long amount, string expected)
    {
        string text = TicketFormat.Rupees(amount);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void Arrival_SameDayInIst_ShowsNoDate()
    {
        var departure = new DateTimeOffset(2026, 11, 2, 3, 0, 0, TimeSpan.Zero);
        var arrival = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

        string text = TicketFormat.Arrival(departure, arrival);

        Assert.Equal("Arrives 13:30 IST", text);
    }

    [Fact]
    public void Arrival_NextDayInIst_ShowsThatDate()
    {
        var departure = new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero);
        var arrival = new DateTimeOffset(2026, 11, 2, 21, 45, 0, TimeSpan.Zero);

        string text = TicketFormat.Arrival(departure, arrival);

        Assert.Equal("Arrives 03:15 IST (3 Nov)", text);
    }

    [Theory]
    [InlineData(-1, TicketChangeOrder.Older)]
    [InlineData(0, TicketChangeOrder.Same)]
    [InlineData(1, TicketChangeOrder.Newer)]
    public void CompareChange_IncomingAgainstStored_FollowsNewestWins(int secondsFromStored, TicketChangeOrder expected)
    {
        var stored = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        TicketChangeOrder order = TicketRules.CompareChange(stored, stored.AddSeconds(secondsFromStored));

        Assert.Equal(expected, order);
    }

    [Fact]
    public void For_SameBookingAndChangeTime_GivesTheSameIdentity()
    {
        var change = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        Guid first = TicketReadyIdentity.For("B-77", change);
        Guid second = TicketReadyIdentity.For("B-77", change.ToOffset(TimeSpan.FromHours(5.5)));

        Assert.Equal(first, second);
    }

    [Fact]
    public void For_DifferentChangeTime_GivesADifferentIdentity()
    {
        var change = new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        Guid first = TicketReadyIdentity.For("B-77", change);
        Guid second = TicketReadyIdentity.For("B-77", change.AddSeconds(1));

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("B-77", true)]
    [InlineData("3f2a9c1e-7b1d-4d6e-9d0a-1c2b3d4e5f60", true)]
    [InlineData("../B-77", false)]
    [InlineData("B 77", false)]
    [InlineData("", false)]
    public void IsSafeBookingId_VariousIds_AllowsOnlyPlainCharacters(string bookingId, bool expected)
    {
        bool isSafe = TicketRules.IsSafeBookingId(bookingId);

        Assert.Equal(expected, isSafe);
    }
}
