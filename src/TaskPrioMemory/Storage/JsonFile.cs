using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace TaskPrioMemory.Storage
{
    /// <summary>
    /// Tiny JSON load/save helper using the framework's built-in
    /// DataContractJsonSerializer (no third-party dependencies).
    /// </summary>
    public static class JsonFile
    {
        public static T Load<T>(string path) where T : class, new()
        {
            try
            {
                if (!File.Exists(path)) return new T();
                using (var fs = File.OpenRead(path))
                {
                    if (fs.Length == 0) return new T();
                    var ser = new DataContractJsonSerializer(typeof(T));
                    var obj = ser.ReadObject(fs) as T;
                    return obj ?? new T();
                }
            }
            catch
            {
                // Corrupt/older file: start fresh rather than crash the tray app.
                return new T();
            }
        }

        public static void Save<T>(string path, T value)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var ser = new DataContractJsonSerializer(typeof(T));
            // Serialize to memory first so a failure never truncates the real file.
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, value);
                var tmp = path + ".tmp";
                File.WriteAllBytes(tmp, ms.ToArray());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
        }

        public static string PrettyPrint(byte[] utf8Json)
        {
            // Not strictly needed; kept minimal.
            return Encoding.UTF8.GetString(utf8Json);
        }
    }
}
