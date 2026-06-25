using System.Windows;

namespace OfisPensionera.Launcher;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        rbRu.IsChecked = App.CurrentLanguage == "ru";
        rbEn.IsChecked = App.CurrentLanguage == "en";

        var (name, birthDate) = SharedDb.GetUserProfile();
        tbName.Text  = name      ?? "";
        tbBirth.Text = birthDate ?? "";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SharedDb.SetUserProfile(
            string.IsNullOrWhiteSpace(tbName.Text)  ? null : tbName.Text.Trim(),
            string.IsNullOrWhiteSpace(tbBirth.Text) ? null : tbBirth.Text.Trim());
        App.SetLanguage(rbEn.IsChecked == true ? "en" : "ru");
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        btnCheckUpdates.IsEnabled = false;
        bool found = await Updater.CheckSeniorHubUpdateAsync();
        btnCheckUpdates.IsEnabled = true;

        if (!found)
        {
            string noUpdates = (string)Application.Current.Resources["MsgNoUpdates"];
            MessageBox.Show(noUpdates, "SeniorHub", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
