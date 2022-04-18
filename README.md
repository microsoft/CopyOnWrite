The CopyOnWrite library provides a .NET layer on top of OS-specific logic that provides copy-on-write linking for files (a.k.a. CoW, file cloning, or reflinking). CoW linking provides the ability to copy a file without actually copying the original file's bytes from one disk location to another. The filesystem is in charge of ensuring that if the original file is modified or deleted, the CoW linked files remain unmodified by lazily copying the original file's bytes into each link. Unlike symlinks or hardlinks, writes to CoW links do not write through to the original file, as the filesystem breaks the link and copies in a lazy fashion. This enables scenarios like file caches where a single copy of a file held in a content-addressable or other store is safely linked to many locations in a filesystem with low I/O overhead.

This library allows a .NET developer to:

* Discover whether CoW links are allowed between two filesystem paths,
* Discover whether CoW links are allowed for a directory tree based at a specific root directory,
* Create CoW links,
* Find filesystem CoW link limits.

Discovery is important, as different operating systems and different filesystems available for those operating systems provide varying levels of CoW link support:

* Windows: The default NTFS filesystem does NOT support CoW, but the ReFS filesystem does.
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


## Release History
[NuGet package](https://www.nuget.org/packages/CopyOnWrite):

* 0.1.6 April 2022: Perf improvement for all Windows APIs
* 0.1.5 October 2021: Separate exception type for when link limit is exceeded. Mac and Linux throw NotSupportedException.
* 0.1.4 October 2021: Fix doc XML naming. Mac and Linux throw NotSupportedException.
* 0.1.3 October 2021: Bug fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.2 October 2021: Performance fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.1 October 2021: Bug fixes for Windows. Mac and Linux throw NotSupportedException.
* 0.1.0 July 2021: Windows ReFS support. Mac and Linux throw NotSupportedException.


## Performance Comparisons

### Windows
CoW links on ReFS take approximately constant time, saving time over file copies except at file size zero. The savings is proportional to the file size, with 16MB files at about 35X performance, 1MB at 3.2X, and small sizes at about 1.3X.

Detailed numbers for a VHD formatted empty with ReFS for each iteration, comparing `System.IO.File.Copy()` with `CloneFile()`, 25 copies/clones of a single source file per measurement. See CoWComparisons.cs. Machine was an 8/16-core M.2 SSD, Win10 21H1 Enterprise.

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   4.933 ms | 0.3274 ms | 0.9551 ms |   4.799 ms |  1.00 |    0.00 |
|       CoW |        0 |   5.002 ms | 0.2447 ms | 0.7100 ms |   4.885 ms |  1.04 |    0.21 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   8.841 ms | 0.4976 ms | 1.4197 ms |   8.515 ms |  1.00 |    0.00 |
|       CoW |        1 |   6.499 ms | 0.3647 ms | 1.0579 ms |   6.169 ms |  0.75 |    0.13 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   8.614 ms | 0.4982 ms | 1.4374 ms |   8.471 ms |  1.00 |    0.00 |
|       CoW |     1024 |   6.678 ms | 0.3776 ms | 1.0834 ms |   6.269 ms |  0.79 |    0.14 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   8.950 ms | 0.5148 ms | 1.4770 ms |   8.454 ms |  1.00 |    0.00 |
|       CoW |    16384 |   6.774 ms | 0.3240 ms | 0.9137 ms |   6.622 ms |  0.77 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |  12.700 ms | 0.6236 ms | 1.7993 ms |  12.140 ms |  1.00 |    0.00 |
|       CoW |   262144 |   7.036 ms | 0.3911 ms | 1.1470 ms |   6.882 ms |  0.56 |    0.10 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |  21.920 ms | 0.4359 ms | 1.1785 ms |  21.690 ms |  1.00 |    0.00 |
|       CoW |  1048576 |   6.940 ms | 0.3652 ms | 1.0711 ms |   6.743 ms |  0.32 |    0.05 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 252.637 ms | 2.9167 ms | 2.5855 ms | 251.895 ms |  1.00 |    0.00 |
|       CoW | 16777216 |   7.272 ms | 0.2882 ms | 0.7987 ms |   7.136 ms |  0.03 |    0.00 |

Same benchmark performed on a ReFS partition (no VHD) on an M.2 SSD:

|    Method | FileSize |       Mean |     Error |    StdDev |     Median | Ratio | RatioSD |
|---------- |--------- |-----------:|----------:|----------:|-----------:|------:|--------:|
| File.Copy |        0 |   3.627 ms | 0.2430 ms | 0.7127 ms |   3.453 ms |  1.00 |    0.00 |
|       CoW |        0 |   4.173 ms | 0.1934 ms | 0.5641 ms |   4.067 ms |  1.19 |    0.27 |
|           |          |            |           |           |            |       |         |
| File.Copy |        1 |   6.711 ms | 0.2276 ms | 0.6567 ms |   6.652 ms |  1.00 |    0.00 |
|       CoW |        1 |   5.597 ms | 0.1989 ms | 0.5803 ms |   5.471 ms |  0.84 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |     1024 |   6.476 ms | 0.2533 ms | 0.7146 ms |   6.398 ms |  1.00 |    0.00 |
|       CoW |     1024 |   5.647 ms | 0.2135 ms | 0.6194 ms |   5.614 ms |  0.89 |    0.14 |
|           |          |            |           |           |            |       |         |
| File.Copy |    16384 |   6.514 ms | 0.2421 ms | 0.6946 ms |   6.340 ms |  1.00 |    0.00 |
|       CoW |    16384 |   5.703 ms | 0.2212 ms | 0.6453 ms |   5.636 ms |  0.88 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |   262144 |   9.579 ms | 0.4325 ms | 1.2751 ms |   9.421 ms |  1.00 |    0.00 |
|       CoW |   262144 |   5.770 ms | 0.2632 ms | 0.7761 ms |   5.543 ms |  0.61 |    0.12 |
|           |          |            |           |           |            |       |         |
| File.Copy |  1048576 |  20.679 ms | 0.8702 ms | 2.4966 ms |  20.426 ms |  1.00 |    0.00 |
|       CoW |  1048576 |   6.253 ms | 0.2658 ms | 0.7628 ms |   6.152 ms |  0.31 |    0.05 |
|           |          |            |           |           |            |       |         |
| File.Copy | 16777216 | 240.713 ms | 4.7471 ms | 6.6547 ms | 240.357 ms |  1.00 |    0.00 |
|       CoW | 16777216 |   7.291 ms | 0.3910 ms | 1.1217 ms |   7.070 ms |  0.03 |    0.01 |

## Contributing
This project welcomes contributions and suggestions. See CONTRIBUTING.md.
