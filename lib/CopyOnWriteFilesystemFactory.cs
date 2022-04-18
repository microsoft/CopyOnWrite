// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// A factory for <see cref="ICopyOnWriteFilesystem"/> instances.
/// </summary>
/// <remarks>This implementation provides access to an appdomain-wide singleton.</remarks>
public static class CopyOnWriteFilesystemFactory
{
    private static readonly ICopyOnWriteFilesystem Instance = Create();

    /// <summary>
    /// Gets a singleton instance of the CoW filesystem appropriate for this operating system.
    /// </summary>
    public static ICopyOnWriteFilesystem GetInstance() => Instance;

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
