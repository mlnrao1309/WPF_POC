using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace AlgoTrading.Behaviors
{
    public static class ListBoxSelectedItemsBehavior
    {
        public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(ListBoxSelectedItemsBehavior),
            new PropertyMetadata(null, OnSelectedItemsChanged));

        public static void SetSelectedItems(DependencyObject element, IList value) => element.SetValue(SelectedItemsProperty, value);
        public static IList? GetSelectedItems(DependencyObject element) => (IList?)element.GetValue(SelectedItemsProperty);

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListBox lb)
            {
                lb.SelectionChanged -= ListBox_SelectionChanged;
                if (e.NewValue is IList)
                {
                    lb.SelectionChanged += ListBox_SelectionChanged;
                }
            }
        }

        private static void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb)
            {
                var bound = GetSelectedItems(lb);
                if (bound == null) return;

                // Update bound list to match ListBox.SelectedItems
                bound.Clear();
                foreach (var item in lb.SelectedItems)
                {
                    bound.Add(item);
                }
            }
        }
    }
}
