using System.Globalization;
using System.Windows;
using System.Windows.Data;
using AnchorFeeds.Models;

namespace AnchorFeeds.Services;

public sealed class ArticleListEndMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 3 && values[0] is ArticleAudio article &&
        ReferenceEquals(article, values[1]) && values[2] is double height
            ? new Thickness(0, 0, 0, height + 1)
            : new Thickness();

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
