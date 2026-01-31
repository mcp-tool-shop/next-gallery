using CommunityToolkit.Maui;
using Gallery.App.Services;
using Gallery.App.ViewModels;
using Gallery.App.Views;
using Gallery.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Gallery.App;

public static class MauiProgram
{
    /// <summary>
    /// Workspace path from command-line args. Set by platform-specific entry point.
    /// </summary>
    public static string? WorkspacePath { get; set; }

    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        // Copy from Windows App.xaml.cs static property
        WorkspacePath = Gallery.App.WinUI.App.WorkspacePath;
#endif

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register infrastructure services
        builder.Services.AddGalleryInfrastructure();

        // Register app services (singletons for state)
        builder.Services.AddSingleton<SelectionService>();
        builder.Services.AddSingleton<QueryService>();
        builder.Services.AddSingleton<PrefetchService>();

        // Configure workspace from command-line
        builder.Services.AddSingleton(sp =>
        {
            var config = new WorkspaceConfiguration();
            if (!string.IsNullOrEmpty(WorkspacePath))
            {
                config.WorkspacePath = WorkspacePath;
            }
            return config;
        });

        // Register ViewModels
        builder.Services.AddTransient<MainViewModel>();
        builder.Services.AddTransient<CodeComfyViewModel>(sp =>
        {
            var config = sp.GetRequiredService<WorkspaceConfiguration>();
            if (config.WorkspacePath == null)
                throw new InvalidOperationException("WorkspacePath must be set before creating CodeComfyViewModel");
            return new CodeComfyViewModel(config.WorkspacePath);
        });

        // Register Pages and Views
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<QuickPreviewOverlay>();
        builder.Services.AddTransient<CodeComfyPage>();

        // Register App with service provider access
        builder.Services.AddSingleton<App>(sp => new App(sp));

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
