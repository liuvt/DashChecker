namespace DashChecker.Models;

public sealed class ColumnOption
{
    public ColumnOption(string key, string label, bool visible = true)
    {
        Key = key;
        Label = label;
        Visible = visible;
    }

    public string Key { get; }
    public string Label { get; }
    public bool Visible { get; set; }
}
