// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite
{
    internal class MacCopyOnWriteFilesystem : ICopyOnWriteFilesystem
    {
        public bool CopyOnWriteLinkSupportedBetweenPaths(string source, string destination)
        {
            // AppleFS always supports CoW.
            // return true;
            throw new NotImplementedException();
        }

        public bool CopyOnWriteLinkSupportedInDirectoryTree(string rootDirectory)
        {
            // AppleFS always supports CoW.
            // return true;
            throw new NotImplementedException();
        }

        public void CloneFile(string source, string destination)
        {
            // TODO: Use clonefile().
            throw new NotImplementedException();
        }
    }
}
