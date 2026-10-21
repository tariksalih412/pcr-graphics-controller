using CommunityToolkit.Mvvm.ComponentModel;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.App.ViewModels;

/// <summary>One prepared graphic in the rundown, with a tally dot showing if it is cued or on air.</summary>
public sealed class RundownItemViewModel : ObservableObject
{
    private GraphicState _state = GraphicState.Empty;
    private bool _isNext;

    public RundownItemViewModel(GraphicItem item, GraphicSlotViewModel slot)
    {
        Item = item;
        Slot = slot;
    }

    public GraphicItem Item { get; }
    public GraphicSlotViewModel Slot { get; }

    public string SlotTitle => Slot.Title.ToUpperInvariant();
    public string Line1 => Slot.Fields.Select(f => Item.Fields.GetValueOrDefault(f.Spec.Key, "")).FirstOrDefault(v => v.Length > 0) ?? "";
    public string Line2 => string.Join("  ·  ", Slot.Fields.Skip(1).Select(f => Item.Fields.GetValueOrDefault(f.Spec.Key, "")).Where(v => v.Length > 0));

    /// <summary>This is the item F5 will load and cue.</summary>
    public bool IsNext
    {
        get => _isNext;
        set => SetProperty(ref _isNext, value);
    }

    // Same names as the slot, so the list can reuse the TallyDot style.
    public bool TallyPreview => _state == GraphicState.Cued;
    public bool TallyProgram => _state == GraphicState.OnAir;
    public bool TallyUnknown => _state == GraphicState.Unknown;

    /// <summary>Re-reads the slot: is this exact item the one cued or on air there?</summary>
    public void RefreshState()
    {
        var state = Slot.Holds(Item) ? Slot.State : GraphicState.Empty;
        if (state == _state) return;
        _state = state;
        OnPropertyChanged(nameof(TallyPreview));
        OnPropertyChanged(nameof(TallyProgram));
        OnPropertyChanged(nameof(TallyUnknown));
    }
}
