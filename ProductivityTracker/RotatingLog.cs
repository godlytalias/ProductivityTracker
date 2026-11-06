using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ProductivityTracker
{
    internal static class RotatingLog
    {
        internal const int MaximumBytes = 1024 * 1024;
        internal const int ArchiveCount = 3;
        private const int MaximumMessageCharacters = 16384;

        public static void Write(string path, string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var lease = AcquireLease(path + ".lock");
                if (lease == null)
                {
                    return;
                }
                if (message.Length > MaximumMessageCharacters)
                {
                    message = message[..MaximumMessageCharacters] + " [truncated]";
                }
                var bytes = Encoding.UTF8.GetBytes(
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
                for (var index = 1; index <= ArchiveCount; index++)
                {
                    var archive = path + "." + index;
                    if (File.Exists(archive))
                    {
                        TrimOversizedLog(archive);
                    }
                }
                if (File.Exists(path))
                {
                    TrimOversizedLog(path);
                    if (new FileInfo(path).Length + bytes.Length > MaximumBytes)
                    {
                        for (var index = ArchiveCount; index >= 1; index--)
                        {
                            var source = index == 1 ? path : path + "." + (index - 1);
                            var destination = path + "." + index;
                            if (File.Exists(source))
                            {
                                TrimOversizedLog(source);
                                File.Move(source, destination, true);
                            }
                        }
                    }
                }
                using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                output.Write(bytes);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static FileStream? AcquireLease(string path)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
                {
                    if (attempt < 2)
                    {
                        Thread.Sleep(10);
                    }
                }
            }
            return null;
        }

        private static void TrimOversizedLog(string path)
        {
            if (new FileInfo(path).Length <= MaximumBytes)
            {
                return;
            }
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            file.Position = file.Length - MaximumBytes;
            var tail = new byte[MaximumBytes];
            file.ReadExactly(tail);
            var start = Array.IndexOf(tail, (byte)'\n') + 1;
            file.Position = 0;
            file.Write(tail.AsSpan(start));
            file.SetLength(tail.Length - start);
        }
    }
}
