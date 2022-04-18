// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Microsoft.CopyOnWrite.TestUtilities;

/// <summary>
/// A disposable wrapper around creation and cleanup of a ReFS filesystem in
/// a VHD. Requires admin/elevated execution context.
/// </summary>
public class WindowsReFsVhdSession : IDisposable
{
    private readonly char _vhdDriveLetter;
    private readonly string _removeVhdScriptPath;

    private WindowsReFsVhdSession(char vhdDriveLetter, string removeVhdScriptPath)
    {
        _vhdDriveLetter = vhdDriveLetter;
        _removeVhdScriptPath = removeVhdScriptPath;
        ReFsDriveRoot = $@"{vhdDriveLetter}:\";
    }

    /// <summary>
    /// Creates a session by mounting a ReFS drive as a VHD.
    /// Dispose the returned object to clean up the drive.
    /// </summary>
    public static WindowsReFsVhdSession Create()
    {
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

        RunPowershellScript(createVhdScriptPath, $"{openDriveLetter}");
        return new WindowsReFsVhdSession(openDriveLetter, removeVhdScriptPath);
    }

    public void Dispose()
    {
        RunPowershellScript(_removeVhdScriptPath, $"{_vhdDriveLetter}");
    }

    /// <summary>
    /// Gets the fully qualified drive root for this session, e.g. "D:\".
    /// </summary>
    public string ReFsDriveRoot { get; }

    private static void RunPowershellScript(string scriptPath, string args)
    {
        ProcessExecutionUtilities.RunAndCaptureOutput("powershell", $"-ExecutionPolicy Bypass {scriptPath} {args}");
    }
}
