using System.Globalization;
using System.Text.Json;

namespace OceanFresh.SortingSystem.Infrastructure;

public static class ModelLabelMapParser
{
    public static Dictionary<int, string> Parse(string? labelMapJson)
    {
        if (string.IsNullOrWhiteSpace(labelMapJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(labelMapJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var result = new Dictionary<int, string>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idFromKey) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var label = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        result[idFromKey] = label.Trim();
                    }

                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var idFromValue) &&
                    !string.IsNullOrWhiteSpace(property.Name))
                {
                    result[idFromValue] = property.Name.Trim();
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
