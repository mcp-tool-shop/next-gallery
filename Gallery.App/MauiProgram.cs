using CommunityToolkit.Maui;
using Gallery.App.ViewModels;
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
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register infrastructure services
        builder.Services.AddGalleryInfrastructure();

        // Register ViewModels
        builder.Services.AddTransient<MainViewModel>();

        // Register Pages
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
