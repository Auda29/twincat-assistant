using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace TwinCatAssistant.Native
{
    internal static class HostTextBindings
    {
        private static readonly MessageConverter Converter = new MessageConverter();
        internal static void Apply(DependencyObject root)
        {
            if (root == null) return;
            root.Dispatcher.VerifyAccess();
            var seen = new HashSet<DependencyObject>();
            var pending = new Stack<DependencyObject>();
            pending.Push(root);
            while (pending.Count != 0 && seen.Count < 4000)
            {
                var item = pending.Pop();
                if (!seen.Add(item)) continue;
                if (item is TextBlock) Bind(item, TextBlock.TextProperty);
                if (item is Run) Bind(item, Run.TextProperty);
                if (item is ContentControl) Bind(item, ContentControl.ContentProperty);
                if (item is FrameworkElement) { Bind(item, FrameworkElement.ToolTipProperty); Bind(item, AutomationProperties.NameProperty); }
                if (item is Popup popup && popup.Child != null) pending.Push(popup.Child);
                if (item.GetType().Name.IndexOf("WebView2", StringComparison.Ordinal) >= 0) continue;
                foreach (var child in LogicalTreeHelper.GetChildren(item)) if (child is DependencyObject dependency) pending.Push(dependency);
                if (item is Visual)
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) pending.Push(VisualTreeHelper.GetChild(item, i));
            }
        }
        private static void Bind(DependencyObject item, DependencyProperty property)
        {
            if (BindingOperations.IsDataBound(item, property)) return;
            var message = HostText.Description(item.GetValue(property) as string);
            if (message == null) return;
            BindingOperations.SetBinding(item, property, new Binding(nameof(HostText.LanguageSource.Language))
            {
                Source = HostText.Source, Mode = BindingMode.OneWay,
                Converter = Converter, ConverterParameter = message
            });
        }
        private sealed class MessageConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return ((HostText.Message)parameter).Render(); }
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
        }
    }
}
