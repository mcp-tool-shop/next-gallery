using Gallery.App.ViewModels;

namespace Gallery.App.Views;

public partial class CodeComfyPage : ContentPage
{
    private readonly CodeComfyViewModel _viewModel;

    public CodeComfyPage(CodeComfyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

#if WINDOWS
        HandlerChanged += OnHandlerChanged;
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopPolling();
    }

#if WINDOWS
    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            element.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Ctrl+Shift+D toggles diagnostics
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
            Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (ctrlPressed && shiftPressed && e.Key == Windows.System.VirtualKey.D)
        {
            _viewModel.ToggleDiagnosticsCommand.Execute(null);
            e.Handled = true;
        }
    }
#endif
}
