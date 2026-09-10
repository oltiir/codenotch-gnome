using System.Text.Json;
using Codenotch.Core.Model;
using Xunit;

namespace Codenotch.Core.Tests;

public class EnvelopeTests
{
    [Fact]
    public void ABareJsonArrayIsTheProviderList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""[{"provider":"claude"},{"provider":"codex"}]""");
        Assert.Equal(2, list.Count);
        Assert.Equal("claude", list[0].Provider);
        Assert.Equal("codex", list[1].Provider);
    }

    [Fact]
    public void ProvidersWrapperKeyYieldsTheList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"providers":[{"provider":"claude"}]}""");
        Assert.Single(list);
        Assert.Equal("claude", list[0].Provider);
    }

    [Fact]
    public void UsagesWrapperKeyYieldsTheList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"usages":[{"provider":"claude"}]}""");
        Assert.Single(list);
        Assert.Equal("claude", list[0].Provider);
    }

    [Fact]
    public void ResultsWrapperKeyYieldsTheList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"results":[{"provider":"claude"}]}""");
        Assert.Single(list);
        Assert.Equal("claude", list[0].Provider);
    }

    [Fact]
    public void ItemsWrapperKeyYieldsTheList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"items":[{"provider":"claude"}]}""");
        Assert.Single(list);
        Assert.Equal("claude", list[0].Provider);
    }

    [Fact]
    public void ASingleObjectWithUsageBecomesAOneEntryList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"usage":{"primary":{"usedPercent":10}}}""");
        Assert.Single(list);
        Assert.NotNull(list[0].Usage);
    }

    [Fact]
    public void ASingleObjectWithOnlyProviderBecomesAOneEntryList()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""{"provider":"claude"}""");
        Assert.Single(list);
        Assert.Equal("claude", list[0].Provider);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("""{"unrelated":1}""")]
    public void NullAStringANumberAndAnUnrelatedObjectAllYieldAnEmptyList(string json)
    {
        Assert.Empty(UsageEnvelopeParser.ProvidersFrom(json));
    }

    [Fact]
    public void MalformedJsonThrowsJsonException()
    {
        // JsonDocument/JsonNode parse failures surface as JsonReaderException, a
        // subclass of JsonException, so match by base type rather than an exact one.
        Assert.ThrowsAny<JsonException>(() => UsageEnvelopeParser.ProvidersFrom("{not json"));
    }

    [Fact]
    public void AnArrayElementThatIsNotAnObjectIsSkippedRatherThanAbortingTheParse()
    {
        var list = UsageEnvelopeParser.ProvidersFrom("""[{"provider":"claude"}, "garbage", {"provider":"codex"}]""");
        Assert.Equal(2, list.Count);
        Assert.Equal("claude", list[0].Provider);
        Assert.Equal("codex", list[1].Provider);
    }

    [Fact]
    public void UnknownTopLevelAndNestedFieldsAreIgnored()
    {
        var text = Fixtures.Text("live.json");
        var list = UsageEnvelopeParser.ProvidersFrom(text);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void UsedPercentSuppliedAsAnIntegerAndAsADecimalBothBind()
    {
        var asInt = UsageEnvelopeParser.ProvidersFrom("""{"provider":"claude","usage":{"primary":{"usedPercent":12}}}""");
        var asDecimal = UsageEnvelopeParser.ProvidersFrom("""{"provider":"claude","usage":{"primary":{"usedPercent":12.4}}}""");
        Assert.Equal(12, asInt[0].Usage!.Primary!.UsedPercent);
        Assert.Equal(12.4, asDecimal[0].Usage!.Primary!.UsedPercent);
    }
}
