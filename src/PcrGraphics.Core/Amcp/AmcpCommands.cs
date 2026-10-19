using System.Text.Encodings.Web;
using System.Text.Json;

namespace PcrGraphics.Core.Amcp;

/// <summary>
/// Builds AMCP command strings. Pure functions: no network, easy to test.
/// The CG layer inside a channel-layer is always 1 in this app.
/// </summary>
public static class AmcpCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // keep Arabic and other non-Latin text readable instead of \uXXXX escapes
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Wraps text in AMCP double quotes, escaping backslashes and quotes.</summary>
    public static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static string ToJson(IReadOnlyDictionary<string, string> fields) =>
        JsonSerializer.Serialize(fields, JsonOptions);

    /// <summary>Cue: load the template with its data. playOnLoad false keeps it hidden until PLAY.</summary>
    public static string CgAdd(Layer layer, string template, bool playOnLoad, IReadOnlyDictionary<string, string> data) =>
        $"CG {layer} ADD 1 {TemplateRef(template)} {(playOnLoad ? 1 : 0)} {Quote(ToJson(data))}";

    /// <summary>Take in: runs the template's play() animation.</summary>
    public static string CgPlay(Layer layer) => $"CG {layer} PLAY 1";

    /// <summary>Take out: runs the template's stop() animation.</summary>
    public static string CgStop(Layer layer) => $"CG {layer} STOP 1";

    /// <summary>Changes the text of a cued or on-air template.</summary>
    public static string CgUpdate(Layer layer, IReadOnlyDictionary<string, string> data) =>
        $"CG {layer} UPDATE 1 {Quote(ToJson(data))}";

    /// <summary>Panic: removes everything on the layer at once, no animation.</summary>
    public static string Clear(Layer layer) => $"CLEAR {layer}";

    public static string Version() => "VERSION";

    private static string TemplateRef(string template) =>
        template.Contains(' ') ? Quote(template) : template;
}
