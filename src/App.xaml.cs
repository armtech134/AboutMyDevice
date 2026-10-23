using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace AboutMyDevice;

public partial class App : Application
{
  protected override void OnStartup(StartupEventArgs e)
  {
    if (!IsAdministrator())
    {
      try
      {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executable))
        {
          Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
          Shutdown();
          return;
        }
      }
      catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
      {
        // UAC was canceled; continue in the current, non-elevated instance.
      }
      catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
      {
        MessageBox.Show($"Could not request administrator rights. The app will continue without elevation.\n\n{ex.Message}",
          "Elevation unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
      }
    }

    base.OnStartup(e);
  }

  private static bool IsAdministrator()
  {
    using var identity = WindowsIdentity.GetCurrent();
    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
  }
}
