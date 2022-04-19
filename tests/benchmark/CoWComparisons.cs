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
    // Do more work per round to meet minimum BenchmarkDotNet times.
    private const int CopiesPerJob = 25;

    // Static to avoid multiple drives being created during benchmark and to allow control
    // of call to Dispose().
    public static WindowsReFsVhdSession? ReFsVhdSession;

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
        
    private readonly ICopyOnWriteFilesystem _cow = CopyOnWriteFilesystemFactory.GetInstance();
    private string? _testOutputDir;
    private readonly Dictionary<long, string> _fileSizeToSourcePathMap = new ();

    [GlobalSetup]
    public void GlobalSetup()
    {
        string testRootDir;
        if (OsHelper.IsWindows)
        {
            ReFsVhdSession = WindowsReFsVhdSession.Create();
            testRootDir = ReFsVhdSession.ReFsDriveRoot;
        }
        else
        {
            // TODO: Other OSes.
            testRootDir = Environment.CurrentDirectory;
        }

        _testOutputDir = Path.Combine(testRootDir, "Output");
        Directory.CreateDirectory(_testOutputDir);

        foreach (long fileSize in FileSizesBytes)
        {
            string p = Path.Combine(testRootDir, $"File{fileSize}");
            using FileStream s = File.Create(p);
            s.SetLength(fileSize);
            _fileSizeToSourcePathMap[fileSize] = p;
        }
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        ReFsVhdSession?.Dispose();
    }

    [ParamsSource(nameof(FileSizesBytes))]
    public long FileSize;

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
    [EvaluateOverhead(false)]
    public void CopyFileNoExistingTarget()
    {
        string sourceFilePath = _fileSizeToSourcePathMap[FileSize];
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
        string sourceFilePath = _fileSizeToSourcePathMap[FileSize];
        for (int i = 0; i < CopiesPerJob; i++)
        {
            string targetFilePath = Path.Combine(_testOutputDir!, FileNames[i]);
            _cow.CloneFile(sourceFilePath, targetFilePath);
        }
    }
}
