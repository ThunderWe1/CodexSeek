using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace CodexSeek.Dialogs;

public partial class FluentDialog : FluentWindow
{
    public FluentDialog(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        bool danger = false)
    {
        InitializeComponent();

        Title = title;
        DialogTitleBar.Title = title;
        MessageText.Text = message;
        PrimaryButton.Content = primaryText;

        if (string.IsNullOrWhiteSpace(secondaryText))
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = secondaryText;
        }

        if (danger)
        {
            PrimaryButton.Appearance = ControlAppearance.Danger;
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            DialogIcon.Symbol = SymbolRegular.ErrorCircle24;
        }
        else
        {
            PrimaryButton.Appearance = ControlAppearance.Primary;
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));
            DialogIcon.Symbol = SymbolRegular.Info24;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
