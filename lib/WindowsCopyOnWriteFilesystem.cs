// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.CopyOnWrite
{
    /// <summary>
    /// Windows version. Uses an internal cache, hence assumes drive letters are non-removable or
    /// don't change filesystem formats after they are first encountered.
    /// </summary>
    internal class WindowsCopyOnWriteFilesystem : ICopyOnWriteFilesystem
    {
        private class DriveVolumeInfo
        {
            public DriveVolumeInfo(bool supportsCoW, long clusterSize)
            {
                SupportsCoW = supportsCoW;
                ClusterSize = clusterSize;
            }

            public bool SupportsCoW { get; }
            public long ClusterSize { get; }
        }

        // Each cloned region must be < 4GB in length. Use a smaller default.
        internal const long MaxChunkSize = 1L << 31;  // 2GB

        private readonly ConcurrentDictionary<char, DriveVolumeInfo> _driveLetterToInfoMap = new ();

        // TODO: Deal with \??\ prefixes
        // TODO: Deal with \\?\ prefixes
        /// <inheritdoc />
        public bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination)
        {
            (string resolvedSource, bool sourceOk) = ResolvePathAndEnsureDriveLetterVolume(source);
            if (!sourceOk)
            {
                return false;
            }

            (string resolvedDestination, bool destOk) = ResolvePathAndEnsureDriveLetterVolume(destination);
            if (!destOk)
            {
                return false;
            }

            char sourceDriveLetter = char.ToUpper(resolvedSource[0]);
            char destDriveLetter = char.ToUpper(resolvedDestination[0]);
            
            // Must be on the same filesystem volume (drive letter).
            // TODO: This does not handle a subst'd drive letter pointing to the same original volume.
            if (sourceDriveLetter != destDriveLetter)
            {
                return false;
            }

            DriveVolumeInfo driveInfo = GetOrUpdateDriveVolumeInfo(sourceDriveLetter);
            return driveInfo.SupportsCoW;
        }

        /// <inheritdoc />
        public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory)
        {
            (string resolvedSource, bool sourceOk) = ResolvePathAndEnsureDriveLetterVolume(rootDirectory);
            if (!sourceOk)
            {
                return false;
            }

            char sourceDriveLetter = char.ToUpper(resolvedSource[0]);
            DriveVolumeInfo driveInfo = GetOrUpdateDriveVolumeInfo(sourceDriveLetter);
            return driveInfo.SupportsCoW;
        }

        /// <inheritdoc />
        // Ref: https://docs.microsoft.com/en-us/windows/win32/fileio/block-cloning
        // Also see https://github.com/0xbadfca11/reflink/blob/master/reflink.cpp
        // (discussion in http://blog.dewin.me/2017/02/under-hood-how-does-refs-block-cloning.html).
        public void CloneFile(string source, string destination)
        {
            (string resolvedSource, bool sourceOk) = ResolvePathAndEnsureDriveLetterVolume(source);
            if (!sourceOk)
            {
                throw new NotSupportedException($"Source path '{source}' is not in the correct format");
            }

            char driveLetter = char.ToUpper(resolvedSource[0]);
            DriveVolumeInfo driveInfo = GetOrUpdateDriveVolumeInfo(driveLetter);
            if (!driveInfo.SupportsCoW)
            {
                throw new NotSupportedException($@"Drive volume {driveLetter}:\ does not support copy-on-write clone links, e.g. is not formatted with ReFS");
            }

            // Get an open file handle to the source file.
            using var sourceStream = File.Open(resolvedSource, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            long sourceFileLength = sourceStream.Length;

            // Create an empty destination file.
            using var destStream = File.Create(destination);

            // Set destination as sparse if the source is. Must be done while file is zero bytes.
            FileAttributes sourceAttr = File.GetAttributes(resolvedSource);
            if ((sourceAttr & FileAttributes.SparseFile) != 0)
            {
                // Set the destination to be sparse to match the source.
                int numBytesReturned = 0;
                if (!NativeMethods.DeviceIoControl(
                    destStream.SafeFileHandle!,
                    NativeMethods.FSCTL_SET_SPARSE,
                    (object?)null,
                    0,
                    null,
                    0,
                    ref numBytesReturned,
                    IntPtr.Zero))
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastErr, $"Failed to set file sparseness with winerror {lastErr} for destination file '{destination}'");
                }
            }

            // Clone file integrity settings from source to destination. Must be done while file is zero size.
            int sizeReturned = 0;
            var getIntegrityInfo = new NativeMethods.FSCTL_GET_INTEGRITY_INFORMATION_BUFFER();
            if (!NativeMethods.DeviceIoControl(
                sourceStream.SafeFileHandle!,
                NativeMethods.FSCTL_GET_INTEGRITY_INFORMATION,
                (object?)null,
                0,
                getIntegrityInfo,
                NativeMethods.SizeOfGetIntegrityInformationBuffer,
                ref sizeReturned,
                IntPtr.Zero))
            {
                int lastErr = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastErr, $"Failed to get integrity information with winerror {lastErr} from source file '{source}'");
            }

            if (getIntegrityInfo.ChecksumAlgorithm != 0 || getIntegrityInfo.Flags != 0)
            {
                var setIntegrityInfo = new NativeMethods.FSCTL_SET_INTEGRITY_INFORMATION_BUFFER
                {
                    ChecksumAlgorithm = getIntegrityInfo.ChecksumAlgorithm,
                    Flags = getIntegrityInfo.Flags,
                    Reserved = getIntegrityInfo.Reserved,
                };
                if (!NativeMethods.DeviceIoControl(
                    destStream.SafeFileHandle!,
                    NativeMethods.FSCTL_SET_INTEGRITY_INFORMATION,
                    setIntegrityInfo,
                    NativeMethods.SizeOfSetIntegrityInformationBuffer,
                    null,
                    0,
                    ref sizeReturned,
                    IntPtr.Zero))
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastErr, $"Failed to set integrity information with winerror {lastErr} on destination file '{destination}'");
                }
            }

            // Set the destination on-disk size the same as the source.
            // TODO: Remove?
            destStream.SetLength(sourceFileLength);
            /*
            bool result = NativeMethods.SetFileInformationByHandle(
                destStream.SafeFileHandle!,
                NativeMethods.FileInformationClass.FileEndOfFileInfo,
                ref sourceFileLength,
                Marshal.SizeOf(typeof(long)));
            if (!result)
            {
                int lastErr = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastErr, $"Failed setting file size for destination '{destination}' with winerror {lastErr}");
            }
            */

            var duplicateExtentsData = new NativeMethods.DUPLICATE_EXTENTS_DATA
            {
                FileHandle = sourceStream.SafeFileHandle,
            };

            // ReFS requires that cloned regions reside on a disk cluster boundary.
            long fileSizeRoundedUpToClusterBoundary = RoundUpToPowerOf2(sourceFileLength, driveInfo.ClusterSize);
            long sourceOffset = 0;
            while (sourceOffset < sourceFileLength)
            {
                duplicateExtentsData.SourceFileOffset = sourceOffset;
                duplicateExtentsData.TargetFileOffset = sourceOffset;
                long thisChunkSize = Math.Min(fileSizeRoundedUpToClusterBoundary - sourceOffset, MaxChunkSize);
                duplicateExtentsData.ByteCount = thisChunkSize;

                int numBytesReturned = 0;
                bool ioctlResult = NativeMethods.DeviceIoControl(
                    destStream.SafeFileHandle!,
                    NativeMethods.FSCTL_DUPLICATE_EXTENTS_TO_FILE,
                    duplicateExtentsData,
                    NativeMethods.SizeOfDuplicateExtentsData,
                    outBuffer: null,
                    nOutBufferSize: 0,
                    ref numBytesReturned,
                    lpOverlapped: IntPtr.Zero);
                if (!ioctlResult)
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    throw new Win32Exception(lastErr, $"Failed copy-on-write cloning with winerror {lastErr} from source file '{source}' to '{destination}'");
                }

                sourceOffset += thisChunkSize;
            }
        }

        private DriveVolumeInfo GetOrUpdateDriveVolumeInfo(char driveLetter)
        {
            return _driveLetterToInfoMap.GetOrAdd(driveLetter, GetDriveVolumeInfo);
        }

        internal static long RoundUpToPowerOf2(long originalValue, long roundingMultiplePowerOf2)
        {
            long mask = roundingMultiplePowerOf2 - 1;
            if ((originalValue & mask) == 0)
            {
                return originalValue;
            }

            return (originalValue & ~mask) + roundingMultiplePowerOf2;
        }

        private static DriveVolumeInfo GetDriveVolumeInfo(char driveLetter)
        {
            return GetDriveVolumeInfo($@"{driveLetter}:\");
        }

        // driveRootPath in the form "C:\".
        private static DriveVolumeInfo GetDriveVolumeInfo(string driveRootPath)
        {
            bool result = NativeMethods.GetVolumeInformation(
                driveRootPath,
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
                throw new Win32Exception(lastErr, $"Failed retrieving drive volume information for {driveRootPath} with winerror {lastErr}");
            }

            result = NativeMethods.GetDiskFreeSpace(
                driveRootPath,
                out ulong sectorsPerCluster,
                out ulong bytesPerSector,
                out ulong _,
                out ulong _);
            if (!result)
            {
                int lastErr = Marshal.GetLastWin32Error();
                throw new Win32Exception(lastErr, $"Failed retrieving drive volume cluster layout information for {driveRootPath} with winerror {lastErr}");
            }

            return new DriveVolumeInfo(
                (featureFlags & NativeMethods.FileSystemFeature.BlockRefcounting) != 0,
                (long)(sectorsPerCluster * bytesPerSector));
        }

        private static (string, bool) ResolvePathAndEnsureDriveLetterVolume(string path)
        {
            if (path.Length < 2 || path[1] != ':')
            {
                path = Path.GetFullPath(path);
                if (path.Length < 2 || path[1] != ':')
                {
                    // Possible UNC path or other strangeness.
                    return (path, false);
                }
            }

            return (path, true);
        }

        // ReSharper disable NotAccessedField.Local
        private static class NativeMethods
        {
            [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                SafeHandle hDevice,
                uint dwIoControlCode,
                [In] object? InBuffer,
                int nInBufferSize,
                [Out] object? outBuffer,
                int nOutBufferSize,
                ref int pBytesReturned,
                [In] IntPtr lpOverlapped);

            [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeviceIoControl(
                SafeHandle hDevice,
                uint dwIoControlCode,
                [In] DUPLICATE_EXTENTS_DATA InBuffer,
                int nInBufferSize,
                [Out] object? outBuffer,
                int nOutBufferSize,
                ref int pBytesReturned,
                [In] IntPtr lpOverlapped);

            [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            public static extern bool GetVolumeInformation(
                string rootPathName,
                StringBuilder? volumeName,
                int volumeNameSize,
                out uint volumeSerialNumber,
                out uint maximumComponentLength,
                out FileSystemFeature fileSystemFlags,
                StringBuilder? fileSystemNameBuffer,
                int nFileSystemNameSize);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            public static extern bool GetDiskFreeSpace(
                string lpRootPathName,
                out ulong lpSectorsPerCluster,
                out ulong lpBytesPerSector,
                out ulong lpNumberOfFreeClusters,
                out ulong lpTotalNumberOfClusters);

            /// <summary>
            /// Specific overload for setting file size via implicit ref to FILE_END_OF_FILE_INFO.
            /// </summary>
            [DllImport("Kernel32.dll", SetLastError = true)]
            public static extern bool SetFileInformationByHandle(
                SafeHandle hFile,
                FileInformationClass fileInformationClass,  // Use FileInformationClass.FileEndOfFileInfo
                ref long endOfFileInfo,
                int dwBufferSize);

            // Full version: http://www.pinvoke.net/default.aspx/kernel32/SetFileInformationByHandle.html
            public enum FileInformationClass
            {
                FileEndOfFileInfo = 6,
            }

            // For full version see https://www.pinvoke.net/default.aspx/kernel32.GetVolumeInformation
            [Flags]
            public enum FileSystemFeature : uint
            {
                /// <summary>
                /// The specified volume supports sharing logical clusters between files on the same volume.
                /// The file system reallocates on writes to shared clusters. Indicates that
                /// FSCTL_DUPLICATE_EXTENTS_TO_FILE is a supported operation.
                /// </summary>
                BlockRefcounting = 0x08000000,
            }

            // ReSharper disable ShiftExpressionZeroLeftOperand
            public const uint FSCTL_DUPLICATE_EXTENTS_TO_FILE = 0x00090000 | (0x2 << 14) | (209 << 2);
            public const uint FSCTL_GET_INTEGRITY_INFORMATION = 0x00090000 | (0x0 << 14) | (159 << 2);
            public const uint FSCTL_SET_INTEGRITY_INFORMATION = 0x00090000 | (0x3 << 14) | (160 << 2);
            public const uint FSCTL_SET_SPARSE                = 0x00090000 | (0x0 << 14) | (49 << 2);

            public static readonly int SizeOfDuplicateExtentsData = Marshal.SizeOf(typeof(DUPLICATE_EXTENTS_DATA));

            [StructLayout(LayoutKind.Sequential)]
            public class DUPLICATE_EXTENTS_DATA
            {
                public SafeHandle? FileHandle;
                public long SourceFileOffset;
                public long TargetFileOffset;
                public long ByteCount;
            }

            public static readonly int SizeOfGetIntegrityInformationBuffer = Marshal.SizeOf(typeof(FSCTL_GET_INTEGRITY_INFORMATION_BUFFER));

            [StructLayout(LayoutKind.Sequential)]
            public class FSCTL_GET_INTEGRITY_INFORMATION_BUFFER
            {
#pragma warning disable 649
                public ushort ChecksumAlgorithm;
                public ushort Reserved;
                public uint Flags;
                public uint ChecksumChunkSizeInBytes;
                public uint ClusterSizeInBytes;
#pragma warning restore 649
            }

            public static readonly int SizeOfSetIntegrityInformationBuffer = Marshal.SizeOf(typeof(FSCTL_SET_INTEGRITY_INFORMATION_BUFFER));

            [StructLayout(LayoutKind.Sequential)]
            public class FSCTL_SET_INTEGRITY_INFORMATION_BUFFER
            {
                public ushort ChecksumAlgorithm;
                public ushort Reserved;
                public uint Flags;
            }
        }
    }
}
