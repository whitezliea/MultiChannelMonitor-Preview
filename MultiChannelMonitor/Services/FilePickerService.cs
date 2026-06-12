namespace Presentation.Wpf.Services;

public sealed class FilePickerService
{
    public string GetDefaultExportPath(string fileName) =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), fileName);

    public string? PickCsvSavePath(string suggestedFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = suggestedFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
