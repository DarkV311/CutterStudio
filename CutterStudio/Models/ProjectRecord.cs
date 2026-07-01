namespace CutterStudio.Models;

public sealed class ProjectRecord
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public required string ArtworkSvg { get; set; }
    public string SettingsJson { get; set; } = "{}";
}
