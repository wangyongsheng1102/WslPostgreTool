using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WslPostgreTool.Models;

namespace WslPostgreTool.Converters;

public class LogLevelToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => new SolidColorBrush(Color.Parse("#0D6EFD")),
                LogLevel.Warning => new SolidColorBrush(Color.Parse("#FFC107")),
                LogLevel.Error => new SolidColorBrush(Color.Parse("#DC3545")),
                LogLevel.Success => new SolidColorBrush(Color.Parse("#198754")),
                _ => new SolidColorBrush(Color.Parse("#212529"))
            };
        }
        return new SolidColorBrush(Color.Parse("#212529"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

