using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace JustOneClick
{
    public class BubbleCornerConverter: IValueConverter
    {

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isUser = (bool)value;

            return isUser
                ? new CornerRadius(14, 2, 14, 14)   // пользователь — хвостик справа
                : new CornerRadius(2, 14, 14, 14);  // бот — хвостик слева
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }
}

