// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CopyOnWrite.Mac;

internal sealed class MacCopyOnWriteFilesystem : ICopyOnWriteFilesystem
{
    public int MaxClonesPerFile => int.MaxValue;

    public bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination, bool pathsAreFullyResolved = false)
    {
        return false;
    }

    public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory, bool pathIsFullyResolved = false)
    {
        return false;
    }

    public void CloneFile(string source, string destination) => CloneFile(source, destination, CloneFlags.None);

    public void CloneFile(string source, string destination, CloneFlags cloneFlags)
    {
        // TODO: Use clonefile().
        throw new NotImplementedException();
    }

    public void ClearFilesystemCache()
    {
    }
}
