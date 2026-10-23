using AboutMyDevice.Shared.Models;
using AboutMyDevice.Shared.Services;
using System.Runtime.InteropServices;

namespace AboutMyDevice.Features.BasicInfo.Services;

public sealed class BasicInfoService
{
  [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFirmwareType(out uint firmwareType);

  public IReadOnlyList<InfoRow> GetInfo()
  {
    var rows = new List<InfoRow>();
    void Add(string property, string value) => rows.Add(new("Basic", property, value));
    Add("Computer Name", Environment.MachineName);
    Add("Username", Environment.UserName);
    using var osResults = WmiQuery.Get("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
    var os = osResults.FirstOrDefault();
    Add("OS", os is null ? Environment.OSVersion.ToString() : $"{WmiQuery.Text(os, "Caption")} {WmiQuery.Text(os, "Version")} Build {WmiQuery.Text(os, "BuildNumber")}");
    using var systemResults = WmiQuery.Get("SELECT Manufacturer, Model, PCSystemType FROM Win32_ComputerSystem");
    var system = systemResults.FirstOrDefault();
    if (system is not null)
    {
      Add("System Manufacturer", WmiQuery.Text(system, "Manufacturer")); Add("System Model", WmiQuery.Text(system, "Model"));
      var role = Convert.ToUInt16(system["PCSystemType"] ?? 0);
      Add("Platform Role", role switch { 1 => "Desktop", 2 => "Mobile", 3 => "Workstation", 4 => "Enterprise Server", 5 => "SOHO Server", 6 => "Appliance PC", 7 => "Performance Server", 8 => "Slate", _ => "Unspecified" });
    }
    using var processors = WmiQuery.Get("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
    var cpu = processors.FirstOrDefault();
    Add("Processor", cpu is null ? "Unknown" : WmiQuery.Text(cpu, "Name"));
    if (processors.Count > 0)
    {
      Add("Physical Cores", processors.Aggregate(0UL, (total, processor) => total + WmiQuery.Number(processor, "NumberOfCores")).ToString());
      Add("Logical Processors", processors.Aggregate(0UL, (total, processor) => total + WmiQuery.Number(processor, "NumberOfLogicalProcessors")).ToString());
    }
    Add("Is X64 Bit Device", Environment.Is64BitOperatingSystem ? "Yes" : "No");
    using var biosResults = WmiQuery.Get("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
    var bios = biosResults.FirstOrDefault();
    Add("BIOS", bios is null ? "Unknown" : $"{WmiQuery.Text(bios, "Manufacturer")} {WmiQuery.Text(bios, "SMBIOSBIOSVersion")}");
    var date = bios is null ? "Unknown" : WmiQuery.Text(bios, "ReleaseDate");
    if (date.Length >= 8 && DateTime.TryParseExact(date[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsed)) date = parsed.ToString("yyyy-MM-dd");
    Add("BIOS Date", date);
    Add("BIOS Mode", GetFirmwareType(out var mode) ? mode switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => "Unknown" } : "Unknown");
    try { using var tpmResults = WmiQuery.Get("SELECT SpecVersion FROM Win32_Tpm", @"\\.\ROOT\CIMV2\Security\MicrosoftTpm"); var tpm = tpmResults.FirstOrDefault(); Add("TPM Version", tpm is null ? "Not detected" : WmiQuery.Text(tpm, "SpecVersion")); }
    catch (System.Management.ManagementException) { Add("TPM Version", "Not detected"); }
    using var gpus = WmiQuery.Get("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
    foreach (var gpu in gpus)
    {
      Add("GPU", WmiQuery.Text(gpu, "Name"));
      var width = WmiQuery.Number(gpu, "CurrentHorizontalResolution"); var height = WmiQuery.Number(gpu, "CurrentVerticalResolution"); var hz = WmiQuery.Number(gpu, "CurrentRefreshRate");
      if (width > 0 && height > 0) Add("Display Resolution", $"{width} x {height}{(hz > 0 ? $" @ {hz} Hz" : "")}");
    }
    return rows;
  }
}
