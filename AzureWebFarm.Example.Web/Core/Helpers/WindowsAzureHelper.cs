using System;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm.Example.Web.Core.Helpers
{
    public static class WindowsAzureHelper
    {
        public static bool IsComputeEmulatorEnvironment
        {
            get
            {
                return RoleEnvironment.IsAvailable && RoleEnvironment.DeploymentId.StartsWith("deployment", StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}