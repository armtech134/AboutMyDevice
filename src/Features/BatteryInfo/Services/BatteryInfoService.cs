using AboutMyDevice.Shared.Models;
using AboutMyDevice.Shared.Services;

namespace AboutMyDevice.Features.BatteryInfo.Services;

public sealed class BatteryInfoService
{
  public IReadOnlyList<InfoRow> GetInfo()
  {
    var rows = new List<InfoRow>();
    try
    {
      using var batteryResults = WmiQuery.Get("SELECT Name, EstimatedChargeRemaining FROM Win32_Battery");
      var battery = batteryResults.FirstOrDefault();
      if (battery is null) return [new("Battery", "Status", "No battery detected")];
      rows.Add(new("Battery", "Name", WmiQuery.Text(battery, "Name", "Unknown Battery")));
      rows.Add(new("Battery", "Estimated Charge Remaining", $"{Convert.ToUInt16(battery["EstimatedChargeRemaining"] ?? 0)} %"));
      var cycleCount = ReadCycleCount();
      rows.Add(new("Battery", "Cycle Count", cycleCount.HasValue ? $"{cycleCount.Value} cycles" : "Not reported by device"));
      using var designResults = WmiQuery.Get("SELECT DesignedCapacity FROM BatteryStaticData", @"\\.\ROOT\WMI");
      using var fullResults = WmiQuery.Get("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"\\.\ROOT\WMI");
      var design = designResults.FirstOrDefault();
      var full = fullResults.FirstOrDefault();
      var designed = design is null ? 0UL : WmiQuery.Number(design, "DesignedCapacity"); var charged = full is null ? 0UL : WmiQuery.Number(full, "FullChargedCapacity");
      if (designed > 0) rows.Add(new("Battery", "Design Capacity", $"{designed} mWh"));
      if (charged > 0) rows.Add(new("Battery", "Full Charge Capacity", $"{charged} mWh"));
      if (designed > 0 && charged > 0) rows.Add(new("Battery", "Battery Health", $"{charged * 100d / designed:F2} %"));
    }
    catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
    {
      return rows.Count > 0
        ? rows
        : [new("Battery", "Status", "Battery information is unavailable")];
    }
    return rows;
  }

  private static ulong? ReadCycleCount()
  {
    foreach (var className in new[] { "BatteryCycleCount", "MSBatteryClass" })
    {
      try
      {
        using var batteryResults = WmiQuery.Get($"SELECT CycleCount FROM {className}", @"\\.\ROOT\WMI");
        var batteryData = batteryResults.FirstOrDefault();
        if (batteryData is not null)
        {
          var cycleCount = WmiQuery.Number(batteryData, "CycleCount");
          if (cycleCount > 0)
          {
            return cycleCount;
          }
        }
      }
      catch (System.Management.ManagementException)
      {
        // The WMI class may not be provided by this battery or device driver.
      }
    }

    return null;
  }
}
