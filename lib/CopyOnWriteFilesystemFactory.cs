// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.CopyOnWrite.Linux;
using Microsoft.CopyOnWrite.Mac;
using Microsoft.CopyOnWrite.Windows;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// A factory for <see cref="ICopyOnWriteFilesystem"/> instances.
/// </summary>
/// <remarks>This implementation provides access to an appdomain-wide singleton.</remarks>
public static class CopyOnWriteFilesystemFactory
{
    private static readonly Lazy<ICopyOnWriteFilesystem> InProcessLocksInstance = new(() => Create(useCrossProcessLocks: false));
    private static readonly Lazy<ICopyOnWriteFilesystem> CrossProcessLocksInstance = new(() => Create(useCrossProcessLocks: true));

    /// <summary>
    /// Gets an instance of the CoW filesystem appropriate for this operating system.
    /// This instance uses in-process locks where needed for the current operating system.
    /// </summary>
    public static ICopyOnWriteFilesystem GetInstance() => InProcessLocksInstance.Value;

    /// <summary>
    /// Gets an instance of the CoW filesystem appropriate for this operating system.
    /// </summary>
    /// <param name="forceUniqueInstance">
    /// Forces return of a unique instance instead of a singleton. Useful for unit tests.
    /// This can be expensive as creating a new instance can re-scan the filesystem to fill the cache.
    /// Consider instead using <paramref name="forceUniqueInstance"/>=false and utilize
    /// <see cref="ICopyOnWriteFilesystem.ClearFilesystemCache"/> to clear the cache on the singleton instance.
    /// </param>
    /// <param name="useCrossProcessLocksWhereApplicable">
    /// If true, uses cross-process locks where needed for the current operating system.
    /// These locks are more expensive than in-process locks, but are required when cloning
    /// the same source file across process boundaries. Example of cross-process requirement:
    /// MSBuild uses many worker node processes that may all clone the same source to multiple
    /// output locations. Counter-example for intra-process locking: If a build engine controls
    /// all cloning of a source file from a content-addressable store into the filesystem.
    /// </param>
    public static ICopyOnWriteFilesystem GetInstance(bool forceUniqueInstance, bool useCrossProcessLocksWhereApplicable)
    {
        if (forceUniqueInstance)
        {
            return Create(useCrossProcessLocksWhereApplicable);
        }

        return useCrossProcessLocksWhereApplicable ? CrossProcessLocksInstance.Value : InProcessLocksInstance.Value;
    }

    private static ICopyOnWriteFilesystem Create(bool useCrossProcessLocks)
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32NT:
                return new WindowsCopyOnWriteFilesystem(useCrossProcessLocks);
            case PlatformID.Unix:
                return new LinuxCopyOnWriteFilesystem();
            case PlatformID.MacOSX:
                return new MacCopyOnWriteFilesystem();
            default:
                throw new PlatformNotSupportedException($"Not supported on platformID '{Environment.OSVersion.Platform}'");
        }
    }
}
