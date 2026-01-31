using CommunityToolkit.Maui;
using Gallery.App.Services;
using Gallery.App.ViewModels;
using Gallery.App.Views;
using Gallery.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Gallery.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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
        builder.Services.AddSingleton<WorkspaceConfiguration>();

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

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
