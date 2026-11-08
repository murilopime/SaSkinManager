using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaSkinManager.App.Services;
using SaSkinManager.Core.Game;

namespace SaSkinManager.App.Views;

public partial class FirstRunWindow : Window
{
    public string? SelectedPath { get; private set; }

    public FirstRunWindow(string? currentPath)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            var found = await Task.Run(() => GameInstall.GuessLocations().ToList());
            if (found.Count > 0)
            {
                DetectedHeader.Visibility = Visibility.Visible;
                DetectedList.ItemsSource = found;
                if (string.IsNullOrEmpty(PathBox.Text)) PathBox.Text = found[0];
            }
        };
        PathBox.Text = currentPath ?? "";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Pasta do GTA San Andreas (onde está o gta_sa.exe)" };
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FolderName;
    }

    private void Detected_Click(object sender, RoutedEventArgs e) =>
        PathBox.Text = (string)((Button)sender).Content;

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var err = GameInstall.Validate(PathBox.Text.Trim());
        ContinueButton.IsEnabled = err is null;
        if (string.IsNullOrWhiteSpace(PathBox.Text))
        {
            ValidationText.Text = "";
            return;
        }
        if (err is null)
        {
            var g = GameInstall.Open(PathBox.Text.Trim());
            ValidationText.Text = "✔ GTA San Andreas encontrado" + (g.HasModloader ? " · Mod Loader instalado" : " · sem Mod Loader (as trocas editam o gta3.img com backup)");
            ValidationText.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
        }
        else
        {
            ValidationText.Text = "✖ " + err;
            ValidationText.Foreground = (System.Windows.Media.Brush)FindResource("Danger");
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void Continue_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        var game = GameInstall.Open(path);
        bool withThumbs = ScanNowBox.IsChecked == true;

        SetupPanel.Visibility = Visibility.Collapsed;
        ScanPanel.Visibility = Visibility.Visible;

        var progress = new Progress<ScanProgress>(p =>
        {
            Progress.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
            ProgressText.Text = p.Current.Length > 0 ? $"{p.Done} de {p.Total} · {p.Current}" : $"{p.Total} personagens carregados";
        });
        try
        {
            var scanner = new CatalogScanner(game, App.Paths);
            scanner.ClearCache();
            await Task.Run(() => scanner.Scan(withThumbs ? (t, p) => Thumbs.Save(t, p) : null, progress));
            SelectedPath = game.Root;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Não foi possível ler o jogo:\n" + ex.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
            ScanPanel.Visibility = Visibility.Collapsed;
            SetupPanel.Visibility = Visibility.Visible;
        }
    }
}
