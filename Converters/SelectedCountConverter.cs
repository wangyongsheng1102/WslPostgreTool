using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using WslPostgreTool.Models;

namespace WslPostgreTool.Converters;

public class SelectedCountConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable enumerable)
        {
            var count = enumerable.Cast<CsvFileInfo>().Count(f => f.IsSelected);
            return count;
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

