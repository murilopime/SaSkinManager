using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SaSkinManager.App.Services;
using SaSkinManager.Core;
using SaSkinManager.Core.Game;
using SaSkinManager.Core.Library;
using SaSkinManager.Core.Swap;

namespace SaSkinManager.App.ViewModels;

public sealed partial class PedItem : ObservableObject
{
    public PedItem(GameModel model, ImageSource? thumb)
    {
        Model = model;
        Thumb = thumb;
    }

    public GameModel Model { get; }
    public ImageSource? Thumb { get; }
    public string Title => Model.DisplayName;
    public string Subtitle => Model.Kind == ModelKind.Player ? "player.img · especial"
        : Model.Id < 0 ? $"{Model.ModelName} · {(Model.Category == PedCatalog.CutsceneCategory ? "cutscene" : "missão")}"
        : $"{Model.ModelName} · ID {Model.Id}";
    public bool IsCj => Model.Kind == ModelKind.Player;

    [ObservableProperty] private string? _swapText;
    public bool IsSwapped => SwapText is not null;
    partial void OnSwapTextChanged(string? value) => OnPropertyChanged(nameof(IsSwapped));
}

public sealed class SkinItem
{
    public SkinItem(LibrarySkin skin, ImageSource? thumb)
    {
        Skin = skin;
        Thumb = thumb;
    }

    public LibrarySkin Skin { get; }
    public ImageSource? Thumb { get; }
    public string Title => Skin.Name;
    public string Subtitle => Skin.Kind == SkinKind.CjPack
        ? $"Pacote do CJ · {Skin.Files.Count} arquivos"
        : $"{Skin.DffFile} + {Skin.TxdFile}";
}

public sealed record ModeOption(SwapMode Mode, string Label);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly GameInstall _game;
    private readonly CatalogScanner _scanner;
    private readonly SkinLibrary _library;
    private readonly SwapService _swaps;

    public MainViewModel(GameInstall game)
    {
        _game = game;
        _scanner = new CatalogScanner(game, App.Paths);
        _library = new SkinLibrary(App.Paths);
        _swaps = new SwapService(game, _library, App.Paths);

        PedsView = CollectionViewSource.GetDefaultView(Peds);
        PedsView.Filter = FilterPed;
        _selectedMode = Modes.FirstOrDefault(m => m.Mode == App.Settings.PreferredMode) ?? Modes[0];
        ReloadSkins();
        RefreshSwaps();
    }

    public string GamePath => _game.Root;
    public string ModeBadge => _game.HasModloader ? "Mod Loader detectado" : "Sem Mod Loader · modo IMG";

    public ObservableCollection<PedItem> Peds { get; } = new();
    public ICollectionView PedsView { get; }
    public ObservableCollection<string> Categories { get; } = new() { "Todas" };
    public ObservableCollection<SkinItem> Skins { get; } = new();
    public ObservableCollection<SwapRecord> ActiveSwaps { get; } = new();

    public List<ModeOption> Modes { get; } = new()
    {
        new(SwapMode.Auto, "Automático (Mod Loader se existir)"),
        new(SwapMode.Modloader, "Mod Loader (não mexe nos .img)"),
        new(SwapMode.Img, "Editar .img direto (com backup)"),
    };

    [ObservableProperty] private ModeOption _selectedMode;
    partial void OnSelectedModeChanged(ModeOption value)
    {
        App.Settings.PreferredMode = value.Mode;
        App.Settings.Save(App.Paths);
    }

    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value) => PedsView.Refresh();

    [ObservableProperty] private string _selectedCategory = "Todas";
    partial void OnSelectedCategoryChanged(string value) => PedsView.Refresh();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RevertSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedSwap), nameof(HasSelection))]
    private PedItem? _selectedPed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RemoveSkinCommand))]
    private SkinItem? _selectedSkin;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string _status = "Pronto.";
    [ObservableProperty] private int _pedCount;

    public bool HasSelection => SelectedPed is not null;
    public SwapRecord? SelectedSwap => SelectedPed is null ? null : _swaps.FindFor(SelectedPed.Model);

    private bool FilterPed(object o)
    {
        var p = (PedItem)o;
        if (SelectedCategory != "Todas" && p.Model.Category != SelectedCategory) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return p.Model.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Model.ModelName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Model.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Model.Id.ToString() == q;
    }

    /// <summary>Carrega do cache; se não houver (ou o jogo mudou), escaneia tudo automaticamente.</summary>
    public async Task InitializeAsync()
    {
        var models = _scanner.TryLoadCache();
        if (models is null) await RescanAsync();
        else Fill(models);
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        IsBusy = true;
        BusyText = "Carregando todos os personagens do jogo…";
        Progress = 0;
        try
        {
            var prog = new Progress<ScanProgress>(p =>
            {
                Progress = p.Total == 0 ? 0 : (double)p.Done / p.Total;
                BusyText = p.Current.Length > 0 ? $"Lendo {p.Current} ({p.Done}/{p.Total})" : "Concluído";
            });
            _scanner.ClearCache();
            var models = await Task.Run(() => _scanner.Scan((t, path) => Thumbs.Save(t, path), prog));
            Fill(models);
            Status = $"{models.Count} personagens carregados.";
        }
        catch (Exception e)
        {
            Status = "Erro ao escanear: " + e.Message;
            MessageBox.Show(e.Message, "Erro ao escanear o jogo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Fill(List<GameModel> models)
    {
        var selectedKey = SelectedPed?.Model.Key;
        Peds.Clear();
        foreach (var m in models) Peds.Add(new PedItem(m, Thumbs.Load(_scanner.ThumbPath(m))));
        PedCount = models.Count;

        var cats = models.Select(m => m.Category).Distinct().OrderBy(c => c == PedCatalog.CjCategory ? "" : c).ToList();
        Categories.Clear();
        Categories.Add("Todas");
        foreach (var c in cats) Categories.Add(c);
        SelectedCategory = "Todas";

        RefreshSwaps();
        SelectedPed = Peds.FirstOrDefault(p => p.Model.Key == selectedKey) ?? Peds.FirstOrDefault();
    }

    private void ReloadSkins()
    {
        var selected = SelectedSkin?.Skin.Id;
        Skins.Clear();
        foreach (var s in _library.Skins)
        {
            try { Thumbs.EnsureSkinThumb(_library, s); } catch (Exception) { }
            Skins.Add(new SkinItem(s, Thumbs.Load(_library.ThumbPath(s))));
        }
        SelectedSkin = Skins.FirstOrDefault(s => s.Skin.Id == selected) ?? SelectedSkin;
    }

    private void RefreshSwaps()
    {
        ActiveSwaps.Clear();
        foreach (var r in _swaps.Manifest.Records.OrderByDescending(r => r.AppliedAt)) ActiveSwaps.Add(r);
        foreach (var p in Peds)
        {
            var r = _swaps.FindFor(p.Model);
            p.SwapText = r is null ? null : $"→ {r.SkinName}";
        }
        OnPropertyChanged(nameof(SelectedSwap));
        RevertSelectedCommand.NotifyCanExecuteChanged();
        RevertAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanApply() => SelectedPed is not null && SelectedSkin is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        var target = SelectedPed!.Model;
        var skin = SelectedSkin!.Skin;
        if (target.Kind == ModelKind.Player && skin.Kind != SkinKind.CjPack)
        {
            MessageBox.Show(
                "O CJ não é um personagem comum: o corpo dele é montado por peças de roupa do player.img.\n\n" +
                "• Se o mod que você baixou é um \"CJ replacement\" com várias peças (torso, legs, feet, head…), importe com \"Pacote do CJ\".\n" +
                "• Para jogar com um modelo de NPC comum no lugar do CJ, use um mod como o Skin Selector (CLEO).",
                "Trocar o CJ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Run(() =>
        {
            var rec = _swaps.Apply(target, skin, SelectedMode.Mode);
            Status = $"{target.DisplayName} agora usa \"{skin.Name}\" ({ModeName(rec.Mode)}).";
        });
    }

    private bool CanRevertSelected() => SelectedSwap is not null;

    [RelayCommand(CanExecute = nameof(CanRevertSelected))]
    private void RevertSelected()
    {
        if (SelectedSwap is { } r) Revert(r);
    }

    [RelayCommand]
    private void Revert(SwapRecord? r)
    {
        if (r is null) return;
        Run(() =>
        {
            _swaps.Revert(r);
            Status = $"{r.TargetName} voltou ao original.";
        });
    }

    private bool CanRevertAll() => _swaps.Manifest.Records.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRevertAll))]
    private void RevertAll()
    {
        if (MessageBox.Show($"Desfazer todas as {_swaps.Manifest.Records.Count} trocas e voltar os personagens ao original?",
                "Restaurar tudo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() =>
        {
            _swaps.RevertAll();
            Status = "Todos os personagens voltaram ao original.";
        });
    }

    private void Run(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is SwapException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidDataException or NotSupportedException)
        {
            var msg = e is UnauthorizedAccessException
                ? "Sem permissão para escrever na pasta do jogo. Rode o programa como administrador ou mova o jogo para fora de \"Arquivos de Programas\".\n\n" + e.Message
                : e.Message;
            Status = "Erro: " + msg;
            MessageBox.Show(msg, "Não foi possível concluir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshSwaps();
    }

    private static string ModeName(SwapMode m) => m == SwapMode.Modloader ? "Mod Loader" : "IMG";

    [RelayCommand]
    private void ImportFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Escolha os arquivos da skin (.dff + .txd) ou um .zip",
            Filter = "Skins do GTA SA (*.dff;*.txd;*.zip)|*.dff;*.txd;*.zip|Todos os arquivos|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true) ImportPaths(dlg.FileNames, false);
    }

    [RelayCommand]
    private void ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Pasta com os arquivos da skin" };
        if (dlg.ShowDialog() == true) ImportPaths(new[] { dlg.FolderName }, false);
    }

    [RelayCommand]
    private void ImportCjPack()
    {
        var dlg = new OpenFolderDialog { Title = "Pasta do pacote do CJ (peças do player.img)" };
        if (dlg.ShowDialog() == true) ImportPaths(new[] { dlg.FolderName }, true);
    }

    public void ImportPaths(IEnumerable<string> paths, bool asCjPack)
    {
        try
        {
            var r = _library.Import(paths, asCjPack);
            ReloadSkins();
            if (r.Imported.Count > 0) SelectedSkin = Skins.FirstOrDefault(s => s.Skin.Id == r.Imported[0].Id);
            Status = r.Imported.Count == 1 ? $"Skin \"{r.Imported[0].Name}\" adicionada." : $"{r.Imported.Count} skins adicionadas.";
            if (r.Warnings.Count > 0)
                MessageBox.Show(string.Join("\n", r.Warnings), "Avisos da importação", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            MessageBox.Show(e.Message, "Erro ao importar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool HasSkin() => SelectedSkin is not null;

    [RelayCommand(CanExecute = nameof(HasSkin))]
    private void RemoveSkin()
    {
        var s = SelectedSkin!.Skin;
        if (_swaps.Manifest.Records.Any(r => r.SkinId == s.Id))
        {
            MessageBox.Show("Esta skin está em uso. Desfaça as trocas que a usam antes de removê-la.", "Remover skin");
            return;
        }
        if (MessageBox.Show($"Remover \"{s.Name}\" da biblioteca?", "Remover skin", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _library.Remove(s);
        SelectedSkin = null;
        ReloadSkins();
    }

    [RelayCommand]
    private void Play()
    {
        try { _game.Launch(); Status = "Iniciando o GTA San Andreas…"; }
        catch (Exception e) { MessageBox.Show(e.Message, "Jogar"); }
    }

    [RelayCommand]
    private void OpenGameFolder() =>
        Process.Start(new ProcessStartInfo(_game.Root) { UseShellExecute = true });
}
