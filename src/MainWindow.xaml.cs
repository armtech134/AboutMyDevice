using AboutMyDevice.Features.Dashboard.ViewModels;
using System.ComponentModel;
using System.Windows.Automation.Peers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AboutMyDevice;

public partial class MainWindow : Window
{
 public MainWindow()
 {
  InitializeComponent();
  var viewModel = new MainViewModel();
  viewModel.PropertyChanged += ViewModel_PropertyChanged;
  DataContext = viewModel;
 }

  private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName != nameof(MainViewModel.Status) ||
        sender is not MainViewModel viewModel ||
        !viewModel.Status.StartsWith("Refreshed '", StringComparison.Ordinal))
    {
      return;
    }

    Dispatcher.BeginInvoke(new Action(() =>
    {
      var peer = FrameworkElementAutomationPeer.FromElement(StatusAnnouncement)
        ?? UIElementAutomationPeer.CreatePeerForElement(StatusAnnouncement);
      peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }), DispatcherPriority.Background);
  }

  private void MainTabs_OnLoaded(object sender, RoutedEventArgs e)
  {
    MainTabs.SelectedIndex = 0;
    if (MainTabs.ItemContainerGenerator.ContainerFromIndex(0) is TabItem firstTab)
    {
      firstTab.Focus();
      Keyboard.Focus(firstTab);
    }
  }
}
