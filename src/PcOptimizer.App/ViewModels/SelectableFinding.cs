using CommunityToolkit.Mvvm.ComponentModel;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Platform;

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

    public string Recommendation => Model.Recommendation;

    public bool HasRecommendation => !string.IsNullOrWhiteSpace(Model.Recommendation);

    public FindingSeverity Severity => Model.Severity;

    public long ReclaimableBytes => Model.ReclaimableBytes;

    public string Size => Model.DisplayBytes > 0 ? ByteSize.Format(Model.DisplayBytes) : string.Empty;

    public static string FormatBytes(long bytes) => ByteSize.Format(bytes);
}
