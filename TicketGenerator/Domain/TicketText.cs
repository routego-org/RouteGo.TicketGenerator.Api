namespace TicketGenerator.Domain;

/// <summary>Every fixed word printed on an e-ticket. Only the event's values vary.</summary>
public static class TicketText
{
    public const string Title = "RouteGo e-ticket";

    public const string BookingIdLabel = "Booking ID";

    public const string IssuedLabel = "Issued";

    public const string ReissueMarker = "Re-issued after seat change";

    public const string RouteArrow = "→";

    public const string DepartureLabel = "Departs";

    public const string OperatorLabel = "Operator";

    public const string BusLabel = "Bus";

    public const string AirConditioned = "(AC)";

    public const string NonAirConditioned = "(Non-AC)";

    public const string TotalLabel = "Total paid";

    public const string BoardingNotesLabel = "Boarding notes";

    public static readonly string[] SeatHeadings = { "Seat", "Class", "Passenger", "Age", "Gender", "Fare" };

    public static readonly string[] BoardingNotes =
    {
        "Please reach the boarding point at least 15 minutes before departure.",
        "Carry a valid government photo ID for every passenger.",
        "Show this e-ticket (printed or on your phone) to the bus staff when boarding.",
        "Contact the operator for the exact boarding point and any changes on the day of travel.",
    };
}
