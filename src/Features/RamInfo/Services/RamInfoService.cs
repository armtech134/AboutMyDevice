using AboutMyDevice.Shared.Models;
using AboutMyDevice.Shared.Services;

namespace AboutMyDevice.Features.RamInfo.Services;

public sealed class RamInfoService
{
  public IReadOnlyList<InfoRow> GetInfo()
  {
    var rows = new List<InfoRow>();
    using var osResults = WmiQuery.Get("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
    var os = osResults.FirstOrDefault();
    if (os is not null)
    {
      var total = WmiQuery.Number(os, "TotalVisibleMemorySize") / 1024d / 1024d; var free = WmiQuery.Number(os, "FreePhysicalMemory") / 1024d / 1024d;
      rows.Add(new("RAM", "Total Memory", $"{total:F2} GB")); rows.Add(new("RAM", "Used Memory", $"{total-free:F2} GB")); rows.Add(new("RAM", "Free Memory", $"{free:F2} GB"));
    }
    using var modules = WmiQuery.Get("SELECT Capacity, Speed, SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory");
    for (var i = 0; i < modules.Count; i++)
    {
      var module = modules[i]; var speed = WmiQuery.Number(module, "Speed"); var type = Convert.ToUInt32(module["SMBIOSMemoryType"] ?? 0); if (type is 0 or 1) type = Convert.ToUInt32(module["MemoryType"] ?? 0);
      var ddr = type switch { 20 => "DDR", 21 or 22 => "DDR2", 24 or 30 => "DDR3", 26 or 31 => "DDR4", 34 or 35 => "DDR5", _ => "" };
      if (speed >= 4800) ddr = "DDR5"; else if (speed >= 2133 && ddr is not "DDR5") ddr = "DDR4"; else if (speed >= 800 && ddr.Length == 0) ddr = "DDR3";
      rows.Add(new("RAM", $"Module {i+1}", $"{WmiQuery.Number(module, "Capacity") / 1024d / 1024d / 1024d:F2} GB{(ddr.Length > 0 ? $" {ddr}" : "")} @ {speed} MHz"));
    }
    return rows;
  }
}
