using PcOptimizer.Core.Abstractions;

namespace PcOptimizer.Core.Bloatware;

public sealed class BloatwareFinding : Finding
{
    public BloatwareFinding(AppxPackageInfo package, AppClassification classification, string reason, string? caveat)
        : base(package.PackageFullName, FriendlyName(package.Name))
    {
        Package = package;
        Classification = classification;

        var publisher = ExtractPublisher(package.Publisher);
        Details = string.IsNullOrEmpty(reason)
            ? publisher
            : string.IsNullOrEmpty(publisher) ? reason : $"{reason} - {publisher}";

        Recommendation = caveat ?? string.Empty;

        Severity = caveat is not null
            ? FindingSeverity.Warning
            : classification == AppClassification.Bloatware
                ? FindingSeverity.Suggestion
                : FindingSeverity.Info;

        // Desinstalar nunca va marcado por defecto: es una decision del usuario,
        // no una limpieza obvia como borrar un temporal de hace una semana.
        SelectedByDefault = false;
    }

    public AppxPackageInfo Package { get; }

    public AppClassification Classification { get; }

    /// <summary>
    /// Convierte "Microsoft.WindowsMaps" en "Windows Maps". Los nombres de
    /// paquete no estan pensados para leerse.
    /// </summary>
    internal static string FriendlyName(string packageName)
    {
        var last = packageName.Split('.').LastOrDefault() ?? packageName;

        if (last.Length == 0)
        {
            return packageName;
        }

        var spaced = new System.Text.StringBuilder(last.Length + 8);

        for (var i = 0; i < last.Length; i++)
        {
            var current = last[i];

            var startsWord = i > 0
                             && char.IsUpper(current)
                             && (!char.IsUpper(last[i - 1]) || (i + 1 < last.Length && char.IsLower(last[i + 1])));

            if (startsWord)
            {
                spaced.Append(' ');
            }

            spaced.Append(current);
        }

        return spaced.ToString();
    }

    /// <summary>El publicador viene como "CN=Microsoft Corporation, O=..., L=...".</summary>
    private static string ExtractPublisher(string publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher))
        {
            return string.Empty;
        }

        var commonName = publisher
            .Split(',')
            .Select(part => part.Trim())
            .FirstOrDefault(part => part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase));

        return commonName is null ? publisher : commonName[3..];
    }
}
