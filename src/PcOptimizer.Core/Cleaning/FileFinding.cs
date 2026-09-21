using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Cleaning;

public sealed class FileFinding : Finding
{
    public FileFinding(string fullPath, string targetName, long sizeInBytes)
        : base(fullPath, System.IO.Path.GetFileName(fullPath))
    {
        FullPath = fullPath;
        TargetName = targetName;
        ReclaimableBytes = sizeInBytes;
        Details = $"{targetName} - {System.IO.Path.GetDirectoryName(fullPath)}";
        SelectedByDefault = true;
        Severity = FindingSeverity.Info;
    }

    public string FullPath { get; }

    public string TargetName { get; }
}
