using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace RemapUSB.Ui;

/// <summary>
/// Helper for restarting the application
/// </summary>
public static class AppRestartHelper
{
    /// <summary>
    /// Restarts the current application
    /// </summary>
    public static void RestartApp()
    {
        var currentProcess = Process.GetCurrentProcess();
        var exePath = currentProcess.MainModule?.FileName;
        
        if (string.IsNullOrEmpty(exePath))
        {
            // Fallback for single-file publish
            exePath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath))
                exePath = AppContext.BaseDirectory;
        }

        if (File.Exists(exePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exePath) 
                { 
                    UseShellExecute = true 
                });
            }
            catch
            {
                // Fallback: try with --tray if that's how it was started
                Process.Start(new ProcessStartInfo(exePath, "--tray") 
                { 
                    UseShellExecute = true 
                });
            }
        }

        // Exit current instance
        Application.Current?.Shutdown();
    }

    /// <summary>
    /// Shows a styled dialog asking to restart the app, then restarts if confirmed
    /// </summary>
    public static async Task<bool> ShowRestartDialogAsync(Window owner, string message, string title = "Reiniciar necessário")
    {
        var result = DialogHelper.ShowYesNo(owner, title, message, DialogIcon.Information, "Reiniciar agora", "Depois");
        if (result == CustomDialogResult.Yes)
        {
            RestartApp();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Shows a styled dialog asking to restart the machine, then offers to restart if confirmed
    /// </summary>
    public static async Task<bool> ShowMachineRestartDialogAsync(Window owner, string message, string title = "Reiniciar computador necessário")
    {
        var result = DialogHelper.ShowYesNoCancel(owner, title, message, 
            DialogIcon.Warning, "Reiniciar agora", "Reiniciar depois", "Cancelar");
        
        if (result == CustomDialogResult.Yes)
        {
            // Restart machine
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") 
            { 
                Verb = "runas",
                UseShellExecute = true 
            });
            Application.Current?.Shutdown();
            return true;
        }
        else if (result == CustomDialogResult.No)
        {
            // User chose "later" - just close dialog
            return false;
        }
        return false;
    }
}