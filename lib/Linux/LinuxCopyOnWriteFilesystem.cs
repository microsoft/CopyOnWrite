// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite.Linux;

internal sealed class LinuxCopyOnWriteFilesystem : ICopyOnWriteFilesystem
{
    public int MaxClonesPerFile => int.MaxValue;

    public bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination, bool pathsAreFullyResolved = false)
    {
        // TODO: Implement FS probing and return a real value.
        return false;
    }

    public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory, bool pathIsFullyResolved = false)
    {
        return false;
    }

    public void CloneFile(string source, string destination) => CloneFile(source, destination, CloneFlags.None);

    public void CloneFile(string source, string destination, CloneFlags cloneFlags)
    {
        // TODO: Use ficlone().
        throw new NotImplementedException();
    }

    public void ClearFilesystemCache()
    {
    }
}
