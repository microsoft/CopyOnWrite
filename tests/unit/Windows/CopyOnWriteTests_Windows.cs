// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    [DataRow(false)]
    [DataRow(true)]
    public void NtfsNegativeDetectionAndFailureToCopyExtents(bool fullyResolvedPaths)
    {
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);
        bool anyNtfsFound = false;
        foreach (DriveInfo driveInfo in DriveInfo.GetDrives()
            .Where(di => di.IsReady))
        {
            if (string.Equals(driveInfo.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                anyNtfsFound = true;
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(
                    Path.Combine(driveInfo.Name.ToUpperInvariant(), Guid.NewGuid().ToString()),
                    Path.Combine(driveInfo.Name.ToLowerInvariant(), Guid.NewGuid().ToString()),
                    fullyResolvedPaths));
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedBetweenPaths(
                    Path.Combine(driveInfo.Name.ToLowerInvariant(), Guid.NewGuid().ToString()),
                    Path.Combine(driveInfo.Name.ToUpperInvariant(), Guid.NewGuid().ToString()),
                    fullyResolvedPaths));
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
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);
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
                cow.CloneFile(sourceFilePath, destFilePath, CloneFlags.None);
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
    [DataRow(CloneFlags.DestinationMustMatchSourceSparseness)]
    [DataRow(CloneFlags.PathIsFullyResolved)]
    public async Task ReFSPositiveDetectionAndCloneFileCorrectBehavior(CloneFlags cloneFlags)
    {
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create($"{nameof(ReFSPositiveDetectionAndCloneFileCorrectBehavior)}({(int)cloneFlags})");

        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);
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

        // Clone a file with an Alternate Data Stream.
        string adsSource = Path.Combine(refsTestRoot, "AltDataStreamSource");
        var adsSourceFI = new FileInfo(adsSource);
        await File.WriteAllTextAsync(adsSource, "aaaaa");
        await File.WriteAllTextAsync(adsSource + ":x", "bbbbbbb");
        string adsDest = Path.Combine(refsTestRoot, "AltDataStreamDestination");
        cow.CloneFile(adsSource, adsDest, cloneFlags);
        Assert.IsTrue(File.Exists(adsDest));
        var adsDestFI = new FileInfo(adsDest);
        Assert.AreEqual(adsSourceFI.Length, adsDestFI.Length);
        string adsDestContents = await File.ReadAllTextAsync(adsDest);
        Assert.AreEqual("aaaaa", adsDestContents);
        Assert.IsFalse(File.Exists(adsDest + ":x"), "Expect that the alt data stream was not cloned");

        const int ERROR_NOT_SUPPORTED = 50;
        try
        {
            cow.CloneFile(source1Path, adsDest + ":x", cloneFlags);
            Assert.Fail("Expected ERROR_NOT_SUPPORTED exception. Has cloning an alt data stream been added in newer Windows?");
        }
        catch (Win32Exception win32Ex) when (win32Ex.NativeErrorCode == ERROR_NOT_SUPPORTED)
        {
            // Expected.
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }

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
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReFSMountAndCacheClearingBehavior(bool fullyResolvedPaths)
    {
        const string testSubDir = nameof(ReFSMountAndCacheClearingBehavior);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);

        // Create a filesystem object before mounting the ReFS volume to allow cache semantics check.
        ICopyOnWriteFilesystem cowBeforeMount = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

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
                ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

                // C: drive (NTFS).
                Assert.IsFalse(cow.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\", fullyResolvedPaths));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\", fullyResolvedPaths));

                // ReFS root.
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(refs.ReFsDriveRoot, fullyResolvedPaths));
                Assert.IsTrue(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(refs.ReFsDriveRoot, fullyResolvedPaths));

                // Mount.
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(mountPath, fullyResolvedPaths));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(mountPath, fullyResolvedPaths), "Cached filesystem state should not show mount");

                // Clear pre-mount FS instance cache and verify it sees current reality.
                cowBeforeMount.ClearFilesystemCache();
                Assert.IsTrue(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(mountPath, fullyResolvedPaths));
                Assert.IsFalse(cowBeforeMount.CopyOnWriteLinkSupportedInDirectoryTree(@"C:\", fullyResolvedPaths));

                // Should be able to clone between the mount and the drive since they are the same underlying volume.
                string source1Path = Path.Combine(refs.TestRootDir, "source1");
                await File.WriteAllTextAsync(source1Path, "AABBCCDD");
                string destDir = Path.Combine(mountPath, testSubDir);
                Directory.CreateDirectory(destDir);
                string dest1Path = Path.Combine(destDir, "dest1");
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedBetweenPaths(source1Path, dest1Path, fullyResolvedPaths));

                cow.CloneFile(source1Path, dest1Path, fullyResolvedPaths ? CloneFlags.PathIsFullyResolved : CloneFlags.None);
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
    [TestCategory("Admin")]
    public void ReFSVolumeSubstDetection()
    {
        const string testSubDir = nameof(ReFSVolumeSubstDetection);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);

        // Find two open drive letters after mounting a ReFS volume.
        var openDriveLetters = new List<char>(2);
        for (char driveLetter = 'Z'; driveLetter > 'A'; driveLetter--)
        {
            if (!Directory.Exists($@"{driveLetter}:\"))
            {
                openDriveLetters.Add(driveLetter);
                if (openDriveLetters.Count == 2)
                {
                    break;
                }
            }
        }

        if (openDriveLetters.Count < 2)
        {
            Assert.Fail("Could not find two open drive letters");
        }

        ICopyOnWriteFilesystem cowBeforeSubst = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

        foreach (char openDriveLetter in openDriveLetters)
        {
            try
            {
                cowBeforeSubst.CopyOnWriteLinkSupportedInDirectoryTree($@"{openDriveLetter}:\", pathIsFullyResolved: true);
                Assert.Fail("Expected ArgumentException for unknown drive root");
            }
            catch (ArgumentException)
            {
                // Expected.
            }
        }

        // SUBST the open drive letters to the ReFS volume.
        foreach (char openDriveLetter in openDriveLetters)
        {
            bool success = NativeMethods.DefineDosDevice(0, $"{openDriveLetter}:", refs.ReFsDriveRoot.Substring(0, 2));
            if (!success)
            {
                Assert.Fail($"Failed to SUBST {openDriveLetter}: to {refs.ReFsDriveRoot} with error {Marshal.GetLastWin32Error()}");
            }
        }

        try
        {
            ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

            foreach (char openDriveLetter in openDriveLetters)
            {
                string openDriveRoot = $@"{openDriveLetter}:\";
                Assert.IsTrue(cow.CopyOnWriteLinkSupportedInDirectoryTree(openDriveRoot, pathIsFullyResolved: true), openDriveRoot);
            }
        }
        finally
        {
            foreach (char openDriveLetter in openDriveLetters)
            {
                bool success = NativeMethods.DefineDosDevice(NativeMethods.DDD_REMOVE_DEFINITION, $"{openDriveLetter}:", null);
                if (!success)
                {
                    Assert.Fail($"Failed to remove SUBST on {openDriveLetter}: with error {Marshal.GetLastWin32Error()}");
                }
            }
        }
    }

    [TestMethod]
    public void UncPathsNotLinkable()
    {
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);
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

    [TestMethod]
    public async Task VerifyMatchingSparseness()
    {
        const string testSubDir = nameof(VerifyMatchingSparseness);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

        string sparseFile = Path.Combine(refs.TestRootDir, "sparseFile");
        await File.WriteAllTextAsync(sparseFile, "AABBCCDD");
        ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"sparse setFlag {sparseFile}");
        var fsutilResult = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"sparse queryFlag {sparseFile}");
        Assert.AreEqual("This file is set as sparse", fsutilResult.Output.Trim());
        string sparseFileClone = Path.Combine(refs.TestRootDir, "sparseFileClone");

        string nonSparseFile = Path.Combine(refs.TestRootDir, "nonSparseFile");
        await File.WriteAllTextAsync(nonSparseFile, "AABBCCDD");
        fsutilResult = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"sparse queryFlag {nonSparseFile}");
        Assert.AreEqual("This file is NOT set as sparse", fsutilResult.Output.Trim());
        string nonSparseFileClone = Path.Combine(refs.TestRootDir, "nonSparseFileClone");

        cow.CloneFile(sparseFile, sparseFileClone, CloneFlags.DestinationMustMatchSourceSparseness);
        fsutilResult = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"sparse queryFlag {sparseFileClone}");
        Assert.AreEqual("This file is set as sparse", fsutilResult.Output.Trim());

        cow.CloneFile(nonSparseFile, nonSparseFileClone, CloneFlags.DestinationMustMatchSourceSparseness);
        fsutilResult = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"sparse queryFlag {nonSparseFileClone}");
        Assert.AreEqual("This file is NOT set as sparse", fsutilResult.Output.Trim());
    }

    [TestMethod]
    public async Task StressAddRemoveRefs_SingleThreaded()
    {
        const string testSubDir = nameof(StressAddRemoveRefs_SingleThreaded);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

        string originalFile = Path.Combine(refs.TestRootDir, "originalFile");
        await File.WriteAllTextAsync(originalFile, "AABBCCDD");
        ProcessRunResult originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
        Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");

        for (int i = 0; i < 400 /*cow.MaxClonesPerFile * 10*/; i++)
        {
            string clone = Path.Combine(refs.TestRootDir, $"clone{i}");
            cow.CloneFile(originalFile, clone);
            originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
            // Cannot assert exact refcount on logical cluster - the Dev Drive/ReFS logical cluster refcount has delayed accounting.
            Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");

            Console.WriteLine($"Deleting clone {clone}");
            File.Delete(clone);
            originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
            // Cannot assert exact refcount on logical cluster - the Dev Drive/ReFS logical cluster refcount has delayed accounting.
            Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");
        }

        await WaitForRefAccountingAsync(originalFile);
    }

    [TestMethod]
    public async Task StressAddRemoveRefs_ParallelClonesDeletes()
    {
        const string testSubDir = nameof(StressAddRemoveRefs_ParallelClonesDeletes);
        using WindowsReFsDriveSession refs = WindowsReFsDriveSession.Create(testSubDir);
        ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

        string originalFile = Path.Combine(refs.TestRootDir, "originalFile");
        await File.WriteAllTextAsync(originalFile, "AABBCCDD");
        ProcessRunResult originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
        Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");

        const int numClones = 40;
        var clonePaths = new string[numClones];

        for (int iterations = 0; iterations < 20; iterations++)
        {
            Console.WriteLine($"Parallel creating {numClones} clones");
            Parallel.For(0, numClones, i =>
            {
                clonePaths[i] = Path.Combine(refs.TestRootDir, $"clone{i}");
                cow.CloneFile(originalFile, clonePaths[i]);
            });

            originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
            // Cannot assert exact refcount on logical cluster - the Dev Drive/ReFS logical cluster refcount has delayed accounting.
            Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");

            Console.WriteLine($"Parallel deleting {numClones} clones");
            Parallel.ForEach(clonePaths, File.Delete);

            originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
            // Cannot assert exact refcount on logical cluster - the Dev Drive/ReFS logical cluster refcount has delayed accounting.
            Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");
        }

        await WaitForRefAccountingAsync(originalFile);
    }

    private static async Task WaitForRefAccountingAsync(string originalFile)
    {
        Console.WriteLine("Waiting for delayed ref accounting on original file to reset to 1 ref");

        // Wait a long time but not infinitely. Typical accounting delay is a few seconds.
        bool accountingCorrect = false;
        for (int i = 0; i < 1000; i++)
        {
            ProcessRunResult originalQueryExtents = ProcessExecutionUtilities.RunAndCaptureOutput("fsutil", $"file queryExtentsAndRefCounts {originalFile}");
            Console.WriteLine($"{originalFile} : {originalQueryExtents.Output}");
            if (originalQueryExtents.Output.Trim().EndsWith("Ref: 0x1", StringComparison.Ordinal))
            {
                accountingCorrect = true;
                break;
            }

            await Task.Delay(250);
        }

        Console.WriteLine();
        if (!accountingCorrect)
        {
            Assert.Fail("Ref accounting did not reset after some time");
        }
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
            Stopwatch sw = Stopwatch.StartNew();
            int numClones = cow.MaxClonesPerFile;

            for (int file = 1; file <= numFiles; file++)
            {
                string stressFolder = Path.Combine(refsRoot, $"Stress{file}_par{parallelism}");
                Directory.CreateDirectory(stressFolder);
                string origFilePath = Path.Combine(stressFolder, "orig");
                string content = $"1234abcd_{file}";
                await File.WriteAllTextAsync(origFilePath, content);

                Parallel.For(0, numClones, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
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

            Console.WriteLine($"Stress: Parallelism {parallelism}, files {numFiles}, clones per file {numClones}: {sw.Elapsed.TotalMilliseconds:F3}ms");
        }
    }
}
