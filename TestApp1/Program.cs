using System.IO;

namespace TestApp1
{
    class Program
    {
        static void Main()
        {
            File.WriteAllText("file.txt", "1");
        }
    }
}
