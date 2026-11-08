using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SaSkinManager.App.Services;

/// <summary>Barra de título escura no Windows 10 (1809+) e 11, combinando com o tema do programa.</summary>
public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeOld = 19; // Windows 10 antes do 20H1

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Chamar no construtor da janela (antes de ela aparecer).</summary>
    public static void Attach(Window w) => w.SourceInitialized += (_, _) => Apply(w);

    private static void Apply(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
