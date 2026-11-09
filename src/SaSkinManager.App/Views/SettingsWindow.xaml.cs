using System.Diagnostics;
using System.Reflection;
using System.Windows;
using SaSkinManager.App.Services;
using SaSkinManager.App.ViewModels;
using SaSkinManager.Core;

namespace SaSkinManager.App.Views;

public partial class SettingsWindow : Window
{
    private bool _loadingAdult;

    /// <summary>Ao ligar o modo 18+, avisa o que vai aparecer e pede confirmação.</summary>
    private void AdultBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingAdult) return;
        var r = MessageBox.Show(this,
            SaSkinManager.Core.L.T(
                "Ao ativar o modo 18+, a lista vai mostrar os modelos das namoradas do CJ na versão \"Hot Coffee\" (versões 1 e 2: roupa íntima e sem roupa).\n\nEsse conteúdo é só para maiores de 18 anos. Deseja ativar?",
                "Turning on 18+ mode will show the \"Hot Coffee\" versions of CJ's girlfriends in the list (versions 1 and 2: underwear and nude).\n\nThis content is for adults (18+) only. Turn it on?"),
            SaSkinManager.Core.L.T("Modo 18+", "18+ mode"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) AdultBox.IsChecked = false;
    }

    private sealed record Option<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly string _oldLanguage = App.Settings.Language;

    public bool LanguageChanged { get; private set; }
    public bool RescanRequested { get; private set; }
    public bool ChangeGameFolderRequested { get; private set; }

    public SettingsWindow(string gamePath)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        var s = App.Settings;

        var langs = new List<Option<string>>
        {
            new(L.Auto, L.T("Automático (idioma do Windows)", "Automatic (Windows language)")),
            new(L.Portuguese, "Português (Brasil)"),
            new(L.English, "English"),
        };
        LanguageBox.ItemsSource = langs;
        LanguageBox.SelectedItem = langs.FirstOrDefault(o => o.Value == s.Language) ?? langs[0];

        var modes = ModeOption.All();
        ModeBox.ItemsSource = modes;
        ModeBox.SelectedItem = modes.FirstOrDefault(m => m.Mode == s.PreferredMode) ?? modes[0];

        var views = new List<Option<bool>>
        {
            new(true, L.T("Grade (miniaturas grandes)", "Grid (large thumbnails)")),
            new(false, L.T("Lista (compacta)", "List (compact)")),
        };
        ViewBox.ItemsSource = views;
        ViewBox.SelectedItem = views.First(v => v.Value == s.GridView);

        var accents = AccentColors.All();
        AccentBox.ItemsSource = accents;
        AccentBox.SelectedItem = accents.FirstOrDefault(a => a.Key == s.Accent) ?? accents[0];
        _ready = true;
        // fechou sem salvar (Cancelar, Esc ou X): volta a cor que estava
        Closed += (_, _) => { if (DialogResult != true) AccentColors.Apply(App.Settings.Accent); };

        ThumbSlider.Value = Math.Clamp(s.ThumbSize, 72, 200);
        CloseOnPlayBox.IsChecked = s.CloseOnPlay;
        ShowCutscenesBox.IsChecked = s.ShowCutscenes;
        _loadingAdult = true;
        AdultBox.IsChecked = s.AdultMode;
        _loadingAdult = false;
        Preview3DBox.IsChecked = s.Preview3D;
        AutoRotateBox.IsChecked = s.AutoRotate;
        SpinGridBox.IsChecked = s.SpinGrid;
        GamePathText.Text = gamePath;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AboutText.Text = $"SA Skin Manager {version?.ToString(3)} · GPL-3.0\n" +
                         L.T("Dados do programa: ", "Program data: ") + App.Paths.Root;
    }

    private void Apply()
    {
        var s = App.Settings;
        s.Language = ((Option<string>)LanguageBox.SelectedItem).Value;
        s.PreferredMode = ((ModeOption)ModeBox.SelectedItem).Mode;
        s.GridView = ((Option<bool>)ViewBox.SelectedItem).Value;
        s.ThumbSize = (int)ThumbSlider.Value;
        s.CloseOnPlay = CloseOnPlayBox.IsChecked == true;
        s.ShowCutscenes = ShowCutscenesBox.IsChecked == true;
        s.AdultMode = AdultBox.IsChecked == true;
        s.Preview3D = Preview3DBox.IsChecked == true;
        s.AutoRotate = AutoRotateBox.IsChecked == true;
        s.SpinGrid = SpinGridBox.IsChecked == true;
        s.Accent = ((AccentOption)AccentBox.SelectedItem).Key;
        s.Save(App.Paths);

        LanguageChanged = L.Resolve(s.Language) != L.Resolve(_oldLanguage);
        L.Set(s.Language);
    }

    private bool _ready;

    /// <summary>Prévia ao vivo: a cor muda no programa inteiro assim que é escolhida.</summary>
    private void Accent_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_ready && AccentBox.SelectedItem is AccentOption a) AccentColors.Apply(a.Key);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        DialogResult = true;
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        ChangeGameFolderRequested = true;
        DialogResult = true;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        Apply();
        RescanRequested = true;
        DialogResult = true;
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(App.Paths.Root) { UseShellExecute = true });
}
