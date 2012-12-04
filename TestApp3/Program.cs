using System;
using System.IO;

namespace TestApp3
{
    class Program
    {
        static void Main()
        {
            if (!File.Exists("file.txt"))
                File.WriteAllText("file.txt", "");

            switch (File.ReadAllText("file.txt"))
            {
                case "":
                    File.WriteAllText("file.txt", "1");
                    throw new Exception();
                case "1":
                    File.WriteAllText("file.txt", "2");
                    throw new Exception();
                default:
                    File.WriteAllText("file.txt", "3");
                    break;
            }
        }
    }
}
