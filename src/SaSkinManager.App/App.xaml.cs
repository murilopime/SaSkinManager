using System.Windows;
using System.Windows.Threading;
using SaSkinManager.App.Views;
using SaSkinManager.Core;
using SaSkinManager.Core.Game;

namespace SaSkinManager.App;

public partial class App : Application
{
    /// <summary>SASKINMANAGER_DATA permite usar outra pasta de dados (testes).</summary>
    public static AppPaths Paths { get; } = new(Environment.GetEnvironmentVariable("SASKINMANAGER_DATA"));
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            Settings = AppSettings.Load(Paths);
            if (GameInstall.Validate(Settings.GamePath) is not null)
            {
                if (!RunSetup()) { Shutdown(); return; }
            }
            ShowMain();
        }
        catch (Exception ex)
        {
            LogError(ex);
            MessageBox.Show("Não foi possível abrir o programa:\n" + ex.Message + "\n\nDetalhes em: " + ErrorLogPath,
                "SA Skin Manager — erro", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    /// <summary>Tela de primeira execução: escolher a pasta e (opcionalmente) escanear todos os NPCs.</summary>
    public static bool RunSetup()
    {
        var w = new FirstRunWindow(Settings.GamePath);
        if (w.ShowDialog() != true) return false;
        Settings.GamePath = w.SelectedPath;
        Settings.Save(Paths);
        return true;
    }

    /// <summary>true enquanto a janela principal está sendo trocada (mudança de pasta do jogo).</summary>
    private static bool _switchingMain;

    public static void ShowMain()
    {
        var game = GameInstall.Open(Settings.GamePath!);
        var main = new MainWindow(game);
        Current.MainWindow = main;
        // Fechar a janela principal sempre encerra o programa (evita processo invisível preso em segundo plano).
        main.Closed += (_, _) =>
        {
            if (!_switchingMain) Current.Shutdown();
        };
        main.Show();
    }

    /// <summary>Trocar a pasta do jogo: volta para a tela de configuração e reabre a janela principal.</summary>
    public static void ChangeGameFolder(Window current)
    {
        current.Hide();
        bool ok;
        try { ok = RunSetup(); }
        catch { current.Show(); throw; }
        if (!ok)
        {
            current.Show();
            return;
        }
        _switchingMain = true;
        try
        {
            current.Close();
            ShowMain();
        }
        finally
        {
            _switchingMain = false;
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        e.Handled = true;
        MessageBox.Show(e.Exception.Message + "\n\nDetalhes em: " + ErrorLogPath, "SA Skin Manager — erro",
            MessageBoxButton.OK, MessageBoxImage.Error);
        // Se o erro deixou o programa sem nenhuma janela visível, encerra em vez de ficar preso em segundo plano.
        if (!Windows.OfType<Window>().Any(w => w.IsVisible)) Shutdown();
    }

    public static string ErrorLogPath => System.IO.Path.Combine(Paths.Root, "erro.log");

    public static void LogError(Exception ex)
    {
        try { System.IO.File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n"); }
        catch (System.IO.IOException) { }
    }
}
