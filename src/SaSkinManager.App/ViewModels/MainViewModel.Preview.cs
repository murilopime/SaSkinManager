using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using CommunityToolkit.Mvvm.ComponentModel;
using SaSkinManager.App.Services;
using SaSkinManager.Core;
using SaSkinManager.Core.Game;
using SaSkinManager.Core.Library;

namespace SaSkinManager.App.ViewModels;

/// <summary>Prévia 3D: modelo original × nova skin, miniaturas 3D e giro ao passar o mouse na grade.</summary>
public sealed partial class MainViewModel
{
    private const int ModelCacheSize = 120;
    private readonly Dictionary<string, Model3D?> _models = new();
    private readonly LinkedList<string> _modelOrder = new();
    private Thumb3DWorker? _thumbWorker;
    private int _origVersion, _newVersion, _thumbGeneration;
    private PedItem? _hovered;

    public bool Preview3D => App.Settings.Preview3D;
    public bool AutoRotate => App.Settings.AutoRotate;
    public bool SpinGrid => App.Settings.Preview3D && App.Settings.SpinGrid;

    private HashSet<PedItem> _live = new();
    private int _liveVersion;

    /// <summary>
    /// Blocos da grade que estão na tela agora (a janela avisa ao rolar ou redimensionar).
    /// Só esses ganham o modelo 3D girando, para não pesar com centenas de personagens.
    /// </summary>
    public async void SetVisibleTiles(IReadOnlyCollection<PedItem> visible)
    {
        int v = ++_liveVersion;
        var now = SpinGrid ? new HashSet<PedItem>(visible) : new HashSet<PedItem>();
        foreach (var p in _live)
            if (!now.Contains(p) && p != _hovered) p.LiveModel = null;
        _live = now;
        foreach (var p in visible)
        {
            if (!SpinGrid || v != _liveVersion) return;
            if (p.LiveModel is not null || !Has3D(p.Model)) continue;
            var m = await GetModelAsync(KeyOf(p.Model), () => OriginalFiles(p.Model));
            if (_live.Contains(p) && SpinGrid) p.LiveModel = m;
        }
    }

    [ObservableProperty] private Model3D? _originalModel;
    [ObservableProperty] private Model3D? _newModel;
    [ObservableProperty] private bool _loadingOriginal;
    [ObservableProperty] private bool _loadingNew;

    /// <summary>Progresso das miniaturas 3D na barra de status (vazio quando terminou).</summary>
    [ObservableProperty] private string _thumbProgress = "";

    private Thumb3DWorker Worker => _thumbWorker ??= new Thumb3DWorker();

    /// <summary>Chamado quando a janela já está na tela: começa a gerar as miniaturas 3D.</summary>
    public void StartBackgroundWork() => Worker.Start();

    private (byte[]? Dff, byte[]? Txd) OriginalFiles(GameModel m) =>
        (_swaps.ReadOriginal(m, m.ModelName + ".dff"), _swaps.ReadOriginal(m, m.TxdName + ".txd"));

    private (byte[]? Dff, byte[]? Txd) SkinFiles(LibrarySkin s)
    {
        byte[]? Read(string? f) => f is null || !File.Exists(_library.FilePath(s, f)) ? null : _library.ReadFile(s, f);
        return (Read(s.DffFile), Read(s.TxdFile));
    }

    private static bool Has3D(GameModel m) => m.Kind == ModelKind.Ped;
    private static bool Has3D(LibrarySkin s) => s.Kind != SkinKind.CjPack && s.DffFile is not null;

    private async Task<Model3D?> GetModelAsync(string key, Func<(byte[]? Dff, byte[]? Txd)> load)
    {
        if (_models.TryGetValue(key, out var cached))
        {
            _modelOrder.Remove(key);
            _modelOrder.AddFirst(key);
            return cached;
        }
        Model3D? model = await Task.Run(() =>
        {
            try
            {
                var (dff, txd) = load();
                return ModelScene.TryBuild(dff, txd);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                return null;
            }
        });
        _models[key] = model;
        _modelOrder.AddFirst(key);
        while (_modelOrder.Count > ModelCacheSize)
        {
            _models.Remove(_modelOrder.Last!.Value);
            _modelOrder.RemoveLast();
        }
        return model;
    }

    private static string KeyOf(GameModel m) => "game:" + m.Key;
    private static string KeyOf(LibrarySkin s) => "skin:" + s.Id;

    private async void LoadOriginalPreview()
    {
        int v = ++_origVersion;
        var ped = SelectedPed;
        if (!Preview3D || ped is null || !Has3D(ped.Model))
        {
            OriginalModel = null;
            LoadingOriginal = false;
            return;
        }
        LoadingOriginal = true;
        var m = await GetModelAsync(KeyOf(ped.Model), () => OriginalFiles(ped.Model));
        if (v != _origVersion) return;
        OriginalModel = m;
        LoadingOriginal = false;
    }

    partial void OnSelectedSkinChanged(SkinItem? value) => LoadNewPreview();

    private async void LoadNewPreview()
    {
        int v = ++_newVersion;
        var skin = SelectedSkin;
        if (!Preview3D || skin is null || !Has3D(skin.Skin))
        {
            NewModel = null;
            LoadingNew = false;
            return;
        }
        LoadingNew = true;
        var m = await GetModelAsync(KeyOf(skin.Skin), () => SkinFiles(skin.Skin));
        if (v != _newVersion) return;
        NewModel = m;
        LoadingNew = false;
    }

    /// <summary>Mouse entrou num bloco da grade: depois de um instante, mostra o modelo girando.</summary>
    public async void HoverEnter(PedItem item)
    {
        _hovered = item;
        if (!Preview3D || SpinGrid || !Has3D(item.Model)) return;
        await Task.Delay(180);
        if (_hovered != item) return;
        var m = await GetModelAsync(KeyOf(item.Model), () => OriginalFiles(item.Model));
        if (_hovered == item) item.LiveModel = m;
    }

    public void HoverLeave(PedItem item)
    {
        if (_hovered == item) _hovered = null;
        if (!_live.Contains(item)) item.LiveModel = null;
    }

    /// <summary>Usa a miniatura 3D quando já existe; as que faltam são geradas aos poucos, na ordem da lista.</summary>
    private void ApplyPedThumbs()
    {
        int gen = ++_thumbGeneration;
        var missing = new List<PedItem>();
        foreach (var p in Peds)
        {
            var path3d = _scanner.Thumb3DPath(p.Model);
            if (Preview3D && Has3D(p.Model) && File.Exists(path3d)) p.Thumb = Thumbs.Load(path3d) ?? Thumbs.Load(_scanner.ThumbPath(p.Model));
            else
            {
                p.Thumb = Thumbs.Load(_scanner.ThumbPath(p.Model));
                if (Preview3D && Has3D(p.Model) && (!p.IsAdult || App.Settings.AdultMode)) missing.Add(p);
            }
        }
        if (missing.Count == 0) { ThumbProgress = ""; return; }

        int done = 0, total = missing.Count;
        ThumbProgress = L.T($"Gerando miniaturas 3D… 0/{total}", $"Rendering 3D thumbnails… 0/{total}");
        var dispatcher = Application.Current.Dispatcher;
        foreach (var p in missing)
        {
            var item = p;
            var path3d = _scanner.Thumb3DPath(item.Model);
            Worker.Enqueue(new Thumb3DWorker.Job(path3d, () => gen == _thumbGeneration ? OriginalFiles(item.Model) : (null, null), ok =>
                dispatcher.BeginInvoke(() =>
                {
                    if (gen != _thumbGeneration) return;
                    if (ok && Thumbs.Load(path3d) is { } img) item.Thumb = img;
                    done++;
                    ThumbProgress = done >= total ? ""
                        : L.T($"Gerando miniaturas 3D… {done}/{total}", $"Rendering 3D thumbnails… {done}/{total}");
                })));
        }
    }

    private ImageSourceOrNull SkinThumb(LibrarySkin s, Action<System.Windows.Media.ImageSource> later)
    {
        var path3d = _library.Thumb3DPath(s);
        if (Preview3D && Has3D(s))
        {
            if (File.Exists(path3d)) return new(Thumbs.Load(path3d) ?? Thumbs.Load(_library.ThumbPath(s)));
            var dispatcher = Application.Current.Dispatcher;
            Worker.Enqueue(new Thumb3DWorker.Job(path3d, () => SkinFiles(s), ok =>
            {
                if (ok) dispatcher.BeginInvoke(() => { if (Thumbs.Load(path3d) is { } img) later(img); });
            }), priority: true);
        }
        return new(Thumbs.Load(_library.ThumbPath(s)));
    }

    private readonly record struct ImageSourceOrNull(System.Windows.Media.ImageSource? Value);

    /// <summary>A janela recalcula os blocos visíveis quando a opção muda.</summary>
    public event Action? SpinGridChanged;

    private void PreviewSettingsChanged()
    {
        OnPropertyChanged(nameof(Preview3D));
        OnPropertyChanged(nameof(AutoRotate));
        OnPropertyChanged(nameof(SpinGrid));
        foreach (var p in Peds) p.LiveModel = null;
        _live.Clear();
        SpinGridChanged?.Invoke();
        ApplyPedThumbs();
        foreach (var s in Skins) s.Thumb = SkinThumb(s.Skin, img => s.Thumb = img).Value;
        LoadOriginalPreview();
        LoadNewPreview();
    }
}
