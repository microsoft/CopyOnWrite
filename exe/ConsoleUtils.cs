// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// Common logic for console.
/// </summary>
public static class ConsoleUtils
{
    /// <summary>
    /// Gets <see cref="Console.WindowWidth"/>, dealing with headless process execution.
    /// </summary>
    /// <returns>The console window width or a default.</returns>
    public static int SafeGetWindowWidth()
    {
        const int defaultWidth = 80;
        try
        {
            int width = Console.WindowWidth;
            return width > 0 ? width : defaultWidth;
        }
        catch
        {
            // E.g.:
            // System.IO.IOException: The handle is invalid.
            //  at System.ConsolePal.GetBufferInfo(Boolean throwOnNoConsole, Boolean & succeeded)
            //  at System.ConsolePal.get_WindowWidth()
            //  at System.Console.get_WindowWidth()
            return defaultWidth;
        }
    }

    /// <summary>
    /// Provides a console width to use when rendering help text. Typically this is 
    /// one less than the console's window width to avoid the console printing blank lines.
    /// </summary>
    /// <returns>The console width to render to.</returns>
    public static int GetConsoleWidthForHelpText() => SafeGetWindowWidth() - 1;
}
