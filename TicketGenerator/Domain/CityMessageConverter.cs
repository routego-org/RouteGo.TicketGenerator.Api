using System.Text.Json.Serialization;

namespace TicketGenerator.Domain;

/// <summary>
/// Reads a city as Booking publishes it, the city's name as plain text, and also as an object that
/// carries the name. Booking is the authority for the event, so its own shape must work.
/// </summary>
public sealed class CityMessageConverter : JsonConverter<CityMessage>
{
    private const string NameProperty = "name";

    public override CityMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement city = document.RootElement;
        if (city.ValueKind == JsonValueKind.String)
        {
            return new CityMessage(city.GetString()!);
        }

        if (city.ValueKind != JsonValueKind.Object || !city.TryGetProperty(NameProperty, out JsonElement name))
        {
            throw new JsonException("A city must be a name or an object with a name.");
        }

        if (name.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("A city name must be text.");
        }

        return new CityMessage(name.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, CityMessage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(NameProperty, value.Name);
        writer.WriteEndObject();
    }
}
