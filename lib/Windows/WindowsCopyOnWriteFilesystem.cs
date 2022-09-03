// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.CopyOnWrite.Windows;

/// <summary>
/// Windows version. Uses an internal cache, hence assumes drive letters are non-removable or
/// don't change filesystem formats after they are first encountered.
/// </summary>
internal sealed class WindowsCopyOnWriteFilesystem : ICopyOnWriteFilesystem
{
    // Each cloned region must be < 4GB in length. Use a smaller default.
    internal const long MaxChunkSize = 1L << 31;  // 2GB

    private VolumeInfoCache _volumeInfoCache = VolumeInfoCache.BuildFromCurrentFilesystem();

    // https://docs.microsoft.com/en-us/windows-server/storage/refs/block-cloning#functionality-restrictions-and-remarks
    /// <inheritdoc />
    public int MaxClonesPerFile => 8175;

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

        // Must be in the same volume.
        VolumeInfo sourceVolume = _volumeInfoCache.GetVolumeForPath(resolvedSource);
        VolumeInfo destVolume = _volumeInfoCache.GetVolumeForPath(resolvedDestination);
        if (!ReferenceEquals(sourceVolume, destVolume))
        {
            return false;
        }

        return sourceVolume.SupportsCoW;
    }

    /// <inheritdoc />
    public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory)
    {
        (string resolvedSource, bool sourceOk) = ResolvePathAndEnsureDriveLetterVolume(rootDirectory);
        if (!sourceOk)
        {
            return false;
        }

        VolumeInfo volumeInfo = _volumeInfoCache.GetVolumeForPath(resolvedSource);
        return volumeInfo.SupportsCoW;
    }

    /// <inheritdoc />
    public void CloneFile(string source, string destination)
    {
        CloneFile(source, destination, CloneFlags.None);
    }

    /// <inheritdoc />
    // Ref: https://docs.microsoft.com/en-us/windows/win32/fileio/block-cloning
    // Also see https://github.com/0xbadfca11/reflink/blob/master/reflink.cpp
    // (discussion in http://blog.dewin.me/2017/02/under-hood-how-does-refs-block-cloning.html).
    public void CloneFile(string source, string destination, CloneFlags cloneFlags)
    {
        (string resolvedSource, bool sourceOk) = ResolvePathAndEnsureDriveLetterVolume(source);
        if (!sourceOk)
        {
            throw new NotSupportedException($"Source path '{source}' is not in the correct format");
        }

        VolumeInfo sourceVolume = _volumeInfoCache.GetVolumeForPath(resolvedSource);
        if (!sourceVolume.SupportsCoW)
        {
            throw new NotSupportedException($@"Drive volume {sourceVolume.PrimaryDriveLetterRoot} does not support copy-on-write clone links, i.e. is not formatted with ReFS");
        }

        // Get an open file handle to the source file.
        using SafeFileHandle sourceFileHandle = NativeMethods.CreateFile(resolvedSource, FileAccess.Read,
            FileShare.Read | FileShare.Delete, IntPtr.Zero, FileMode.Open, FileAttributes.Normal, IntPtr.Zero);
        if (sourceFileHandle.IsInvalid)
        {
            int lastErr = Marshal.GetLastWin32Error();
            if (lastErr == NativeMethods.ERROR_PATH_NOT_FOUND && Directory.Exists(resolvedSource))
            {
                lastErr = NativeMethods.ERROR_INVALID_HANDLE;
            }
            NativeMethods.ThrowSpecificIoException(lastErr,
                $"Failed to open file with winerror {lastErr} for source file '{resolvedSource}'");
        }

        // Create an empty destination file.
        using SafeFileHandle destFileHandle = NativeMethods.CreateFile(destination, FileAccess.Write,
            FileShare.Delete, IntPtr.Zero, FileMode.Create, FileAttributes.Normal, IntPtr.Zero);
        if (destFileHandle.IsInvalid)
        {
            int lastErr = Marshal.GetLastWin32Error();
            NativeMethods.ThrowSpecificIoException(lastErr,
                $"Failed to create destination file '{destination}' with winerror {lastErr}");
        }

        long sourceFileLength;
        if ((cloneFlags & CloneFlags.NoSparseFileCheck) != 0)
        {
            // Use cheaper API to just get the size.
            if (!NativeMethods.GetFileSizeEx(sourceFileHandle, out sourceFileLength))
            {
                int lastErr = Marshal.GetLastWin32Error();
                NativeMethods.ThrowSpecificIoException(lastErr,
                    $"Failed to get file info with winerror {lastErr} for source file '{resolvedSource}'");
            }
        }
        else
        {
            var fileInfo = new NativeMethods.BY_HANDLE_FILE_INFORMATION();
            if (!NativeMethods.GetFileInformationByHandle(sourceFileHandle, ref fileInfo))
            {
                int lastErr = Marshal.GetLastWin32Error();
                NativeMethods.ThrowSpecificIoException(lastErr,
                    $"Failed to get file info with winerror {lastErr} for source file '{resolvedSource}'");
            }

            sourceFileLength = fileInfo.FileSize;

            // Set destination as sparse if the source is. Must be done while file is zero bytes.
            if ((fileInfo.FileAttributes & FileAttributes.SparseFile) != 0)
            {
                // Set the destination to be sparse to match the source.
                int numBytesReturned = 0;
                if (!NativeMethods.DeviceIoControl(
                        destFileHandle,
                        NativeMethods.FSCTL_SET_SPARSE,
                        null,
                        0,
                        null,
                        0,
                        ref numBytesReturned,
                        IntPtr.Zero))
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    NativeMethods.ThrowSpecificIoException(lastErr,
                        $"Failed to set file sparseness with winerror {lastErr} for destination file '{destination}'");
                }
            }
        }

        // Clone file integrity settings from source to destination. Must be done while file is zero size.
        if ((cloneFlags & CloneFlags.NoFileIntegrityCheck) == 0)
        {
            int sizeReturned = 0;
            var getIntegrityInfo = new NativeMethods.FSCTL_GET_INTEGRITY_INFORMATION_BUFFER();
            if (!NativeMethods.DeviceIoControl(
                sourceFileHandle,
                NativeMethods.FSCTL_GET_INTEGRITY_INFORMATION,
                null,
                0,
                getIntegrityInfo,
                NativeMethods.SizeOfGetIntegrityInformationBuffer,
                ref sizeReturned,
                IntPtr.Zero))
            {
                int lastErr = Marshal.GetLastWin32Error();
                NativeMethods.ThrowSpecificIoException(lastErr,
                    $"Failed to get integrity information with winerror {lastErr} from source file '{source}'");
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
                        destFileHandle,
                        NativeMethods.FSCTL_SET_INTEGRITY_INFORMATION,
                        setIntegrityInfo,
                        NativeMethods.SizeOfSetIntegrityInformationBuffer,
                        null,
                        0,
                        ref sizeReturned,
                        IntPtr.Zero))
                {
                    int lastErr = Marshal.GetLastWin32Error();
                    NativeMethods.ThrowSpecificIoException(lastErr,
                        $"Failed to set integrity information with winerror {lastErr} on destination file '{destination}'");
                }
            }
        }

        // Set the destination on-disk size the same as the source.
        var fileSizeInfo = new NativeMethods.FILE_END_OF_FILE_INFO { FileSize = sourceFileLength };
        if (!NativeMethods.SetFileInformationByHandle(destFileHandle, NativeMethods.FileInformationClass.FileEndOfFileInfo,
                ref fileSizeInfo, NativeMethods.SizeOfFileEndOfFileInfo))
        {
            int lastErr = Marshal.GetLastWin32Error();
            NativeMethods.ThrowSpecificIoException(lastErr,
                $"Failed to set end of file with winerror {lastErr} on destination file '{destination}'");
        }

        var duplicateExtentsData = new NativeMethods.DUPLICATE_EXTENTS_DATA
        {
            FileHandle = sourceFileHandle,
        };

        // ReFS requires that cloned regions reside on a disk cluster boundary.
        long fileSizeRoundedUpToClusterBoundary =
            RoundUpToPowerOf2(sourceFileLength, sourceVolume.ClusterSize);
        long sourceOffset = 0;
        while (sourceOffset < sourceFileLength)
        {
            duplicateExtentsData.SourceFileOffset = sourceOffset;
            duplicateExtentsData.TargetFileOffset = sourceOffset;
            long thisChunkSize = Math.Min(fileSizeRoundedUpToClusterBoundary - sourceOffset, MaxChunkSize);
            duplicateExtentsData.ByteCount = thisChunkSize;

            int numBytesReturned = 0;
            bool ioctlResult = NativeMethods.DeviceIoControl(
                destFileHandle,
                NativeMethods.FSCTL_DUPLICATE_EXTENTS_TO_FILE,
                ref duplicateExtentsData,
                NativeMethods.SizeOfDuplicateExtentsData,
                outBuffer: null,
                nOutBufferSize: 0,
                ref numBytesReturned,
                lpOverlapped: IntPtr.Zero);
            if (!ioctlResult)
            {
                int lastErr = Marshal.GetLastWin32Error();
                string additionalMessage = string.Empty;
                if (lastErr == NativeMethods.ERROR_BLOCK_TOO_MANY_REFERENCES)
                {
                    additionalMessage =
                        " This is ERROR_BLOCK_TOO_MANY_REFERENCES and may mean you have surpassed the maximum " +
                        $"allowed {MaxClonesPerFile} references for a single file. " +
                        "See https://docs.microsoft.com/en-us/windows-server/storage/refs/block-cloning#functionality-restrictions-and-remarks";
                }

                NativeMethods.ThrowSpecificIoException(lastErr,
                    $"Failed copy-on-write cloning with winerror {lastErr} from source file '{source}' to '{destination}'.{additionalMessage}");
            }

            sourceOffset += thisChunkSize;
        }
    }

    public void ClearFilesystemCache()
    {
        _volumeInfoCache = VolumeInfoCache.BuildFromCurrentFilesystem();
    }

    internal static long RoundUpToPowerOf2(long originalValue, long roundingMultiplePowerOf2)
    {
        long mask = roundingMultiplePowerOf2 - 1;
        return (originalValue + mask) & ~mask;
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
}
