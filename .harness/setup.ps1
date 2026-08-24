[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$canonicalSkills = Join-Path $PSScriptRoot "skills"
$agentsRoot = Join-Path $repositoryRoot ".agents"
$discoveryPath = Join-Path $agentsRoot "skills"

if (-not (Test-Path -LiteralPath $canonicalSkills -PathType Container)) {
    throw "Canonical skill directory was not found: $canonicalSkills"
}

if (-not (Test-Path -LiteralPath $agentsRoot -PathType Container)) {
    throw "OpenCode discovery directory was not found: $agentsRoot"
}

if (Test-Path -LiteralPath $discoveryPath) {
    $existing = Get-Item -LiteralPath $discoveryPath -Force
    if ($existing.LinkType -ne "Junction") {
        throw "Discovery path already exists and is not a junction: $discoveryPath"
    }

    $actualTargetValue = [string]($existing.Target | Select-Object -First 1)
    $actualTarget = [IO.Path]::GetFullPath($actualTargetValue).TrimEnd("\")
    $expectedTarget = (Resolve-Path -LiteralPath $canonicalSkills).Path.TrimEnd("\")
    if ($actualTarget -ne $expectedTarget) {
        throw "Discovery junction targets '$actualTarget' instead of '$expectedTarget'. Remove it explicitly and rerun setup."
    }

    Write-Output "OpenCode skill discovery is already configured: $discoveryPath"
    exit 0
}

New-Item -ItemType Junction -Path $discoveryPath -Target $canonicalSkills | Out-Null
Write-Output "Created OpenCode skill discovery junction: $discoveryPath -> $canonicalSkills"
