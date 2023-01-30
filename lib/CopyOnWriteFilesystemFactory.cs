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
    private static readonly Lazy<ICopyOnWriteFilesystem> Instance = new(Create);

    /// <summary>
    /// Gets an instance of the CoW filesystem appropriate for this operating system.
    /// </summary>
    public static ICopyOnWriteFilesystem GetInstance() => Instance.Value;

    /// <summary>
    /// Gets an instance of the CoW filesystem appropriate for this operating system.
    /// </summary>
    /// <param name="forceUniqueInstance">
    /// Forces return of a unique instance instead of a singleton. Useful for unit tests.
    /// This can be expensive as creating a new instance can re-scan the filesystem to fill the cache.
    /// Consider instead using <paramref name="forceUniqueInstance"/>=false and utilize
    /// <see cref="ICopyOnWriteFilesystem.ClearFilesystemCache"/> to clear the cache on the singleton instance.
    /// </param>
    public static ICopyOnWriteFilesystem GetInstance(bool forceUniqueInstance)
    {
        if (forceUniqueInstance)
        {
            return Create();
        }

        return Instance.Value;
    }

    private static ICopyOnWriteFilesystem Create()
    {
        switch (Environment.OSVersion.Platform)
        {
            case PlatformID.Win32NT:
                return new WindowsCopyOnWriteFilesystem();
            case PlatformID.Unix:
                return new LinuxCopyOnWriteFilesystem();
            case PlatformID.MacOSX:
                return new MacCopyOnWriteFilesystem();
            default:
                throw new PlatformNotSupportedException($"Not supported on platformID '{Environment.OSVersion.Platform}'");
        }
    }
}
