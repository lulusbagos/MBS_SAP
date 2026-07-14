using System;
using System.IO;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            var filePath = @"d:\4. PROJECT\2. Web\MBS_SAP\Controllers\DisplayController.cs";
            Console.WriteLine("=== SEARCHING DisplayController.cs ===");

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found!");
                return;
            }

            string content;
            using (var reader = new StreamReader(filePath, true))
            {
                content = reader.ReadToEnd();
            }

            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Contains("startOfMonthMaincon"))
                {
                    Console.WriteLine($"Line {i + 1}: {line.Trim()}");
                }
            }
        }
    }
}
