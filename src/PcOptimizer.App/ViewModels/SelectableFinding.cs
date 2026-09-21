using CommunityToolkit.Mvvm.ComponentModel;
using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.App.ViewModels;

public partial class SelectableFinding : ObservableObject
{
    public SelectableFinding(Finding model)
    {
        Model = model;
        _isSelected = model.SelectedByDefault;
    }

    public Finding Model { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Title => Model.Title;

    public string Details => Model.Details;

    public string Size => Model.ReclaimableBytes > 0
        ? FormatBytes(Model.ReclaimableBytes)
        : string.Empty;

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
