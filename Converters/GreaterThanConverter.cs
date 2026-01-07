using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WslPostgreTool.Converters;

public class GreaterThanConverter : IValueConverter
{
    public static readonly GreaterThanConverter Instance = new();
    
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && parameter is string param && int.TryParse(param, out int min))
        {
            return count > min;
        }
        return false;
    }
    
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}