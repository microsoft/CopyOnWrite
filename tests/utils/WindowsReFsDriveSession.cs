// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.CopyOnWrite.TestUtilities;

/// <summary>
/// A disposable wrapper around either use of a predefined local drive if configured
/// in the environment, or creation and cleanup of a ReFS filesystem in a VHD.
/// VHD creation requires admin/elevated execution context.
/// </summary>
public class WindowsReFsDriveSession : IDisposable
{
    private readonly char _vhdDriveLetter;
    private readonly string? _removeVhdScriptPath;

    private WindowsReFsDriveSession(char vhdDriveLetter, string? removeVhdScriptPath, string testRelativeDir)
    {
        _vhdDriveLetter = vhdDriveLetter;
        _removeVhdScriptPath = removeVhdScriptPath;
        ReFsDriveRoot = $@"{vhdDriveLetter}:\";
        TestRootDir = Path.Combine(ReFsDriveRoot, "CoWTests", testRelativeDir);
        Directory.CreateDirectory(TestRootDir);
    }

    /// <summary>
    /// Gets the fully qualified drive root for this session, e.g. "D:\".
    /// </summary>
    public string ReFsDriveRoot { get; }

    /// <summary>
    /// Gets the pre-created test root directory configured for this session.
    /// </summary>
    public string TestRootDir { get; }

    /// <summary>
    /// Creates a session by using the configured pre-created ReFS volume or by mounting a ReFS drive as a VHD.
    /// Dispose the returned object to clean up the drive and/or test root.
    /// </summary>
    public static WindowsReFsDriveSession Create(string relativeTestRootDir)
    {
        // Allow short-circuiting for CI environment and machines with locally created ReFS drives.
        // CODESYNC: .github/workflows/CI.yaml
        string? preCreatedReFsDriveRoot = Environment.GetEnvironmentVariable("CoW_Test_ReFS_Drive");
        if (!string.IsNullOrEmpty(preCreatedReFsDriveRoot))
        {
            return new WindowsReFsDriveSession(preCreatedReFsDriveRoot[0], null, relativeTestRootDir);
        }
        
        var driveLetterHashSet = new HashSet<char>();
        foreach (DriveInfo driveInfo in DriveInfo.GetDrives())
        {
            driveLetterHashSet.Add(char.ToUpper(driveInfo.Name[0]));
        }

        bool openDriveLetterFound = false;
        char openDriveLetter = 'B';
        for (; openDriveLetter <= 'Z'; openDriveLetter++)
        {
            if (driveLetterHashSet.Contains(openDriveLetter))
            {
                continue;
            }

            openDriveLetterFound = true;
            break;
        }

        if (!openDriveLetterFound)
        {
            throw new InvalidOperationException("No open drive letters found to use for mounting a ReFS VHD");
        }

        string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        string createVhdScriptPath = Path.Combine(assemblyDirectory, "CreateReFSVhd.ps1");
        string removeVhdScriptPath = Path.Combine(assemblyDirectory, "RemoveVhd.ps1");
        if (!File.Exists(createVhdScriptPath) || !File.Exists(removeVhdScriptPath))
        {
            throw new InvalidOperationException("Missing create/remove scripts in same directory as this assembly");
        }

        string driveVhdPath = Path.Combine(Environment.CurrentDirectory, $"{openDriveLetter}.vhd");
        if (File.Exists(driveVhdPath))
        {
            Console.WriteLine($"VHD already exists at {driveVhdPath}, deleting");
            File.Delete(driveVhdPath);
        }

        RunPowershellScript(createVhdScriptPath, $"{openDriveLetter}");

        // Wait a moment for the drive mount to stabilize. A new Explorer window often opens and the first CoW link can fail.
        Thread.Sleep(1000);
        
        return new WindowsReFsDriveSession(openDriveLetter, removeVhdScriptPath, relativeTestRootDir);
    }

    public void Dispose()
    {
        Directory.Delete(TestRootDir, recursive: true);
        if (_removeVhdScriptPath != null)
        {
            RunPowershellScript(_removeVhdScriptPath, $"{_vhdDriveLetter}");
        }
    }

    private static void RunPowershellScript(string scriptPath, string args)
    {
        ProcessExecutionUtilities.RunAndCaptureOutput("powershell", $"-ExecutionPolicy Bypass {scriptPath} {args}");
    }
}
