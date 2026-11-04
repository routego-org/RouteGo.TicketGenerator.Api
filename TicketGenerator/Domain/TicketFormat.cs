using System.Globalization;

namespace TicketGenerator.Domain;

/// <summary>
/// How times and amounts are printed on a ticket. Times are kept in UTC and printed in IST;
/// amounts are whole rupees with Indian digit grouping.
/// </summary>
public static class TicketFormat
{
    private const string RupeeSign = "₹";

    // India has no daylight saving, so a fixed offset is exact and needs no time zone database
    // (which a minimal Lambda image may not carry).
    private static readonly TimeSpan _istOffset = new TimeSpan(5, 30, 0);

    public static DateTimeOffset ToIst(DateTimeOffset moment)
    {
        return moment.ToOffset(_istOffset);
    }

    /// <summary>For example "25 Oct 2026".</summary>
    public static string IssuedDate(DateTimeOffset changeTime)
    {
        return ToIst(changeTime).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>For example "Mon 2 Nov 2026".</summary>
    public static string TravelDate(DateTimeOffset departure)
    {
        return ToIst(departure).ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>For example "22:30".</summary>
    public static string Time(DateTimeOffset moment)
    {
        return ToIst(moment).ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>For example "Arrives 03:15 IST (3 Nov)"; the date shows only when it differs from departure.</summary>
    public static string Arrival(DateTimeOffset departure, DateTimeOffset arrival)
    {
        DateTimeOffset arrivalIst = ToIst(arrival);
        string text = "Arrives " + Time(arrival) + " IST";
        if (arrivalIst.Date != ToIst(departure).Date)
        {
            text += " (" + arrivalIst.ToString("d MMM", CultureInfo.InvariantCulture) + ")";
        }

        return text;
    }

    /// <summary>For example "₹850", "₹1,700" or "₹1,25,000".</summary>
    public static string Rupees(long amount)
    {
        string digits = amount.ToString(CultureInfo.InvariantCulture);
        if (digits.Length <= 3)
        {
            return RupeeSign + digits;
        }

        string lastThree = digits.Substring(digits.Length - 3);
        string rest = digits.Substring(0, digits.Length - 3);
        var groups = new List<string>();
        while (rest.Length > 2)
        {
            groups.Insert(0, rest.Substring(rest.Length - 2));
            rest = rest.Substring(0, rest.Length - 2);
        }

        if (rest.Length > 0)
        {
            groups.Insert(0, rest);
        }

        groups.Add(lastThree);
        return RupeeSign + string.Join(",", groups);
    }
}
