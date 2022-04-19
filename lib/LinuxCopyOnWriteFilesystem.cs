// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite;

internal sealed class LinuxCopyOnWriteFilesystem : ICopyOnWriteFilesystem
{
    public int MaxClonesPerFile => int.MaxValue;

    public bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination)
    {
        // TODO: Implement FS probing and return a real value.
        throw new NotImplementedException();
    }

    public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory)
    {
        throw new NotImplementedException();
    }

    public void CloneFile(string source, string destination)
    {
        // TODO: Use ficlone().
        throw new NotImplementedException();
    }
}
