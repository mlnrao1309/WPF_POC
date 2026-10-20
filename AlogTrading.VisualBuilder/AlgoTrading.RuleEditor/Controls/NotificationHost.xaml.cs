using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AlgoTrading.RuleEditor.Controls;

/// <summary>
/// Non-blocking, in-app notification area. Notifications stack from the top,
/// auto-dismiss after 5 seconds, or on click. Capped at 3 visible.
/// </summary>
public partial class NotificationHost : UserControl
{
    private const int MaxVisible = 3;
    private const int AutoDismissMs = 5000;

    public NotificationHost()
    {
        InitializeComponent();
    }

    public void Show(string title, string body)
    {
        var border = BuildNotification(title, body);
        Stack.Children.Insert(0, border);
        while (Stack.Children.Count > MaxVisible)
            Stack.Children.RemoveAt(Stack.Children.Count - 1);

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(AutoDismissMs)
        };
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            Stack.Children.Remove(border);
        };
        timer.Start();

        border.MouseLeftButtonUp += (s, e) =>
        {
            timer.Stop();
            Stack.Children.Remove(border);
        };
    }

    private static Border BuildNotification(string title, string body)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrEmpty(body))
        {
            panel.Children.Add(new TextBlock
            {
                Text = body,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = Brushes.DimGray
            });
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xD0, 0xDA)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(4),
            Child = panel,
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect
            {
                Color = Colors.Gray,
                BlurRadius = 10,
                ShadowDepth = 2,
                Opacity = 0.30
            }
        };
    }
}