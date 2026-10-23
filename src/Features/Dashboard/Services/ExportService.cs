using AboutMyDevice.Shared.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace AboutMyDevice.Features.Dashboard.Services;

public sealed class ExportService
{
  public async Task ExportAsync(string path, string format, IReadOnlyList<InfoRow> rows)
  {
    string content = format switch
    {
      ".json" => JsonSerializer.Serialize(ToJson(rows), new JsonSerializerOptions { WriteIndented = true }),
      ".html" => ToHtml(rows),
      _ => ToCsv(rows)
    };
    await File.WriteAllTextAsync(path, content, new UTF8Encoding(true));
  }

  private static string ToCsv(IEnumerable<InfoRow> rows) => "Category,Property,Value\r\n" + string.Join("\r\n", rows.Select(r => string.Join(",", new[] { r.Category, r.Property, r.Value }.Select(EscapeCsv))));
  private static string EscapeCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
  private static string Key(string value)
  {
    var key = Regex.Replace(Regex.Replace(value.Trim(), "[^a-zA-Z0-9\\s-]", ""), "[\\s-]+", "_").ToLowerInvariant();
    return key.Length == 0 ? "field" : key;
  }

  private static string UniqueKey(IEnumerable<string> existingKeys, string key)
  {
    var uniqueKey = key;
    var keys = existingKeys.ToHashSet(StringComparer.Ordinal);
    for (var suffix = 2; keys.Contains(uniqueKey); suffix++) uniqueKey = $"{key}_{suffix}";
    return uniqueKey;
  }

  private static void AddUnique(IDictionary<string, string> target, string key, string value)
  {
    target.Add(UniqueKey(target.Keys, key), value);
  }

  private static Dictionary<string, object> ToJson(IReadOnlyList<InfoRow> rows)
  {
    var result = new Dictionary<string, object>();
    foreach (var group in rows.GroupBy(r => r.Category))
    {
      var key = UniqueKey(result.Keys, Key(group.Key) + "_info");
      if (Key(group.Key) == "disk")
      {
        var disks = new List<Dictionary<string, string>>(); Dictionary<string, string>? current = null;
        foreach (var row in group)
        {
          if (Key(row.Property) == "disk_name" || current is null) { current = new(); disks.Add(current); }
          AddUnique(current, Key(row.Property), row.Value);
        }
        result[key] = disks;
      }
      else
      {
        var fields = new Dictionary<string, string>();
        foreach (var row in group) AddUnique(fields, Key(row.Property), row.Value);
        result[key] = fields;
      }
    }
    return result;
  }

  private static string ToHtml(IReadOnlyList<InfoRow> rows)
  {
    var b = new StringBuilder("<!doctype html><html><head><meta charset=\"utf-8\"><title>About Device Summary</title><style>body{font:15px Segoe UI,sans-serif;margin:32px;background:#f7f8fa;color:#20252b}h1{color:#176b5b}h2{margin-top:28px;border-bottom:2px solid #176b5b;padding-bottom:6px}table{border-collapse:collapse;background:white;width:100%;margin-bottom:20px}th,td{border:1px solid #ddd;padding:10px;text-align:left}th{width:30%;background:#f2f4f5}</style></head><body><h1>About Device Summary</h1>");
    foreach (var group in rows.GroupBy(r => r.Category))
    {
      b.Append("<h2>").Append(System.Net.WebUtility.HtmlEncode(group.Key)).Append(" Info</h2><table><thead><tr><th>Property</th><th>Value</th></tr></thead><tbody>");
      foreach (var row in group) b.Append("<tr><td>").Append(System.Net.WebUtility.HtmlEncode(row.Property)).Append("</td><td>").Append(System.Net.WebUtility.HtmlEncode(row.Value)).Append("</td></tr>");
      b.Append("</tbody></table>");
    }
    return b.Append("</body></html>").ToString();
  }
}
