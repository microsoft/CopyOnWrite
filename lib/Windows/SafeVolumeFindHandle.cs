// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.ConstrainedExecution;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.CopyOnWrite.Windows;

/// <summary>
/// Wraps a handle for filesystem volume enumeration.
/// </summary>
internal sealed class SafeVolumeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>
    /// Creates a volume find handle
    /// </summary>
    public SafeVolumeFindHandle()
        : base(true)
    {
    }

    /// <summary>
    /// Override release to use proper close.
    /// </summary>
    /// <returns>true if successful false otherwise</returns>
    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
    protected override bool ReleaseHandle()
    {
        return NativeMethods.FindVolumeClose(handle);
    }
}
