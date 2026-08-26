$ErrorActionPreference = 'Stop'

$packages = Get-AppxPackage -Name 'CodexToastMonitor' -ErrorAction SilentlyContinue
if ($packages) {
    $packages | Remove-AppxPackage
    Write-Host 'Codex Toast Monitor 已从当前用户卸载。'
} else {
    Write-Host '当前用户没有已注册的 Codex Toast Monitor。'
}

Write-Host '本地日志、飞书配置和待发送队列未被删除。'
