using System;
using System.IO;
using System.Diagnostics;
using AzureWebFarm.Example.Web.Core.Extensions;

namespace AzureWebFarm.Example.Web.Core.Helpers
{
    public static class FilesHelper
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