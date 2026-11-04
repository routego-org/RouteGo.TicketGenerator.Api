namespace TicketGenerator.Common.Ports;

/// <summary>What the renderer needs to draw one ticket.</summary>
public sealed record TicketRenderRequest(TicketSnapshotMessage Ticket, string IssueKind, DateTimeOffset ChangeTime);

/// <summary>Draws the PDF e-ticket. The same request always gives the same document.</summary>
public interface ITicketRenderer
{
    byte[] Render(TicketRenderRequest request);
}
