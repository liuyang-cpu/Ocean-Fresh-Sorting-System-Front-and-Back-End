using System.Text.Json;
using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ProductPostprocessConfigTests
{
    [Fact]
    public void Normalize_LegacyFullConfig_KeepsOnlyProductHeightRange()
    {
        var normalized = ProductPostprocessConfigFile.Normalize(
            """
            {
              "imgsz": 640,
              "device": "0",
              "conf": 0.25,
              "adjacent_frame_height_min": 42.5,
              "adjacent_frame_height_max": 126.75
            }
            """);

        using var document = JsonDocument.Parse(normalized);
        var root = document.RootElement;
        Assert.Equal(3, root.EnumerateObject().Count());
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(42.5m, root.GetProperty("adjacent_frame_height_min").GetDecimal());
        Assert.Equal(126.75m, root.GetProperty("adjacent_frame_height_max").GetDecimal());
        Assert.False(root.TryGetProperty("imgsz", out _));
    }

    [Fact]
    public void Normalize_InvalidHeightRange_IsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductPostprocessConfigFile.Normalize(
                """{"adjacent_frame_height_min":120,"adjacent_frame_height_max":40}"""));

        Assert.Contains("height_min", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
