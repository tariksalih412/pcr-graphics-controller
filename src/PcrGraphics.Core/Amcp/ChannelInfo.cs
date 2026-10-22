using System.Xml.Linq;

namespace PcrGraphics.Core.Amcp;

/// <summary>What one layer's foreground holds, as CasparCG reports it.</summary>
/// <param name="Producer">"html" for our templates, "ffmpeg", "color", ...</param>
/// <param name="Path">The loaded file, e.g. file://C:\caspar\template/lower_third.html (may be null).</param>
public sealed record LayerContent(string Producer, string? Path)
{
    /// <summary>File name without folder or extension: "lower_third". Null when there is no path.</summary>
    public string? FileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path)) return null;
            var p = Path;
            try { p = Uri.UnescapeDataString(p); } catch { /* keep it as it is */ }
            var cut = p.LastIndexOfAny(['/', '\\']);
            var name = cut >= 0 ? p[(cut + 1)..] : p;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }
    }

    /// <summary>True if this is the given HTML template ("lower_third" or "folder/lower_third").</summary>
    public bool IsTemplate(string template)
    {
        var cut = template.LastIndexOfAny(['/', '\\']);
        var wanted = cut >= 0 ? template[(cut + 1)..] : template;
        return Producer.Equals("html", StringComparison.OrdinalIgnoreCase)
            && string.Equals(FileName, wanted, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The reply to "INFO {channel}" on CasparCG 2.5.1, measured with tools/probe_info.ps1:
///   &lt;channel&gt; ... &lt;stage&gt;&lt;layer&gt;&lt;layer_30&gt;&lt;foreground&gt;
///       &lt;file&gt;&lt;path&gt;file://...template/lower_third.html&lt;/path&gt;&lt;/file&gt;
///       &lt;producer&gt;html&lt;/producer&gt; ...
/// A cleared layer is simply absent (and &lt;stage&gt; disappears when no layer is loaded).
/// The XML says WHAT is loaded, never whether a template is shown or hidden:
/// cued, on air and taken out look exactly the same.
/// </summary>
public sealed class ChannelInfo
{
    private readonly Dictionary<int, LayerContent> _layers;

    private ChannelInfo(Dictionary<int, LayerContent> layers) => _layers = layers;

    public IReadOnlyDictionary<int, LayerContent> Layers => _layers;

    /// <summary>The foreground of a layer, or null if nothing is loaded there.</summary>
    public LayerContent? Foreground(int layer) => _layers.GetValueOrDefault(layer);

    /// <summary>Parses the data lines of a 201 INFO reply. Throws FormatException if it is not channel XML.</summary>
    public static ChannelInfo Parse(IEnumerable<string> dataLines)
    {
        XDocument doc;
        try { doc = XDocument.Parse(string.Join("\n", dataLines)); }
        catch (Exception ex) { throw new FormatException("INFO reply is not valid XML: " + ex.Message, ex); }

        var channel = doc.Root;
        if (channel is null || channel.Name.LocalName != "channel")
            throw new FormatException($"INFO reply root is <{channel?.Name.LocalName}>, expected <channel>");

        var layers = new Dictionary<int, LayerContent>();
        var stageLayers = channel.Element("stage")?.Element("layer")?.Elements() ?? [];
        foreach (var layer in stageLayers)
        {
            var name = layer.Name.LocalName;                       // layer_30
            if (!name.StartsWith("layer_") || !int.TryParse(name.AsSpan(6), out var number)) continue;

            var fg = layer.Element("foreground");
            var producer = fg?.Element("producer")?.Value.Trim() ?? "";
            if (producer.Length == 0 || producer.Equals("empty", StringComparison.OrdinalIgnoreCase)) continue;

            var path = fg!.Descendants("path").FirstOrDefault()?.Value.Trim();
            layers[number] = new LayerContent(producer, path);
        }
        return new ChannelInfo(layers);
    }
}
