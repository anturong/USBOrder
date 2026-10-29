#Requires -Version 5.1
<#
.SYNOPSIS
    扫描 dist，生成 WiX v4 的 Generated.wxs（目录树 + ComponentGroup）。

.DESCRIPTION
    dist 里的每个文件生成一个 Component，全部挂在 ComponentGroup "ProductFiles" 下；
    目录结构用 DirectoryRef 嵌套表达。Package.wxs 负责 Package/Feature/快捷方式。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File deploy/make-wxs.ps1
#>
[CmdletBinding()]
param(
    [string]$Dist = '',
    [string]$Output = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($Dist)) { $Dist = Join-Path $root 'dist' }
if ([string]::IsNullOrWhiteSpace($Output)) { $Output = Join-Path (Join-Path $PSScriptRoot '..') 'installer\Generated.wxs' }

$editorExe = Join-Path $Dist 'UsbConfigEditor.exe'
if (-not (Test-Path $editorExe)) {
    throw "dist 里没有 UsbConfigEditor.exe，请先运行 deploy/publish.ps1"
}

$files = @(Get-ChildItem -Path $Dist -Recurse -File)
if ($files.Count -eq 0) { throw "dist 是空的：$Dist" }

function Get-RelativeDirectory([string]$FullPath) {
    return $FullPath.Substring($Dist.Length).TrimStart('\')
}

function Get-SafeId([string]$RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return 'INSTALLFOLDER' }
    $id = $RelativePath -replace '[\\/]', '_'
    $id = $id -replace '[^A-Za-z0-9_]', '_'
    return "d_$id"
}

function Get-Xml([string]$Text) {
    return [System.Security.SecurityElement]::Escape($Text)
}

# 收集所有目录（含父级）
$dirs = New-Object 'System.Collections.Generic.HashSet[string]'
[void]$dirs.Add('')
foreach ($file in $files) {
    $rel = Get-RelativeDirectory $file.DirectoryName
    [void]$dirs.Add($rel)
    if ($rel) {
        $parts = $rel -split '\\'
        for ($i = 1; $i -lt $parts.Count; $i++) {
            [void]$dirs.Add(($parts[0..($i - 1)] -join '\'))
        }
    }
}

function Get-Children([string]$Parent) {
    $result = @()
    foreach ($dir in $dirs) {
        if ($dir -eq $Parent) { continue }
        $parentOfDir = ''
        if ($dir.Contains('\')) { $parentOfDir = $dir.Substring(0, $dir.LastIndexOf('\')) }
        if ($parentOfDir -eq $Parent) { $result += $dir }
    }
    return $result | Sort-Object
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

function Write-Directory([string]$Rel, [int]$Depth) {
    $indent = '  ' * ($Depth + 3)
    foreach ($child in (Get-Children $Rel)) {
        $name = ($child -split '\\')[-1]
        [void]$sb.AppendLine("$indent<Directory Id=""$(Get-SafeId $child)"" Name=""$(Get-Xml $name)"">")
        Write-Directory $child ($Depth + 1)
        [void]$sb.AppendLine("$indent</Directory>")
    }
}

Write-Directory '' 0
[void]$sb.AppendLine('    </DirectoryRef>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="ProductFiles">')

$index = 0
foreach ($file in $files) {
    $index++
    $rel = Get-RelativeDirectory $file.DirectoryName
    $dirId = Get-SafeId $rel
    $fileId = if ($file.Name -eq 'UsbConfigEditor.exe') { 'FileUsbConfigEditorExe' } else { "f_$index" }
    $componentId = "c_$index"

    [void]$sb.AppendLine("      <Component Id=""$componentId"" Directory=""$dirId"" Guid=""*"">")
    [void]$sb.AppendLine("        <File Id=""$fileId"" Source=""$(Get-Xml $file.FullName)"" />")
    [void]$sb.AppendLine('      </Component>')
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$text = $sb.ToString()
[System.IO.File]::WriteAllText($Output, $text, (New-Object System.Text.UTF8Encoding($true)))
Write-Host "已生成 $Output（$($files.Count) 个文件，$($dirs.Count) 个目录）"
