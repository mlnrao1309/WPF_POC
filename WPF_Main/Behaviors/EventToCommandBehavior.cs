using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AlgoTrading.Behaviors;

public static class EventToCommandBehavior
{
    private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
        "Handler",
        typeof(Delegate),
        typeof(EventToCommandBehavior));

    public static readonly DependencyProperty EventNameProperty = DependencyProperty.RegisterAttached(
        "EventName",
        typeof(string),
        typeof(EventToCommandBehavior),
        new PropertyMetadata(null, OnHookChanged));

    public static void SetEventName(DependencyObject element, string? value) =>
        element.SetValue(EventNameProperty, value);

    public static string? GetEventName(DependencyObject element) =>
        (string?)element.GetValue(EventNameProperty);

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(EventToCommandBehavior),
        new PropertyMetadata(null, OnHookChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CommandProperty);

    public static readonly DependencyProperty HandledEventsTooProperty = DependencyProperty.RegisterAttached(
        "HandledEventsToo",
        typeof(bool),
        typeof(EventToCommandBehavior),
        new PropertyMetadata(false, OnHandledEventsTooChanged));

    public static void SetHandledEventsToo(DependencyObject element, bool value) =>
        element.SetValue(HandledEventsTooProperty, value);

    public static bool GetHandledEventsToo(DependencyObject element) =>
        (bool)element.GetValue(HandledEventsTooProperty);

    private static void OnHookChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element) return;
        Unhook(element);
        Hook(element);
    }

    private static void Hook(FrameworkElement element)
    {
        string? eventName = GetEventName(element);
        if (string.IsNullOrEmpty(eventName)) return;

        EventInfo? eventInfo = element.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
        if (eventInfo is null) return;

        RoutedEventHandler handler = (sender, _) =>
        {
            if (sender is not DependencyObject senderObject) return;
            ICommand? command = GetCommand(senderObject);
            if (command?.CanExecute(null) == true) command.Execute(null);
        };

        element.SetValue(HandlerProperty, (Delegate)handler);
        eventInfo.AddEventHandler(element, handler);
    }

    private static void Unhook(FrameworkElement element)
    {
        string? eventName = GetEventName(element);
        if (string.IsNullOrEmpty(eventName)) return;
        if (element.GetValue(HandlerProperty) is not RoutedEventHandler handler) return;

        element.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance)
            ?.RemoveEventHandler(element, handler);
        element.ClearValue(HandlerProperty);
    }

    private static void OnHandledEventsTooChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not ButtonBase button) return;

        button.Click -= OnHandledClick;
        if ((bool)args.NewValue) button.Click += OnHandledClick;
    }

    private static void OnHandledClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
    }
}
