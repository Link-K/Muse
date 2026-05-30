Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source

if (-not $dotnetCommand) {
    $fallbackDotnet = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path $fallbackDotnet) {
        $dotnetCommand = $fallbackDotnet
    }
    else {
        throw '未找到 dotnet 可执行文件。'
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    Write-Host "[通过] $Name" -ForegroundColor Green
}

Push-Location $repoRoot
try {
    Invoke-Step -Name '解决方案构建' -Action {
        & $dotnetCommand build '.\Muse.sln' '/property:GenerateFullPaths=true' '/consoleloggerparameters:NoSummary%3BForceNoAlign'
    }

    Invoke-Step -Name '架构边界检查' -Action {
        & '.\scripts\Test-ArchitectureBoundaries.ps1'
    }

    Invoke-Step -Name 'Core 契约测试' -Action {
        & $dotnetCommand test '.\Muse.Editor.Core.Tests\Muse.Editor.Core.Tests.csproj'
    }

    Invoke-Step -Name 'Rendering 契约测试' -Action {
        & $dotnetCommand test '.\Muse.Editor.Rendering.Tests\Muse.Editor.Rendering.Tests.csproj'
    }

    Invoke-Step -Name '桌面冒烟构建' -Action {
        & $dotnetCommand build '.\Muse.Desktop\Muse.Desktop.csproj'
    }

    Write-Host '质量门禁全部通过。' -ForegroundColor Green
}
finally {
    Pop-Location
}
