using System.Windows;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

public partial class ExportOptionsWindow : Window
{
    public ExportOptionsWindow(bool hasVideo = false, bool hasInputLog = false, bool hasEnvironment = false)
    {
        InitializeComponent();
        VideoCheckBox.IsEnabled = hasVideo;
        VideoCheckBox.IsChecked = hasVideo;
        if (!hasVideo)
        {
            VideoDescription.Text = "No video was recorded for this guide.";
        }

        InputLogCheckBox.IsEnabled = hasInputLog;
        InputLogCheckBox.IsChecked = hasInputLog;
        EnvironmentCheckBox.IsEnabled = hasEnvironment;
        EnvironmentCheckBox.IsChecked = hasEnvironment;
    }

    public GuideExportFormat SelectedFormats { get; private set; } = GuideExportFormat.Markdown | GuideExportFormat.Html;

    public GuideExportOptions SelectedOptions { get; private set; } = GuideExportOptions.Default;

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var formats = 0;
        if (MarkdownCheckBox.IsChecked == true)
        {
            formats |= (int)GuideExportFormat.Markdown;
        }

        if (HtmlCheckBox.IsChecked == true)
        {
            formats |= (int)GuideExportFormat.Html;
        }

        if (formats == 0)
        {
            System.Windows.MessageBox.Show("Choose at least one export file type.", "OpenSteps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedFormats = (GuideExportFormat)formats;
        SelectedOptions = new GuideExportOptions(
            IncludeVideo: VideoCheckBox.IsChecked == true,
            IncludeInputLog: InputLogCheckBox.IsChecked == true,
            IncludeEnvironment: EnvironmentCheckBox.IsChecked == true,
            CreateZip: ZipCheckBox.IsChecked == true);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
