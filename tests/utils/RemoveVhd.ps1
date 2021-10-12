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

Dismount-VHD -Path $vhdPath
del $vhdPath
