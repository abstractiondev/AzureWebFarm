using System;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm.Helpers
{
    public static class AzureRoleEnvironment
    {
        public static Func<string> DeploymentId = () => RoleEnvironment.DeploymentId;
        public static Func<string> CurrentRoleInstanceId = () => RoleEnvironment.CurrentRoleInstance.Id;
        public static Func<string, string> GetConfigurationSettingValue = RoleEnvironment.GetConfigurationSettingValue;

        public static bool IsComputeEmulatorEnvironment
        {
            get
            {
                return RoleEnvironment.IsAvailable && RoleEnvironment.DeploymentId.StartsWith("deployment", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
