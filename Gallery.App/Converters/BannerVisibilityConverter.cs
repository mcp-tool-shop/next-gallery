using System.Globalization;
using Gallery.Domain.Index;

namespace Gallery.App.Converters;

/// <summary>
/// Shows banner when severity is not None.
/// </summary>
public class BannerVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BannerInfo banner)
        {
            return banner.Severity != BannerSeverity.None;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Maps BannerSeverity to background color.
/// </summary>
public class BannerSeverityColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BannerInfo banner)
        {
            return banner.Severity switch
            {
                BannerSeverity.Warning => Colors.Orange,
                BannerSeverity.Info => Colors.LightBlue,
                _ => Colors.Transparent
            };
        }
        return Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
