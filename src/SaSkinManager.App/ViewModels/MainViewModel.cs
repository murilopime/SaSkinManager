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
        _thumb = thumb;
        Title = PedCatalog.DisplayNameFor(model);
        Description = PedCatalog.DescriptionFor(model);
        CategoryLabel = PedCatalog.CategoryLabel(model.Category);
        IsAdult = PedCatalog.IsAdult(model);
    }

    public GameModel Model { get; }
    [ObservableProperty] private ImageSource? _thumb;

    /// <summary>Modelo 3D girando no bloco da grade (blocos visíveis na tela, ou o que está sob o mouse).</summary>
    [ObservableProperty] private System.Windows.Media.Media3D.Model3D? _liveModel;
    public string Title { get; }
    public string Description { get; }
    public string CategoryLabel { get; }
    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";

    public string Subtitle => Model.Kind == ModelKind.Player ? L.T("player.img · especial", "player.img · special")
        : Model.Id < 0 ? $"{Model.ModelName} · {(Model.Category == PedCatalog.CutsceneCategory ? "cutscene" : L.T("missão", "mission"))}"
        : $"{Model.ModelName} · ID {Model.Id}";

    public string Files => $"{Model.ModelName}.dff + {Model.TxdName}.txd · {Model.ImgName}";
    public bool IsCj => Model.Kind == ModelKind.Player;

    [ObservableProperty] private string? _swapText;
    public bool IsSwapped => SwapText is not null;
    partial void OnSwapTextChanged(string? value) => OnPropertyChanged(nameof(IsSwapped));

    [ObservableProperty] private bool _isFavorite;
    public bool IsCutscene => Model.Category == PedCatalog.CutsceneCategory;
    public bool IsAdult { get; } = false;
}

/// <summary>Filtros rápidos da lista de personagens.</summary>
public enum PedFilter { All, Swapped, NotSwapped, Favorites }

/// <summary>Modelo ligado ao personagem selecionado (cutscene ou variação de missão), trocado junto.</summary>
public sealed partial class LinkedItem : ObservableObject
{
    public LinkedItem(PedItem ped) => Ped = ped;
    public PedItem Ped { get; }
    public string Label => $"{Ped.Title} ({Ped.Model.ModelName})";
    [ObservableProperty] private bool _include = true;
}

/// <summary>Uma linha de "Trocas ativas": o personagem e, dentro, as cutscenes/variações trocadas junto.</summary>
public sealed partial class SwapGroup : ObservableObject
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string SkinName { get; init; }
    public required string ModeText { get; init; }
    public required List<SwapEntry> Entries { get; init; }
    public required IRelayCommand UndoCommand { get; init; }

    public bool HasMany => Entries.Count > 1;
    public string CountText => HasMany ? $" ({Entries.Count})" : "";

    [ObservableProperty] private bool _isExpanded;
    public string Arrow => IsExpanded ? "▾" : "▸";
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(Arrow));

    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
}

public sealed class SwapEntry
{
    public required SwapRecord Record { get; init; }
    public required string Title { get; init; }
    public string Model => Record.TargetModel;
}

public sealed partial class SkinItem : ObservableObject
{
    public SkinItem(LibrarySkin skin, ImageSource? thumb)
    {
        Skin = skin;
        _thumb = thumb;
    }

    public LibrarySkin Skin { get; }
    [ObservableProperty] private ImageSource? _thumb;
    public string Title => Skin.Name;

    /// <summary>Renomeando na própria lista (caixa de texto no lugar do nome).</summary>
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = "";

    public void NameChanged() => OnPropertyChanged(nameof(Title));

    /// <summary>Em quantos personagens a skin está aplicada agora.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsageText), nameof(IsUsed))]
    private int _usageCount;
    public bool IsUsed => UsageCount > 0;
    public string UsageText => UsageCount == 1
        ? L.T("em uso: 1 personagem", "in use: 1 character")
        : L.T($"em uso: {UsageCount} personagens", $"in use: {UsageCount} characters");
    public string Subtitle => Skin.Kind == SkinKind.CjPack
        ? L.T($"Pacote do CJ · {Skin.Files.Count} arquivos", $"CJ pack · {Skin.Files.Count} files")
        : $"{Skin.DffFile} + {Skin.TxdFile}";
}

public sealed record ModeOption(SwapMode Mode, string Label)
{
    // o ComboBox escuro mostra o ToString() do item selecionado
    public override string ToString() => Label;

    public static List<ModeOption> All() => new()
    {
        new(SwapMode.Auto, L.T("Automático (Mod Loader se existir)", "Automatic (Mod Loader if installed)")),
        new(SwapMode.Modloader, L.T("Mod Loader (não mexe nos .img)", "Mod Loader (leaves .img files untouched)")),
        new(SwapMode.Img, L.T("Editar .img direto (com backup)", "Edit .img directly (with backup)")),
    };
}

public sealed record SortOption(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Categoria do filtro. Key vazia = todas.</summary>
public sealed record CategoryOption(string Key, string Label)
{
    public override string ToString() => Label;
}

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
        SkinsView = CollectionViewSource.GetDefaultView(Skins);
        SkinsView.Filter = FilterSkin;
        _selectedSkinSort = SkinSorts.FirstOrDefault(o => o.Key == App.Settings.SkinSort) ?? SkinSorts[0];
        ApplySkinSort();
        Categories.Add(AllCategories);
        _selectedCategory = AllCategories;
        _selectedMode = Modes.FirstOrDefault(m => m.Mode == App.Settings.PreferredMode) ?? Modes[0];
        _status = L.T("Pronto.", "Ready.");
        ReloadSkins();
        RefreshSwaps();
    }

    private readonly CategoryOption AllCategories = new("", L.T("Todas as categorias", "All categories"));

    public ToastHost Toasts { get; } = new();

    public string GamePath => _game.Root;
    public string ModeBadge => _game.HasModloader
        ? L.T("Mod Loader detectado", "Mod Loader detected")
        : L.T("Sem Mod Loader · modo IMG", "No Mod Loader · IMG mode");

    public ObservableCollection<PedItem> Peds { get; } = new();
    public ICollectionView PedsView { get; }
    public ObservableCollection<CategoryOption> Categories { get; } = new();
    public ObservableCollection<SkinItem> Skins { get; } = new();
    public ICollectionView SkinsView { get; }

    // ---- biblioteca: busca e ordem ----
    [ObservableProperty] private string _skinSearch = "";
    partial void OnSkinSearchChanged(string value) => SkinsView.Refresh();

    public List<SortOption> SkinSorts { get; } = new()
    {
        new("recent", L.T("Mais recentes", "Newest")),
        new("name", L.T("Nome (A–Z)", "Name (A–Z)")),
        new("usage", L.T("Mais usadas", "Most used")),
    };

    [ObservableProperty] private SortOption _selectedSkinSort;
    partial void OnSelectedSkinSortChanged(SortOption value)
    {
        App.Settings.SkinSort = value.Key;
        App.Settings.Save(App.Paths);
        ApplySkinSort();
    }

    private void ApplySkinSort()
    {
        using (SkinsView.DeferRefresh())
        {
            SkinsView.SortDescriptions.Clear();
            switch (SelectedSkinSort.Key)
            {
                case "name":
                    SkinsView.SortDescriptions.Add(new SortDescription(nameof(SkinItem.Title), ListSortDirection.Ascending));
                    break;
                case "usage":
                    SkinsView.SortDescriptions.Add(new SortDescription(nameof(SkinItem.UsageCount), ListSortDirection.Descending));
                    SkinsView.SortDescriptions.Add(new SortDescription(nameof(SkinItem.Title), ListSortDirection.Ascending));
                    break;
                // "recent": ordem da biblioteca (mais nova primeiro)
            }
        }
    }

    private bool FilterSkin(object o)
    {
        if (string.IsNullOrWhiteSpace(SkinSearch)) return true;
        var s = (SkinItem)o;
        var q = SkinSearch.Trim();
        return s.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || s.Skin.Files.Any(f => f.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    // ---- personagens: filtros rápidos e favoritos ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterIsAll), nameof(FilterIsSwapped), nameof(FilterIsNotSwapped), nameof(FilterIsFavorites))]
    private PedFilter _filter = PedFilter.All;
    partial void OnFilterChanged(PedFilter value) => RefreshPedsView();

    public bool FilterIsAll => Filter == PedFilter.All;
    public bool FilterIsSwapped => Filter == PedFilter.Swapped;
    public bool FilterIsNotSwapped => Filter == PedFilter.NotSwapped;
    public bool FilterIsFavorites => Filter == PedFilter.Favorites;

    [RelayCommand] private void SetFilter(string name) => Filter = Enum.Parse<PedFilter>(name);

    /// <summary>Quantos personagens aparecem com os filtros atuais.</summary>
    [ObservableProperty] private int _visibleCount;

    private void RefreshPedsView()
    {
        PedsView.Refresh();
        var visible = PedsView.Cast<PedItem>().ToList();
        VisibleCount = visible.Count;
        // o selecionado sumiu com o filtro: seleciona o primeiro que aparece
        if (SelectedPed is null || !visible.Contains(SelectedPed)) SelectedPed = visible.FirstOrDefault();
    }

    [RelayCommand]
    private void ToggleFavorite(PedItem? ped)
    {
        ped ??= SelectedPed;
        if (ped is null) return;
        ped.IsFavorite = !ped.IsFavorite;
        var favs = App.Settings.Favorites;
        favs.RemoveAll(k => k == ped.Model.Key);
        if (ped.IsFavorite) favs.Add(ped.Model.Key);
        App.Settings.Save(App.Paths);
        if (Filter == PedFilter.Favorites) RefreshPedsView();
    }

    // ---- seleção de vários personagens (Ctrl/Shift) ----
    private List<PedItem> _selectedPeds = new();

    public int MultiCount => _selectedPeds.Count;
    public bool IsMulti => _selectedPeds.Count > 1;
    public string MultiText => L.T(
        $"{_selectedPeds.Count} personagens selecionados: a troca vale para todos (com as cutscenes de cada um).",
        $"{_selectedPeds.Count} characters selected: the swap applies to all of them (with each one's cutscenes).");

    /// <summary>Chamado pela janela quando a seleção da lista muda.</summary>
    public void SetSelection(IEnumerable<PedItem> peds)
    {
        _selectedPeds = peds.ToList();
        OnPropertyChanged(nameof(MultiCount));
        OnPropertyChanged(nameof(IsMulti));
        OnPropertyChanged(nameof(MultiText));
    }

    // ---- desfazer a última troca (Ctrl+Z) ----
    private readonly Stack<List<(GameModel Target, SwapRecord Record)>> _undo = new();
    public ObservableCollection<SwapGroup> SwapGroups { get; } = new();
    private readonly HashSet<string> _expandedGroups = new();
    public ObservableCollection<LinkedItem> Linked { get; } = new();
    public bool HasLinked => Linked.Count > 0;

    partial void OnSelectedPedChanged(PedItem? value)
    {
        Linked.Clear();
        if (value is not null)
        {
            var byKey = Peds.ToDictionary(p => p.Model.Key);
            foreach (var m in PedCatalog.LinkedModels(value.Model, Peds.Select(p => p.Model)))
                if (byKey.TryGetValue(m.Key, out var p)) Linked.Add(new LinkedItem(p));
        }
        OnPropertyChanged(nameof(HasLinked));
        LoadOriginalPreview();
    }
    public List<ModeOption> Modes { get; } = ModeOption.All();

    // ---- aparência (vem das configurações) ----
    public bool GridView
    {
        get => App.Settings.GridView;
        set
        {
            if (App.Settings.GridView == value) return;
            App.Settings.GridView = value;
            App.Settings.Save(App.Paths);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ListView));
        }
    }

    public bool ListView => !GridView;
    public double ThumbSize => Math.Clamp(App.Settings.ThumbSize, 72, 200);
    public double TileWidth => ThumbSize + 16;

    /// <summary>Chamado depois da janela de configurações.</summary>
    public void SettingsChanged()
    {
        SelectedMode = Modes.FirstOrDefault(m => m.Mode == App.Settings.PreferredMode) ?? Modes[0];
        OnPropertyChanged(nameof(GridView));
        OnPropertyChanged(nameof(ListView));
        OnPropertyChanged(nameof(ThumbSize));
        OnPropertyChanged(nameof(TileWidth));
        PreviewSettingsChanged();
        RefreshPedsView();
    }

    [RelayCommand] private void ShowGrid() => GridView = true;
    [RelayCommand] private void ShowList() => GridView = false;

    [ObservableProperty] private ModeOption _selectedMode;
    partial void OnSelectedModeChanged(ModeOption value)
    {
        App.Settings.PreferredMode = value.Mode;
        App.Settings.Save(App.Paths);
    }

    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value) => RefreshPedsView();

    [ObservableProperty] private CategoryOption _selectedCategory;
    partial void OnSelectedCategoryChanged(CategoryOption value) => RefreshPedsView();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RevertSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedSwap), nameof(SelectedSwapText), nameof(HasSelection))]
    private PedItem? _selectedPed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(RemoveSkinCommand), nameof(BeginRenameCommand))]
    private SkinItem? _selectedSkin;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _busyText = "";
    [ObservableProperty] private string _status;
    [ObservableProperty] private int _pedCount;

    public bool HasSelection => SelectedPed is not null;
    public SwapRecord? SelectedSwap => SelectedPed is null ? null : _swaps.FindFor(SelectedPed.Model);
    public string? SelectedSwapText => SelectedSwap is { } r ? L.T("Troca ativa: ", "Active swap: ") + SkinNameOf(r) : null;

    private bool FilterPed(object o)
    {
        var p = (PedItem)o;
        if (p.IsAdult && !App.Settings.AdultMode) return false;
        if (SelectedCategory.Key.Length > 0 && p.Model.Category != SelectedCategory.Key) return false;
        // as versões de cutscene já são trocadas junto: ficam escondidas, a não ser que a categoria seja Cutscenes
        if (p.IsCutscene && !App.Settings.ShowCutscenes && SelectedCategory.Key != PedCatalog.CutsceneCategory) return false;
        switch (Filter)
        {
            case PedFilter.Swapped when !p.IsSwapped: return false;
            case PedFilter.NotSwapped when p.IsSwapped: return false;
            case PedFilter.Favorites when !p.IsFavorite: return false;
        }
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return p.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Model.ModelName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || p.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
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
        BusyText = L.T("Carregando todos os personagens do jogo…", "Loading every character in the game…");
        Progress = 0;
        try
        {
            var prog = new Progress<ScanProgress>(p =>
            {
                Progress = p.Total == 0 ? 0 : (double)p.Done / p.Total;
                BusyText = p.Current.Length > 0 ? $"{p.Current} ({p.Done}/{p.Total})" : L.T("Concluído", "Done");
            });
            _scanner.ClearCache();
            var models = await Task.Run(() => _scanner.Scan((t, path) => Thumbs.Save(t, path), prog));
            Fill(models);
            Status = L.T($"{models.Count} personagens carregados.", $"{models.Count} characters loaded.");
        }
        catch (Exception e)
        {
            Status = L.T("Erro ao escanear: ", "Scan error: ") + e.Message;
            Toasts.Show(Status, ToastKind.Error);
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
        var favs = new HashSet<string>(App.Settings.Favorites);
        foreach (var m in models)
            Peds.Add(new PedItem(m, null) { IsFavorite = favs.Contains(m.Key) });
        PedCount = models.Count;
        ApplyPedThumbs();

        var cats = models.Select(m => m.Category).Distinct()
            .Select(c => new CategoryOption(c, PedCatalog.CategoryLabel(c)))
            .OrderBy(c => c.Key == PedCatalog.CjCategory ? "" : c.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var c in cats) Categories.Add(c);
        SelectedCategory = AllCategories;

        RefreshSwaps();
        RefreshPedsView();
        SelectedPed = Peds.FirstOrDefault(p => p.Model.Key == selectedKey) ?? Peds.FirstOrDefault();
    }

    private void ReloadSkins()
    {
        var selected = SelectedSkin?.Skin.Id;
        Skins.Clear();
        foreach (var s in _library.Skins)
        {
            try { Thumbs.EnsureSkinThumb(_library, s); } catch (Exception) { }
            var item = new SkinItem(s, null);
            item.Thumb = SkinThumb(s, img => item.Thumb = img).Value;
            Skins.Add(item);
        }
        SelectedSkin = Skins.FirstOrDefault(s => s.Skin.Id == selected) ?? SelectedSkin;
        if (Peds.Count > 0 || _swaps.Manifest.Records.Count > 0) RefreshSwaps();
    }

    private void RefreshSwaps()
    {
        BuildSwapGroups();
        foreach (var p in Peds)
        {
            var r = _swaps.FindFor(p.Model);
            p.SwapText = r is null ? null : $"→ {SkinNameOf(r)}";
        }
        // uso de cada skin = quantos personagens (grupos) estão com ela
        var usage = _swaps.Manifest.Records.GroupBy(r => r.SkinId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.GroupKey ?? r.TargetKey).Distinct().Count());
        foreach (var s in Skins) s.UsageCount = usage.GetValueOrDefault(s.Skin.Id);
        if (SelectedSkinSort.Key == "usage") SkinsView.Refresh();
        if (Filter is PedFilter.Swapped or PedFilter.NotSwapped) RefreshPedsView();

        OnPropertyChanged(nameof(SelectedSwap));
        OnPropertyChanged(nameof(SelectedSwapText));
        UndoLastCommand.NotifyCanExecuteChanged();
        RevertSelectedCommand.NotifyCanExecuteChanged();
        RevertAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Agrupa as trocas ativas pelo personagem principal (o personagem + cutscenes/variações).</summary>
    private void BuildSwapGroups()
    {
        foreach (var g in SwapGroups)
        {
            if (g.IsExpanded) _expandedGroups.Add(g.Key);
            else _expandedGroups.Remove(g.Key);
        }

        var records = _swaps.Manifest.Records.ToList();
        var byKey = Peds.GroupBy(p => p.Model.Key).ToDictionary(g => g.Key, g => g.First());

        // trocas antigas (sem GroupKey): liga à troca do personagem principal com a mesma skin, se houver
        string GroupOf(SwapRecord r)
        {
            if (r.GroupKey is not null) return r.GroupKey;
            if (!byKey.TryGetValue(r.TargetKey, out var me)) return r.TargetKey;
            foreach (var other in records)
            {
                if (other == r || other.SkinId != r.SkinId || !byKey.TryGetValue(other.TargetKey, out var main)) continue;
                if (main.Model.Category == PedCatalog.CutsceneCategory) continue;
                if (PedCatalog.LinkedModels(main.Model, new[] { me.Model }).Count > 0) return other.TargetKey;
            }
            return r.TargetKey;
        }

        string TitleOf(string key, string fallback) => byKey.TryGetValue(key, out var p) ? p.Title : fallback;

        SwapGroups.Clear();
        foreach (var g in records.GroupBy(GroupOf).OrderByDescending(g => g.Max(r => r.AppliedAt)))
        {
            var list = g.OrderBy(r => r.TargetKey == g.Key ? 0 : 1).ThenBy(r => r.TargetModel).ToList();
            var main = list[0];
            var skins = list.Select(SkinNameOf).Distinct().ToList();
            var modes = list.Select(r => r.Mode).Distinct().ToList();
            SwapGroups.Add(new SwapGroup
            {
                Key = g.Key,
                Title = TitleOf(g.Key, main.GroupName ?? main.TargetName),
                SkinName = skins.Count == 1 ? skins[0] : L.T("várias skins", "several skins"),
                ModeText = modes.Count == 1 ? ModeName(modes[0]) : L.T("misto", "mixed"),
                Entries = list.Select(r => new SwapEntry { Record = r, Title = TitleOf(r.TargetKey, r.TargetName) }).ToList(),
                UndoCommand = new RelayCommand(() => RevertMany(list)),
                IsExpanded = _expandedGroups.Contains(g.Key),
            });
        }
    }

    private void RevertMany(List<SwapRecord> list)
    {
        Run(() =>
        {
            foreach (var r in list) _swaps.Revert(r);
            Status = L.T($"{list.Count} modelo(s) voltaram ao original.", $"{list.Count} model(s) restored to the original.");
            Toasts.Show(Status);
        });
    }

    private bool CanApply() => SelectedPed is not null && SelectedSkin is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (IsMulti)
        {
            ApplyMany();
            return;
        }
        var target = SelectedPed!.Model;
        var title = SelectedPed.Title;
        var skin = SelectedSkin!.Skin;
        if (target.Kind == ModelKind.Player && skin.Kind != SkinKind.CjPack)
        {
            MessageBox.Show(L.T(
                "O CJ não é um personagem comum: o corpo dele é montado por peças de roupa do player.img.\n\n" +
                "• Se o mod que você baixou é um \"CJ replacement\" com várias peças (torso, legs, feet, head…), importe com \"Pacote do CJ\".\n" +
                "• Para jogar com um modelo de NPC comum no lugar do CJ, use um mod como o Skin Selector (CLEO).",
                "CJ is not a regular character: his body is built from player.img clothing parts.\n\n" +
                "• If the mod you downloaded is a \"CJ replacement\" with several parts (torso, legs, feet, head…), import it with \"CJ pack\".\n" +
                "• To play as a regular NPC model instead of CJ, use a mod such as Skin Selector (CLEO)."),
                L.T("Trocar o CJ", "Replace CJ"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // o personagem + as versões de cutscene/variações marcadas
        var targets = new List<GameModel> { target };
        targets.AddRange(Linked.Where(l => l.Include).Select(l => l.Ped.Model));
        var done = new List<(GameModel Target, SwapRecord Record)>();
        Run(() =>
        {
            foreach (var t in targets)
                done.Add((t, _swaps.Apply(t, skin, SelectedMode.Mode, t == target ? null : target)));
        });
        if (done.Count == 0) return;
        _undo.Push(done);
        UndoLastCommand.NotifyCanExecuteChanged();

        var mode = ModeName(done[0].Record.Mode);
        var extra = done.Count > 1 ? L.T($" (+{done.Count - 1} cutscene/variação)", $" (+{done.Count - 1} cutscene/variant)") : "";
        Status = L.T($"{title} agora usa \"{skin.Name}\" ({mode}){extra}.", $"{title} now uses \"{skin.Name}\" ({mode}){extra}.");
        Toasts.Show(L.T($"{title} trocado por \"{skin.Name}\"{extra}", $"{title} replaced with \"{skin.Name}\"{extra}"),
            ToastKind.Success, L.T("Desfazer", "Undo"), () => RevertBatch(done));
    }

    /// <summary>Mesma skin em vários personagens; cada um leva junto as suas cutscenes/variações.</summary>
    private void ApplyMany()
    {
        var skin = SelectedSkin!.Skin;
        var peds = _selectedPeds.Where(p => !p.IsCj).ToList();
        bool skippedCj = peds.Count < _selectedPeds.Count;
        var all = Peds.Select(p => p.Model).ToList();
        var excluded = new HashSet<string>(Linked.Where(l => !l.Include).Select(l => l.Ped.Model.Key));
        var targets = new List<(GameModel Target, GameModel? Group)>();
        var seen = new HashSet<string>();
        foreach (var p in peds)
        {
            if (seen.Add(p.Model.Key)) targets.Add((p.Model, null));
            foreach (var m in PedCatalog.LinkedModels(p.Model, all))
            {
                // o que o usuário desmarcou em "Trocar também" (do personagem principal) fica de fora
                if (p == SelectedPed && excluded.Contains(m.Key)) continue;
                if (seen.Add(m.Key)) targets.Add((m, p.Model));
            }
        }
        var done = new List<(GameModel Target, SwapRecord Record)>();
        Run(() =>
        {
            foreach (var (t, g) in targets) done.Add((t, _swaps.Apply(t, skin, SelectedMode.Mode, g)));
        });
        if (done.Count == 0) return;
        _undo.Push(done);
        UndoLastCommand.NotifyCanExecuteChanged();

        Status = L.T($"{peds.Count} personagens agora usam \"{skin.Name}\" ({done.Count} modelos).",
                     $"{peds.Count} characters now use \"{skin.Name}\" ({done.Count} models).");
        Toasts.Show(L.T($"{peds.Count} personagens trocados por \"{skin.Name}\"", $"{peds.Count} characters replaced with \"{skin.Name}\""),
            ToastKind.Success, L.T("Desfazer", "Undo"), () => RevertBatch(done));
        if (skippedCj)
            Toasts.Show(L.T("O CJ ficou de fora: ele precisa de um \"Pacote do CJ\".", "CJ was skipped: he needs a \"CJ pack\"."), ToastKind.Info);
    }

    /// <summary>Desfaz um lote de trocas, só o que ainda for daquela troca (o usuário pode ter trocado de novo depois).</summary>
    private int RevertBatch(List<(GameModel Target, SwapRecord Record)> batch)
    {
        var still = batch
            .Select(b => _swaps.FindFor(b.Target) is { } cur && cur.AppliedAt == b.Record.AppliedAt ? cur : null)
            .OfType<SwapRecord>().ToList();
        if (still.Count == 0) return 0;
        RevertMany(still);
        return still.Count;
    }

    private bool CanUndoLast() => _undo.Count > 0;

    /// <summary>Ctrl+Z: desfaz a última troca feita nesta sessão (e a anterior, se apertar de novo).</summary>
    [RelayCommand(CanExecute = nameof(CanUndoLast))]
    private void UndoLast()
    {
        while (_undo.Count > 0)
            if (RevertBatch(_undo.Pop()) > 0) break;
        UndoLastCommand.NotifyCanExecuteChanged();
    }

    private bool CanRevertSelected() => SelectedSwap is not null;

    [RelayCommand(CanExecute = nameof(CanRevertSelected))]
    private void RevertSelected()
    {
        if (SelectedSwap is not { } r) return;
        // desfaz junto as cutscenes/variações que estão com a mesma skin
        foreach (var l in Linked)
            if (_swaps.FindFor(l.Ped.Model) is { } lr && lr.SkinId == r.SkinId) Revert(lr);
        Revert(r);
    }

    [RelayCommand]
    private void Revert(SwapRecord? r)
    {
        if (r is null) return;
        Run(() =>
        {
            _swaps.Revert(r);
            Status = L.T($"{r.TargetName} voltou ao original.", $"{r.TargetName} is back to the original.");
            Toasts.Show(Status);
        });
    }

    private bool CanRevertAll() => _swaps.Manifest.Records.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRevertAll))]
    private void RevertAll()
    {
        int n = _swaps.Manifest.Records.Count;
        if (MessageBox.Show(
                L.T($"Desfazer todas as {n} trocas e voltar os personagens ao original?",
                    $"Undo all {n} swaps and restore the original characters?"),
                L.T("Restaurar tudo", "Restore all"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Run(() =>
        {
            _swaps.RevertAll();
            Status = L.T("Todos os personagens voltaram ao original.", "All characters are back to the original.");
            Toasts.Show(Status);
        });
    }

    private void Run(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is SwapException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidDataException or NotSupportedException)
        {
            var msg = e is UnauthorizedAccessException
                ? L.T("Sem permissão para escrever na pasta do jogo. Rode o programa como administrador ou mova o jogo para fora de \"Arquivos de Programas\".",
                      "No permission to write to the game folder. Run the program as administrator or move the game out of \"Program Files\".")
                  + "\n" + e.Message
                : e.Message;
            Status = L.T("Erro: ", "Error: ") + msg;
            Toasts.Show(msg, ToastKind.Error);
        }
        RefreshSwaps();
    }

    private static string ModeName(SwapMode m) => m == SwapMode.Modloader ? "Mod Loader" : "IMG";

    [RelayCommand]
    private void ImportFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = L.T("Escolha os arquivos da skin (.dff + .txd) ou um .zip", "Choose the skin files (.dff + .txd) or a .zip"),
            Filter = L.T("Skins do GTA SA (*.dff;*.txd;*.zip)|*.dff;*.txd;*.zip|Todos os arquivos|*.*",
                         "GTA SA skins (*.dff;*.txd;*.zip)|*.dff;*.txd;*.zip|All files|*.*"),
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true) ImportPaths(dlg.FileNames, false);
    }

    [RelayCommand]
    private void ImportFolder()
    {
        var dlg = new OpenFolderDialog { Title = L.T("Pasta com os arquivos da skin", "Folder with the skin files") };
        if (dlg.ShowDialog() == true) ImportPaths(new[] { dlg.FolderName }, false);
    }

    [RelayCommand]
    private void ImportCjPack()
    {
        var dlg = new OpenFolderDialog { Title = L.T("Pasta do pacote do CJ (peças do player.img)", "CJ pack folder (player.img parts)") };
        if (dlg.ShowDialog() == true) ImportPaths(new[] { dlg.FolderName }, true);
    }

    [RelayCommand]
    private void ExportLibrary()
    {
        if (_library.Skins.Count == 0)
        {
            Toasts.Show(L.T("A biblioteca está vazia.", "The library is empty."), ToastKind.Info);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = L.T("Salvar backup da biblioteca", "Save library backup"),
            Filter = L.T("Backup da biblioteca (*.zip)|*.zip", "Library backup (*.zip)|*.zip"),
            FileName = $"SaSkinManager-biblioteca-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _library.ExportBackup(dlg.FileName);
            Toasts.Show(L.T($"Backup salvo: {_library.Skins.Count} skins.", $"Backup saved: {_library.Skins.Count} skins."),
                ToastKind.Success, L.T("Abrir pasta", "Open folder"),
                () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dlg.FileName}\"") { UseShellExecute = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toasts.Show(L.T("Erro ao salvar o backup: ", "Could not save the backup: ") + e.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private void RestoreLibrary()
    {
        var dlg = new OpenFileDialog
        {
            Title = L.T("Restaurar backup da biblioteca", "Restore library backup"),
            Filter = L.T("Backup da biblioteca (*.zip)|*.zip", "Library backup (*.zip)|*.zip"),
        };
        if (dlg.ShowDialog() == true) ImportPaths(new[] { dlg.FileName }, false);
    }

    private void RestoreBackup(string zip)
    {
        var (added, skipped) = _library.ImportBackup(zip);
        ReloadSkins();
        var msg = L.T($"Backup restaurado: {added} skin(s) adicionada(s)", $"Backup restored: {added} skin(s) added");
        if (skipped > 0) msg += L.T($", {skipped} já estava(m) na biblioteca", $", {skipped} already in the library");
        Toasts.Show(msg + ".", added > 0 ? ToastKind.Success : ToastKind.Info);
    }

    public void ImportPaths(IEnumerable<string> paths, bool asCjPack)
    {
        try
        {
            var list = paths.ToList();
            // um .zip de backup da biblioteca (arrastado ou escolhido) é restaurado em vez de importado
            if (!asCjPack && list.Count == 1 && list[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && SkinLibrary.IsBackup(list[0]))
            {
                RestoreBackup(list[0]);
                return;
            }
            var r = _library.Import(list, asCjPack);
            ReloadSkins();
            if (r.Imported.Count > 0)
            {
                SelectedSkin = Skins.FirstOrDefault(s => s.Skin.Id == r.Imported[0].Id);
                Status = r.Imported.Count == 1
                    ? L.T($"Skin \"{r.Imported[0].Name}\" adicionada.", $"Skin \"{r.Imported[0].Name}\" added.")
                    : L.T($"{r.Imported.Count} skins adicionadas.", $"{r.Imported.Count} skins added.");
                Toasts.Show(Status);
            }
            if (r.Warnings.Count > 0)
                Toasts.Show(string.Join("\n", r.Warnings), r.Imported.Count == 0 ? ToastKind.Error : ToastKind.Info);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Toasts.Show(L.T("Erro ao importar: ", "Import error: ") + e.Message, ToastKind.Error);
        }
    }

    private bool HasSkin() => SelectedSkin is not null;

    /// <summary>Nome atual da skin (pode ter sido renomeada depois da troca).</summary>
    private string SkinNameOf(SwapRecord r) =>
        _library.Skins.FirstOrDefault(s => s.Id == r.SkinId)?.Name ?? r.SkinName;

    [RelayCommand(CanExecute = nameof(HasSkin))]
    private void BeginRename()
    {
        foreach (var s in Skins) s.IsEditing = false;
        var item = SelectedSkin!;
        item.EditName = item.Title;
        item.IsEditing = true;
    }

    public void CommitRename(SkinItem item)
    {
        if (!item.IsEditing) return;
        item.IsEditing = false;
        var name = item.EditName.Trim();
        if (name.Length == 0 || name == item.Title) return;
        var old = item.Title;
        try
        {
            _library.Rename(item.Skin, name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Toasts.Show(L.T("Não foi possível renomear: ", "Could not rename: ") + e.Message, ToastKind.Error);
            return;
        }
        item.NameChanged();
        RefreshSwaps();
        Toasts.Show(L.T($"\"{old}\" agora se chama \"{name}\"", $"\"{old}\" is now called \"{name}\""));
    }

    public void CancelRename(SkinItem item) => item.IsEditing = false;

    [RelayCommand(CanExecute = nameof(HasSkin))]
    private void RemoveSkin()
    {
        var s = SelectedSkin!.Skin;
        if (_swaps.Manifest.Records.Any(r => r.SkinId == s.Id))
        {
            Toasts.Show(L.T("Esta skin está em uso. Desfaça as trocas que a usam antes de removê-la.",
                            "This skin is in use. Undo the swaps that use it before removing it."), ToastKind.Error);
            return;
        }
        if (MessageBox.Show(L.T($"Remover \"{s.Name}\" da biblioteca?", $"Remove \"{s.Name}\" from the library?"),
                L.T("Remover skin", "Remove skin"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _library.Remove(s);
        SelectedSkin = null;
        ReloadSkins();
        Toasts.Show(L.T($"\"{s.Name}\" removida.", $"\"{s.Name}\" removed."));
    }

    [RelayCommand]
    private void Play()
    {
        try
        {
            _game.Launch();
            Status = L.T("Iniciando o GTA San Andreas…", "Starting GTA San Andreas…");
            if (App.Settings.CloseOnPlay) Application.Current.Shutdown();
        }
        catch (Exception e) { Toasts.Show(e.Message, ToastKind.Error); }
    }

    [RelayCommand]
    private void OpenGameFolder() =>
        Process.Start(new ProcessStartInfo(_game.Root) { UseShellExecute = true });
}
