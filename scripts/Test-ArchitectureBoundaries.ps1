Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

$allowedReferences = @{
    'Muse' = @('Muse.Shell')
    'Muse.Shell' = @(
        'Muse.Editor.Core',
        'Muse.Editor.Rendering',
        'Muse.Workspace',
        'Muse.Assets',
        'Muse.Export',
        'Muse.ThemeUX',
        'Muse.AI.RAG'
    )
    'Muse.Editor.Core' = @()
    'Muse.Editor.Rendering' = @('Muse.Editor.Core')
    'Muse.Workspace' = @()
    'Muse.Assets' = @()
    'Muse.Export' = @()
    'Muse.ThemeUX' = @()
    'Muse.AI.RAG' = @()
    'Muse.Desktop' = @('Muse')
    'Muse.Browser' = @('Muse')
    'Muse.iOS' = @('Muse')
    'Muse.Android' = @('Muse')
    'Muse.Editor.Core.Tests' = @('Muse.Editor.Core')
    'Muse.Editor.Rendering.Tests' = @('Muse.Editor.Rendering')
}

$projectFiles = Get-ChildItem -Path $repoRoot -Recurse -Filter *.csproj -File |
    Where-Object {
        $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\obj\\'
    }

$projectNameByPath = @{}
foreach ($projectFile in $projectFiles) {
    $projectNameByPath[$projectFile.FullName] = [System.IO.Path]::GetFileNameWithoutExtension($projectFile.Name)
}

$violations = [System.Collections.Generic.List[string]]::new()
$unknownProjects = [System.Collections.Generic.List[string]]::new()

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -Path $projectFile.FullName
    $projectName = $projectNameByPath[$projectFile.FullName]

    if (-not $allowedReferences.ContainsKey($projectName)) {
        $unknownProjects.Add("未配置边界规则的项目: $projectName")
        continue
    }

    $allowed = @($allowedReferences[$projectName])
    $itemGroups = if ($projectXml.Project.PSObject.Properties['ItemGroup']) {
        @($projectXml.Project.ItemGroup)
    }
    else {
        @()
    }

    $projectReferences = @(
        foreach ($itemGroup in $itemGroups) {
            if (-not $itemGroup.PSObject.Properties['ProjectReference']) {
                continue
            }

            foreach ($projectReference in @($itemGroup.ProjectReference)) {
                if ($projectReference -and $projectReference.Include) {
                    $projectReference
                }
            }
        }
    )

    $references = @(
        $projectReferences | ForEach-Object {
            $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $projectFile.DirectoryName $_.Include))
            if ($projectNameByPath.ContainsKey($resolvedPath)) {
                $projectNameByPath[$resolvedPath]
            }
            else {
                [System.IO.Path]::GetFileNameWithoutExtension($resolvedPath)
            }
        }
    )

    foreach ($reference in $references) {
        if ($reference -notin $allowed) {
            $violations.Add("禁止引用: $projectName -> $reference")
        }
    }
}

if ($unknownProjects.Count -gt 0) {
    foreach ($unknownProject in $unknownProjects) {
        Write-Host $unknownProject -ForegroundColor Yellow
    }
}

if ($violations.Count -gt 0) {
    Write-Host '发现架构边界违规:' -ForegroundColor Red
    foreach ($violation in $violations) {
        Write-Host " - $violation" -ForegroundColor Red
    }

    exit 1
}

Write-Host "架构边界检查通过，共检查 $($projectFiles.Count) 个项目。" -ForegroundColor Green
