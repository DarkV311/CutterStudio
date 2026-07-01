namespace CutterStudio.Models;

public sealed record RecentProject(long Id, string Name, DateTime ModifiedUtc)
{
    public string DisplayText => $"{Name}  ·  {ModifiedUtc.ToLocalTime():g}";
}
