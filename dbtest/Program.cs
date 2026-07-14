using System;
using System.IO;

namespace dbtest
{
    class Program
    {
        static void Main(string[] args)
        {
            var filePath = @"d:\4. PROJECT\2. Web\MBS_SAP\Views\Display\Index.cshtml";
            Console.WriteLine("=== SEARCHING Index.cshtml CHART UPDATES ===");

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
                if (i > 780) // JS scripts start after line 780
                {
                    if (line.Contains("cSap.update") || line.Contains("cToday.update") || line.Contains("updateCharts") || line.Contains("cSap.data"))
                    {
                        Console.WriteLine($"Line {i + 1}: {line.Trim()}");
                    }
                }
            }
        }
    }
}
