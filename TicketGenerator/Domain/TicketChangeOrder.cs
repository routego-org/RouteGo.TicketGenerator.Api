namespace TicketGenerator.Domain;

/// <summary>How an incoming booking change compares with the change the stored ticket shows.</summary>
public enum TicketChangeOrder
{
    /// <summary>The incoming change happened before the stored one; it must not replace the ticket.</summary>
    Older,

    /// <summary>The same change; the same ticket is made again.</summary>
    Same,

    /// <summary>A later change; it replaces the stored ticket.</summary>
    Newer,
}
