using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Disk;

/// <summary>
/// Que puede hacer la aplicacion con un hallazgo de disco.
/// </summary>
public enum SpaceAction
{
    /// <summary>Solo informar. Borrarlo requiere herramientas de Windows o es mala idea.</summary>
    None,

    /// <summary>Vaciar el contenido de la carpeta, conservando la carpeta.</summary>
    DeleteFolderContents,

    /// <summary>Vaciar la papelera de reciclaje por la API de Windows.</summary>
    EmptyRecycleBin
}

public sealed class SpaceFinding : Finding
{
    public SpaceFinding(string id, string title, SpaceAction action, long bytes, string path = "")
        : base(id, title)
    {
        Action = action;
        Path = path;
        ReclaimableBytes = action == SpaceAction.None ? 0 : bytes;
        OccupiedBytes = bytes;

        // Nada se marca solo: incluso vaciar la papelera puede sorprender.
        SelectedByDefault = false;
    }

    public SpaceAction Action { get; }

    public string Path { get; }

    /// <summary>
    /// Lo que ocupa, se pueda recuperar o no. ReclaimableBytes es 0 en los
    /// hallazgos informativos para no prometer espacio que la app no libera.
    /// </summary>
    public long OccupiedBytes { get; }

    public override long DisplayBytes => OccupiedBytes;
}
