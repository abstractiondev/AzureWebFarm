using System.IO;
using System.Threading;

namespace TestApp4
{
    class Program
    {
        static void Main()
        {
            File.WriteAllText("file.txt", "4");
            Thread.Sleep(-1);
        }
    }
}
