using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Extensions.DependencyInjection;

namespace AlgoTrading
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.ServiceProvider.GetRequiredService<ViewModels.WebViewViewModel>();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await EnsureCoreWebView2Async();
        }

        private async Task EnsureCoreWebView2Async()
        {
            try
            {
                if (WebView2Control.CoreWebView2 == null)
                {
                   await  WebView2Control.EnsureCoreWebView2Async(null);
                }
            }
            catch
            {
                // ignore initialization errors for now
            }
        }
    }
}
