using System.Management;
using System.Runtime.Versioning;

namespace PcOptimizer.Core.Safety;

[SupportedOSPlatform("windows")]
public sealed class WmiRestorePointService : IRestorePointService
{
    private const uint ApplicationInstall = 0;
    private const uint BeginSystemChange = 100;

    public bool TryCreate(string description, out string? error)
    {
        try
        {
            using var systemRestore = new ManagementClass(
                new ManagementPath(@"\\localhost\root\default:SystemRestore"), null);

            var parameters = systemRestore.GetMethodParameters("CreateRestorePoint");
            parameters["Description"] = description;
            parameters["RestorePointType"] = ApplicationInstall;
            parameters["EventType"] = BeginSystemChange;

            var result = systemRestore.InvokeMethod("CreateRestorePoint", parameters, null);
            var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);

            if (returnValue != 0)
            {
                error = $"CreateRestorePoint devolvió el código {returnValue}.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
