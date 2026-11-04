using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TicketGenerator.Domain;

/// <summary>
/// The identity of a TicketReady announcement. The same booking and change time always give the
/// same identity, so Notification can recognise a repeat (a redelivery or a replay) and send one
/// email per identity.
/// </summary>
public static class TicketReadyIdentity
{
    private const int GuidByteCount = 16;

    public static Guid For(string bookingId, DateTimeOffset changeTime)
    {
        string changeText = changeTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("ticket-ready:" + bookingId + ":" + changeText));
        byte[] bytes = new byte[GuidByteCount];
        Array.Copy(hash, bytes, GuidByteCount);

        // Mark it as a name-based (version 5 style) identifier so it reads as a normal UUID.
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
