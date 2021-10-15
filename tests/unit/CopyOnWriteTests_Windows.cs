// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CopyOnWrite.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CopyOnWrite.Tests
{
    // E.g. dotnet test --filter TestCategory=Windows
    [TestClass]
    [TestCategory("Windows")]
    public sealed class CopyOnWriteTests_Windows
    {
        [TestMethod]
        public void NtfsNegativeDetectionAndFailureToCopyExtents()
        {
            var cow = new WindowsCopyOnWriteFilesystem();
            bool anyNtfsFound = false;
            foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
            {
                if (string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    anyNtfsFound = true;
                    Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(
                        Path.Combine(driveInfo.Name, Guid.NewGuid().ToString()),
                        Path.Combine(driveInfo.Name, Guid.NewGuid().ToString())));
                }
            }

            if (!anyNtfsFound)
            {
                Assert.Inconclusive("No NTFS drives found");
            }
        }

        [TestMethod]
        public void TempDirValidateCloneFileFailureIfNtfs()
        {
            var cow = new WindowsCopyOnWriteFilesystem();
            using var tempDir = new DisposableTempDirectory();
            var tempDriveInfo = new DriveInfo(tempDir.Path.Substring(0, 1));
            if (string.Equals(tempDriveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                string sourceFilePath = Path.Combine(tempDir.Path, "Source1");
                File.WriteAllText(sourceFilePath, "ABCDEF");
                string destFilePath = Path.Combine(tempDir.Path, "Dest1");
                try
                {
                    cow.CloneFile(sourceFilePath, destFilePath);
                    Assert.Fail("Expected exception on NTFS");
                }
                catch (NotSupportedException ex)
                {
                    Assert.IsTrue(ex.Message.Contains("is not formatted with ReFS", StringComparison.Ordinal));
                }
            }
            else
            {
                Assert.Inconclusive("%TEMP% is on a non-NTFS drive");
            }
        }

        [TestMethod]
        [TestCategory("Admin")]
        public void ReFSPositiveDetectionAndCloneFileCorrectBehavior()
        {
            using WindowsReFsVhdSession refs = WindowsReFsVhdSession.Create();

            var cow = new WindowsCopyOnWriteFilesystem();
            string refsDriveRoot = refs.ReFsDriveRoot;

            Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(refsDriveRoot));

            string source1Path = Path.Combine(refsDriveRoot, "source1");
            File.WriteAllText(source1Path, "AABBCCDD");
            string dest1Path = Path.Combine(refsDriveRoot, "dest1");

            Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, dest1Path));

            string differentVolumePath = Path.Combine(Environment.CurrentDirectory, "file000");
            Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(differentVolumePath, dest1Path), "Cross-volume CoW should not be allowed");
            Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, differentVolumePath), "Cross-volume CoW should not be allowed");

            cow.CloneFile(source1Path, dest1Path);
            Assert.IsTrue(File.Exists(dest1Path));
            var source1FI = new FileInfo(source1Path);
            var dest1FI = new FileInfo(dest1Path);
            Assert.AreEqual(source1FI.Length, dest1FI.Length);
            string dest1Contents = File.ReadAllText(dest1Path);
            Assert.AreEqual("AABBCCDD", dest1Contents);

            // Clone a clone.
            string dest2Path = Path.Combine(refsDriveRoot, "dest2");
            cow.CloneFile(dest1Path, dest2Path);
            Assert.IsTrue(File.Exists(dest2Path));
            var dest2FI = new FileInfo(dest2Path);
            Assert.AreEqual(source1FI.Length, dest2FI.Length);
            string dest2Contents = File.ReadAllText(dest2Path);
            Assert.AreEqual("AABBCCDD", dest2Contents);

            // TODO: Clone a hardlink and symlink.

            // Delete original file, ensure clones remain materialized.
            File.Delete(source1Path);
            Assert.IsTrue(File.Exists(dest1Path));
            Assert.IsTrue(File.Exists(dest2Path));
            dest1Contents = File.ReadAllText(dest1Path);
            Assert.AreEqual("AABBCCDD", dest1Contents);
            dest2Contents = File.ReadAllText(dest2Path);
            Assert.AreEqual("AABBCCDD", dest2Contents);

            // Create and clone a large file onto previously created clones.
            string largeSourcePath = Path.Combine(refsDriveRoot, "largeFile");
            const long largeSourceSize = WindowsCopyOnWriteFilesystem.MaxChunkSize + 1024L;  // A bit above limit to force multiple chunk copies.
            Console.WriteLine($"Creating file with size {largeSourceSize}");
            using (FileStream s = File.OpenWrite(largeSourcePath))
            {
                s.SetLength(largeSourceSize);
                s.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });
                s.Seek(largeSourceSize - 4, SeekOrigin.Begin);
                s.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD});
            }

            cow.CloneFile(largeSourcePath, dest1Path);
            Assert.IsTrue(File.Exists(dest1Path));
            dest1FI = new FileInfo(dest1Path);
            Assert.AreEqual(largeSourceSize, dest1FI.Length);
            using (FileStream s = File.OpenRead(dest1Path))
            {
                var buffer = new byte[4];
                s.Read(buffer, 0, buffer.Length);
                Assert.AreEqual(0x01, buffer[0]);
                Assert.AreEqual(0x02, buffer[1]);
                Assert.AreEqual(0x03, buffer[2]);
                Assert.AreEqual(0x04, buffer[3]);

                s.Seek(largeSourceSize - 4, SeekOrigin.Begin);
                s.Read(buffer, 0, buffer.Length);
                Assert.AreEqual(0xAA, buffer[0]);
                Assert.AreEqual(0xBB, buffer[1]);
                Assert.AreEqual(0xCC, buffer[2]);
                Assert.AreEqual(0xDD, buffer[3]);

                // Other tests.
                CloneFileDestinationIsDir(refsDriveRoot);
                CloneFileMissingSourceDir(refsDriveRoot);
                CloneFileMissingSourceFileInExistingDir(refsDriveRoot);
                CloneFileSourceIsDir(refsDriveRoot);
                CloneFileExceedReFsLimit(refsDriveRoot);
                StressTestCloning(refsDriveRoot);
            }
        }

        [TestMethod]
        public void UncPathsNotLinkable()
        {
            var cow = new WindowsCopyOnWriteFilesystem();
            const string uncSourcePath = @"\\someMachine\someShare";
            Assert.IsFalse(
                cow.CopyOnWriteLinkSupportedBetweenPaths(uncSourcePath, @"\\otherMachine\otherShare"));
            Assert.IsFalse(cow.CopyOnWriteLinkSupportedInDirectoryTree(uncSourcePath));
        }

        [TestMethod]
        public void TestRoundUpToPowerOf2()
        {
            Assert.AreEqual(0L, WindowsCopyOnWriteFilesystem.RoundUpToPowerOf2(0, 4096));
            Assert.AreEqual(4096L, WindowsCopyOnWriteFilesystem.RoundUpToPowerOf2(1, 4096));
            Assert.AreEqual(4096L, WindowsCopyOnWriteFilesystem.RoundUpToPowerOf2(4096, 4096));
            Assert.AreEqual(8192L, WindowsCopyOnWriteFilesystem.RoundUpToPowerOf2(4097, 4096));
        }

        private static void CloneFileMissingSourceFileInExistingDir(string refsRoot)
        {
            string sourceFilePath = Path.Combine(refsRoot, "source_" + nameof(CloneFileMissingSourceFileInExistingDir));
            string destFilePath = Path.Combine(refsRoot, nameof(CloneFileMissingSourceFileInExistingDir));
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            Assert.ThrowsException<FileNotFoundException>(() => cow.CloneFile(sourceFilePath, destFilePath));
        }

        private static void CloneFileMissingSourceDir(string refsRoot)
        {
            string sourceFilePath = Path.Combine(refsRoot, "missingDir", "source_" + nameof(CloneFileMissingSourceDir));
            string destFilePath = Path.Combine(refsRoot, nameof(CloneFileMissingSourceDir));
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            Assert.ThrowsException<DirectoryNotFoundException>(() => cow.CloneFile(sourceFilePath, destFilePath));
        }

        private static void CloneFileSourceIsDir(string refsRoot)
        {
            string destFilePath = Path.Combine(refsRoot, nameof(CloneFileSourceIsDir));
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            Assert.ThrowsException<UnauthorizedAccessException>(() => cow.CloneFile(refsRoot, destFilePath));
        }

        private static void CloneFileDestinationIsDir(string refsRoot)
        {
            string sourceFilePath = Path.Combine(refsRoot, "source_" + nameof(CloneFileDestinationIsDir));
            File.WriteAllText(sourceFilePath, "ABC");
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            Assert.ThrowsException<UnauthorizedAccessException>(() => cow.CloneFile(sourceFilePath, refsRoot));
        }

        private static void CloneFileExceedReFsLimit(string refsRoot)
        {
            string testSubDir = Path.Combine(refsRoot, nameof(CloneFileExceedReFsLimit));
            Directory.CreateDirectory(testSubDir);
            string sourceFilePath = Path.Combine(testSubDir, "source");
            File.WriteAllText(sourceFilePath, "ABC");
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
            for (int i = 0; i < cow.MaxClones; i++)
            {
                // Up to limit expected to succeed.
                cow.CloneFile(sourceFilePath, Path.Combine(testSubDir, $"dest{i}"));
            }

            Assert.ThrowsException<MaxCloneFileLinksExceededException>(() => cow.CloneFile(sourceFilePath, Path.Combine(testSubDir, $"dest{cow.MaxClones}")));
        }

        private static void StressTestCloning(string refsRoot)
        {
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();

            for (int round = 0; round < 3; round++)
            {
                string stressFolder = Path.Combine(refsRoot, $"Stress{round}");
                Directory.CreateDirectory(stressFolder);
                string origFilePath = Path.Combine(stressFolder, "orig");
                File.WriteAllText(origFilePath, "1234abcd");

                Console.WriteLine($"Running parallel stress test for {cow.MaxClones} CoW links");
                Parallel.For(0, cow.MaxClones, i =>
                {
                    string testPath = Path.Combine(stressFolder, $"test{i}");
                    cow.CloneFile(origFilePath, testPath);
                    Assert.AreEqual("1234abcd", File.ReadAllText(testPath), testPath);
                });
            }
        }
    }
}
