using CommunityToolkit.Mvvm.ComponentModel;

namespace PcrGraphics.App.ViewModels;

/// <summary>Describes one text field of a template: its JSON key, label and length limit.</summary>
public sealed record FieldSpec(string Key, string Label, int MaxLength, bool Required = false);

/// <summary>One editable text field, with its character counter.</summary>
public sealed class FieldViewModel : ObservableObject
{
    private string _value = "";

    public FieldViewModel(FieldSpec spec) => Spec = spec;

    public FieldSpec Spec { get; }
    public string Label => Spec.Label;

    /// <summary>Raised after every edit.</summary>
    public event Action? Changed;

    public string Value
    {
        get => _value;
        set
        {
            if (!SetProperty(ref _value, value)) return;
            OnPropertyChanged(nameof(Counter));
            OnPropertyChanged(nameof(TooLong));
            Changed?.Invoke();
        }
    }

    public string Trimmed => Value.Trim();
    public string Counter => $"{Trimmed.Length}/{Spec.MaxLength}";
    public bool TooLong => Trimmed.Length > Spec.MaxLength;
    public bool IsValid => !TooLong && (!Spec.Required || Trimmed.Length > 0);
}
