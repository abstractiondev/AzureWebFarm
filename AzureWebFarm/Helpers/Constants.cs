using System;
using System.Collections.Generic;
using System.Configuration;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace AzureWebFarm.Helpers
{
    public static class Constants
    {
        public const string StorageConnectionStringKey = "DataConnectionString";

        public static bool IsSyncEnabled { get { return Convert.ToBoolean(Get("SyncEnabled")); } }
        public static IEnumerable<string> DirectoriesToExclude { get { return Get("DirectoriesToExclude").Split(';'); } }
        public static TimeSpan SyncInterval { get { return TimeSpan.FromSeconds(Convert.ToInt32(Get("SyncIntervalInSeconds"))); } }

        private static string Get(string key)
        {
            return RoleEnvironment.IsAvailable
                ? RoleEnvironment.GetConfigurationSettingValue(key)
                : ConfigurationManager.AppSettings[key];
        }
    }
}
