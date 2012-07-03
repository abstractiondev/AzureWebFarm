using System;

namespace AzureWebFarm.Entities
{
    public class FileEntry
    {
        public DateTime LocalLastModified { get; set; }

        public DateTime CloudLastModified { get; set; }

        public bool IsDirectory { get; set; }

        public DateTime LastDeployed { get; set; }
    }
}