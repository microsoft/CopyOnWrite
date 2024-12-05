﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace Microsoft.CopyOnWrite;

/// <summary>
/// The exception thrown when an attempt to add a new copy-on-write file clone
/// via <see cref="ICopyOnWriteFilesystem"/> failed because a limit on the number
/// of links per file imposed by the filesystem was exceeded.
/// </summary>
[Serializable]
public sealed class MaxCloneFileLinksExceededException : IOException
{
    /// <summary>
    /// Constructs a new instance of <see cref="MaxCloneFileLinksExceededException"/>.
    /// </summary>
    public MaxCloneFileLinksExceededException()
    {
    }

    /// <summary>
    /// Constructs a new instance of <see cref="MaxCloneFileLinksExceededException"/>.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public MaxCloneFileLinksExceededException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Constructs a new instance of <see cref="MaxCloneFileLinksExceededException"/>.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">A child exception.</param>
    public MaxCloneFileLinksExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
