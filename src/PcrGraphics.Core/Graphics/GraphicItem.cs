namespace PcrGraphics.Core.Graphics;

/// <summary>One prepared graphic: which template, and the text that fills it.</summary>
public sealed record GraphicItem(string Label, string Template, IReadOnlyDictionary<string, string> Fields);
