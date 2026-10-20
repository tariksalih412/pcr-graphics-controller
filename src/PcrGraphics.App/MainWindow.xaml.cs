using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PcrGraphics.App.ViewModels;

namespace PcrGraphics.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        // Optional: PcrGraphics.App.exe <host> <port>
        var args = Environment.GetCommandLineArgs();
        var host = args.Length > 1 ? args[1] : "127.0.0.1";
        var port = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 5250;

        DataContext = _vm = new MainViewModel(host, port);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc leaves the text field, so the next Space is a TAKE and not a typed space.
        if (e.Key == Key.Escape)
        {
            Root.Focus();
            e.Handled = true;
            return;
        }

        // Space = TAKE IN, but only when the operator is not typing.
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox)
        {
            if (_vm.TakeInCommand.CanExecute(null)) _vm.TakeInCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await _vm.DisposeAsync();
    }
}
