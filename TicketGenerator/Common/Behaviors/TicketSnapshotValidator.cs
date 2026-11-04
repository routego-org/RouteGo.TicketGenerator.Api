namespace TicketGenerator.Common.Behaviors;

/// <summary>
/// Says whether a booking event carries everything a ticket needs. An event missing any of it is
/// unusable and must fail, so it is retried and then parked rather than printed half-empty.
/// </summary>
public sealed class TicketSnapshotValidator : AbstractValidator<TicketSnapshotMessage>
{
    public TicketSnapshotValidator()
    {
        RuleFor(snapshot => snapshot.BookingId)
            .Must(TicketRules.IsSafeBookingId)
            .WithMessage("The booking id is missing or contains characters other than letters, digits, '-' and '_'.");
        RuleFor(snapshot => snapshot.Contact).NotNull().SetValidator(new ContactValidator());
        RuleFor(snapshot => snapshot.Operator).NotNull().SetValidator(new OperatorValidator());
        RuleFor(snapshot => snapshot.Trip).NotNull().SetValidator(new TripValidator());
        RuleFor(snapshot => snapshot.Seats).NotEmpty().WithMessage("The booking lists no seats.");
        RuleForEach(snapshot => snapshot.Seats).NotNull().SetValidator(new SeatValidator());
        RuleFor(snapshot => snapshot.TotalAmount)
            .Must(TicketRules.IsValidAmount)
            .WithMessage("The total paid is missing or not a whole, non-negative rupee amount.");
        RuleFor(snapshot => snapshot.Currency)
            .Must(TicketRules.IsIndianRupees)
            .WithMessage("The currency must be INR.");
    }
}

internal sealed class ContactValidator : AbstractValidator<ContactMessage>
{
    public ContactValidator()
    {
        RuleFor(contact => contact.Name).NotEmpty().WithMessage("The contact name is missing.");
        RuleFor(contact => contact.Email).NotEmpty().WithMessage("The contact email is missing.");
    }
}

internal sealed class OperatorValidator : AbstractValidator<OperatorMessage>
{
    public OperatorValidator()
    {
        RuleFor(operatorMessage => operatorMessage.Name).NotEmpty().WithMessage("The operator name is missing.");
    }
}

internal sealed class TripValidator : AbstractValidator<TripMessage>
{
    public TripValidator()
    {
        RuleFor(trip => trip.Source).NotNull().WithMessage("The trip source is missing.");
        RuleFor(trip => trip.Source.Name).NotEmpty().When(trip => trip.Source != null)
            .WithMessage("The trip source city name is missing.");
        RuleFor(trip => trip.Destination).NotNull().WithMessage("The trip destination is missing.");
        RuleFor(trip => trip.Destination.Name).NotEmpty().When(trip => trip.Destination != null)
            .WithMessage("The trip destination city name is missing.");
        RuleFor(trip => trip.DepartureUtc).NotNull().WithMessage("The departure time is missing.");
        RuleFor(trip => trip.ArrivalUtc).NotNull().WithMessage("The arrival time is missing.");
        RuleFor(trip => trip.BusName).NotEmpty().WithMessage("The bus name is missing.");
        RuleFor(trip => trip.BusType).NotEmpty().WithMessage("The bus type is missing.");
        RuleFor(trip => trip.IsAc).NotNull().WithMessage("Whether the bus is air-conditioned is missing.");
    }
}

internal sealed class SeatValidator : AbstractValidator<SeatMessage>
{
    public SeatValidator()
    {
        RuleFor(seat => seat.SeatNo).NotEmpty().WithMessage("A seat label is missing.");
        RuleFor(seat => seat.SeatClass).NotEmpty().WithMessage("A seat class is missing.");
        RuleFor(seat => seat.Fare).Must(TicketRules.IsValidAmount)
            .WithMessage("A seat fare is missing or not a whole, non-negative rupee amount.");
        RuleFor(seat => seat.Passenger).NotNull().SetValidator(new PassengerValidator());
    }
}

internal sealed class PassengerValidator : AbstractValidator<PassengerMessage>
{
    public PassengerValidator()
    {
        RuleFor(passenger => passenger.Name).NotEmpty().WithMessage("A passenger name is missing.");
        RuleFor(passenger => passenger.Age).Must(TicketRules.IsValidAge)
            .WithMessage("A passenger age is missing or negative.");
        RuleFor(passenger => passenger.Gender).Must(TicketRules.IsValidGender)
            .WithMessage("A passenger gender must be Male, Female or Other.");
    }
}
