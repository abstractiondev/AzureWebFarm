using System;
using System.Diagnostics;
using System.IO;
using AzureToolkit;

namespace AzureWebFarm.Helpers
{
    internal static class FilesHelper
    {
        public static void RemoveFolder(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch (Exception e)
                {
                    Trace.TraceWarning("Remove Folder Error{0}{1}", Environment.NewLine, e.TraceInformation());
                }
            }
        }
    }
}