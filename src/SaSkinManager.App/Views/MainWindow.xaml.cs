using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaSkinManager.App.Services;
using SaSkinManager.App.ViewModels;
using SaSkinManager.Core.Game;

namespace SaSkinManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(GameInstall game)
    {
        InitializeComponent();
        DarkTitleBar.Attach(this);
        DataContext = _vm = new MainViewModel(game);
        Loaded += async (_, _) => await _vm.InitializeAsync();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>Ctrl+F: buscar · Enter (fora da busca): trocar · Ctrl+Z (fora de caixas de texto): desfazer a última troca.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && e.OriginalSource is not TextBox)
        {
            if (_vm.UndoLastCommand.CanExecute(null)) _vm.UndoLastCommand.Execute(null);
            else _vm.Toasts.Show(SaSkinManager.Core.L.T("Nada para desfazer.", "Nothing to undo."), ToastKind.Info);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None
                 && e.OriginalSource is not TextBox && e.OriginalSource is not Button
                 && _vm.ApplyCommand.CanExecute(null))
        {
            _vm.ApplyCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow(_vm.GamePath) { Owner = this };
        if (w.ShowDialog() != true) return;

        if (w.ChangeGameFolderRequested)
        {
            App.ChangeGameFolder(this);
            return;
        }
        if (w.LanguageChanged)
        {
            // os textos da janela são lidos ao criar: reabre a janela principal no novo idioma
            App.ReloadMain(this);
            return;
        }
        _vm.SettingsChanged();
        if (w.RescanRequested) _vm.RescanCommand.Execute(null);
    }

    /// <summary>Seleção com Ctrl/Shift: passa todos os personagens selecionados para o ViewModel.</summary>
    private void Peds_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb) _vm.SetSelection(lb.SelectedItems.OfType<PedItem>());
    }

    private void LibraryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } b)
        {
            menu.PlacementTarget = b;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    // ---- renomear skin na própria lista ----

    private void SkinList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(SkinList, d) is ListBoxItem
            && _vm.BeginRenameCommand.CanExecute(null))
            _vm.BeginRenameCommand.Execute(null);
    }

    private void SkinList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2 && _vm.BeginRenameCommand.CanExecute(null))
        {
            _vm.BeginRenameCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is TextBox box)
            Dispatcher.BeginInvoke(() =>
            {
                box.Focus();
                box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: SkinItem item }) return;
        if (e.Key == Key.Enter)
        {
            _vm.CommitRename(item);
            SkinList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CancelRename(item);
            SkinList.Focus();
            e.Handled = true;
        }
    }

    private void RenameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: SkinItem item }) _vm.CommitRename(item);
    }

    private void SkinList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void SkinList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            _vm.ImportPaths(files, asCjPack: false);
    }
}
