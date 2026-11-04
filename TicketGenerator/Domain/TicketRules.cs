namespace TicketGenerator.Domain;

/// <summary>
/// The rules that decide whether a booking event can become a ticket, and whether it may replace
/// the ticket already stored. Kept here so no slice restates them.
/// </summary>
public static class TicketRules
{
    public const string Currency = "INR";

    private static readonly string[] _genders = { "Male", "Female", "Other" };

    /// <summary>A booking id becomes part of the storage name, so only plain characters are allowed.</summary>
    public static bool IsSafeBookingId(string? bookingId)
    {
        if (string.IsNullOrWhiteSpace(bookingId))
        {
            return false;
        }

        foreach (char character in bookingId)
        {
            bool isAllowed = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';
            if (!isAllowed)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidGender(string? gender)
    {
        return gender != null && Array.IndexOf(_genders, gender) >= 0;
    }

    public static bool IsValidAge(int? age)
    {
        return age != null && age.Value >= 0;
    }

    /// <summary>Fares and totals are whole rupees, never negative.</summary>
    public static bool IsValidAmount(long? amount)
    {
        return amount != null && amount.Value >= 0;
    }

    public static bool IsIndianRupees(string? currency)
    {
        return string.Equals(currency, Currency, StringComparison.Ordinal);
    }

    /// <summary>Newest booking change wins: compares the incoming change time with the stored one.</summary>
    public static TicketChangeOrder CompareChange(DateTimeOffset stored, DateTimeOffset incoming)
    {
        if (incoming < stored)
        {
            return TicketChangeOrder.Older;
        }

        if (incoming == stored)
        {
            return TicketChangeOrder.Same;
        }

        return TicketChangeOrder.Newer;
    }
}
