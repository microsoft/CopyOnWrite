The CopyOnWrite library provides a .NET layer on top of Windows OS-specific logic that provides copy-on-write linking for files (a.k.a. CoW, file cloning, or reflinking). CoW linking provides the ability to copy a file without actually copying the original file's bytes from one disk location to another. The filesystem is in charge of ensuring that if the original file is modified or deleted, the CoW linked files remain unmodified by lazily copying the original file's bytes into each link. Unlike symlinks or hardlinks, writes to CoW links do not write through to the original file, as the filesystem breaks the link and copies in a lazy fashion. This enables scenarios like file caches where a single copy of a file held in a content-addressable or other store is safely linked to many locations in a filesystem with low I/O overhead.

*NOTE: Only Windows functionality is implemented. On Linux and Mac using `File.Copy` is sufficient as it automatically uses CoW for [Linux](https://github.com/dotnet/runtime/pull/64264) (starting in .NET 7, and as long as a CoW compatible filesystem like `btrfs` is in use) and [Mac](https://github.com/dotnet/runtime/pull/79243) (.NET 8). A [similar PR](https://github.com/dotnet/runtime/pull/88695) for Windows did not make it into .NET, however there is [work underway](https://devblogs.microsoft.com/engineering-at-microsoft/copy-on-write-in-win32-api-early-access/) to integrate CoW into the Windows API in a possible future release.

This library allows a .NET developer to:

* Discover whether CoW links are allowed between two filesystem paths,
* Discover whether CoW links are allowed for a directory tree based at a specific root directory,
* Create CoW links,
* Find filesystem CoW link limits.

Discovery is important, as different operating systems and different filesystems available for those operating systems provide varying levels of CoW link support:

* Windows: The default NTFS filesystem does NOT support CoW, but the ReFS filesystem and 2023's new Dev Drive do.
* Linux: Btrfs, Xfs, Zfs support CoW while ext4 does not.
* Mac: AppleFS supports CoW by default.

When using this library you may need to create a wrapper that copies the file if CoW is not available.


## Example
```c#
using Microsoft.CopyOnWrite;

ICopyOnWriteFilesystem cow = CopyOnWriteFilesystemFactory.GetInstance();
bool canCloneInCurrentDirectory = cow.CopyOnWriteLinkSupportedInDirectoryTree(Environment.CurrentDirectory);
if (canCloneInCurrentDirectory)
{
    cow.CloneFile(existingFile, cowLinkFilePath);
}
```

## OS-specific caveats

### Windows
File clones on Windows do not actually allocate space on-drive for the clone. This has a good and a possibly bad implication:

* Good: You save space on-disk, as the clones only take up space for region clone metadata (small).
* Possibly bad: If cloned files are opened for append or random-access write, the lazy materialization of the original content into the opened file may result in disk out-of-space errors.


## Release History

[![NuGet version (CopyOnWrite)](https://img.shields.io/nuget/v/CopyOnWrite?style=plastic)](https://www.nuget.org/packages/CopyOnWrite)

* 0.3.12 October 2024: Add ERROR_DEV_NOT_EXIST handling on getting free disk space
* 0.3.11 September 2024: Add ERROR_DEV_NOT_EXIST handling on volume enumeration
* 0.3.10 September 2024: Add ERROR_NO_SUCH_DEVICE handling on volume enumeration
* 0.3.9 September 2024: Fix https://github.com/microsoft/CopyOnWrite/issues/44 - follow up on ignoring FILE_NOT_FOUND on volume enumeration
* 0.3.8 March 2024: Fix https://github.com/microsoft/MSBuildSdks/issues/546 - ignore FILE_NOT_FOUND on volume enumeration. Plus add SourceLink to the main library.
* 0.3.7 September 2023: Fix #30 - ignore ACCESS_DENIED on volume enumeration to avoid need to escalate privilege on Windows.
* 0.3.6 July 2023: Set AssemblyVersion to 0.9.9999.0 to allow mixing different minor-version binaries from different packages in the same appdomain/process.
* 0.3.5 July 2023: Set AssemblyVersion to 0.0.0.1 to allow mixing different minor-version binaries from different packages in the same appdomain/process.
* 0.3.4 July 2023: Handle locked BitLocker volume during volume scan.
* 0.3.3 July 2023: For Linux and Mac unimplemented filesystems, return false from `CopyOnWriteLinkSupported...` methods to avoid the need for checking for Windows OS before calling.
* 0.3.2 February 2023: Fix issue with ERROR_UNRECOGNIZED_VOLUME returned from some volumes causing an error on initialization on some machines.
* 0.3.1 February 2023: Fix issue with Windows drive information scanning hanging reading removable SD Card drives. Updated README with Windows clone behavior.
* 0.3.0 January 2023: Remove Windows serialization by path along with `CloneFlags.NoSerializedCloning` and the `useCrossProcessLocksWhereApplicable` flag to `CopyOnWriteFilesystemFactory`. The related concurrency bug in Windows was fixed in recent patches and retested on Windows 11.
* 0.2.2 January 2023: Fix mismatched sparseness when `CloneFlags.DestinationMustMatchSourceSparseness` was used (https://github.com/microsoft/CopyOnWrite/issues/17)
* 0.2.1 September 2022: Add detection for DOS SUBST drives as additional source of mappings.
* 0.2.0 September 2022: Improve documentation for ReFS parallel cloning bug workarounds.
  Improve Windows cloning performance by 7.2% by using sparse destination files.
  Default behavior change to leave destination file sparse and replaced `CloneFlags.NoSparseFileCheck` with `DestinationMustMatchSourceSparseness`,
  hence minor version increase.
* 0.1.13 September 2022: Fix CloneFlags to use individual bits.
* 0.1.12 September 2022: Add new factory flag that sets a mode to require cross-process Windows mutexes for safe source file locking to avoid a ReFS concurrency bug.
  Add optimization to allow bypassing redundant Path.GetFullPath() when caller has done it already.
* 0.1.11 September 2022: Serialize Windows cloning on source path to work around ReFS limitation in multithreaded cloning.
* 0.1.10 September 2022: Fix missing destination file failure detection.
* 0.1.9 September 2022: Add explicit cache invalidation call to interface.
  Update Windows implementation to detect ReFS mount points that are not drive roots, e.g. mounting D:\ (ReFS volume) under C:\ReFS.
* 0.1.8 April 2022: Add overload for CoW clone to allow bypassing some Windows filesystem feature checks
* 0.1.7 April 2022: Perf improvement for Windows CoW link creation by reducing kernel round-trips
* 0.1.6 April 2022: Perf improvement for all Windows APIs
* 0.1.5 October 2021: Separate exception type for when link limit is exceeded. Mac and Linux throw NotSupportedException.
* 0.1.4 October 2021: Fix doc XML naming. Mac and Linux throw NotSupportedException.
* 0.1.3 October 2021: Bug fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.2 October 2021: Performance fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.1 October 2021: Bug fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.0 July 2021: Windows ReFS support. Mac and Linux throw NotSupportedException.

## Related Works
* MSBuild SDK plugin to replace Copy task with one that supports CoW: https://github.com/microsoft/MSBuildSdks/tree/main/src/CopyOnWrite
* CoW now available in the [`Microsoft.Build.Artifacts`](https://github.com/microsoft/MSBuildSdks/tree/main/src/Artifacts) MSBuild SDK plugin to speed up file copies for artifact staging.
* CoW and Dev Drive series on Engineering@Microsoft blog:
  * [Intro](https://devblogs.microsoft.com/engineering-at-microsoft/dev-drive-and-copy-on-write-for-developer-performance/)
  * [Dev Drive released in Win11](https://devblogs.microsoft.com/engineering-at-microsoft/dev-drive-is-now-available/)
  * [CoW-in-Win32 early access](https://devblogs.microsoft.com/engineering-at-microsoft/copy-on-write-in-win32-api-early-access/) - which when released may make this package unneeded at least for Windows.
* Rust CoW: https://github.com/nicokoch/reflink

## Contributing
This project welcomes contributions and suggestions. See CONTRIBUTING.md.

### Running Unit Tests on Windows
If you have a local ReFS drive volume on which to run ReFS related tests, set the following user or system level environment variable:

  `CoW_Test_ReFS_Drive=D:\`

(You may need to exit and restart VS, VSCode, or consoles after setting this.)
When this env var is not available, unit tests create and mount a local ReFS VHD for testing and must be run elevated (as admin), e.g. by opening Visual Studio as an admin before opening the solution.


## Performance Comparisons
See [benchmark data](./BenchmarkData/BenchmarkData.md).
