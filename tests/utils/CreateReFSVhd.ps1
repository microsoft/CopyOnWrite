# Copyright (c) Microsoft Corporation. All Rights Reserved.
# Licensed under the MIT License.

<#
.SYNOPSIS
Creates a VHD with a ReFS filesystem mounted to the provided drive letter.
.DESCRIPTION
For unit and integration testing for CopyOnWrite.
.PARAMETER DriveLetter
The drive letter to which to mount the VHD.
#>

Param(
  [Parameter(Mandatory=$true)]
  [ValidateNotNullOrEmpty()]
  [char]$DriveLetter)

Set-StrictMode -Version latest
$ErrorActionPreference = 'Stop'

$vhdFolder = '.\'
if (!(Test-Path -Path $vhdFolder)) {
    New-Item -ItemType directory -Path $bindFltVhdFolder
}
$vhdPath = $vhdFolder + $DriveLetter + '.vhd'

Write-Host "Creating new VHD at $vhdPath"

# Size is large to support testing multiple-block ReFS clones beyond ReFS block limit.
$partition = New-VHD -Path $vhdPath -Fixed -SizeBytes 8GB | Mount-VHD -Passthru | Initialize-Disk -PartitionStyle MBR -Passthru -Confirm:$false | New-Partition -UseMaximumSize
Write-Host "Formatting new VHD"
$partition | Format-Volume -NewFileSystemLabel CopyOnWriteReFSTestDrive -FileSystem ReFS -Confirm:$false -Force
# The drive letter must be assigned after everything else instead of when calling New-Partition, as calling "New-Partition -AssignDriveLetter" will create a dialog prompt asking to format the drive
Write-Host "Setting drive letter to $DriveLetter"
$partition | Set-Partition -NewDriveLetter $DriveLetter

# Set exit code to 0 (successful) if the path does exist
# Set exit code to 1 (failed) if the path doesn't exist
exit !(Test-Path -Path ($DriveLetter +":\"))
