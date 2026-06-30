using System.Windows;
using Microsoft.Win32;

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

    private static string Res(string key) => (string)Application.Current.Resources[key];

    // ── Резервное копирование ────────────────────────────────────────────────

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title    = Res("BtnBackup"),
            FileName = $"Офис_пенсионера_копия_{DateTime.Now:yyyy-MM-dd}.zip",
            Filter   = Res("BackupFileFilter") + "|*.zip",
            AddExtension = true,
            DefaultExt   = "zip"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            SharedDb.ExportTo(dlg.FileName);
            MessageBox.Show(Res("BackupDone"), "SeniorHub",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Res("BackupFailed")}\n{ex.Message}", "SeniorHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = Res("BtnRestore"),
            Filter = Res("BackupFileFilter") + "|*.zip;*.db",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        if (MessageBox.Show(Res("RestoreConfirm"), "SeniorHub",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            SharedDb.RestoreFrom(dlg.FileName);
            // Перечитываем профиль и язык из восстановленной базы
            var (name, birthDate) = SharedDb.GetUserProfile();
            tbName.Text  = name      ?? "";
            tbBirth.Text = birthDate ?? "";
            var lang = SharedDb.GetSetting("language");
            if (lang is "en" or "ru") { rbRu.IsChecked = lang == "ru"; rbEn.IsChecked = lang == "en"; App.SetLanguage(lang); }

            MessageBox.Show(Res("RestoreDone"), "SeniorHub",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{Res("RestoreFailed")}\n{ex.Message}", "SeniorHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        btnCheckUpdates.IsEnabled = false;
        var prev = btnCheckUpdates.Content;
        btnCheckUpdates.Content = App.CurrentLanguage == "en" ? "Checking…" : "Проверяю…";

        // Полная проверка всех модулей + лаунчера (логика в MainWindow — у него список Apps и FindExe)
        bool found = Owner is MainWindow mw
            ? await mw.CheckAllUpdatesAsync()
            : await Updater.CheckSeniorHubUpdateAsync();

        btnCheckUpdates.Content = prev;
        btnCheckUpdates.IsEnabled = true;

        if (!found)
            MessageBox.Show(Res("MsgNoUpdates"), "SeniorHub", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
