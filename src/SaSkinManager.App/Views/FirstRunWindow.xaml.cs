using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaSkinManager.App.Services;
using SaSkinManager.Core;
using SaSkinManager.Core.Game;

namespace SaSkinManager.App.Views;

public partial class FirstRunWindow : Window
{
    private sealed record LangOption(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public string? SelectedPath { get; private set; }

    /// <summary>true se o usuário trocou o idioma: a tela é reaberta já traduzida.</summary>
    public bool LanguageChanged { get; private set; }
    public string TypedPath => PathBox.Text.Trim();

    private readonly bool _ready;

    public FirstRunWindow(string? currentPath)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);

        var langs = new List<LangOption> { new(L.Portuguese, "Português (Brasil)"), new(L.English, "English") };
        LanguageBox.ItemsSource = langs;
        LanguageBox.SelectedItem = langs.First(l => l.Value == L.Lang);

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
        _ready = true;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageBox.SelectedItem is not LangOption o || o.Value == L.Lang) return;
        App.Settings.Language = o.Value;
        App.Settings.Save(App.Paths);
        L.Set(o.Value);
        LanguageChanged = true;
        DialogResult = false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = L.T("Pasta do GTA San Andreas (onde está o gta_sa.exe)", "GTA San Andreas folder (where gta_sa.exe is)") };
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
            ValidationText.Text = L.T("✔ GTA San Andreas encontrado", "✔ GTA San Andreas found") + (g.HasModloader
                ? L.T(" · Mod Loader instalado", " · Mod Loader installed")
                : L.T(" · sem Mod Loader (as trocas editam o gta3.img com backup)", " · no Mod Loader (swaps edit gta3.img with a backup)"));
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
        LanguageBox.IsEnabled = false;

        var progress = new Progress<ScanProgress>(p =>
        {
            Progress.Value = p.Total == 0 ? 0 : (double)p.Done / p.Total;
            ProgressText.Text = p.Current.Length > 0
                ? L.T($"{p.Done} de {p.Total} · {p.Current}", $"{p.Done} of {p.Total} · {p.Current}")
                : L.T($"{p.Total} personagens carregados", $"{p.Total} characters loaded");
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
            MessageBox.Show(this, L.T("Não foi possível ler o jogo:\n", "Could not read the game:\n") + ex.Message,
                L.T("Erro", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            ScanPanel.Visibility = Visibility.Collapsed;
            SetupPanel.Visibility = Visibility.Visible;
            LanguageBox.IsEnabled = true;
        }
    }
}
