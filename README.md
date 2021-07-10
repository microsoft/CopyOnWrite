# CopyOnWrite

This repo and its resulting [NuGet package](https://www.nuget.org/packages/CopyOnWrite) provide a .NET layer on top of OS-specific logic that provides copy-on-write linking for files (a.k.a. CoW, file cloning, or reflinking). CoW linking provides the ability to copy a file without actually copying the original file's bytes from one disk location to another. The filesystem is in charge of ensuring that if the original file is modified or deleted, the CoW linked files remain unmodified by lazily copying the original file's bytes into each link. Unlike symlinks or hardlinks, writes to CoW links do not write through to the original file, as the filesystem breaks the link and copies in a lazy fashion. This enables scenarios like file caches where a single copy of a file held in a content-addressable or other store is safely linked to many locations in a filesystem with low I/O overhead.

This library allows a .NET developer to:

* Discover whether CoW links are allowed between two filesystem paths,
* Discover whether CoW links are allowed for a directory tree based at a specific root directory,
* Create CoW links.

Discovery is important, as different operating systems and different filesystems available for those operating systems provide varying levels of CoW link suppport:

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
* 0.1.0 July 2021: Windows ReFS support. Mac and Linux throw NotSupportedException.


## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft 
trademarks or logos is subject to and must follow 
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
