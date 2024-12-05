// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.CopyOnWrite.Windows;

internal sealed class VolumeInfo
{
    public VolumeInfo(string primaryDriveLetterRoot, string volumeName, bool supportsCoW, long clusterSize)
    {
        PrimaryDriveLetterRoot = primaryDriveLetterRoot;
        VolumeName = volumeName;
        SupportsCoW = supportsCoW;
        ClusterSize = clusterSize;
    }

    /// <summary>
    /// If the volume is mounted at a drive letter, the root directory like "C:\", else empty.
    /// </summary>
    public string PrimaryDriveLetterRoot { get; }
    public string VolumeName { get; }

    public bool SupportsCoW { get; }
    public long ClusterSize { get; }
}
