// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.CopyOnWrite.TestUtilities;

namespace Microsoft.CopyOnWrite.Benchmarking
{
    public class CoWComparisons
    {
        // Static to avoid multiple drives being created during benchmark and to allow control
        // of call to Dispose() (see BenchmarkProgram).
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

        [Benchmark(Baseline = true, Description = "Kernel copy")]
        public void CopyFileNoExistingTarget()
        {
            string sourceFilePath = _fileSizeToSourcePathMap[FileSize];
            string targetFilePath = Path.Combine(_testOutputDir!,
                Path.GetFileName(sourceFilePath) + "_" + Guid.NewGuid().ToString("N"));
            File.Copy(sourceFilePath, targetFilePath);
            File.Delete(targetFilePath);
        }

        [Benchmark]
        public void CowFileNoExistingTarget()
        {
            string sourceFilePath = _fileSizeToSourcePathMap[FileSize];
            string targetFilePath = Path.Combine(_testOutputDir!,
                Path.GetFileName(sourceFilePath) + "_" + Guid.NewGuid().ToString("N"));
            _cow.CloneFile(sourceFilePath, targetFilePath);
            File.Delete(targetFilePath);
        }
    }
}
