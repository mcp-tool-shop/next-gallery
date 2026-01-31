using Gallery.App.Services;
using Gallery.App.Views;

namespace Gallery.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var config = _services.GetRequiredService<WorkspaceConfiguration>();
        Page startPage;
        string title;

        if (config.IsCodeComfyMode)
        {
            // CodeComfy workspace mode
            startPage = _services.GetRequiredService<CodeComfyPage>();
            title = $"CodeComfy Gallery - {Path.GetFileName(config.WorkspacePath)}";
        }
        else
        {
            // Normal gallery mode
            startPage = _services.GetRequiredService<MainPage>();
            title = "NextGallery";
        }

        return new Window(startPage)
        {
            Title = title,
            Width = 1400,
            Height = 900,
            MinimumWidth = 1000,
            MinimumHeight = 600
        };
    }
}
