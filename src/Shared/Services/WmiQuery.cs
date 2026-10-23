using System.Management;

namespace AboutMyDevice.Shared.Services;

internal static class WmiQuery
{
  public static WmiObjectCollection Get(string query, string scope = @"\\.\ROOT\CIMV2")
  {
    using var searcher = new ManagementObjectSearcher(scope, query);
    using var results = searcher.Get();
    var objects = new WmiObjectCollection();
    try
    {
      foreach (ManagementObject item in results)
      {
        objects.Add(item);
      }

      return objects;
    }
    catch
    {
      foreach (var item in objects) item.Dispose();
      throw;
    }
  }

  public static string Text(ManagementBaseObject item, string property, string fallback = "Unknown") =>
    Convert.ToString(item[property])?.Trim() is { Length: > 0 } value ? value : fallback;

  public static ulong Number(ManagementBaseObject item, string property) =>
    ulong.TryParse(Convert.ToString(item[property]), out var value) ? value : 0;
}

internal sealed class WmiObjectCollection : List<ManagementObject>, IDisposable
{
  public void Dispose()
  {
    foreach (var item in this) item.Dispose();
    Clear();
  }
}
