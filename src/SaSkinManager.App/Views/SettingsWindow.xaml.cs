using System.Diagnostics;
using System.Reflection;
using System.Windows;
using SaSkinManager.App.Services;
using SaSkinManager.App.ViewModels;
using SaSkinManager.Core;

namespace SaSkinManager.App.Views;

public partial class SettingsWindow : Window
{
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

        ThumbSlider.Value = Math.Clamp(s.ThumbSize, 72, 200);
        CloseOnPlayBox.IsChecked = s.CloseOnPlay;
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
        s.Save(App.Paths);

        LanguageChanged = L.Resolve(s.Language) != L.Resolve(_oldLanguage);
        L.Set(s.Language);
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
