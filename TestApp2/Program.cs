using System.IO;

namespace TestApp2
{
    class Program
    {
        static void Main()
        {
            File.WriteAllText("file.txt", "2");
        }
    }
}
