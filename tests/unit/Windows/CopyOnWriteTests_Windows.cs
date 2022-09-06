// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CopyOnWrite.TestUtilities;
using Microsoft.CopyOnWrite.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CopyOnWrite.Tests.Windows;

// E.g. dotnet test --filter TestCategory=Windows
[TestClass]
[TestCategory("Windows")]
[DoNotParallelize]  // Ensure the 32-bit and 64-bit suites do not collide.
public sealed class CopyOnWriteTests_Windows
{
    [TestMethod]
    public void NtfsNegativeDetectionAndFailureToCopyExtents()
    {
        var cow = new WindowsCopyOnWriteFilesystem();
        bool anyNtfsFound = false;
        foreach (DriveInfo driveInfo in DriveInfo.GetDrives()
            .Where(di => di.IsReady))
        {
            if (string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                anyNtfsFound = true;
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(
                    Path.Combine(driveInfo.Name.ToUpperInvariant(), Guid.NewGuid().ToString()),
                    Path.Combine(driveInfo.Name.ToLowerInvariant(), Guid.NewGuid().ToString())));
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(
                    Path.Combine(driveInfo.Name.ToLowerInvariant(), Guid.NewGuid().ToString()),
                    Path.Combine(driveInfo.Name.ToUpperInvariant(), Guid.NewGuid().ToString())));
            }
        }

        if (!anyNtfsFound)
        {
            Assert.Inconclusive("No NTFS drives found");
        }
    }

    [TestMethod]
    public async Task TempDirValidateCloneFileFailureIfNtfs()
    {
        var cow = new WindowsCopyOnWriteFilesystem();
        using var tempDir = new DisposableTempDirectory();
        var tempDriveInfo = new DriveInfo(tempDir.Path.Substring(0, 1));
        if (string.Equals(tempDriveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            string sourceFilePath = Path.Combine(tempDir.Path, "Source1");
            await File.WriteAllTextAsync(sourceFilePath, "ABCDEF");
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

            try
            {
                await cow.CloneFileAsync(sourceFilePath, destFilePath, CloneFlags.None, CancellationToken.None);
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
    [DataRow(CloneFlags.None)]
    [DataRow(CloneFlags.NoFileIntegrityCheck)]
    [DataRow(CloneFlags.NoSparseFileCheck)]
    [DataRow(CloneFlags.NoFileIntegrityCheck | CloneFlags.NoSparseFileCheck)]
    [DataRow(CloneFlags.NoSerializedCloning)]
    public async Task ReFSPositiveDetectionAndCloneFileCorrectBehavior(CloneFlags cloneFlags)
    {
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create($"{nameof(ReFSPositiveDetectionAndCloneFileCorrectBehavior)}({(int)cloneFlags})");

        var cow = new WindowsCopyOnWriteFilesystem();
        string refsTestRoot = refs.TestRootDir;

        Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(refsTestRoot));

        string source1Path = Path.Combine(refsTestRoot, "source1");
        await File.WriteAllTextAsync(source1Path, "AABBCCDD");
        string dest1Path = Path.Combine(refsTestRoot, "dest1");

        Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, dest1Path));
        Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path.ToLowerInvariant(), dest1Path.ToUpperInvariant()));
        Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path.ToUpperInvariant(), dest1Path.ToLowerInvariant()));

        string differentVolumePath = Path.Combine(Environment.CurrentDirectory, "file000");
        Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(differentVolumePath, dest1Path), "Cross-volume CoW should not be allowed");
        Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, differentVolumePath), "Cross-volume CoW should not be allowed");

        cow.CloneFile(source1Path, dest1Path, cloneFlags);
        Assert.IsTrue(File.Exists(dest1Path));
        var source1FI = new FileInfo(source1Path);
        Console.WriteLine($"source1 size {source1FI.Length}");
        var dest1FI = new FileInfo(dest1Path);
        Assert.AreEqual(source1FI.Length, dest1FI.Length);
        string dest1Contents = await File.ReadAllTextAsync(dest1Path);
        Assert.AreEqual("AABBCCDD", dest1Contents);

        // Clone a clone.
        string dest2Path = Path.Combine(refsTestRoot, "dest2");
        cow.CloneFile(dest1Path, dest2Path, cloneFlags);
        Assert.IsTrue(File.Exists(dest2Path));
        var dest2FI = new FileInfo(dest2Path);
        Assert.AreEqual(source1FI.Length, dest2FI.Length);
        string dest2Contents = await File.ReadAllTextAsync(dest2Path);
        Assert.AreEqual("AABBCCDD", dest2Contents);

        // TODO: Clone a hardlink and symlink.

        // Delete original file, ensure clones remain materialized.
        File.Delete(source1Path);
        Assert.IsTrue(File.Exists(dest1Path));
        Assert.IsTrue(File.Exists(dest2Path));
        dest1Contents = await File.ReadAllTextAsync(dest1Path);
        Assert.AreEqual("AABBCCDD", dest1Contents);
        dest2Contents = await File.ReadAllTextAsync(dest2Path);
        Assert.AreEqual("AABBCCDD", dest2Contents);

        // Create and clone a large file onto previously created clones.
        string largeSourcePath = Path.Combine(refsTestRoot, "largeFile");
        const long largeSourceSize = WindowsCopyOnWriteFilesystem.MaxChunkSize + 1024L;  // A bit above limit to force multiple chunk copies.
        Console.WriteLine($"Creating file with size {largeSourceSize}");
        await using (FileStream s = File.OpenWrite(largeSourcePath))
        {
            s.SetLength(largeSourceSize);
            s.Write(new byte[] { 0x01, 0x02, 0x03, 0x04 });
            s.Seek(largeSourceSize - 4, SeekOrigin.Begin);
            s.Write(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD});
        }

        cow.CloneFile(largeSourcePath, dest1Path, cloneFlags);
        Assert.IsTrue(File.Exists(dest1Path));
        dest1FI = new FileInfo(dest1Path);
        Assert.AreEqual(largeSourceSize, dest1FI.Length);
        await using (FileStream s = File.OpenRead(dest1Path))
        {
            var buffer = new byte[4];
            int bytesRead = s.Read(buffer, 0, buffer.Length);
            Assert.AreEqual(buffer.Length, bytesRead);
            Assert.AreEqual(0x01, buffer[0]);
            Assert.AreEqual(0x02, buffer[1]);
            Assert.AreEqual(0x03, buffer[2]);
            Assert.AreEqual(0x04, buffer[3]);

            s.Seek(largeSourceSize - 4, SeekOrigin.Begin);
            bytesRead = s.Read(buffer, 0, buffer.Length);
            Assert.AreEqual(buffer.Length, bytesRead);
            Assert.AreEqual(0xAA, buffer[0]);
            Assert.AreEqual(0xBB, buffer[1]);
            Assert.AreEqual(0xCC, buffer[2]);
            Assert.AreEqual(0xDD, buffer[3]);

            // Other tests.
            CloneFileDestinationIsDir(refsTestRoot, cloneFlags);
            CloneFileMissingSourceDir(refsTestRoot, cloneFlags);
            CloneFileMissingSourceFileInExistingDir(refsTestRoot, cloneFlags);
            CloneFileSourceIsDir(refsTestRoot, cloneFlags);
            await CloneFileExceedReFsLimitAsync(refsTestRoot, cloneFlags);
            await StressTestCloningAsync(refsTestRoot, cloneFlags);
        }
    }

    [TestMethod]
    [TestCategory("Admin")]
    public async Task ReFSMountAndCacheClearingBehavior()
    {
        const string testSubDir = nameof(ReFSMountAndCacheClearingBehavior);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);

        // Create a filesystem object before mounting the ReFS volume to allow cache semantics check.
        var cowBeforeMount = new WindowsCopyOnWriteFilesystem();

        // Mount the ReFS volume under the C: drive before creating a filesystem object that will read current filesystem state.
        const string cDriveBaseTestDir = @"C:\CoWMountTest";
        Directory.CreateDirectory(cDriveBaseTestDir);
        try
        {
            using var cTestDir = new DisposableTempDirectory(cDriveBaseTestDir);
            string mountPath = Path.Combine(cTestDir.Path, "mount");
            Directory.CreateDirectory(mountPath);
            string diskPartScriptPath = Path.Combine(cTestDir.Path, "DiskPartScript.txt");
            await File.WriteAllTextAsync(diskPartScriptPath,
                $"SELECT VOLUME {refs.ReFsDriveRoot}" + Environment.NewLine +
                $"ASSIGN MOUNT={mountPath}");
            ProcessExecutionUtilities.RunAndCaptureOutput("diskpart", $"/s {diskPartScriptPath}");
            try
            {
                var cow = new WindowsCopyOnWriteFilesystem();

                // C: drive (NTFS).
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\"));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\"));

                // ReFS root.
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(refs.ReFsDriveRoot));
                Assert.IsTrue(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(refs.ReFsDriveRoot));

                // Mount.
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(mountPath));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(mountPath), "Cached filesystem state should not show mount");

                // Clear pre-mount FS instance cache and verify it sees current reality.
                cowBeforeMount.ClearFilesystemCache();
                Assert.IsTrue(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(mountPath));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\"));

                // Should be able to clone between the mount and the drive since they are the same underlying volume.
                string source1Path = Path.Combine(refs.TestRootDir, "source1");
                await File.WriteAllTextAsync(source1Path, "AABBCCDD");
                string destDir = Path.Combine(mountPath, testSubDir);
                Directory.CreateDirectory(destDir);
                string dest1Path = Path.Combine(destDir, "dest1");
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, dest1Path));

                cow.CloneFile(source1Path, dest1Path, CloneFlags.None);
                Assert.IsTrue(File.Exists(dest1Path));
                var source1FI = new FileInfo(source1Path);
                Console.WriteLine($"source1 size {source1FI.Length}");
                var dest1FI = new FileInfo(dest1Path);
                Assert.AreEqual(source1FI.Length, dest1FI.Length);
                string dest1Contents = await File.ReadAllTextAsync(dest1Path);
                Assert.AreEqual("AABBCCDD", dest1Contents);
            }
            finally
            {
                await File.WriteAllTextAsync(diskPartScriptPath,
                    $"SELECT VOLUME {refs.ReFsDriveRoot}" + Environment.NewLine +
                    $"REMOVE MOUNT={mountPath}");
                ProcessExecutionUtilities.RunAndCaptureOutput("diskpart", $"/s {diskPartScriptPath}");
            }
        }
        finally
        {
            Directory.Delete(cDriveBaseTestDir, recursive: true);
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

    private static void CloneFileMissingSourceFileInExistingDir(string refsRoot, CloneFlags cloneFlags)
    {
        string sourceFilePath = Path.Combine(refsRoot, "source_" + nameof(CloneFileMissingSourceFileInExistingDir));
        string destFilePath = Path.Combine(refsRoot, nameof(CloneFileMissingSourceFileInExistingDir));
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
        Assert.ThrowsException<FileNotFoundException>(() => cow.CloneFile(sourceFilePath, destFilePath, cloneFlags));
    }

    private static void CloneFileMissingSourceDir(string refsRoot, CloneFlags cloneFlags)
    {
        string sourceFilePath = Path.Combine(refsRoot, "missingDir", "source_" + nameof(CloneFileMissingSourceDir));
        string destFilePath = Path.Combine(refsRoot, nameof(CloneFileMissingSourceDir));
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
        Assert.ThrowsException<DirectoryNotFoundException>(() => cow.CloneFile(sourceFilePath, destFilePath, cloneFlags));
    }

    private static void CloneFileSourceIsDir(string refsRoot, CloneFlags cloneFlags)
    {
        string destFilePath = Path.Combine(refsRoot, nameof(CloneFileSourceIsDir));
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
        Assert.ThrowsException<UnauthorizedAccessException>(() => cow.CloneFile(refsRoot, destFilePath, cloneFlags));
    }

    private static void CloneFileDestinationIsDir(string refsRoot, CloneFlags cloneFlags)
    {
        string sourceFilePath = Path.Combine(refsRoot, "source_" + nameof(CloneFileDestinationIsDir));
        File.WriteAllText(sourceFilePath, "ABC");
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
        Assert.ThrowsException<UnauthorizedAccessException>(() => cow.CloneFile(sourceFilePath, refsRoot, cloneFlags));
    }

    private static async Task CloneFileExceedReFsLimitAsync(string refsRoot, CloneFlags cloneFlags)
    {
        string testSubDir = Path.Combine(refsRoot, nameof(CloneFileExceedReFsLimitAsync));
        Directory.CreateDirectory(testSubDir);
        string sourceFilePath = Path.Combine(testSubDir, "source");
        await File.WriteAllTextAsync(sourceFilePath, "ABC");
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
        for (int i = 0; i < cow.MaxClonesPerFile; i++)
        {
            // Up to limit expected to succeed.
            string testPath = Path.Combine(testSubDir, $"dest{i}");
            cow.CloneFile(sourceFilePath, testPath, cloneFlags);
            Assert.AreEqual("ABC", await File.ReadAllTextAsync(testPath), testPath);
        }

        // Windows appears to be lazy in its accounting for clones: Sometimes we get unexpected success adding an additional clone.
        // Try a few times to exceed the limit.
        int iter = cow.MaxClonesPerFile;
        Assert.ThrowsException<MaxCloneFileLinksExceededException>(() =>
        {
            while (true)
            {
                cow.CloneFile(sourceFilePath, Path.Combine(testSubDir, $"dest{iter}"), cloneFlags);
                iter++;
            }
        });
    }

    private static async Task StressTestCloningAsync(string refsRoot, CloneFlags cloneFlags)
    {
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();

        int[] parallelismSettings = { 1, 2, Environment.ProcessorCount, 2 * Environment.ProcessorCount };

        const int numFiles = 3;
        foreach (int parallelism in parallelismSettings)
        {
            for (int file = 1; file <= numFiles; file++)
            {
                string stressFolder = Path.Combine(refsRoot, $"Stress{file}_par{parallelism}");
                Directory.CreateDirectory(stressFolder);
                string origFilePath = Path.Combine(stressFolder, "orig");
                string content = $"1234abcd_{file}";
                await File.WriteAllTextAsync(origFilePath, content);

                try
                {
                    Parallel.For(0, cow.MaxClonesPerFile, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
                    {
                        string testPath = Path.Combine(stressFolder, $"test{i}_par{parallelism}_cloneflags{(int)cloneFlags}");
                        try
                        {
                            cow.CloneFile(origFilePath, testPath, cloneFlags);
                        }
                        catch (Exception ex)
                        {
                            throw new AssertFailedException($"{testPath} file {i} parallelism {parallelism} CloneFlags {cloneFlags}", ex);
                        }
                        Assert.AreEqual(content, File.ReadAllText(testPath), "{0} file {1} parallelism {2} CloneFlags {3}", testPath, i, parallelism, cloneFlags);
                    });
                }
                catch (AggregateException aggEx) when (cloneFlags.HasFlag(CloneFlags.NoSerializedCloning) && parallelism > 1)
                {
                    throw new AssertInconclusiveException("Windows CoW: Expected instability when disabling serialization on multithreaded tests", aggEx);
                }
            }
        }
    }
}
