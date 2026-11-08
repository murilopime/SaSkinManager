using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;

namespace SaSkinManager.App.ViewModels;

public enum ToastKind { Success, Info, Error }

/// <summary>Aviso discreto no canto da janela, que some sozinho. Pode ter um botão (ex.: "Desfazer").</summary>
public sealed class ToastItem
{
    public required string Text { get; init; }
    public ToastKind Kind { get; init; }
    public string? ActionLabel { get; init; }
    public ICommand? ActionCommand { get; init; }
    public ICommand? CloseCommand { get; set; }

    public string Icon => Kind switch { ToastKind.Success => "✔", ToastKind.Error => "✖", _ => "ℹ" };
    public bool IsError => Kind == ToastKind.Error;
    public bool HasAction => ActionCommand is not null;
}

public sealed class ToastHost
{
    private const int MaxVisible = 4;

    public ObservableCollection<ToastItem> Items { get; } = new();

    public void Show(string text, ToastKind kind = ToastKind.Success, string? actionLabel = null, Action? action = null)
    {
        ToastItem? toast = null;
        toast = new ToastItem
        {
            Text = text,
            Kind = kind,
            ActionLabel = actionLabel,
            ActionCommand = action is null ? null : new RelayCommand(() =>
            {
                Items.Remove(toast!);
                action();
            }),
        };
        toast.CloseCommand = new RelayCommand(() => Items.Remove(toast));
        Items.Add(toast);
        while (Items.Count > MaxVisible) Items.RemoveAt(0);

        // erros e avisos com botão ficam mais tempo na tela
        var seconds = kind == ToastKind.Error ? 9 : action is not null ? 7 : 4;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Items.Remove(toast);
        };
        timer.Start();
    }
}
