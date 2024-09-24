# Benchmark Data

## Windows
[Dev Drive](https://aka.ms/devdrive) is a new evolution of the ReFS filesystem starting in Windows 11 22H2 Oct 2023. It was announced at Microsoft Build 2023, and features significantly faster execution vs. earlier 22H2 ReFS or NTFS along with CoW support and a performance mode for Defender antivirus. More details can be found [here](https://aka.ms/EngMSDevDrive).

Windows benchmarks on various versions of Windows compare `System.IO.File.Copy()` with `CloneFile()`,
50 or more copies/clones of a single source file per measurement, highest performance clone flags and settings.
Defender real-time scanning was disabled in the test directory. See CoWComparisons.cs.

### Benchmark data in reverse temporal order

* Jan 2024 - Windows [Preview Win11 23H2 with copy-on-write implementation built into OS APIs](./Win11_23H2_Jan2024_10.0.26020.1000.md) (with feature flag turned on) - fixes large file clone performance
* Oct 2023 - Windows [Canary Win11 22H2 with early copy-on-write implementation built into OS APIs](./Win11_22H2_Oct2023_10.0.25982.1000.md)
* Oct 2023 - Windows [Win11 22H2 initial Dev Drive release](./Win11_22H2_Oct2023_10.0.22621.2506.md)
* May 2023 - Windows [prerelease Dev Drive](./Win11_22H2_May2023.md)
* Dec 2022 - Windows [Win11 22H2 ReFS](./Win11_22H2_Dec2022_ReFS.md)
