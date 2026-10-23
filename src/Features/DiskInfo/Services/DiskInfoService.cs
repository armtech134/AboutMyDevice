using AboutMyDevice.Shared.Models;
using AboutMyDevice.Shared.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AboutMyDevice.Features.DiskInfo.Services;

public sealed class DiskInfoService
{
  public IReadOnlyList<InfoRow> GetInfo(bool retrieveHealth = true)
  {
    var rows = new List<InfoRow>();
    var executable = Path.Combine(AppContext.BaseDirectory, "tools", "smartctl.exe");
    var usbDeviceTypes = retrieveHealth && File.Exists(executable)
      ? DiscoverUsbDeviceTypes(executable)
      : new Dictionary<int, string>();

    using var drives = WmiQuery.Get("SELECT DeviceID, Model, Size, InterfaceType, MediaType, Status FROM Win32_DiskDrive");
    foreach (var drive in drives)
    {
      var device = WmiQuery.Text(drive, "DeviceID");
      var model = WmiQuery.Text(drive, "Model");
      var size = WmiQuery.Number(drive, "Size");
      var health = retrieveHealth ? ReadHealth(device, usbDeviceTypes, executable) : "Loading...";
      var volumeCapacity = GetVolumeCapacity(drive);

      rows.Add(new("Disk", "Disk Name", $"{model} ({device})"));
      rows.Add(new("Disk", "Bus Interface", WmiQuery.Text(drive, "InterfaceType")));
      rows.Add(new("Disk", "Media Type", WmiQuery.Text(drive, "MediaType")));
      rows.Add(new("Disk", "Device Status", WmiQuery.Text(drive, "Status")));
      rows.Add(new("Disk", "Total Capacity", size > 0 ? FormatBytes(size) : "Unavailable"));
      rows.Add(new("Disk", "Used Capacity", volumeCapacity is { } usedCapacity ? FormatBytes(usedCapacity.Used) : "Unavailable"));
      rows.Add(new("Disk", "Remaining Capacity", volumeCapacity is { } freeCapacity ? FormatBytes(freeCapacity.Free) : "Unavailable"));
      rows.Add(new("Disk", "Health", health));
    }

    return rows.Count == 0 ? [new("Disk", "Status", "No disks detected")] : rows;
  }

  private static (long Used, long Free)? GetVolumeCapacity(ManagementObject disk)
  {
    try
    {
      var volumeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      using var partitions = disk.GetRelated("Win32_DiskPartition");
      foreach (ManagementObject partition in partitions)
      {
        using (partition)
        {
          using var logicalDisks = partition.GetRelated("Win32_LogicalDisk");
          foreach (ManagementObject logicalDisk in logicalDisks)
          {
            using (logicalDisk)
            {
              var name = WmiQuery.Text(logicalDisk, "DeviceID", "");
              if (!string.IsNullOrWhiteSpace(name)) volumeNames.Add(name);
            }
          }
        }
      }

      long usedTotal = 0;
      long freeTotal = 0;
      var foundVolume = false;
      foreach (var volume in DriveInfo.GetDrives().Where(drive => volumeNames.Contains(drive.Name.TrimEnd('\\'))))
      {
        if (!volume.IsReady || volume.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
        var total = volume.TotalSize;
        if (total <= 0) continue;
        var free = volume.AvailableFreeSpace;
        usedTotal += total - free;
        freeTotal += free;
        foundVolume = true;
      }

      return foundVolume ? (usedTotal, freeTotal) : null;
    }
    catch (Exception ex) when (ex is ManagementException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
    {
      return null;
    }
  }

  private static Dictionary<int, string> DiscoverUsbDeviceTypes(string executable)
  {
    var types = new Dictionary<int, string>();
    var json = RunSmartctl(executable, "", null, scanUsbDevices: true);
    if (string.IsNullOrWhiteSpace(json)) return types;

    try
    {
      using var document = JsonDocument.Parse(json);
      if (!document.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
        return types;

      foreach (var candidate in devices.EnumerateArray())
      {
        if (!candidate.TryGetProperty("name", out var nameElement) ||
            !candidate.TryGetProperty("type", out var typeElement)) continue;

        var match = Regex.Match(nameElement.GetString() ?? "", @"/dev/pd(\d+)$", RegexOptions.IgnoreCase);
        var type = typeElement.GetString();
        if (match.Success && int.TryParse(match.Groups[1].Value, out var index) && !string.IsNullOrWhiteSpace(type))
          types[index] = type;
      }
    }
    catch (JsonException)
    {
      // If USB scan output is unavailable, use the generic device probes below.
    }

    return types;
  }

  private static string ReadHealth(string device, IReadOnlyDictionary<int, string> usbDeviceTypes, string executable)
  {
    var match = Regex.Match(device, @"PHYSICALDRIVE(\d+)", RegexOptions.IgnoreCase);
    if (!match.Success) return "SMART unavailable";

    if (!File.Exists(executable)) return "smartctl not found";

    var diskPath = $"/dev/pd{match.Groups[1].Value}";
    var deviceTypes = new List<string?>();
    if (int.TryParse(match.Groups[1].Value, out var index) && usbDeviceTypes.TryGetValue(index, out var detectedType))
      deviceTypes.Add(detectedType);
    deviceTypes.AddRange([null, "sat", "sat,12", "scsi", "nvme"]);
    string? lastError = null;

    foreach (var deviceType in deviceTypes.Distinct(StringComparer.Ordinal))
    {
      var json = RunSmartctl(executable, diskPath, deviceType);
      if (json is null) continue;

      try
      {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var health = GetHealth(root);
        if (health is not null) return health;
        lastError = GetSmartctlError(root) ?? lastError;
      }
      catch (JsonException)
      {
        // Try the next device type; some controllers need an explicit smartctl driver.
      }
    }

    if (lastError?.Contains("Error=5", StringComparison.OrdinalIgnoreCase) == true ||
        lastError?.Contains("access denied", StringComparison.OrdinalIgnoreCase) == true)
      return "SMART access denied (administrator rights required)";

    return "SMART unavailable (controller or drive does not expose health data)";
  }

  private static string? GetSmartctlError(JsonElement root)
  {
    if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
      return null;

    foreach (var message in messages.EnumerateArray())
    {
      if (message.TryGetProperty("severity", out var severity) && severity.GetString() == "error" &&
          message.TryGetProperty("string", out var text))
        return text.GetString();
    }

    return null;
  }

  private static string? RunSmartctl(string executable, string diskPath, string? deviceType, bool scanUsbDevices = false)
  {
    try
    {
      var start = new ProcessStartInfo(executable)
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
      };
      if (scanUsbDevices)
      {
        start.ArgumentList.Add("--scan-open");
        start.ArgumentList.Add("-d");
        start.ArgumentList.Add("usb,pd");
        start.ArgumentList.Add("-j");
      }
      else
      {
        start.ArgumentList.Add("-a");
        start.ArgumentList.Add("-j");
        if (deviceType is not null)
        {
          start.ArgumentList.Add("-d");
          start.ArgumentList.Add(deviceType);
        }
        start.ArgumentList.Add(diskPath);
      }

      using var process = Process.Start(start);
      if (process is null) return null;

      var outputTask = process.StandardOutput.ReadToEndAsync();
      var errorTask = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(12000))
      {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
      }

      Task.WaitAll(outputTask, errorTask);
      return !string.IsNullOrWhiteSpace(outputTask.Result) ? outputTask.Result : errorTask.Result;
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
    {
      return null;
    }
  }

  private static string? GetHealth(JsonElement root)
  {
    var hasSmartStatus = false;
    var smartPassed = false;
    if (root.TryGetProperty("smart_status", out var status) &&
        status.TryGetProperty("passed", out var passed) &&
        (passed.ValueKind is JsonValueKind.True or JsonValueKind.False))
    {
      hasSmartStatus = true;
      smartPassed = passed.GetBoolean();
    }
    if (hasSmartStatus && !smartPassed) return "Warning";

    if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
    {
      var criticalWarning = nvme.TryGetProperty("critical_warning", out var warning) && warning.TryGetInt32(out var warningValue)
        ? warningValue
        : 0;
      var spareOkay = !nvme.TryGetProperty("available_spare", out var spare) ||
        !nvme.TryGetProperty("available_spare_threshold", out var threshold) ||
        !spare.TryGetInt32(out var spareValue) || !threshold.TryGetInt32(out var thresholdValue) || spareValue >= thresholdValue;

      if (nvme.TryGetProperty("percentage_used", out var used) && used.TryGetInt32(out var usedValue))
      {
        var remainingLife = Math.Clamp(100 - usedValue, 0, 100);
        return criticalWarning == 0 && spareOkay
          ? $"Healthy ({remainingLife}% estimated endurance remaining)"
          : $"Warning ({remainingLife}% estimated endurance remaining)";
      }

      return criticalWarning == 0 && spareOkay ? "Healthy" : "Warning";
    }

    if (root.TryGetProperty("scsi_percentage_used_endurance_indicator", out var scsiUsed) &&
        scsiUsed.TryGetInt32(out var scsiUsedValue) && scsiUsedValue is >= 0 and <= 100)
    {
      var remainingLife = 100 - scsiUsedValue;
      return remainingLife > 0
        ? $"Healthy ({remainingLife}% estimated endurance remaining)"
        : "Warning (estimated endurance exhausted)";
    }

    if (root.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var table))
    {
      var wearAttributes = table.EnumerateArray()
        .Where(attribute => attribute.TryGetProperty("id", out var id) && id.TryGetInt32(out var idValue) &&
          new[] { 231, 202, 233, 177, 232 }.Contains(idValue))
        .Select(attribute => attribute.TryGetProperty("value", out var value) && value.TryGetInt32(out var number) ? (int?)number : null)
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .ToList();
      if (wearAttributes.Count > 0) return $"{wearAttributes.Min()}%";
    }

    if (hasSmartStatus) return "Healthy";
    return null;
  }

  private static string FormatBytes(double bytes) => $"{bytes / 1024d / 1024d / 1024d:F2} GB";
}
