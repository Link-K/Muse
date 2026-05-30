Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommandInfo = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetCommand = $null

if ($dotnetCommandInfo) {
    $dotnetCommand = $dotnetCommandInfo.Source
}

if (-not $dotnetCommand) {
    $fallbackDotnet = 'C:\Program Files\dotnet\dotnet.exe'
    if (Test-Path $fallbackDotnet) {
        $dotnetCommand = $fallbackDotnet
    }
    else {
        throw 'dotnet executable was not found.'
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
    Write-Host "[PASS] $Name" -ForegroundColor Green
}

Push-Location $repoRoot
try {
    Invoke-Step -Name 'Build Solution' -Action {
        & $dotnetCommand build '.\Muse.sln' '/property:GenerateFullPaths=true' '/consoleloggerparameters:NoSummary%3BForceNoAlign'
    }

    Invoke-Step -Name 'Architecture Boundary Check' -Action {
        & '.\scripts\Test-ArchitectureBoundaries.ps1'
    }

    Invoke-Step -Name 'Core Contract Tests' -Action {
        & $dotnetCommand test '.\Muse.Editor.Core.Tests\Muse.Editor.Core.Tests.csproj'
    }

    Invoke-Step -Name 'Rendering Contract Tests' -Action {
        & $dotnetCommand test '.\Muse.Editor.Rendering.Tests\Muse.Editor.Rendering.Tests.csproj'
    }

    Invoke-Step -Name 'Workspace Contract Tests' -Action {
        & $dotnetCommand test '.\Muse.Workspace.Tests\Muse.Workspace.Tests.csproj'
    }

    Invoke-Step -Name 'UI Mapping Tests' -Action {
        & $dotnetCommand test '.\Muse.Tests\Muse.Tests.csproj'
    }

    Invoke-Step -Name 'Desktop Smoke Build' -Action {
        & $dotnetCommand build '.\Muse.Desktop\Muse.Desktop.csproj'
    }

    Write-Host 'Quality gate passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
