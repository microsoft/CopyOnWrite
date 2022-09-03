// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.CopyOnWrite.Windows;

internal sealed class VolumeInfoCache
{
    private sealed class SubPathAndVolume
    {
        public SubPathAndVolume(string subPath, VolumeInfo volumeInfo)
        {
            SubPath = subPath;
            Volume = volumeInfo;
        }

        public readonly string SubPath;
        public readonly VolumeInfo Volume;
    }

    private static readonly char[] Backslash = { '\\' };

    // A cheap 1-level trie to reduce string comparisons.
    private readonly SubPathAndVolume[][] _driveLetterSubPathsSortedInReverseOrder;

    public static VolumeInfoCache BuildFromCurrentFilesystem()
    {
        var fsInfo = new List<(VolumePaths, VolumeInfo)>();
        using var volumeEnum = new VolumeEnumerator();
        foreach (VolumePaths volumePaths in volumeEnum.GetVolumesAndVolumePaths())
        {
            fsInfo.Add((volumePaths, GetVolumeInfo(volumePaths)));
        }

        return new VolumeInfoCache(fsInfo);
    }

    // Exposed for unit testing.
    internal VolumeInfoCache(IList<(VolumePaths, VolumeInfo)> volumesAndMountedPaths)
    {
        _driveLetterSubPathsSortedInReverseOrder = new SubPathAndVolume[26][];
        for (int i = 0; i < _driveLetterSubPathsSortedInReverseOrder.Length; i++)
        {
            _driveLetterSubPathsSortedInReverseOrder[i] = Array.Empty<SubPathAndVolume>();
        }

        foreach ((VolumePaths volumePaths, VolumeInfo volumeInfo) in volumesAndMountedPaths)
        {
            foreach (string mountedPath in volumePaths.MountedAtPaths)
            {
                string mountedPathNoBackslash = mountedPath;
                if (mountedPath.Length > 3)
                {
                    mountedPathNoBackslash = mountedPath.TrimEnd(Backslash);
                }

                int letterIndex = IndexFromDriveLetter(mountedPath[0]);
                SubPathAndVolume[] existingPaths = _driveLetterSubPathsSortedInReverseOrder[letterIndex];
                var newRef = new SubPathAndVolume(mountedPathNoBackslash, volumeInfo);

                // Typical case is for one volume to be mounted at one drive letter root path.
                if (existingPaths.Length == 0)
                {
                    _driveLetterSubPathsSortedInReverseOrder[letterIndex] = new[] { newRef };
                }
                else
                {
                    var list = new List<SubPathAndVolume>(existingPaths.Length + 1) { newRef };
                    list.AddRange(existingPaths);
                    
                    // Sort in reverse order to put longest paths first for subpath comparisons.
                    list.Sort(static (s1, s2) =>
                        string.Compare(s2.SubPath, s1.SubPath, StringComparison.OrdinalIgnoreCase));

                    _driveLetterSubPathsSortedInReverseOrder[letterIndex] = list.ToArray();
                }
            }
        }
    }

    public VolumeInfo GetVolumeForPath(string path)
    {
        // Look up paths by drive letter to reduce the size of the resulting path array to search.
        int driveLetterIndex = IndexFromDriveLetter(path[0]);
        SubPathAndVolume[] subPathsAndVolumes = _driveLetterSubPathsSortedInReverseOrder[driveLetterIndex];

        // Paths are sorted in reverse order to get longer paths ahead of shorter paths for prefix matching.
        // For cases where volumes are mounted under other volumes, e.g. a D: ReFS drive mounted
        // under D:\ReFS, we want to match the deeper path.
        foreach (SubPathAndVolume spv in subPathsAndVolumes)
        {
            if (FileHelper.IsSubpathOfPath(spv.SubPath, path))
            {
                return spv.Volume;
            }
        }

        throw new ArgumentException($"No known volume information for '{path}'. " +
                                    "If the drive was added recently you may need to recreate the filesystem cache.");
    }

    private static VolumeInfo GetVolumeInfo(VolumePaths volumePaths)
    {
        bool result = NativeMethods.GetVolumeInformation(
            volumePaths.VolumeName,
            null,
            0,
            out uint _,
            out uint _,
            out NativeMethods.FileSystemFeature featureFlags,
            null,
            0);
        if (!result)
        {
            int lastErr = Marshal.GetLastWin32Error();
            NativeMethods.ThrowSpecificIoException(lastErr,
                $"Failed retrieving volume information for {volumePaths.PrimaryDriveRootPath} with winerror {lastErr}");
        }

        result = NativeMethods.GetDiskFreeSpace(
            volumePaths.PrimaryDriveRootPath,
            out ulong sectorsPerCluster,
            out ulong bytesPerSector,
            out ulong _,
            out ulong _);
        if (!result)
        {
            int lastErr = Marshal.GetLastWin32Error();
            NativeMethods.ThrowSpecificIoException(lastErr,
                $"Failed retrieving drive volume cluster layout information for {volumePaths.PrimaryDriveRootPath} with winerror {lastErr}");
        }

        return new VolumeInfo(
            volumePaths.PrimaryDriveRootPath,
            volumePaths.VolumeName,
            (featureFlags & NativeMethods.FileSystemFeature.BlockRefcounting) != 0,
            (long)(sectorsPerCluster * bytesPerSector));
    }

    private static int IndexFromDriveLetter(char driveLetter)
    {
        int index = driveLetter - 'A';
        if (index > 26)
        {
            index -= 32;  // Difference between 'A' and 'a'
        }

        return index;
    }
}