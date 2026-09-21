using PcOptimizer.Core.Abstractions;
using PcOptimizer.Core.Safety;

namespace PcOptimizer.Core.Undo;

public sealed class UndoFinding : Finding
{
    public UndoFinding(ReversibleChange change, bool supported)
        : base(change.Id, change.Description)
    {
        Change = change;
        Supported = supported;

        var when = change.Timestamp.LocalDateTime.ToString("dd/MM/yyyy HH:mm");

        Details = supported
            ? $"{when} - {DescribeKind(change.Kind)}"
            : $"{when} - {DescribeKind(change.Kind)} (esta versión no sabe deshacerlo)";

        Recommendation = supported
            ? DescribeRestore(change)
            : "Este cambio quedó anotado pero la aplicación no tiene forma de revertirlo "
              + "automáticamente. El registro de auditoría tiene el detalle para hacerlo a mano.";

        Severity = supported ? FindingSeverity.Suggestion : FindingSeverity.Info;

        // Deshacer es tan decision del usuario como lo fue aplicar.
        SelectedByDefault = false;
    }

    public ReversibleChange Change { get; }

    public bool Supported { get; }

    private static string DescribeKind(string kind) => kind switch
    {
        ChangeKinds.RegistryValue => "ajuste del registro",
        ChangeKinds.StartupEntry => "programa al inicio",
        ChangeKinds.PowerPlan => "plan de energía",
        ChangeKinds.ServiceStartMode => "modo de inicio de un servicio",
        ChangeKinds.FirmwareVariable => "arranque del firmware",
        _ => kind
    };

    private static string DescribeRestore(ReversibleChange change)
    {
        var restart = change.NeedsRestart
            ? " Hará falta reiniciar para que vuelva a surtir efecto."
            : string.Empty;

        if (change.Kind == ChangeKinds.FirmwareVariable)
        {
            return $"Se cancelará lo programado para el próximo arranque.{restart}";
        }

        return change.PreviousValue is null
            ? $"Se quitará el valor que la aplicación creó.{restart}"
            : $"Volverá a: {change.PreviousValue}.{restart}";
    }
}
