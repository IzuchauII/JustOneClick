using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JustOneClick
{
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType,
                      object parameter, System.Globalization.CultureInfo culture)
    => value is bool b && b ? 1.0 : 0.4;

        public object ConvertBack(object value, Type targetType,
                                  object parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();

    }
}
