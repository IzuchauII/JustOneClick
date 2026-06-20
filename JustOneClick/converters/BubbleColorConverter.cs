using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JustOneClick
{
    public class BubbleColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isUser = (bool)value;

            return isUser
                ? new SolidColorBrush(Color.FromArgb(180, 0, 120, 255)) // синий пузырь
                : new SolidColorBrush(Color.FromArgb(180, 50, 50, 50)); // серый пузырь
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

    }
}
