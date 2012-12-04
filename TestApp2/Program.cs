using System.IO;

namespace TestApp2
{
    class Program
    {
        static void Main(string[] args)
        {
            File.WriteAllText("file.txt", "2");
        }
    }
}
