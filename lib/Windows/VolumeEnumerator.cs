// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.CopyOnWrite.Windows;

internal sealed class VolumePaths
{
    internal VolumePaths(string volumeName, IReadOnlyList<string> mountedAtPaths)
    {
        VolumeName = volumeName;
        MountedAtPaths = mountedAtPaths;

        PrimaryDriveRootPath = string.Empty;
        bool foundRoot = false;
        foreach (string mountPath in mountedAtPaths)
        {
            if (mountPath.Length == 3)
            {
                // Drive letter root like "C:\"
                PrimaryDriveRootPath = mountPath;
                foundRoot = true;
                break;
            }
        }

        if (!foundRoot)
        {
            PrimaryDriveRootPath = volumeName + '\\';
        }
    }

    public string VolumeName { get; }
    public IReadOnlyList<string> MountedAtPaths { get; }
    public string PrimaryDriveRootPath { get; }
}

/// <summary>
/// Provides Windows disk volume enumeration, using the Win32 VolumeManager APIs.
/// </summary>
internal sealed class VolumeEnumerator : IDisposable
{
    private SafeVolumeFindHandle? _findHandle;

    public void Dispose()
    {
        _findHandle?.Dispose();
    }

    /// <summary>
    /// Enumerate all the volumes on the local machine. 
    /// It returns the volume GUID path for each volume.
    /// The Volume GUID path is of the form: "\\?\Volume{GUID}\"
    /// </summary>
    /// <returns></returns>
    public IEnumerable<VolumePaths> GetVolumesAndVolumePaths()
    {
        while (GetNextVolume(out string? volumeName))
        {
            yield return new VolumePaths(volumeName!, GetVolumePathNamesForVolumeName(volumeName!));
        }
    }

    private static IReadOnlyList<string> GetVolumePathNamesForVolumeName(string volumeName)
    {
        int bufferLenChars = 5; // Typical case: Drive root like "C:\" plus null character plus one (for some reason).
        IntPtr lpszVolumePathNames = Marshal.AllocHGlobal(sizeof(char) * bufferLenChars);
        try
        {
            bool success = NativeMethods.GetVolumePathNamesForVolumeName(
                volumeName,
                lpszVolumePathNames,
                bufferLenChars,
                out int returnedBufLenChars);
            if (!success)
            {
                int lastErr = Marshal.GetLastWin32Error();
                if (lastErr != NativeMethods.ERROR_MORE_DATA)
                {
                    throw new Win32Exception(lastErr,
                        $"GetVolumePathNamesForVolumeName({volumeName}) failed with Win32 error code {lastErr}");
                }

                // Increase the buffer size and call again.
                bufferLenChars = returnedBufLenChars + 2;  // Add more space at tail to ensure we get all data from API.
                Marshal.FreeHGlobal(lpszVolumePathNames);
                lpszVolumePathNames = Marshal.AllocHGlobal(sizeof(char) * bufferLenChars);
                success = NativeMethods.GetVolumePathNamesForVolumeName(
                    volumeName,
                    lpszVolumePathNames,
                    bufferLenChars,
                    out returnedBufLenChars);
                if (!success)
                {
                    Marshal.FreeHGlobal(lpszVolumePathNames);
                    lastErr = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastErr,
                        $"GetVolumePathNamesForVolumeName({volumeName}) failed with Win32 error code {lastErr} on 2nd attempt");
                }
            }

            // Split the output buffer into individual strings.
            var volumePathNames = new List<string>();
            int charsProcessed = 0;
            while ((charsProcessed + 1) < returnedBufLenChars)
            {
                string volumePathName = Marshal.PtrToStringAuto(lpszVolumePathNames + (sizeof(char) * charsProcessed))!;
                volumePathNames.Add(volumePathName);
                charsProcessed += (volumePathName.Length + 1);
            }

            return volumePathNames;
        }
        finally
        {
            Marshal.FreeHGlobal(lpszVolumePathNames);
        }
    }

    /// <summary>
    /// Moves to the next volume.
    /// </summary>
    /// <param name="volumeName">Returns the volume when the return value is true, null otherwise.</param>
    /// <returns>True if we found a volume. False if we reached the end.</returns>
    private bool GetNextVolume(out string? volumeName)
    {
        const int bufferLen = NativeMethods.MAX_VOLUME_GUID_LENGTH;
        var sb = new StringBuilder(bufferLen);
        volumeName = null;

        if (_findHandle == null)
        {
            _findHandle = NativeMethods.FindFirstVolume(sb, bufferLen);
            if (_findHandle != null && !_findHandle.IsInvalid)
            {
                volumeName = sb.ToString();
                return true;
            }

            int lastErr = Marshal.GetLastWin32Error();
            throw new Win32Exception($"FindFirstVolume() failed with Win32 error code {lastErr}");
        }

        bool found = NativeMethods.FindNextVolume(_findHandle, sb, bufferLen);
        if (found)
        {
            volumeName = sb.ToString();
        }
        else
        {
            int lastErr = Marshal.GetLastWin32Error();
            if (lastErr != NativeMethods.ERROR_NO_MORE_FILES)
            {
                throw new Win32Exception($"FindNextVolume() failed with Win32 error code {lastErr}");
            }
        }

        return found;
    }
}
