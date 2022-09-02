// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.CopyOnWrite;

internal static class EnvironmentHelper
{
    /// <summary>
    /// OS Platform of the current machine.
    /// </summary>
    public static OSPlatform MyOSPlatform { get; } = GetMyOSPlatform();

    /// <summary>
    /// True if OS is Linux.
    /// </summary>
    public static bool IsLinux => MyOSPlatform == OSPlatform.Linux;

    /// <summary>
    /// True if OS is Windows.
    /// </summary>
    public static bool IsWindows => MyOSPlatform == OSPlatform.Windows;

    /// <summary>
    /// True if OS is Mac.
    /// </summary>
    public static bool IsMac => MyOSPlatform == OSPlatform.OSX;

    /// <summary>
    /// Gets a string comparer for path comparisons.
    /// </summary>
    public static StringComparer PathComparer { get; } = IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Gets a string comparison for path comparisons.
    /// </summary>
    /// <remarks>See remarks on <see cref="PathComparer"/>.</remarks>
    public static StringComparison PathComparison { get; } = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static OSPlatform GetMyOSPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OSPlatform.OSX;
        }

        throw new InvalidOperationException("Cannot determine OS Platform.");
    }
}
