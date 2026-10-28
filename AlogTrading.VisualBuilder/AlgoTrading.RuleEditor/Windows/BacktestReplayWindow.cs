using System.IO;

using System.Text.Json;

using System.Windows;

using System.Windows.Controls;

using AlgoTrading.RuleEditor.Services;

using Microsoft.Web.WebView2.Core;

using Microsoft.Web.WebView2.Wpf;



namespace AlgoTrading.RuleEditor.Windows;



public sealed class BacktestReplayWindow : Window

{

    private readonly WebView2 _viewer = new();

    private readonly TextBlock _status = new() { Text = "Loading saved replay...", Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap };

    private readonly string _runJson;

    private bool _closed;



    public BacktestReplayWindow(ReplayRunSnapshot run)

    {

        Title = "Backtest replay · " + run.RunId;

        Width = SystemParameters.WorkArea.Width * 0.85;

        Height = SystemParameters.WorkArea.Height * 0.85;

        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _runJson = JsonSerializer.Serialize(run, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var layout = new Grid();

        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.RowDefinitions.Add(new RowDefinition());

        var export = new Button { Visibility = Visibility.Collapsed, Content = "Save replay snapshot JSON", Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };

        export.Click += (_, _) =>

        {

            var dialog = new Microsoft.Win32.SaveFileDialog

            {

                Filter = "Replay snapshot (*.json)|*.json",

                FileName = $"replay-{run.RunId}.json"

            };

            if (dialog.ShowDialog(this) != true) return;

            try { File.WriteAllText(dialog.FileName, _runJson); }

            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Snapshot could not be saved"); }

        };

        layout.Children.Add(export);

        Grid.SetRow(_viewer, 1);

        Grid.SetRow(_status, 1);

        layout.Children.Add(_viewer);

        layout.Children.Add(_status);

        Content = layout;

        Loaded += OnLoaded;

        Closed += OnClosed;

    }



    private async void OnLoaded(object sender, RoutedEventArgs e)

    {

        Loaded -= OnLoaded;

        try

        {

            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "AlgoTradingReplay"));

            if (_closed) return;

            await _viewer.EnsureCoreWebView2Async(environment);

            if (_closed) return;

            _viewer.CoreWebView2.SetVirtualHostNameToFolderMapping("replay.local",

                Path.Combine(AppContext.BaseDirectory, "Replay"), CoreWebView2HostResourceAccessKind.DenyCors);

            _viewer.NavigationCompleted += OnNavigationCompleted;

            _viewer.Source = new Uri("https://replay.local/index.html");

        }

        catch (Exception ex)

        {

            if (!_closed) _status.Text = "Replay could not be opened: " + ex.Message;

        }

    }



    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)

    {

        if (!e.IsSuccess) { _status.Text = "Replay could not be opened: " + e.WebErrorStatus; return; }

        _viewer.CoreWebView2.PostWebMessageAsJson(_runJson);

        _status.Visibility = Visibility.Collapsed;

    }



    private void OnClosed(object? sender, EventArgs e)

    {

        _closed = true;

        _viewer.Dispose();

    }

}

