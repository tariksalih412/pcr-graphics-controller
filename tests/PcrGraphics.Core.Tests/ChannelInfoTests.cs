using PcrGraphics.Core.Amcp;

namespace PcrGraphics.Core.Tests;

public class ChannelInfoTests
{
    [Fact]
    public void Empty_channel_has_no_layers()
    {
        var info = ChannelInfo.Parse(InfoSamples.Lines(InfoSamples.Empty));
        Assert.Empty(info.Layers);
        Assert.Null(info.Foreground(30));
    }

    [Fact]
    public void Loaded_template_is_found_by_name_despite_mixed_slashes_and_spaces()
    {
        var info = ChannelInfo.Parse(InfoSamples.Lines(InfoSamples.LowerThirdOn30));

        var l30 = info.Foreground(30)!;
        Assert.Equal("html", l30.Producer);
        Assert.Equal("lower_third", l30.FileName);
        Assert.True(l30.IsTemplate("lower_third"));
        Assert.True(l30.IsTemplate("LOWER_THIRD"));
        Assert.False(l30.IsTemplate("breaking_news"));
        Assert.Null(info.Foreground(31));
    }

    [Fact]
    public void Other_producers_and_templates_are_not_ours()
    {
        var info = ChannelInfo.Parse(InfoSamples.Lines(InfoSamples.Foreign));
        Assert.False(info.Foreground(30)!.IsTemplate("lower_third"));
        Assert.Equal("ffmpeg", info.Foreground(31)!.Producer);
        Assert.False(info.Foreground(31)!.IsTemplate("AMB"));      // same name, but not an html template
    }

    [Fact]
    public void Not_xml_throws_format_exception()
    {
        Assert.Throws<FormatException>(() => ChannelInfo.Parse(["1 1080i5000 PLAYING"]));
        Assert.Throws<FormatException>(() => ChannelInfo.Parse(["<other/>"]));
    }
}
