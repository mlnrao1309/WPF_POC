using System.Windows;

namespace AlgoTrading.Views
{
    public partial class StatusPopupWindow : Window
    {
        public StatusPopupWindow(string title, string details)
        {
            InitializeComponent();
            TitleBlock.Text = title;
            DetailsBlock.Text = details;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
