using OceanFresh.SortingSystem.Infrastructure;

namespace OceanFresh.SortingSystem.Tests;

public sealed class ModelLabelMapParserTests
{
    [Fact]
    public void Parse_SupportsNumericKeysUsedByProductConfiguration()
    {
        var result = ModelLabelMapParser.Parse("""{"0":"碎壳","1":"正常","2":"泥包"}""");

        Assert.Equal("碎壳", result[0]);
        Assert.Equal("正常", result[1]);
        Assert.Equal("泥包", result[2]);
    }

    [Fact]
    public void Parse_SupportsLegacyLabelKeys()
    {
        var result = ModelLabelMapParser.Parse("""{"碎壳":0,"正常":1}""");

        Assert.Equal("碎壳", result[0]);
        Assert.Equal("正常", result[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void Parse_ReturnsEmptyMapForInvalidInput(string json)
    {
        Assert.Empty(ModelLabelMapParser.Parse(json));
    }
}
