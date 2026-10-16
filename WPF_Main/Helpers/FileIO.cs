
namespace AlgoTrading.Helpers
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;

    public class FileIO
    {
        private static string _targetDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        public static string TargetDirectory { get => _targetDirectory; private set => _targetDirectory = value; }

        public static async Task SaveJsonToFileAsync(string jsonData, string targetDirectory, string fileName)
        {
            string directory = targetDirectory ?? TargetDirectory;
            // 1. Ensure the target directory safely exists
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 2. Generate a unique, safe filename (e.g., using a timestamp or GUID)
            string _fileName = string.IsNullOrWhiteSpace(fileName) ? $"data_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json" : fileName; 
            string filePath = Path.Combine(directory, _fileName);

            try
            {
                // 3. Write asynchronously using modern C# features
                // This ensures the thread is freed up while the OS writes the file to disk
                await File.WriteAllTextAsync(filePath, jsonData);

                Console.WriteLine($"✅ JSON safely dumped to: {filePath}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // Handle file permission / Windows access issues
                Console.WriteLine($"❌ Permission Error: Can't write to this directory. {ex.Message}");
            }
            catch (IOException ex)
            {
                // Handle disk space issues or lock contentions
                Console.WriteLine($"❌ Disk I/O Error: {ex.Message}");
            }
        }

        public static async Task<string?> ReadJsonFileSafelyAsync(string filePath)
        {
            // 1. Defensively check if the file actually exists
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"⚠️ Read Failed: File does not exist at {filePath}");
                return null;
            }

            try
            {
                // 2. Read the entire text asynchronously without locking the UI/application thread
                string jsonData = await File.ReadAllTextAsync(filePath);
                return jsonData;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"❌ Permission Error: No rights to read this file. {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                // Catches cases where another background thread or process is locking the file
                Console.WriteLine($"❌ File Access Error: {ex.Message}");
                return null;
            }
        }
        public static async Task<T?> StreamAndDeserializeJsonAsync<T>(string filePath)
        {
            if (!File.Exists(filePath)) return default;

            try
            {
                // 1. Open the file in read-only mode, allowing other processes to still read it (FileShare.Read)
                using var fileStream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                // 2. Stream directly into System.Text.Json without creating a giant intermediate string
                T? result = await JsonSerializer.DeserializeAsync<T>(fileStream);
                return result;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ Corrupt JSON: The file format is invalid. {ex.Message}");
                return default;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error streaming file: {ex.Message}");
                return default;
            }
        }

    }
}
