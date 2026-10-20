namespace PcrGraphics.Core.Graphics;

/// <summary>What is on one graphics layer, as far as we know for sure.</summary>
public enum GraphicState
{
    /// <summary>Nothing loaded.</summary>
    Empty,
    /// <summary>Loaded and filled, not visible (Preview).</summary>
    Cued,
    /// <summary>Visible on air (Program).</summary>
    OnAir,
    /// <summary>A command may or may not have run (timeout or lost connection). Only Clear is safe.</summary>
    Unknown,
}

public enum GraphicAction { Cue, TakeIn, Update, TakeOut, Clear }
