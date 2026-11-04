using System.Text.Json.Serialization;

namespace TicketGenerator.Domain;

/// <summary>
/// Reads a seat as Booking publishes it, with the passenger's name, age and gender beside the seat's
/// own fields, and also with them inside a <c>passenger</c> object. Booking is the authority for the
/// event, so its own shape must work.
/// </summary>
public sealed class SeatMessageConverter : JsonConverter<SeatMessage>
{
    private const string PassengerProperty = "passenger";

    public override SeatMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement seat = document.RootElement;
        if (seat.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A seat must be an object.");
        }

        JsonElement passenger = seat;
        if (seat.TryGetProperty(PassengerProperty, out JsonElement nested) && nested.ValueKind == JsonValueKind.Object)
        {
            passenger = nested;
        }

        return new SeatMessage
        {
            SeatNo = ReadText(seat, "seatNo"),
            SeatClass = ReadText(seat, "seatClass"),
            Fare = ReadNumber(seat, "fare"),
            Passenger = new PassengerMessage(ReadText(passenger, "name"), ReadAge(passenger), ReadText(passenger, "gender")),
        };
    }

    public override void Write(Utf8JsonWriter writer, SeatMessage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("seatNo", value.SeatNo);
        writer.WriteString("seatClass", value.SeatClass);
        WriteNumber(writer, "fare", value.Fare);
        writer.WriteStartObject(PassengerProperty);
        writer.WriteString("name", value.Passenger.Name);
        WriteNumber(writer, "age", value.Passenger.Age);
        writer.WriteString("gender", value.Passenger.Gender);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string ReadText(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"The seat is missing the text value '{property}'.");
        }

        return value.GetString()!;
    }

    private static long? ReadNumber(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetInt64();
    }

    private static int? ReadAge(JsonElement passenger)
    {
        long? age = ReadNumber(passenger, "age");
        if (age is null)
        {
            return null;
        }

        return checked((int)age.Value);
    }

    private static void WriteNumber(Utf8JsonWriter writer, string property, long? value)
    {
        if (value is null)
        {
            writer.WriteNull(property);
            return;
        }

        writer.WriteNumber(property, value.Value);
    }
}
