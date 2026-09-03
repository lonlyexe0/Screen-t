using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace IkinciEkran.Services;

public static class SystemIntegration
{
    private const string AppName = "Ikinci Ekran";
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string GetCurrentExecutablePath()
    {
        return Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
    }

    public static bool CreateShortcut(string targetLnkPath, string description = "İkinci Monitör ve Sanal Ekran Yayın Sunucusu")
    {
        try
        {
            string exePath = GetCurrentExecutablePath();
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                return false;

            string workingDir = Path.GetDirectoryName(exePath) ?? "";
            string iconPath = Path.Combine(workingDir, "Assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(targetLnkPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = workingDir;
            shortcut.IconLocation = iconPath;
            shortcut.Description = description;
            shortcut.Save();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RegisterStartMenuShortcut()
    {
        try
        {
            string startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(startMenuPath, $"{AppName}.lnk");
            return CreateShortcut(shortcutPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool RegisterDesktopShortcut()
    {
        try
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktopPath, $"{AppName}.lnk");
            return CreateShortcut(shortcutPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRunOnStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetRunOnStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            if (enable)
            {
                string exePath = GetCurrentExecutablePath();
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\" --minimized");
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
            }
        }
        catch { }
    }
}
