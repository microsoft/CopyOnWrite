// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// The behavior for operating system specific implementations that provide
/// copy-on-write (CoW) filesystem link capability discovery and creation.
///
/// CoW links are different from symlinks and hardlinks: The CoW links do not
/// allow writing though the link into the original file, and the filesystem
/// ensures that, on a write to or delete of the original file, the CoW links
/// are filled in with a copy of the original, breaking the link. This supports
/// patterns like file caches where a common copy of a file is linked in many
/// places in the filesystem without copies, while safely supporting update and
/// delete of the links as well as delete of the original file from the cache
/// (e.g. if it is a limited-size MRU cache).
///
/// CoW is only supported on some filesystems. macOS AppleFS natively
/// supports it. On Windows, ReFS supports CoW while the default NTFS
/// filesystem does not. On Linux, Btrfs, Xfs, and Zfs support CoW.
///
/// Note that implementations of this interface typically use internal caches
/// to avoid making kernel calls as much as possible. This can miss changes to
/// the filesystem made after the cached information was created.
/// <see cref="ClearFilesystemCache"/> allows you to clear the cache when
/// filesystem changes are detected.
/// </summary>
// TODO: Document and UT for if source is a directory, a hardlink, a symlink, or a CoW link.
public interface ICopyOnWriteFilesystem
{
    /// <summary>
    /// Provides a filesystem-specific maximum limit on the number of copy-on-write links allowed
    /// per file. When Int.MaxValue there is no limit.
    /// </summary>
    int MaxClonesPerFile { get; }

    /// <summary>
    /// Determines whether a copy-on-write link can be created between the
    /// provided paths.
    /// </summary>
    /// <param name="source">The file path to which the link will point.</param>
    /// <param name="destination">The file path that would contain the link.</param>
    /// <param name="pathsAreFullyResolved">
    /// When true, avoids expensive calls to <see cref="System.IO.Path.GetFullPath(string)"/> to resolve the
    /// full path by asserting that the caller already called it for the source and destination paths.
    /// </param>
    /// <returns>True if a link can be created, false if it cannot.</returns>
    bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination, bool pathsAreFullyResolved = false);

    /// <summary>
    /// Determines whether copy-on-write links can be created for files at the specified
    /// root directory and within all subdirectories.
    /// </summary>
    /// <param name="rootDirectory">
    /// A root directory to be queried. This can for instance be a repo root or parent
    /// directory containing build and test outputs. For *nix and macOS this can be
    /// the filesystem root '/'. For Windows this can be a drive root like 'C:\' that
    /// determines whether the entire drive volume filesystem is ReFS and supports
    /// copy-on-write links, or it can be a path under a volume mount point within
    /// another volume.
    /// </param>
    /// <param name="pathIsFullyResolved">
    /// When true, avoids an expensive call to <see cref="System.IO.Path.GetFullPath(string)"/> to resolve the
    /// full path by asserting that the caller already called it for the root path.
    /// </param>
    /// <returns>True if a link can be created, false if it cannot.</returns>
    bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory, bool pathIsFullyResolved = false);

    /// <summary>
    /// Creates a copy-on-write link at <paramref name="destination"/> pointing
    /// to <paramref name="source"/>, overwriting any existing file or link.
    /// Implicitly uses <see cref="CloneFlags.None"/>.
    /// </summary>
    /// <param name="source">The original file to which to link.</param>
    /// <param name="destination">
    /// The path where the link will be created. This must not already exist as a directory,
    /// and the parent directory must exist before this call.
    /// </param>
    /// <exception cref="System.NotSupportedException">Copy-on-write links are not supported between source and destination.</exception>
    /// <exception cref="MaxCloneFileLinksExceededException">
    /// The link attempt failed because a filesystem limit on the number of clones per file was exceeded. See <see cref="MaxClonesPerFile"/>.
    /// </exception>
    void CloneFile(string source, string destination);

    /// <summary>
    /// Creates a copy-on-write link at <paramref name="destination"/> pointing
    /// to <paramref name="source"/>, overwriting any existing file or link.
    /// </summary>
    /// <param name="source">The original file to which to link.</param>
    /// <param name="destination">
    /// The path where the link will be created. This must not already exist as a directory,
    /// and the parent directory must exist before this call.
    /// </param>
    /// <param name="cloneFlags">Flags to change behavior during creation of the CoW link.</param>
    /// <exception cref="System.NotSupportedException">Copy-on-write links are not supported between source and destination.</exception>
    /// <exception cref="MaxCloneFileLinksExceededException">
    /// The link attempt failed because a filesystem limit on the number of clones per file was exceeded. See <see cref="MaxClonesPerFile"/>.
    /// </exception>
    void CloneFile(string source, string destination, CloneFlags cloneFlags);

    /// <summary>
    /// Clears and recreates internal cached information about the computer's filesystem.
    /// For performance, the copy-on-write filesystem implementations cache filesystem layout
    /// information instead of making kernel calls on each call to determine the filesystem layout.
    /// If the filesystem has changed after creation of the cached information, e.g. if
    /// a volume or mount point was added or removed, the cached information can be out of date.
    /// </summary>
    void ClearFilesystemCache();
}

/// <summary>
/// Flags to change CoW link behavior.
/// </summary>
[Flags]
public enum CloneFlags
{
    /// <summary>
    /// Default zero value, no behavior changes.
    /// </summary>
    None = 0,

    /// <summary>
    /// Skip check for and copy of Windows file integrity settings from source to destination.
    /// Use when the filesystem and file are known not to use integrity.
    /// Saves 1-2 kernel round-trips.
    /// </summary>
    NoFileIntegrityCheck = 0x01,

    /// <summary>
    /// By default, sparse destination files are used to speed up cloning. The file is typically left
    /// sparse. Set this flag to reset the clone to non-sparse if the source file was non-sparse.
    /// </summary>
    DestinationMustMatchSourceSparseness = 0x02,

    /// <summary>
    /// Avoids expensive calls to <see cref="System.IO.Path.GetFullPath(string)"/> to resolve the full path by asserting
    /// that the caller already called it for the source and destination paths.
    /// </summary>
    PathIsFullyResolved = 0x04,
}
