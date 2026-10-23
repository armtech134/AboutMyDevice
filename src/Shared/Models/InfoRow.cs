namespace AboutMyDevice.Shared.Models;

public sealed record InfoRow(string Category, string Property, string Value)
{
  public string Display => $"{Property}: {Value}";

  public override string ToString() => Display;
}
