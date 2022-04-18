// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite.TestUtilities;

/// <summary>
/// Provides operating system context information.
/// </summary>
public static class OsHelper
{
    public static bool IsWindows { get; } = Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static bool IsLinux { get; } = Environment.OSVersion.Platform == PlatformID.Unix;

    public static bool IsMac { get; } = Environment.OSVersion.Platform == PlatformID.MacOSX;

    public static StringComparer PathComparer { get; } =
        IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
