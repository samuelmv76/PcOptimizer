using CommunityToolkit.Mvvm.ComponentModel;
using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Platform;
<<<<<<< HEAD
using PcOptimizer.Core.Summary;
=======
>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0

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

<<<<<<< HEAD
    /// <summary>Si la ficha lleva a otro apartado (las del resumen).</summary>
    public bool HasTarget => !string.IsNullOrEmpty(Model.RelatedModuleId);

    public string? RelatedModuleId => Model.RelatedModuleId;

    /// <summary>Lo pendiente en el apartado, para las fichas del resumen.</summary>
    public int PendingCount => (Model as SummaryFinding)?.PendingCount ?? 0;

    /// <summary>Ficha de resumen sin nada que hacer: se pinta con una marca verde.</summary>
    public bool IsClear => Model is SummaryFinding && PendingCount == 0 && Severity == FindingSeverity.Info;

    public FindingSeverity Severity => Model.Severity;

=======
    public FindingSeverity Severity => Model.Severity;

>>>>>>> 77a6a47fbf3cb9b7c8cc565933bd42c37265aab0
    public long ReclaimableBytes => Model.ReclaimableBytes;

    public string Size => Model.DisplayBytes > 0 ? ByteSize.Format(Model.DisplayBytes) : string.Empty;

    public static string FormatBytes(long bytes) => ByteSize.Format(bytes);
}
