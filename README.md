The CopyOnWrite library provides a .NET layer on top of OS-specific logic that provides copy-on-write linking for files (a.k.a. CoW, file cloning, or reflinking). CoW linking provides the ability to copy a file without actually copying the original file's bytes from one disk location to another. The filesystem is in charge of ensuring that if the original file is modified or deleted, the CoW linked files remain unmodified by lazily copying the original file's bytes into each link. Unlike symlinks or hardlinks, writes to CoW links do not write through to the original file, as the filesystem breaks the link and copies in a lazy fashion. This enables scenarios like file caches where a single copy of a file held in a content-addressable or other store is safely linked to many locations in a filesystem with low I/O overhead.

*NOTE: In the current package version only Windows functionality is implemented. Issues for [Linux](https://github.com/microsoft/CopyOnWrite/issues/10) and [Mac](https://github.com/microsoft/CopyOnWrite/issues/11) are open and contributions are welcome. Consider reimplementing from the Rust package linked below.*

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
* Rust CoW: https://github.com/nicokoch/reflink

## Contributing
This project welcomes contributions and suggestions. See CONTRIBUTING.md.

### Running Unit Tests on Windows
If you have a local ReFS drive volume on which to run ReFS related tests, set the following user or system level environment variable:

  `CoW_Test_ReFS_Drive=D:\`

(You may need to exit and restart VS, VSCode, or consoles after setting this.)
When this env var is not available, unit tests create and mount a local ReFS VHD for testing.
You must run tests elevated (as admin), e.g. by opening Visual Studio as an admin before opening the solution.


## Performance Comparisons

### Windows
The following tests on various versions of Windows compare `System.IO.File.Copy()` with `CloneFile()`,
50 copies/clones of a single source file per measurement, highest performance clone flags and settings.
Defender real-time scanning was disabled in the test directory.
See CoWComparisons.cs. Machine was an 8/16-core with NVMe.

#### Windows 11 Prerelease 2H23 with Dev Drive
[Dev Drive](https://aka.ms/devdrive) is a new evolution of the ReFS filesystem starting in the Windows 11 23H2 refresh. It was announced at Microsoft Build 2023, and features significantly faster execution vs. 22H2 ReFS or NTFS along with CoW support and a performance mode for Defender antivirus. More details can be found [here](https://aka.ms/EngMSDevDrive).

CoW links on Dev Drive take approximately constant time except at large file sizes.
The savings is proportional to the file size, with 16MB files at about 31X performance, 1MB at 5.9X, and small sizes at about 2X.
Note that all performance numbers are significantly better than the no-VHD 22H2 measurements at the bottom of this page,
reflecting the Windows Filesystem team's work to reduce overhead for Dev Drive.

The Windows Insider build used for testing was from early May, 2023.

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   136.0 us |   3.95 us |  11.57 us |   135.5 us |  1.00 |    0.00 |
|       CoW |        0 |   102.9 us |   2.82 us |   8.17 us |   102.3 us |  0.76 |    0.08 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   305.4 us |   7.50 us |  21.26 us |   305.4 us |  1.00 |    0.00 |
|       CoW |        1 |   130.6 us |   3.96 us |  11.16 us |   129.5 us |  0.43 |    0.05 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   301.9 us |   6.77 us |  18.98 us |   301.7 us |  1.00 |    0.00 |
|       CoW |     1024 |   133.6 us |   3.94 us |  11.49 us |   132.0 us |  0.44 |    0.04 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   274.5 us |   6.35 us |  17.92 us |   272.4 us |  1.00 |    0.00 |
|       CoW |    16384 |   132.1 us |   3.69 us |  10.75 us |   132.4 us |  0.48 |    0.05 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   409.9 us |   8.53 us |  24.34 us |   408.0 us |  1.00 |    0.00 |
|       CoW |   262144 |   134.3 us |   5.48 us |  15.74 us |   131.4 us |  0.33 |    0.04 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |   774.4 us |  22.11 us |  65.20 us |   757.9 us |  1.00 |    0.00 |
|       CoW |  1048576 |   132.7 us |   2.62 us |   6.53 us |   131.2 us |  0.18 |    0.01 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 6,571.5 us | 131.19 us | 239.89 us | 6,520.5 us |  1.00 |    0.00 |
|       CoW | 16777216 |   210.3 us |   5.32 us |  15.34 us |   205.2 us |  0.03 |    0.00 |

#### Windows 11 release 2H22
Detailed numbers for a VHD formatted empty with ReFS for each iteration.
16MB files are about 35X performance, 1MB at 3.2X, and small sizes at about 1.3X.

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   202.8 us |   6.79 us |  19.92 us |   199.5 us |  1.00 |    0.00 |
|       CoW |        0 |   192.8 us |   5.40 us |  15.57 us |   191.2 us |  0.96 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   346.8 us |   9.65 us |  27.99 us |   345.8 us |  1.00 |    0.00 |
|       CoW |        1 |   237.5 us |   7.38 us |  21.28 us |   239.3 us |  0.69 |    0.08 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   372.9 us |   9.75 us |  27.66 us |   371.2 us |  1.00 |    0.00 |
|       CoW |     1024 |   248.0 us |   6.97 us |  20.21 us |   248.5 us |  0.67 |    0.07 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   347.1 us |  12.56 us |  35.62 us |   342.2 us |  1.00 |    0.00 |
|       CoW |    16384 |   248.8 us |   9.76 us |  28.47 us |   244.3 us |  0.72 |    0.11 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   484.3 us |  11.83 us |  34.69 us |   482.4 us |  1.00 |    0.00 |
|       CoW |   262144 |   247.8 us |   8.51 us |  24.69 us |   245.6 us |  0.51 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |   954.4 us |  19.06 us |  41.85 us |   948.0 us |  1.00 |    0.00 |
|       CoW |  1048576 |   251.3 us |   9.77 us |  28.64 us |   246.8 us |  0.27 |    0.03 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 9,867.2 us | 195.87 us | 546.01 us | 9,605.5 us |  1.00 |    0.00 |
|       CoW | 16777216 |   283.5 us |   6.56 us |  18.60 us |   282.7 us |  0.03 |    0.00 |

Same benchmark performed on a ReFS partition (no VHD) on the NVMe disk:

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   205.5 us |   8.82 us |  26.01 us |   200.1 us |  1.00 |    0.00 |
|       CoW |        0 |   183.7 us |   4.85 us |  14.16 us |   181.9 us |  0.91 |    0.13 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   307.9 us |   6.69 us |  19.53 us |   306.7 us |  1.00 |    0.00 |
|       CoW |        1 |   265.5 us |  15.18 us |  44.77 us |   265.6 us |  0.87 |    0.15 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   310.3 us |   6.56 us |  18.82 us |   310.4 us |  1.00 |    0.00 |
|       CoW |     1024 |   235.1 us |   9.51 us |  26.82 us |   227.9 us |  0.76 |    0.10 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   295.5 us |   5.80 us |  15.39 us |   295.2 us |  1.00 |    0.00 |
|       CoW |    16384 |   272.4 us |  16.46 us |  46.96 us |   275.9 us |  0.95 |    0.16 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   425.5 us |   9.29 us |  26.80 us |   423.8 us |  1.00 |    0.00 |
|       CoW |   262144 |   234.6 us |   7.83 us |  22.96 us |   232.7 us |  0.55 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |   851.7 us |  20.00 us |  56.08 us |   836.3 us |  1.00 |    0.00 |
|       CoW |  1048576 |   273.7 us |  15.39 us |  45.39 us |   269.9 us |  0.33 |    0.06 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 9,327.9 us | 179.64 us | 206.88 us | 9,282.0 us |  1.00 |    0.00 |
|       CoW | 16777216 |   273.3 us |  15.22 us |  44.17 us |   260.5 us |  0.04 |    0.00 |
