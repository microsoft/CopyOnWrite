// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;

namespace Microsoft.CopyOnWrite;

internal sealed class FileHelper
{
    /// <summary>
    /// Checks if path is a subpath of a parent directory. This can include being the same directory.
    /// Trailing path separators are allowed.
    /// </summary>
    /// <param name="parentDir">A relative or fully qualified path.</param>
    /// <param name="path">Given path to check if it's a subpath of path.</param>
    /// <returns>True if path is subpath of parentDir, false otherwise</returns>
    public static bool IsSubpathOfPath(string parentDir, string path)
    {
        if (path.Length == 0)
        {
            return (parentDir.Length == 0);  // Empty path is subpath of the empty path.
        }

        if (parentDir.Length == 0)
        {
            return false;
        }

        if (parentDir[parentDir.Length - 1] != Path.DirectorySeparatorChar)
        {
            if (path.Length < parentDir.Length)
            {
                // Cannot match since it's shorter.
                return false;
            }

            if (path.Length == parentDir.Length)
            {
                // path might be just path with no backslash.
                return path.Equals(parentDir, EnvironmentHelper.PathComparison);
            }

            if (path[parentDir.Length] == Path.DirectorySeparatorChar)
            {
                // path has a dir separator in the right place, check if path is a simple match up to that backslash.
                return path.StartsWith(parentDir, EnvironmentHelper.PathComparison);
            }

            return false;
        }

        // Path ends with a backslash - less common.
        if (path.Length < parentDir.Length - 1)
        {
            // Cannot match since path is shorter (not including the backslash).
            return false;
        }

        if (path.Length <= parentDir.Length)
        {
            // path might be just path with no backslash (for path.Length ==
            // path.Length - 1), or a possibly simple match if path also has a trailing
            // backslash (path.Length == path.Length).
            return parentDir.StartsWith(path, EnvironmentHelper.PathComparison);
        }

        if (path[parentDir.Length - 1] != Path.DirectorySeparatorChar)
        {
            // path cannot match since the last subdir in it does not match.
            return false;
        }

        return path.StartsWith(parentDir, EnvironmentHelper.PathComparison);
    }
}

internal static class FileHelperExtensions
{
    /// <summary>
    /// Checks if path is subpath of the path.
    /// </summary>
    /// <param name="path">Given path to check if it's a subpath of <paramref name="parentDir"/>.</param>
    /// <param name="parentDir">A relative or fully qualified path.</param>
    /// <returns>True if path is subpath of parentDir, false otherwise.</returns>
    public static bool IsSubpathOf(this string path, string parentDir) => FileHelper.IsSubpathOfPath(parentDir, path);
}
