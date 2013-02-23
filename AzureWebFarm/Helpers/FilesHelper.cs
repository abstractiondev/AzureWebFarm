using System;
using System.IO;
using Castle.Core.Logging;

namespace AzureWebFarm.Helpers
{
    internal static class FilesHelper
    {
        public static void RemoveFolder(string folderPath, ILogger logger)
        {
            if (Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch (Exception e)
                {
                    logger.WarnFormat(e, "Error removing folder: {0}", folderPath);
                }
            }
        }
    }
}