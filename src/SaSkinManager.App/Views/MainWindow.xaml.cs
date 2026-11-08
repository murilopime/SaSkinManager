using System.Windows;
using SaSkinManager.App.ViewModels;
using SaSkinManager.Core.Game;

namespace SaSkinManager.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(GameInstall game)
    {
        InitializeComponent();
        DataContext = _vm = new MainViewModel(game);
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e) => App.ChangeGameFolder(this);

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
