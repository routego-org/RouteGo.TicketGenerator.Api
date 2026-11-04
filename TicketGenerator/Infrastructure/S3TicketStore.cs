using System.Globalization;
using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace TicketGenerator.Infrastructure;

/// <summary>
/// Keeps each booking's ticket as one private object, <c>tickets/{bookingId}.pdf</c>, and remembers
/// the booking change it shows in the object's metadata. The newest-change-wins check and the write
/// are one conditional request (create only if absent, or replace only the version that was read),
/// so two events for the same booking handled at once cannot let an older ticket win.
/// </summary>
public sealed class S3TicketStore : ITicketStore
{
    private const string KeyPrefix = "tickets/";
    private const string KeySuffix = ".pdf";
    private const string ChangeTimeMetadata = "ticket-change-time";
    private const string IssueKindMetadata = "issue-kind";
    private const string SourceEventMetadata = "source-event-id";
    private const string IssuedAtMetadata = "issued-at";
    private const string PdfContentType = "application/pdf";
    private const string CreateOnly = "*";

    // Two writers racing for one booking is rare and settles in one more read; a handful of tries
    // is enough, and beyond that the event is retried by the queue.
    private const int MaxAttempts = 4;

    private readonly IAmazonS3 _s3;
    private readonly IClock _clock;
    private readonly string _bucket;

    public S3TicketStore(IAmazonS3 s3, IClock clock, IConfiguration configuration)
    {
        _s3 = s3;
        _clock = clock;
        _bucket = configuration[ConfigKeys.TicketBucket]!;
    }

    public async Task<TicketStoreOutcome> StoreAsync(StoreTicketRequest request, CancellationToken cancellationToken)
    {
        string key = KeyPrefix + request.BookingId + KeySuffix;
        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            StoredTicket? stored = await GetStoredAsync(key, cancellationToken);
            if (stored != null)
            {
                TicketChangeOrder order = TicketRules.CompareChange(stored.ChangeTime, request.ChangeTime);
                if (order == TicketChangeOrder.Older)
                {
                    return TicketStoreOutcome.SkippedOlder;
                }
            }

            string? replaceVersion = stored?.ETag;
            bool written = await TryWriteAsync(key, request, replaceVersion, cancellationToken);
            if (written)
            {
                return TicketStoreOutcome.Stored;
            }
        }

        throw new InvalidOperationException(
            "The ticket for booking " + request.BookingId + " kept changing while it was being stored.");
    }

    private async Task<StoredTicket?> GetStoredAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            GetObjectMetadataResponse response = await _s3.GetObjectMetadataAsync(_bucket, key, cancellationToken);
            string? changeText = response.Metadata[ChangeTimeMetadata];
            bool isReadable = DateTimeOffset.TryParse(
                changeText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset changeTime);
            if (!isReadable)
            {
                throw new InvalidOperationException("The stored object " + key + " has no readable change time.");
            }

            return new StoredTicket(response.ETag, changeTime);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<bool> TryWriteAsync(
        string key,
        StoreTicketRequest request,
        string? replaceVersion,
        CancellationToken cancellationToken)
    {
        using var document = new MemoryStream(request.Document);
        var put = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = document,
            ContentType = PdfContentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AWSKMS,
        };
        put.Metadata[ChangeTimeMetadata] = request.ChangeTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        put.Metadata[IssueKindMetadata] = request.IssueKind;
        put.Metadata[SourceEventMetadata] = request.SourceEventId;
        put.Metadata[IssuedAtMetadata] = _clock.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (replaceVersion == null)
        {
            put.IfNoneMatch = CreateOnly;
        }
        else
        {
            put.IfMatch = replaceVersion;
        }

        try
        {
            await _s3.PutObjectAsync(put, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (IsLostRace(exception))
        {
            return false;
        }
    }

    private static bool IsLostRace(AmazonS3Exception exception)
    {
        return exception.StatusCode == HttpStatusCode.PreconditionFailed
            || exception.StatusCode == HttpStatusCode.Conflict;
    }

    private sealed record StoredTicket(string ETag, DateTimeOffset ChangeTime);
}
