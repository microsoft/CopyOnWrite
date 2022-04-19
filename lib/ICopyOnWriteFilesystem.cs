// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// <returns>True if a link can be created, false if it cannot.</returns>
    bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination);

    /// <summary>
    /// Determines whether copy-on-write links can be created for files at the specified
    /// root directory and within all subdirectories.
    /// </summary>
    /// <param name="rootDirectory">
    /// A root directory to be queried. This can for instance be a repo root or parent
    /// directory containing build and test outputs. For *nix and macOS this can be
    /// the filesystem root '/'. For Windows this can be a drive root like 'C:\' which
    /// determines whether the entire drive volume filesystem is ReFS and supports
    /// copy-on-write links.
    /// </param>
    /// <returns></returns>
    bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory);

    /// <summary>
    /// Creates a copy-on-write link at <paramref name="destination"/> pointing
    /// to <paramref name="source"/>, overwriting any existing file or link.
    /// </summary>
    /// <param name="source">The original file to which to link.</param>
    /// <param name="destination">
    /// The path where the link will be created. This must not already exist as a directory,
    /// and the parent directory must exist before this call.
    /// </param>
    /// <exception cref="System.NotSupportedException">Copy-on-write links are not supported between source and destination.</exception>
    /// <exception cref="MaxCloneFileLinksExceededException">
    /// The link attempt failed because a filesystem limit was exceeded. See <see cref="MaxClonesPerFile"/>.
    /// </exception>
    void CloneFile(string source, string destination);
}
