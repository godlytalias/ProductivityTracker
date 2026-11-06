using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProductivityTracker.Tests
{
    [TestClass]
    public sealed class RotatingLogTests
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "ProductivityTracker.Tests-" + Guid.NewGuid().ToString("N"));
        private string LogPath => Path.Combine(_folder, "app.log");

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [TestMethod]
        public void RotationRetainsOnlyThreeBoundedArchivesAndTheNewestMessage()
        {
            Directory.CreateDirectory(_folder);
            for (var index = 0; index < 5; index++)
            {
                File.WriteAllBytes(LogPath, new byte[RotatingLog.MaximumBytes]);
                RotatingLog.Write(LogPath, "message " + index);
            }
            var logs = Directory.GetFiles(_folder, "app.log*").Where(path => !path.EndsWith(".lock")).ToArray();
            Assert.AreEqual(4, logs.Length);
            Assert.IsTrue(logs.All(path => new FileInfo(path).Length <= RotatingLog.MaximumBytes));
            Assert.IsTrue(File.ReadAllText(LogPath).Contains("message 4"));
            Assert.IsFalse(File.Exists(LogPath + ".4"));
        }

        [TestMethod]
        public void OversizedLegacyLogPreservesItsTailWithoutKeepingAnOversizedArchive()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(LogPath, new string('x', 2 * RotatingLog.MaximumBytes) + "\nRECENT TAIL\n");
            RotatingLog.Write(LogPath, "new message");
            var logs = Directory.GetFiles(_folder, "app.log*").Where(path => !path.EndsWith(".lock")).ToArray();
            Assert.IsTrue(logs.All(path => new FileInfo(path).Length <= RotatingLog.MaximumBytes));
            Assert.IsTrue(string.Join("", logs.Select(File.ReadAllText)).Contains("RECENT TAIL"));
            Assert.IsTrue(File.ReadAllText(LogPath).Contains("new message"));
        }

        [TestMethod]
        public async Task ConcurrentWritersAndUnavailableLogNeverThrowOrExceedTheBound()
        {
            Directory.CreateDirectory(_folder);
            await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
            {
                for (var index = 0; index < 30; index++)
                {
                    RotatingLog.Write(LogPath, new string((char)('a' + worker), 8192));
                }
            })));
            using (var reader = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                RotatingLog.Write(LogPath, "temporarily unavailable");
            }
            RotatingLog.Write(LogPath, "final message");
            Assert.IsTrue(File.ReadAllText(LogPath).Contains("final message"));
            Assert.IsTrue(Directory.GetFiles(_folder, "app.log*")
                .Where(path => !path.EndsWith(".lock")).All(path => new FileInfo(path).Length <= RotatingLog.MaximumBytes));
        }

        [TestMethod]
        public void HugeUnicodeMessagesAreBounded()
        {
            RotatingLog.Write(LogPath, new string('\u4e00', RotatingLog.MaximumBytes));
            Assert.IsTrue(new FileInfo(LogPath).Length <= RotatingLog.MaximumBytes);
            Assert.IsTrue(File.ReadAllText(LogPath, Encoding.UTF8).Contains("[truncated]"));
        }

        [TestMethod]
        public void OversizedExistingArchivesAreBoundedEvenWhenTheCurrentLogIsSmall()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(LogPath, "small current log");
            File.WriteAllBytes(LogPath + ".3", new byte[2 * RotatingLog.MaximumBytes]);
            RotatingLog.Write(LogPath, "new message");
            Assert.AreEqual(RotatingLog.MaximumBytes, new FileInfo(LogPath + ".3").Length);
            Assert.IsTrue(File.ReadAllText(LogPath).Contains("new message"));
        }
    }
}
