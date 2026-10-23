using AboutMyDevice.Features.BasicInfo.Services;
using AboutMyDevice.Features.BatteryInfo.Services;
using AboutMyDevice.Features.Dashboard.Services;
using AboutMyDevice.Features.DiskInfo.Services;
using AboutMyDevice.Features.RamInfo.Services;
using AboutMyDevice.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;

namespace AboutMyDevice.Features.Dashboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
  private readonly BasicInfoService _basic = new();
  private readonly RamInfoService _ram = new();
  private readonly DiskInfoService _disk = new();
  private readonly BatteryInfoService _battery = new();
  private readonly ExportService _export = new();
  [ObservableProperty]
  private string activeTab = "Basic Info";

  [ObservableProperty]
  private string status = "Loading device information…";

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(RefreshCategoryCommand))]
  private bool isLoading = true;

  public ObservableCollection<InfoRow> BasicInfo { get; } = [];
  public ObservableCollection<InfoRow> RamInfo { get; } = [];
  public ObservableCollection<InfoRow> DiskInfo { get; } = [];
  public ObservableCollection<InfoRow> BatteryInfo { get; } = [];
  public MainViewModel()
  {
    _ = LoadAsync();
  }

  [RelayCommand]
  private Task ExportCsvAsync() => ExportAsync(".csv");

  [RelayCommand]
  private Task ExportJsonAsync() => ExportAsync(".json");

  [RelayCommand]
  private Task ExportHtmlAsync() => ExportAsync(".html");

  [RelayCommand]
  private void Copy() => CopyActive();

  [RelayCommand]
  private void About() => Process.Start(new ProcessStartInfo("https://armtech134.dev/about_device") { UseShellExecute = true });

  [RelayCommand]
  private void SelectTab(string? tab) => ActiveTab = tab ?? "Basic Info";

  [RelayCommand(CanExecute = nameof(CanRefreshCategory))]
  private async Task RefreshCategoryAsync()
  {
    var category = ActiveTab;
    Status = $"Refreshing {category}...";

    try
    {
      var rows = await Task.Run(() => category switch
      {
        "RAM Info" => _ram.GetInfo(),
        "Disk Info" => _disk.GetInfo(),
        "Battery Info" => _battery.GetInfo(),
        _ => _basic.GetInfo()
      });

      switch (category)
      {
        case "RAM Info": Replace(RamInfo, rows); break;
        case "Disk Info": Replace(DiskInfo, rows); break;
        case "Battery Info": Replace(BatteryInfo, rows); break;
        default: Replace(BasicInfo, rows); break;
      }

      Status = $"Refreshed '{category}'";
    }
    catch (Exception ex)
    {
      Status = $"Could not refresh {category}: {ex.Message}";
    }
  }

  private bool CanRefreshCategory() => !IsLoading;

  private async Task LoadAsync()
  {
    try
    {
      var results = await Task.Run(() => (_basic.GetInfo(), _ram.GetInfo(), _disk.GetInfo(retrieveHealth: false), _battery.GetInfo()));
      Replace(BasicInfo, results.Item1);
      Replace(RamInfo, results.Item2);
      Replace(DiskInfo, results.Item3);
      Replace(BatteryInfo, results.Item4);
      Status = "Retrieving disk health...";

      var diskInfo = await Task.Run(() => _disk.GetInfo());
      Replace(DiskInfo, diskInfo);
      Status = $"Updated {DateTime.Now:t}";
    }
    catch (Exception ex) { Status = $"Could not load all device information: {ex.Message}"; }
    finally { IsLoading = false; }
  }

  private static void Replace(ObservableCollection<InfoRow> target, IEnumerable<InfoRow> source)
  {
    target.Clear();
    foreach (var row in source)
    {
      if (row is null || !HasVisibleText(row.Property) || !HasVisibleText(row.Value))
      {
        continue;
      }

      target.Add(row with
      {
        Property = row.Property.Trim(),
        Value = row.Value.Trim()
      });
    }
  }

  private static bool HasVisibleText(string? text) =>
    !string.IsNullOrWhiteSpace(text) && text.Any(character =>
      !char.IsControl(character) && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format);
  private IReadOnlyList<InfoRow> AllRows => BasicInfo.Concat(RamInfo).Concat(DiskInfo).Concat(BatteryInfo).ToArray();
  private IReadOnlyList<InfoRow> ActiveRows => ActiveTab switch { "RAM Info" => RamInfo, "Disk Info" => DiskInfo, "Battery Info" => BatteryInfo, _ => BasicInfo };
  private void CopyActive() { Clipboard.SetText(string.Join(Environment.NewLine, ActiveRows.Select(r => $"{r.Property}: {r.Value}"))); Status = $"{ActiveTab} information copied to clipboard"; }

  private async Task ExportAsync(string extension)
  {
    var dialog = new SaveFileDialog { FileName = "AboutDevice_Summary" + extension, DefaultExt = extension, Filter = extension switch { ".csv" => "CSV Files (*.csv)|*.csv", ".json" => "JSON Files (*.json)|*.json", _ => "HTML Files (*.html)|*.html" } };
    if (dialog.ShowDialog() != true) return;
    try
    {
      await _export.ExportAsync(dialog.FileName, extension, AllRows);
      Status = $"Export saved to {dialog.FileName}";
      MessageBox.Show(
        $"Export complete.\n\nFile saved to:\n{dialog.FileName}",
        "Export Complete",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }
    catch (Exception ex) { Status = $"Export failed: {ex.Message}"; MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
  }

}
