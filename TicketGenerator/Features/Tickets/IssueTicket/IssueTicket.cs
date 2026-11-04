// Feature: Issue ticket.  Spec: CAP-TKT-001
// Trigger: SQS: ticket-confirmed-q (BookingConfirmed)  (the event source — wired in IaC, see SPEC.md / references/triggers.md)
//
// The ONLY usings in a slice file, and they belong here rather than in GlobalUsings.cs: project
// setup's Observability/*.cs and Startup.cs carry the same ones at file level, so a global using
// would make those redundant and IDE0005 (an error here) would fail the build.
using System.Diagnostics;            // Activity, ActivityEvent — the function's own span
using OpenTelemetry.Trace;           // TracerProvider — the per-invocation flush
using TicketGenerator.Observability;   // Diagnostics.ActivitySource

namespace TicketGenerator.Features.Tickets.IssueTicket;

/// <summary>A BookingConfirmed event for a paid booking: the event's identity and times plus the ticket snapshot.</summary>
public sealed record IssueTicketInput
{
    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string CorrelationId { get; init; }

    public required TicketSnapshotMessage Ticket { get; init; }
}

public sealed class IssueTicketValidator : AbstractValidator<IssueTicketInput>
{
    private const string ExpectedEventType = "BookingConfirmed";

    public IssueTicketValidator()
    {
        RuleFor(input => input.EventId).NotEmpty().WithMessage("The event id is missing.");
        RuleFor(input => input.EventType).Equal(ExpectedEventType)
            .WithMessage("This queue only takes " + ExpectedEventType + " events.");
        RuleFor(input => input.CorrelationId).NotEmpty().WithMessage("The correlation id is missing.");
        RuleFor(input => input.Ticket).NotNull().SetValidator(new TicketSnapshotValidator());
    }
}

/// <summary>
/// Makes the first issue of a confirmed booking's e-ticket, stores it privately and announces TicketReady.
/// Repeat safety: the booking id with the event's change time is the key. The same event again
/// stores the identical ticket in the same place and announces it again; an event with an earlier
/// change time than the stored ticket is skipped and announces nothing.
/// </summary>
public sealed class IssueTicketHandler
{
    private readonly ITicketRenderer _renderer;
    private readonly ITicketStore _store;
    private readonly ITicketAnnouncer _announcer;
    private readonly ILogger _logger;

    public IssueTicketHandler(ITicketRenderer renderer, ITicketStore store, ITicketAnnouncer announcer, ILogger logger)
    {
        _renderer = renderer;
        _store = store;
        _announcer = announcer;
        _logger = logger;
    }

    public async Task HandleAsync(IssueTicketInput input, CancellationToken cancellationToken)
    {
        var renderRequest = new TicketRenderRequest(input.Ticket, TicketIssueKinds.FirstIssue, input.OccurredAt);
        byte[] document = _renderer.Render(renderRequest);

        StoreTicketRequest storeRequest = BuildStoreRequest(input, document);
        TicketStoreOutcome outcome = await _store.StoreAsync(storeRequest, cancellationToken);
        if (outcome == TicketStoreOutcome.SkippedOlder)
        {
            _logger.LogInformation(
                "Skipped event {EventId} for booking {BookingId}: a later change is already stored.",
                input.EventId,
                input.Ticket.BookingId);
            return;
        }

        TicketReadyAnnouncement announcement = BuildAnnouncement(input);
        await _announcer.AnnounceAsync(announcement, cancellationToken);
        _logger.LogInformation(
            "Stored and announced the first issue for booking {BookingId} from event {EventId}.",
            input.Ticket.BookingId,
            input.EventId);
    }

    private static StoreTicketRequest BuildStoreRequest(IssueTicketInput input, byte[] document)
    {
        return new StoreTicketRequest
        {
            BookingId = input.Ticket.BookingId,
            ChangeTime = input.OccurredAt,
            IssueKind = TicketIssueKinds.FirstIssue,
            SourceEventId = input.EventId,
            Document = document,
        };
    }

    private static TicketReadyAnnouncement BuildAnnouncement(IssueTicketInput input)
    {
        TripMessage trip = input.Ticket.Trip;
        return new TicketReadyAnnouncement
        {
            BookingId = input.Ticket.BookingId,
            IssueKind = TicketIssueKinds.FirstIssue,
            ChangeTime = input.OccurredAt,
            CorrelationId = input.CorrelationId,
            Contact = input.Ticket.Contact,
            Trip = new TicketReadyTrip(trip.Source, trip.Destination, trip.DepartureUtc!.Value),
        };
    }
}

// Function entry — the analog of a controller; the ONLY trigger-specific code in the slice.
public sealed class IssueTicketFunction
{
    private readonly IssueTicketHandler _handler;
    private readonly IssueTicketValidator _validator = new IssueTicketValidator();
    private readonly ILogger<IssueTicketFunction> _logger;

    public IssueTicketFunction(
        ITicketRenderer renderer,
        ITicketStore store,
        ITicketAnnouncer announcer,
        ILogger<IssueTicketFunction> logger)
    {
        _logger = logger;
        _handler = new IssueTicketHandler(renderer, store, announcer, logger);
    }

    [LambdaFunction(ResourceName = "IssueTicket")] // unique across slices — the deploy unit / event-source target
    public async Task<SQSBatchResponse> HandleAsync(
        SQSEvent sqsEvent,
        ILambdaContext context,
        [FromServices] TracerProvider tracerProvider)
    {
        // Scale-to-zero Lambda freezes between invocations — flush telemetry EVERY invocation
        // or it is lost. Never remove the finally.
        using Activity? activity = Diagnostics.ActivitySource.StartActivity("Tickets.IssueTicket");
        try
        {
            var failures = new List<SQSBatchResponse.BatchItemFailure>();
            CancellationToken cancellationToken = context.RequestAborted();
            foreach (SQSEvent.SQSMessage record in sqsEvent.Records)
            {
                bool succeeded = await TryProcessAsync(record, activity, cancellationToken);
                if (!succeeded)
                {
                    failures.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = record.MessageId });
                }
            }

            activity?.SetTag("messaging.batch.message_count", sqsEvent.Records.Count);
            activity?.SetTag("messaging.batch.failed_count", failures.Count);
            return new SQSBatchResponse(failures);
        }
        catch (Exception exception)
        {
            // The invocation itself failed — the span must say so or the trace reads as a success.
            activity?.AddException(exception);
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            tracerProvider.ForceFlush();
        }
    }

    // One bad record never holds up the others: its failure is reported to the queue so that only
    // this message is retried, and after the last retry the queue parks it.
    private async Task<bool> TryProcessAsync(
        SQSEvent.SQSMessage record,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        TicketFailureContext failureContext = TicketFailureContext.Unknown;
        try
        {
            IssueTicketInput input = ReadInput(record);
            failureContext = new TicketFailureContext(input.Ticket.BookingId, input.EventId, input.EventType);
            using IDisposable? scope = BeginScope(input);
            await _validator.ValidateAndThrowAsync(input, cancellationToken);
            await _handler.HandleAsync(input, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            bool isLastAttempt = TicketFailureReporter.IsLastAttempt(record.Attributes);
            TicketFailureReporter.Report(_logger, failureContext, isLastAttempt, exception);
            RecordFailure(activity, record, exception);
            return false;
        }
    }

    private static IssueTicketInput ReadInput(SQSEvent.SQSMessage record)
    {
        EventEnvelope<TicketSnapshotMessage>? envelope =
            JsonSerializer.Deserialize<EventEnvelope<TicketSnapshotMessage>>(record.Body, WireJson.Options);
        if (envelope == null)
        {
            throw new JsonException("The message body is empty.");
        }

        return new IssueTicketInput
        {
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            OccurredAt = envelope.OccurredAt,
            CorrelationId = envelope.CorrelationId,
            Ticket = envelope.Data,
        };
    }

    private IDisposable? BeginScope(IssueTicketInput input)
    {
        var properties = new Dictionary<string, object>
        {
            ["CorrelationId"] = input.CorrelationId,
            ["BookingId"] = input.Ticket.BookingId,
            ["EventId"] = input.EventId,
        };
        return _logger.BeginScope(properties);
    }

    // The failure goes on the span too: the invocation itself succeeded (the batch response reports
    // the failure), so the status stays Unset and this is an event, not an error.
    private static void RecordFailure(Activity? activity, SQSEvent.SQSMessage record, Exception exception)
    {
        activity?.AddEvent(new ActivityEvent("message.failed", tags: new ActivityTagsCollection
        {
            { "messaging.message.id", record.MessageId },
            { "exception.type", exception.GetType().FullName },
        }));
    }
}
