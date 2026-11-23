# 去掉快捷方式左下角箭头（需管理员）
# 用法：右键本文件 →「使用 PowerShell 运行」，或双击后在弹出的 UAC 窗口点「是」
# 原理：删除 lnkfile 的 IsShortcut 标记，Explorer 不再把 .lnk 当作“快捷方式”画箭头
# 说明：不改动任何图标资源，因此不会出现黑块/空白图标；以后新装的软件同样无箭头

$needAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $needAdmin) {
    Start-Process powershell -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-File',$PSCommandPath
    exit
}

$log = 'C:\Users\Public\df_arrow_result.txt'
"=== $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $log -Encoding utf8BOM

# 1. 清掉历史上的 Shell Icons\29 覆盖（这条会让快捷方式图标被整块替换成黑块/空白）
foreach ($k in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons','HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons')) {
    try { Remove-ItemProperty -Path $k -Name 29 -Force -ErrorAction Stop; "已移除 $k\29" | Out-File $log -Append -Encoding utf8BOM } catch { }
}

# 2. 删除 IsShortcut 标记
$lk = 'Registry::HKEY_CLASSES_ROOT\lnkfile'
try {
    Remove-ItemProperty -LiteralPath $lk -Name 'IsShortcut' -Force -ErrorAction Stop
    "IsShortcut 已删除" | Out-File $log -Append -Encoding utf8BOM
} catch {
    "IsShortcut 删除失败：" + $_.Exception.Message | Out-File $log -Append -Encoding utf8BOM
}

# 3. 重建图标缓存
$ed = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
Get-ChildItem -LiteralPath $ed -Filter 'iconcache*' -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Start-Process explorer.exe
"完成，资源管理器已重启" | Out-File $log -Append -Encoding utf8BOM
"结果日志：$log" | Out-File $log -Append -Encoding utf8BOM
