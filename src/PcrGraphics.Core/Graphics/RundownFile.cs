using System.Text.Encodings.Web;
using System.Text.Json;

namespace PcrGraphics.Core.Graphics;

/// <summary>
/// Saves and loads a rundown (the prepared list of graphics for a show) as readable JSON:
/// { "version": 1, "items": [ { "template": "lower_third", "label": "...", "fields": { ... } } ] }
/// </summary>
public static class RundownFile
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep Arabic readable in the file
    };

    private sealed class Document
    {
        public int Version { get; set; } = RundownFile.Version;
        public List<Entry> Items { get; set; } = new();
    }

    private sealed class Entry
    {
        public string Template { get; set; } = "";
        public string Label { get; set; } = "";
        public Dictionary<string, string> Fields { get; set; } = new();
    }

    public static string Serialize(IEnumerable<GraphicItem> items) =>
        JsonSerializer.Serialize(new Document
        {
            Items = items.Select(i => new Entry
            {
                Template = i.Template,
                Label = i.Label,
                Fields = new Dictionary<string, string>(i.Fields),
            }).ToList(),
        }, Options);

    /// <summary>Throws InvalidDataException with a readable message if the text is not a rundown.</summary>
    public static List<GraphicItem> Deserialize(string json)
    {
        Document? doc;
        try
        {
            doc = JsonSerializer.Deserialize<Document>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Not a valid rundown file: {ex.Message}", ex);
        }
        if (doc is null) throw new InvalidDataException("The rundown file is empty");
        if (doc.Version > Version)
            throw new InvalidDataException($"Rundown version {doc.Version} is newer than this app supports ({Version})");

        return doc.Items
            .Where(e => e.Template.Length > 0)
            .Select(e => new GraphicItem(e.Label, e.Template, e.Fields ?? new()))
            .ToList();
    }

    public static void Save(string path, IEnumerable<GraphicItem> items)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // write to a temp file first, so a crash mid-write never leaves a broken rundown
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(items));
        File.Move(tmp, path, overwrite: true);
    }

    public static List<GraphicItem> Load(string path) => Deserialize(File.ReadAllText(path));
}
