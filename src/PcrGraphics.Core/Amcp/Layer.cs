namespace PcrGraphics.Core.Amcp;

/// <summary>A CasparCG channel and layer, written "channel-layer" in AMCP (e.g. 1-30).</summary>
public readonly record struct Layer(int Channel, int Number)
{
    public override string ToString() => $"{Channel}-{Number}";
}
