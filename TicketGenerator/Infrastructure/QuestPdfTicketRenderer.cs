using QRCoder;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace TicketGenerator.Infrastructure;

/// <summary>Draws the e-ticket with QuestPDF. Stateless: every call builds its own document.</summary>
public sealed class QuestPdfTicketRenderer : ITicketRenderer
{
    public QuestPdfTicketRenderer()
    {
        // QuestPDF's Community licence covers organisations under its revenue threshold; confirm
        // RouteGo qualifies, or buy a licence, before this runs in production.
        QuestPDF.Settings.License = LicenseType.Community;
        TicketFonts.Register();
    }

    public byte[] Render(TicketRenderRequest request)
    {
        var document = new TicketDocument(request);
        return document.GeneratePdf();
    }
}

/// <summary>
/// Registers the bundled Noto Sans fonts. QuestPDF's built-in font has no rupee sign, and a Lambda
/// has no system fonts, so the ticket carries its own (SIL Open Font License).
/// </summary>
internal static class TicketFonts
{
    public const string Family = "Noto Sans";

    private static readonly string[] _fontFiles = { "NotoSans-Regular.ttf", "NotoSans-Bold.ttf" };

    public static void Register()
    {
        foreach (string fileName in _fontFiles)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "fonts", fileName);
            using FileStream stream = File.OpenRead(path);
            FontManager.RegisterFontFromStream(stream);
        }
    }
}

/// <summary>
/// The layout of one e-ticket. Every word is fixed by <see cref="TicketText"/>; only the booking's
/// values vary. The email, customer and payment identifiers are never printed.
/// </summary>
internal sealed class TicketDocument : IDocument
{
    private const int QrPixelsPerModule = 10;
    private const float QrSize = 110;
    private const float PageMargin = 36;
    private const float BodyFontSize = 10;

    private readonly TicketRenderRequest _request;

    public TicketDocument(TicketRenderRequest request)
    {
        _request = request;
    }

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(PageMargin);
            page.DefaultTextStyle(style => style.FontFamily(TicketFonts.Family).FontSize(BodyFontSize));
            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(16).Element(ComposeContent);
        });
    }

    public DocumentMetadata GetMetadata()
    {
        // Pinned to the booking change, not the clock, so the same ticket is the same document.
        return new DocumentMetadata
        {
            Title = TicketText.Title,
            CreationDate = _request.ChangeTime,
            ModifiedDate = _request.ChangeTime,
        };
    }

    private void ComposeHeader(IContainer container)
    {
        TicketSnapshotMessage ticket = _request.Ticket;
        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(TicketText.Title).FontSize(22).Bold();
                column.Item().Text(TicketText.BookingIdLabel + " " + ticket.BookingId).FontSize(12);
                column.Item().Text(TicketText.IssuedLabel + " " + TicketFormat.IssuedDate(_request.ChangeTime));
                if (_request.IssueKind == TicketIssueKinds.Reissue)
                {
                    column.Item().Text(TicketText.ReissueMarker).Bold();
                }
            });
            row.ConstantItem(QrSize).Image(DrawQrCode(ticket.BookingId));
        });
    }

    private void ComposeContent(IContainer container)
    {
        TripMessage trip = _request.Ticket.Trip;
        DateTimeOffset departure = trip.DepartureUtc!.Value;
        DateTimeOffset arrival = trip.ArrivalUtc!.Value;
        string airConditioning = trip.IsAc!.Value ? TicketText.AirConditioned : TicketText.NonAirConditioned;
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text(trip.Source.Name + " " + TicketText.RouteArrow + " " + trip.Destination.Name)
                .FontSize(16).Bold();
            column.Item().Text(TicketFormat.TravelDate(departure));
            column.Item().Text(TicketText.DepartureLabel + " " + TicketFormat.Time(departure) + " IST");
            column.Item().Text(TicketFormat.Arrival(departure, arrival));
            column.Item().Text(TicketText.OperatorLabel + " " + _request.Ticket.Operator.Name);
            column.Item().Text(TicketText.BusLabel + " " + trip.BusName + " " + trip.BusType + " " + airConditioning);
            column.Item().PaddingTop(10).Element(ComposeSeats);
            column.Item().Text(TicketText.TotalLabel + " " + TicketFormat.Rupees(_request.Ticket.TotalAmount!.Value))
                .FontSize(13).Bold();
            column.Item().PaddingTop(10).Element(ComposeBoardingNotes);
        });
    }

    private void ComposeSeats(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(50);
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
                columns.ConstantColumn(35);
                columns.ConstantColumn(60);
                columns.ConstantColumn(60);
            });
            table.Header(header =>
            {
                foreach (string heading in TicketText.SeatHeadings)
                {
                    header.Cell().BorderBottom(1).PaddingVertical(3).Text(heading).Bold();
                }
            });
            foreach (SeatMessage seat in _request.Ticket.Seats)
            {
                AddSeatRow(table, seat);
            }
        });
    }

    private static void AddSeatRow(TableDescriptor table, SeatMessage seat)
    {
        table.Cell().PaddingVertical(3).Text(seat.SeatNo);
        table.Cell().PaddingVertical(3).Text(seat.SeatClass);
        table.Cell().PaddingVertical(3).Text(seat.Passenger.Name);
        table.Cell().PaddingVertical(3).Text(seat.Passenger.Age!.Value.ToString());
        table.Cell().PaddingVertical(3).Text(seat.Passenger.Gender);
        table.Cell().PaddingVertical(3).Text(TicketFormat.Rupees(seat.Fare!.Value));
    }

    private static void ComposeBoardingNotes(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(3);
            column.Item().Text(TicketText.BoardingNotesLabel).Bold();
            for (int index = 0; index < TicketText.BoardingNotes.Length; index++)
            {
                column.Item().Text((index + 1) + ". " + TicketText.BoardingNotes[index]);
            }
        });
    }

    // The QR code holds the booking id and nothing else.
    private static byte[] DrawQrCode(string bookingId)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(bookingId, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(QrPixelsPerModule);
    }
}
