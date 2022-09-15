// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.CopyOnWrite.Windows;

// UnmanagedType.AsAny obsolete
#pragma warning disable 618

// ReSharper disable NotAccessedField.Local
// ReSharper disable InconsistentNaming
internal static class NativeMethods
{
    public static void ThrowSpecificIoException(int lastErr, string message)
    {
        throw lastErr switch
        {
            ERROR_FILE_NOT_FOUND => new FileNotFoundException(message),
            ERROR_PATH_NOT_FOUND => new DirectoryNotFoundException(message),
            ERROR_ACCESS_DENIED => new UnauthorizedAccessException(message),
            ERROR_INVALID_HANDLE => new UnauthorizedAccessException(message),
            ERROR_BLOCK_TOO_MANY_REFERENCES => new MaxCloneFileLinksExceededException(message),
            _ => new Win32Exception(lastErr, message)
        };
    }

    public const int FILE_FLAG_NO_BUFFERING = 0x20000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern SafeFileHandle CreateFile(
        string lpFileName,
        [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
        [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
        [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool DeviceIoControl(
        SafeHandle hDevice,
        uint dwIoControlCode,
        [MarshalAs(UnmanagedType.AsAny)][In] object? inBuffer,
        int nInBufferSize,
        [MarshalAs(UnmanagedType.AsAny)][Out] object? outBuffer,
        int nOutBufferSize,
        ref int pBytesReturned,
        [In] IntPtr lpOverlapped);

    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool DeviceIoControl(
        SafeHandle hDevice,
        uint dwIoControlCode,
        [In] ref DUPLICATE_EXTENTS_DATA inBuffer,
        int nInBufferSize,
        [MarshalAs(UnmanagedType.AsAny)][Out] object? outBuffer,
        int nOutBufferSize,
        ref int pBytesReturned,
        [In] IntPtr lpOverlapped);

    [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool GetDiskFreeSpace(
        string lpRootPathName,
        out ulong lpSectorsPerCluster,
        out ulong lpBytesPerSector,
        out ulong lpNumberOfFreeClusters,
        out ulong lpTotalNumberOfClusters);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool GetFileSizeEx(SafeFileHandle hFile, out long lpFileSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle hFile, ref BY_HANDLE_FILE_INFORMATION fileInformation);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public FileAttributes FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;

        public long FileSize => ((long)FileSizeHigh << 32) | FileSizeLow;
    }

    [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern bool SetFileInformationByHandle(SafeHandle hFile, FileInformationClass FileInformationClass, ref FILE_END_OF_FILE_INFO endOfFileInfo, int dwBufferSize);

    public enum FileInformationClass
    {
        FileEndOfFileInfo = 6,
    }

    public static readonly int SizeOfFileEndOfFileInfo = Marshal.SizeOf(typeof(FILE_END_OF_FILE_INFO));

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct FILE_END_OF_FILE_INFO
    {
        public long FileSize;
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
    public const uint FSCTL_SET_SPARSE = 0x00090000 | (0x0 << 14) | (49 << 2);

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_HANDLE = 6;
    public const int ERROR_NO_MORE_FILES = 18;
    public const int ERROR_MORE_DATA = 234;

    // ReFS specific WinError codes.
    public const int ERROR_BLOCK_TOO_MANY_REFERENCES = 347;

    public static readonly int SizeOfDuplicateExtentsData = Marshal.SizeOf(typeof(DUPLICATE_EXTENTS_DATA));

    [StructLayout(LayoutKind.Sequential)]
    public ref struct DUPLICATE_EXTENTS_DATA
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

    /// <summary>
    /// According to MSDN: http://msdn.microsoft.com/en-us/library/aa364994(v=vs.85).aspx
    /// </summary>
    public const int MAX_VOLUME_GUID_LENGTH = 50;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeVolumeFindHandle FindFirstVolume(
        [Out] StringBuilder volumeName,
        [In] uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern bool FindNextVolume(
        [In] SafeVolumeFindHandle hFindVolume,
        [Out] StringBuilder volumeName,
        [In] uint bufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindVolumeClose([In] IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathNamesForVolumeName(
        string deviceName,
        [Out] StringBuilder volPathNames,
        int cBuff,
        [Out] out int retLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetVolumePathNamesForVolumeName(
        string lpszVolumeName,
        IntPtr lpszVolumePathNames,
        int cchBufferLength,
        out int lpcchReturnLength);
}
