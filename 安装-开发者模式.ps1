$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path -Path $PSScriptRoot -ChildPath 'AppxManifest.xml'
$executablePath = Join-Path -Path $PSScriptRoot -ChildPath 'CodexToastProbe.exe'

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "找不到程序清单：$manifestPath"
}

Add-AppxPackage -Register $manifestPath -ForceUpdateFromAnyVersion
Write-Host '程序包身份注册完成。正在启动 Codex Toast Monitor。'
Start-Process -FilePath $executablePath -WorkingDirectory $PSScriptRoot
