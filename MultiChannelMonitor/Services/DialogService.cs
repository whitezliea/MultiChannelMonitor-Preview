using System.Windows;

namespace Presentation.Wpf.Services;

public sealed class DialogService
{
    public void ShowInformation(string message) => MessageBox.Show(message, "MultiChannel Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
}
