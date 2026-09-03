using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks;

namespace IkinciEkran.Services;

public class VirtualDisplayController
{
    private const string DeviceInstanceId = "ROOT\\DISPLAY\\0000";

    public event Action<bool>? StatusChanged;
    private bool _isEnabled = false;

    public bool IsEnabled => _isEnabled;

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void RunPowerShellCommand(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -Command \"{script}\""
        };

        if (IsAdministrator())
        {
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
        }
        else
        {
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            psi.WindowStyle = ProcessWindowStyle.Hidden;
        }

        using var proc = Process.Start(psi);
        proc?.WaitForExit(8000);
    }

    public async Task<bool> CheckStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"(Get-PnpDevice -InstanceId '{DeviceInstanceId}' -ErrorAction SilentlyContinue).Status\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(3000);
                    _isEnabled = output.Equals("OK", StringComparison.OrdinalIgnoreCase);
                    StatusChanged?.Invoke(_isEnabled);
                    return _isEnabled;
                }
            }
            catch { }
            return false;
        });
    }

    public async Task<bool> EnableVirtualDisplayAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                RunPowerShellCommand($"Enable-PnpDevice -InstanceId '{DeviceInstanceId}' -Confirm:$false");
                _isEnabled = true;
                StatusChanged?.Invoke(_isEnabled);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    public async Task<bool> DisableVirtualDisplayAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                RunPowerShellCommand($"Disable-PnpDevice -InstanceId '{DeviceInstanceId}' -Confirm:$false");
                _isEnabled = false;
                StatusChanged?.Invoke(_isEnabled);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}
