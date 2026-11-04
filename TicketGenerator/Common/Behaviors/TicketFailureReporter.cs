namespace TicketGenerator.Common.Behaviors;

/// <summary>The identifiers that name a failed event in a log line. They are all unknown when the body cannot be read.</summary>
public sealed record TicketFailureContext(string BookingId, string EventId, string EventType)
{
    public static readonly TicketFailureContext Unknown = new TicketFailureContext("unknown", "unknown", "unknown");
}

/// <summary>
/// Logs a failed ticket event. On the attempt after which the queue parks the event, it raises the
/// TICKET_NOT_GENERATED alert naming the booking; earlier attempts only warn, because they retry.
/// </summary>
public static class TicketFailureReporter
{
    // The queue parks an event after this many receives (the queue's redrive policy). The last
    // receive is the one whose failure parks it, so that is when the alert must fire.
    public const int MaxReceiveCount = 5;

    /// <summary>The machine-readable code the platform alarm channel switches on.</summary>
    public const string TicketNotGeneratedCode = "TICKET_NOT_GENERATED";

    private const string ReceiveCountAttribute = "ApproximateReceiveCount";

    public static bool IsLastAttempt(IDictionary<string, string>? attributes)
    {
        if (attributes == null)
        {
            return false;
        }

        bool hasCount = attributes.TryGetValue(ReceiveCountAttribute, out string? text);
        if (!hasCount)
        {
            return false;
        }

        bool isNumber = int.TryParse(text, out int receiveCount);
        return isNumber && receiveCount >= MaxReceiveCount;
    }

    public static void Report(
        ILogger logger,
        TicketFailureContext context,
        bool isLastAttempt,
        Exception exception)
    {
        // Only the exception type and our own validation messages are reported: they name what is
        // wrong without ever echoing passenger or contact details.
        string lastError = DescribeError(exception);
        if (isLastAttempt)
        {
            logger.LogError(
                "{Code} for booking {BookingId} from event {EventId} ({EventType}); the event is parked. Last error: {LastError}",
                TicketNotGeneratedCode,
                context.BookingId,
                context.EventId,
                context.EventType,
                lastError);
            return;
        }

        logger.LogWarning(
            "Ticket for booking {BookingId} from event {EventId} ({EventType}) failed and will be retried. Last error: {LastError}",
            context.BookingId,
            context.EventId,
            context.EventType,
            lastError);
    }

    private static string DescribeError(Exception exception)
    {
        if (exception is ValidationException validationException)
        {
            return "Unusable event data: " + string.Join(" ", validationException.Errors.Select(error => error.ErrorMessage));
        }

        return exception.GetType().Name;
    }
}
