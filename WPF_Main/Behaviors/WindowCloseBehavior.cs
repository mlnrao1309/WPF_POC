using System.Windows;
using System.Windows.Input;

namespace AlgoTrading.Behaviors;

/// <summary>
/// Attached behavior for a <see cref="Window"/> that turns window lifetime into pure MVVM:
/// the bound <see cref="CloseCommand"/> is invoked through a wrapper that closes the window,
/// and <see cref="ClosedCommand"/> runs cleanup once the window has closed.
/// </summary>
public static class WindowCloseBehavior
{
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.RegisterAttached(
        "CloseCommand",
        typeof(ICommand),
        typeof(WindowCloseBehavior),
        new PropertyMetadata(null, OnCloseCommandChanged));

    public static void SetCloseCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(CloseCommandProperty, value);

    public static ICommand? GetCloseCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(CloseCommandProperty);

    public static readonly DependencyProperty ClosedCommandProperty = DependencyProperty.RegisterAttached(
        "ClosedCommand",
        typeof(ICommand),
        typeof(WindowCloseBehavior),
        new PropertyMetadata(null, OnClosedCommandChanged));

    public static void SetClosedCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ClosedCommandProperty, value);

    public static ICommand? GetClosedCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ClosedCommandProperty);

    private static void OnCloseCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Window window || args.NewValue is not ICommand command) return;
        if (command is CloseWindowCommand) return;

        // Replace the DataContext's CloseCommand with a wrapper that also closes this window,
        // so the VM close logic and the physical close stay in one command path.
        window.DataContextChanged += OnDataContextChanged;
        WrapCloseCommand(window, window.DataContext);
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Window window) WrapCloseCommand(window, args.NewValue);
    }

    private static void WrapCloseCommand(Window window, object? dataContext)
    {
        if (dataContext is null) return;

        var closeCommandProperty = dataContext.GetType().GetProperty("CloseCommand");
        if (closeCommandProperty?.GetValue(dataContext) is not ICommand closeCommand) return;
        if (closeCommand is CloseWindowCommand) return;

        closeCommandProperty.SetValue(dataContext, new CloseWindowCommand(window, closeCommand));
    }

    private static void OnClosedCommandChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Window window) return;

        window.Closed -= OnWindowClosed;
        if (args.NewValue is ICommand) window.Closed += OnWindowClosed;
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.Closed -= OnWindowClosed;

        ICommand? command = GetClosedCommand(window);
        if (command?.CanExecute(null) == true) command.Execute(null);
    }

    /// <summary>
    /// Executes the ViewModel's close logic, then closes the window.
    /// </summary>
    private sealed class CloseWindowCommand : ICommand
    {
        private readonly Window _window;
        private readonly ICommand _inner;

        public CloseWindowCommand(Window window, ICommand inner)
        {
            _window = window;
            _inner = inner;
        }

        public bool CanExecute(object? parameter) => _inner.CanExecute(parameter);

        public void Execute(object? parameter)
        {
            if (!_inner.CanExecute(parameter)) return;
            _inner.Execute(parameter);
            _window.Close();
        }

        public event EventHandler? CanExecuteChanged
        {
            add => _inner.CanExecuteChanged += value;
            remove => _inner.CanExecuteChanged -= value;
        }
    }
}
