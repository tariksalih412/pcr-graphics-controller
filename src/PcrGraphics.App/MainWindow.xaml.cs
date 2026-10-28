using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using PcrGraphics.App.ViewModels;

namespace PcrGraphics.App;

public partial class MainWindow : Window
{
    private const double TemplateWidth = 1920;   // templates are built for a 1920x1080 channel

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _previewDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly SemaphoreSlim _previewLock = new(1, 1);
    private string? _loadedTemplate;

    public MainWindow()
    {
        InitializeComponent();

        // Optional: PcrGraphics.App.exe <host> <port>
        var args = Environment.GetCommandLineArgs();
        var host = args.Length > 1 ? args[1] : "127.0.0.1";
        var port = args.Length > 2 && int.TryParse(args[2], out var p) ? p : 5250;
        DataContext = _vm = new MainViewModel(host, port);

        // File dialogs live in the window; the view model only receives the chosen path.
        var rundownDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PcrGraphics", "rundowns");
        Directory.CreateDirectory(rundownDir);
        _vm.AskOpenPath = () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Rundown (*.json)|*.json", InitialDirectory = rundownDir };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };
        _vm.AskSavePath = () =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Rundown (*.json)|*.json",
                InitialDirectory = rundownDir,
                FileName = $"rundown-{DateTime.Now:yyyy-MM-dd}.json",
            };
            return dlg.ShowDialog(this) == true ? dlg.FileName : null;
        };

        // Typing redraws the preview after a short pause instead of on every key.
        _previewDelay.Tick += async (_, _) =>
        {
            _previewDelay.Stop();
            await ShowPreviewAsync();
        };
        foreach (var slot in _vm.Slots)
            slot.PreviewChanged += () =>
            {
                if (slot != _vm.SelectedSlot) return;
                _previewDelay.Stop();
                _previewDelay.Start();
            };
        _vm.SelectedSlotChanged += async () => await ShowPreviewAsync();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // WebView2 keeps a cache folder. By default it goes next to the exe, which fails when the
            // exe sits in a read-only place (Program Files, a network share): use the user's AppData.
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PcrGraphics", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            await Preview.EnsureCoreWebView2Async(env);
            await ShowPreviewAsync();
        }
        catch (Exception ex)
        {
            ShowPreviewError("Preview unavailable (WebView2): " + ex.Message);
        }
    }

    /// <summary>Loads the selected slot's template (if needed) and fills it with the current field text.</summary>
    private async Task ShowPreviewAsync()
    {
        if (Preview.CoreWebView2 is null) return;
        await _previewLock.WaitAsync();
        try
        {
            var slot = _vm.SelectedSlot;
            if (_loadedTemplate != slot.Template)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "templates", slot.Template + ".html");
                if (!File.Exists(path))
                {
                    ShowPreviewError($"Template not found: {path}");
                    _loadedTemplate = null;
                    return;
                }
                PreviewError.Visibility = Visibility.Collapsed;
                var loaded = new TaskCompletionSource();
                void Done(object? s, CoreWebView2NavigationCompletedEventArgs a)
                {
                    Preview.NavigationCompleted -= Done;
                    loaded.TrySetResult();
                }
                Preview.NavigationCompleted += Done;
                Preview.Source = new UriBuilder(new Uri(path)) { Query = "preview" }.Uri;
                await loaded.Task;
                _loadedTemplate = slot.Template;
            }

            // The same calls CasparCG makes: update(data) then play()
            var js = $"update({JsonSerializer.Serialize(slot.PreviewJson)}); play();";
            await Preview.ExecuteScriptAsync(js);
        }
        finally
        {
            _previewLock.Release();
        }
    }

    private void ShowPreviewError(string text)
    {
        PreviewError.Text = text;
        PreviewError.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The preview takes the space left under the fields, keeps 16:9, and scales the
    /// 1920-wide template to that size. WebView2 cannot zoom below 25 %, so very small
    /// windows show a cropped preview instead of a smaller one.
    /// </summary>
    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = Math.Min(PreviewHost.ActualWidth, PreviewHost.ActualHeight * 16 / 9);
        if (width <= 0) return;
        PreviewFrame.Width = width;
        PreviewFrame.Height = width * 9 / 16;
        Preview.ZoomFactor = Math.Max(0.25, (width - 2) / TemplateWidth);
    }

    /// <summary>
    /// The preview is a separate Chromium window: if it ever gets keyboard focus, the hotkeys stop
    /// reaching this window. Any click outside a text field or a list gives the focus back here.
    /// </summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = ParentOf(d))
            if (d is TextBox or ListBoxItem) return;
        Root.Focus();
    }

    /// <summary>
    /// Text pieces such as Run are not visuals, so VisualTreeHelper cannot walk up from them
    /// (that was the crash when clicking a button label). Those use the logical tree instead.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject d) =>
        d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(d)
            : LogicalTreeHelper.GetParent(d);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Esc leaves the text field, so the next Space is a TAKE and not a typed space.
        if (e.Key == Key.Escape)
        {
            Root.Focus();
            e.Handled = true;
            return;
        }

        // Space = TAKE IN on the selected graphic, but only when the operator is not typing.
        if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox)
        {
            var take = _vm.SelectedSlot.TakeInCommand;
            if (take.CanExecute(null)) take.Execute(null);
            e.Handled = true;
        }
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        await _vm.DisposeAsync();
    }
}
