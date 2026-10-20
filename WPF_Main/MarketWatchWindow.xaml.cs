using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using AlgoTrading.Models;
using AlgoTrading.ViewModels;

namespace AlgoTrading;

public partial class MarketWatchWindow : Window
{
    private const int WmSysCommand = 0x0112;
    private const int ScClose = 0xF060;
    private const uint MfByCommand = 0;
    private const uint MfGrayed = 1;

    private readonly MarketWatchViewModel _viewModel;
    private readonly DispatcherTimer _closeButtonTimer;
    private HwndSource? _windowSource;
    private WindowState _previousWindowState;

    public MarketWatchWindow(MarketWatchViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new System.ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        _previousWindowState = WindowState;
        _closeButtonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _closeButtonTimer.Tick += (_, _) => UpdateCloseButtonVisibility();
        _closeButtonTimer.Start();
        UpdateCloseButtonVisibility();

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.LoadAsync();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        nint handle = new WindowInteropHelper(this).Handle;
        nint systemMenu = GetSystemMenu(handle, false);
        if (systemMenu != 0)
        {
            EnableMenuItem(systemMenu, ScClose, MfByCommand | MfGrayed);
            DrawMenuBar(handle);
        }

        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmSysCommand && ((int)wParam & 0xFFF0) == ScClose)
            handled = true;

        return 0;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        WindowState currentState = WindowState;
        if (_previousWindowState == WindowState.Maximized && currentState == WindowState.Normal)
            Dispatcher.BeginInvoke(CenterOnOwner, DispatcherPriority.Loaded);

        _previousWindowState = currentState;
    }

    private void CenterOnOwner()
    {
        Window? owner = Owner ?? Application.Current?.MainWindow;
        if (owner is null) return;

        Left = owner.Left + (owner.ActualWidth - ActualWidth) / 2;
        Top = owner.Top + (owner.ActualHeight - ActualHeight) / 2;
    }

    private void UpdateCloseButtonVisibility()
    {
        TimeSpan marketTime = MarketTimestamp.ToDatabase(DateTimeOffset.UtcNow).TimeOfDay;
        bool marketHours = marketTime >= new TimeSpan(9, 15, 0) && marketTime < new TimeSpan(16, 0, 0);
        CloseButton.Visibility = marketHours ? Visibility.Hidden : Visibility.Visible;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _closeButtonTimer.Stop();
        _windowSource?.RemoveHook(WindowMessageHook);
    }

    [DllImport("user32.dll")]
    private static extern nint GetSystemMenu(nint windowHandle, bool revert);

    [DllImport("user32.dll")]
    private static extern bool EnableMenuItem(nint menuHandle, uint item, uint enable);

    [DllImport("user32.dll")]
    private static extern bool DrawMenuBar(nint windowHandle);

    private void TextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
       (sender as TextBox)?.ScrollToEnd();
    }
}
