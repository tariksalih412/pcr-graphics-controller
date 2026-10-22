using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PcrGraphics.Core.Amcp;
using PcrGraphics.Core.Graphics;

namespace PcrGraphics.App.ViewModels;

/// <summary>
/// One graphic type on its own CasparCG layer (lower third, breaking news...):
/// its fields, its buttons and its tally. Each slot has its own state machine.
/// </summary>
public sealed class GraphicSlotViewModel : ObservableObject
{
    private readonly GraphicController _controller;
    private readonly Func<bool> _isConnected;
    private readonly Action<Action> _onUi;
    private readonly Action<string> _report;

    public GraphicSlotViewModel(string title, string template, Layer layer, CasparClient client,
        IEnumerable<FieldSpec> fields, Func<bool> isConnected, Action<Action> onUi, Action<string> report)
    {
        Title = title;
        Template = template;
        Layer = layer;
        _isConnected = isConnected;
        _onUi = onUi;
        _report = report;
        _controller = new GraphicController(client, layer, template);

        foreach (var spec in fields)
        {
            var field = new FieldViewModel(spec);
            field.Changed += OnFieldChanged;
            Fields.Add(field);
        }

        CueCommand = new AsyncRelayCommand(CueAsync, () => Can(GraphicAction.Cue) && FieldsValid);
        TakeInCommand = new AsyncRelayCommand(() => RunAsync("Take in", _controller.TakeInAsync), () => Can(GraphicAction.TakeIn));
        UpdateCommand = new AsyncRelayCommand(
            () => RunAsync("Update", () => _controller.UpdateAsync(Values())),
            () => Can(GraphicAction.Update) && FieldsValid && HasPendingEdit);
        TakeOutCommand = new AsyncRelayCommand(() => RunAsync("Take out", _controller.TakeOutAsync), () => Can(GraphicAction.TakeOut));

        _controller.Machine.Changed += () => _onUi(Refresh);
    }

    public string Title { get; }
    public string Template { get; }
    public Layer Layer { get; }
    public string LayerText => $"layer {Layer}";
    public ObservableCollection<FieldViewModel> Fields { get; } = new();
    public GraphicController Controller => _controller;

    public AsyncRelayCommand CueCommand { get; }
    public AsyncRelayCommand TakeInCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand TakeOutCommand { get; }

    /// <summary>Raised after every state or button refresh (the rundown uses it for its tally dots).</summary>
    public event Action? Refreshed;

    /// <summary>Raised whenever the text changes, so the preview can redraw.</summary>
    public event Action? PreviewChanged;

    public bool FieldsValid => Fields.All(f => f.IsValid) && Fields.Any(f => f.Trimmed.Length > 0);

    public bool HasPendingEdit =>
        _controller.Current is { } c &&
        Fields.Any(f => c.Fields.GetValueOrDefault(f.Spec.Key, "") != f.Trimmed);

    public GraphicState State => _controller.State;
    public bool IsBusy => _controller.Machine.IsBusy;
    public bool TallyPreview => State == GraphicState.Cued;
    public bool TallyProgram => State == GraphicState.OnAir;
    public bool TallyUnknown => State == GraphicState.Unknown;

    public string StateText => State switch
    {
        GraphicState.Empty => "EMPTY",
        GraphicState.Cued => "CUED",
        GraphicState.OnAir => "ON AIR",
        _ => "UNKNOWN",
    } + (IsBusy ? "  …" : "");

    public string CurrentText => _controller.Current is { } c
        ? string.Join("\n", Fields.Select(f => c.Fields.GetValueOrDefault(f.Spec.Key, "")).Where(v => v.Length > 0))
        : "—";

    /// <summary>The current field values as JSON, for the preview (the same data CasparCG gets).</summary>
    public string PreviewJson => AmcpCommands.ToJson(Values());

    /// <summary>The current field values, keyed as the template expects.</summary>
    public Dictionary<string, string> Values() => Fields.ToDictionary(f => f.Spec.Key, f => f.Trimmed);

    /// <summary>Re-evaluates every bound property and button. Called on state and connection changes.</summary>
    public void Refresh()
    {
        foreach (var name in new[]
                 {
                     nameof(State), nameof(IsBusy), nameof(StateText), nameof(TallyPreview), nameof(TallyProgram),
                     nameof(TallyUnknown), nameof(CurrentText), nameof(HasPendingEdit), nameof(FieldsValid),
                 })
            OnPropertyChanged(name);
        CueCommand.NotifyCanExecuteChanged();
        TakeInCommand.NotifyCanExecuteChanged();
        UpdateCommand.NotifyCanExecuteChanged();
        TakeOutCommand.NotifyCanExecuteChanged();
        Refreshed?.Invoke();
    }

    /// <summary>Fills the fields from a prepared item (fields it does not have are emptied).</summary>
    public void LoadValues(IReadOnlyDictionary<string, string> values)
    {
        foreach (var f in Fields) f.Value = values.GetValueOrDefault(f.Spec.Key, "");
    }

    /// <summary>The fields as a rundown item.</summary>
    public GraphicItem ToItem() =>
        new(Fields.Select(f => f.Trimmed).FirstOrDefault(v => v.Length > 0) ?? Title, Template, Values());

    /// <summary>Is this item what is cued or on air on this slot right now?</summary>
    public bool Holds(GraphicItem item) =>
        item.Template == Template && _controller.Current is { } c &&
        Fields.All(f => c.Fields.GetValueOrDefault(f.Spec.Key, "") == item.Fields.GetValueOrDefault(f.Spec.Key, ""));

    private bool Can(GraphicAction action) => _isConnected() && _controller.Machine.CanDo(action);

    private Task CueAsync()
    {
        var label = Fields.Select(f => f.Trimmed).FirstOrDefault(v => v.Length > 0) ?? Title;
        var item = new GraphicItem(label, Template, Values());
        return RunAsync("Cue", () => _controller.CueAsync(item));
    }

    private async Task RunAsync(string action, Func<Task<bool>> run)
    {
        var ok = await run();
        _report(ok ? $"{Title} · {action}: OK" : $"{Title} · {action} not done: {_controller.LastError}");
        Refresh();
    }

    private void OnFieldChanged()
    {
        Refresh();
        PreviewChanged?.Invoke();
    }
}
