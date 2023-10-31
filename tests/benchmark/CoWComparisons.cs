// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.CopyOnWrite.TestUtilities;

namespace Microsoft.CopyOnWrite.Benchmarking;

public class CoWComparisons
{
    /// <summary>
    /// A variation dimension for testing - ReFS/Dev Drive tracks extents and shortcuts copies when only
    /// extents are set and not data written to a region.
    /// </summary>
    public enum FileData
    {
        /// <summary>
        /// The file was extended by setting its size but no data was written.
        /// </summary>
        ExtentsOnly,

        /// <summary>
        /// Data was written to the file.
        /// </summary>
        WroteData,
    }

    private readonly record struct FileKey(long FileSize, FileData FileContents);

    // Do more work per round to meet minimum BenchmarkDotNet times.
    private const int CopiesPerJob = 50;

    // Static to avoid multiple drives being created during benchmark and to allow control
    // of call to Dispose().
    public static WindowsReFsDriveSession? ReFsVhdSession;

    public long[] FileSizesBytes { get; } =
    {
        0,
        1,
        1024,
        16 * 1024,
        256 * 1024,
        1024 * 1024,
        16 * 1024 * 1024,
    };

    public FileData[] FileDataVariations { get; } =
    {
        FileData.ExtentsOnly,
        FileData.WroteData,
    };

    private static readonly string[] FileNames = CreateFileNames();

    private static string[] CreateFileNames()
    {
        var names = new string[CopiesPerJob];
        for (int i = 0; i < CopiesPerJob; i++)
        {
            names[i] = $"dest{i}";
        }

        return names;
    }
        
    private ICopyOnWriteFilesystem? _cow;
    private string? _testOutputDir;
    private readonly Dictionary<FileKey, string> _sourcePathMap = new ();

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Default assumes you have the CoW_Test_ReFS_Drive env var set globally to point to the root of a ReFS drive.
        // Set to true to test VHD case when you have that env var set.
        // VHD test runs need to run under an elevated console.
        const bool forceVhd = false;
        
        string testRootDir;
        if (OsHelper.IsWindows)
        {
            ReFsVhdSession = WindowsReFsDriveSession.Create("CowBenchmarks", forceVhd);
            testRootDir = ReFsVhdSession.TestRootDir;
        }
        else
        {
            // TODO: Other OSes.
            testRootDir = Environment.CurrentDirectory;
        }

        // Must create a new CoW object because the filesystem layout may have changed if a VHD was added.
        _cow = CopyOnWriteFilesystemFactory.GetInstance(forceUniqueInstance: true);

        _testOutputDir = Path.Combine(testRootDir, "Output");
        Directory.CreateDirectory(_testOutputDir);

        var data = new byte[4096];
        Span<byte> dataSpan = data.AsSpan();
        var rnd = new Random();
        foreach (long fileSize in FileSizesBytes)
        {
            foreach (FileData fileData in FileDataVariations)
            {
                string p = Path.Combine(testRootDir, $"File{fileSize}_{fileData}");
                using FileStream s = File.Create(p);
                s.SetLength(fileSize);
                if (fileData == FileData.WroteData)
                {
                    long remaining = fileSize;
                    while (remaining > 0)
                    {
                        rnd.NextBytes(dataSpan);
                        int toWrite = (int)Math.Min(remaining, data.Length);
                        s.Write(data.AsSpan(0, toWrite));
                        remaining -= toWrite;
                    }
                }

                _sourcePathMap[new FileKey(fileSize, fileData)] = p;
            }
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        ReFsVhdSession?.Dispose();
    }

    [ParamsSource(nameof(FileSizesBytes))]
    public long FileSize;

    [ParamsSource(nameof(FileDataVariations))]
    public FileData FileContents;

    [IterationCleanup]
    public void IterationCleanup()
    {
        string[] toDelete = Directory.GetFiles(_testOutputDir!);
        foreach (string f in toDelete)
        {
            File.Delete(f);
        }
    }

    [Benchmark(Baseline = true, Description = "File.Copy", OperationsPerInvoke = CopiesPerJob)]
    [EvaluateOverhead(false)]  // Causes too-many-link exceptions.
    public void CopyFileNoExistingTarget()
    {
        string sourceFilePath = _sourcePathMap[new FileKey(FileSize, FileContents)];
        for (int i = 0; i < CopiesPerJob; i++)
        {
            string targetFilePath = Path.Combine(_testOutputDir!, FileNames[i]);
            File.Copy(sourceFilePath, targetFilePath);
        }
    }

    [Benchmark(Description = "CoW", OperationsPerInvoke = CopiesPerJob)]
    [EvaluateOverhead(false)]  // Causes too-many-link exceptions.
    public void CowFileNoExistingTarget()
    {
        string sourceFilePath = _sourcePathMap[new FileKey(FileSize, FileContents)];
        for (int i = 0; i < CopiesPerJob; i++)
        {
            string targetFilePath = Path.Combine(_testOutputDir!, FileNames[i]);

            const CloneFlags highestPerfCloneFlags =
                CloneFlags.NoFileIntegrityCheck |
                CloneFlags.PathIsFullyResolved;

            _cow!.CloneFile(sourceFilePath, targetFilePath, highestPerfCloneFlags);
        }
    }
}
