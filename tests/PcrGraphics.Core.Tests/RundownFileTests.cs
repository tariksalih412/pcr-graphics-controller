using PcrGraphics.Core.Graphics;

namespace PcrGraphics.Core.Tests;

public class RundownFileTests
{
    private static readonly List<GraphicItem> Items =
    [
        new("د. أحمد", "lower_third", new Dictionary<string, string> { ["name"] = "د. أحمد", ["title"] = "محلل \"اقتصادي\"" }),
        new("Breaking", "breaking_news", new Dictionary<string, string> { ["headline"] = "عاجل: خبر" }),
    ];

    [Fact]
    public void Round_trip_keeps_order_templates_and_text()
    {
        var back = RundownFile.Deserialize(RundownFile.Serialize(Items));

        Assert.Equal(2, back.Count);
        Assert.Equal("lower_third", back[0].Template);
        Assert.Equal("محلل \"اقتصادي\"", back[0].Fields["title"]);
        Assert.Equal("breaking_news", back[1].Template);
    }

    [Fact]
    public void File_is_readable_json_with_arabic_kept()
    {
        var json = RundownFile.Serialize(Items);
        Assert.Contains("\"version\": 1", json);
        Assert.Contains("د. أحمد", json);
    }

    [Fact]
    public void Broken_file_gives_a_readable_error() =>
        Assert.Throws<InvalidDataException>(() => RundownFile.Deserialize("{ not json"));

    [Fact]
    public void Newer_version_is_refused() =>
        Assert.Throws<InvalidDataException>(() => RundownFile.Deserialize("{\"version\": 99, \"items\": []}"));

    [Fact]
    public void Entries_without_template_are_skipped()
    {
        var items = RundownFile.Deserialize("{\"version\":1,\"items\":[{\"label\":\"x\"},{\"template\":\"lower_third\"}]}");
        Assert.Single(items);
        Assert.Empty(items[0].Fields);
    }

    [Fact]
    public void Save_and_load_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rundown-{Guid.NewGuid():N}.json");
        try
        {
            RundownFile.Save(path, Items);
            Assert.Equal(2, RundownFile.Load(path).Count);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
