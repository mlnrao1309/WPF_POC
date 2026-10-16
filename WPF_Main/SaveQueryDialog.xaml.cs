using System.Windows;

namespace AlgoTrading.Screener.Wpf;

/// <summary>
/// Small modal that asks for a query name. Returns DialogResult = true
/// when the user clicks Save; QueryName holds the trimmed value.
/// </summary>
public partial class SaveQueryDialog : Window
{
    public string QueryName { get; private set; } = string.Empty;

    public SaveQueryDialog(string suggestedName)
    {
        InitializeComponent();

        NameBox.Text = suggestedName ?? string.Empty;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this,
                "Enter a name.",
                "Save Query", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (name.Length > 200)
        {
            MessageBox.Show(this,
                "Name is too long (max 200 characters).",
                "Save Query", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        QueryName   = name;
        DialogResult = true;
        Close();
    }
}