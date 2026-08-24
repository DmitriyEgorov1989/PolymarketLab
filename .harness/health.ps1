[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$lockPath = Join-Path $PSScriptRoot "harness.lock"
$skillsRoot = Join-Path $PSScriptRoot "skills"
$registryPath = Join-Path $skillsRoot "REGISTRY.md"
$discoveryPath = Join-Path (Join-Path $repositoryRoot ".agents") "skills"

if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "Harness lock was not found: $lockPath"
}

$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($lock.schema -ne 1) {
    throw "Unsupported harness lock schema: $($lock.schema)"
}

$expectedSources = @{
    "agent-harness" = @{
        role = "harness"
        repository = "https://github.com/KirillSachkov/agent-harness.git"
        revision = "5ab9b5d44c57bfa042e2f62730af95c0e9ab7dc4"
        license = "MIT"
        license_path = ".harness/licenses/agent-harness-MIT.txt"
        license_sha256 = "fbb332384199104b1663664f018434b3fffc2cc02a91cbd3853cd02f9c27ed4b"
    }
    "mattpocock-via-harness" = @{
        role = "skill-content"
        repository = "https://github.com/mattpocock/skills.git"
        revision = "9c9f36ccd3995266cd675468af71639c8dde1ec5"
        via_source = "agent-harness"
        license = "MIT"
        license_path = ".harness/licenses/mattpocock-skills-MIT.txt"
        license_sha256 = "0e7ac423bf2c6e223b7c5b156f8cf72da49d748e56a1641402c31f22ad07dbb5"
    }
    "superpowers-direct" = @{
        role = "skill-content"
        repository = "https://github.com/obra/superpowers.git"
        revision = "b36e0829c6d0140e93cfef2ca599b1b07d4a7797"
        license = "MIT"
        license_path = ".harness/licenses/superpowers-MIT.txt"
        license_sha256 = "a37e0e9697144819e1d965176ac4ae5bc3fa02d11e7812036bbcadf6dafe2400"
    }
    "project-local" = @{
        role = "skill-content"
        provenance = "project-local"
    }
}

if ($lock.package_version -ne "1.2.0") {
    throw "Unexpected harness package version: $($lock.package_version)"
}

if ($lock.sources.Count -ne $expectedSources.Count) {
    throw "Harness lock must contain exactly $($expectedSources.Count) pinned sources."
}

$actualSourceIds = @($lock.sources | ForEach-Object { $_.id })
if (Compare-Object -ReferenceObject @($expectedSources.Keys) -DifferenceObject $actualSourceIds) {
    throw "Harness lock source inventory does not match the pinned multi-source set."
}

foreach ($source in $lock.sources) {
    if (-not $expectedSources.ContainsKey($source.id)) {
        throw "Unexpected harness source: $($source.id)"
    }

    foreach ($property in $expectedSources[$source.id].GetEnumerator()) {
        if ([string]$source.($property.Key) -ne [string]$property.Value) {
            throw "Pinned source metadata drift detected: $($source.id).$($property.Key)"
        }
    }

    if ($source.PSObject.Properties.Name -contains "license_path") {
        $licensePath = Join-Path $repositoryRoot $source.license_path.Replace("/", [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
            throw "Pinned source license is missing: $($source.license_path)"
        }

        $licenseHash = (Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($licenseHash -ne $source.license_sha256.ToLowerInvariant()) {
            throw "Pinned source license drift detected: $($source.id)"
        }
    }
}

$expectedSkills = @(
    "brainstorming",
    "code-review",
    "codebase-design",
    "domain-modeling",
    "polymarket-integration",
    "polymarketlab-feature",
    "research",
    "systematic-debugging",
    "tdd",
    "writing-plans",
    "writing-skills"
)

$actualSkillNames = @($lock.skills | ForEach-Object { $_.name })
if ($actualSkillNames.Count -ne $expectedSkills.Count -or
    (Compare-Object -ReferenceObject $expectedSkills -DifferenceObject $actualSkillNames)) {
    throw "Harness lock skill inventory does not match the canonical eleven-skill set."
}

foreach ($skill in $lock.skills) {
    if (-not $expectedSources.ContainsKey($skill.source_id) -or
        $expectedSources[$skill.source_id].role -ne "skill-content") {
        throw "Skill has an unknown content source: $($skill.name)"
    }

    if ($skill.source_id -eq "project-local") {
        $expectedPath = ".harness/skills/$($skill.name)"
        if ($skill.source_path -ne $expectedPath -or $skill.content_mode -ne "project-authored") {
            throw "Project-local skill provenance is incomplete: $($skill.name)"
        }
    }
    elseif (-not $skill.source_path -or $skill.content_mode -ne "byte-for-byte") {
        throw "Upstream skill provenance is incomplete: $($skill.name)"
    }
}

$skillDirectories = @(Get-ChildItem -LiteralPath $skillsRoot -Directory | ForEach-Object { $_.Name })
if ($skillDirectories.Count -ne $expectedSkills.Count -or
    (Compare-Object -ReferenceObject $expectedSkills -DifferenceObject $skillDirectories)) {
    throw "Canonical skill directory inventory does not match the lock."
}

foreach ($file in $lock.files.PSObject.Properties) {
    if (-not $file.Name.StartsWith(".harness/skills/", [StringComparison]::Ordinal)) {
        throw "Locked file is outside the canonical skill store: $($file.Name)"
    }

    $relativePath = $file.Name.Replace("/", [IO.Path]::DirectorySeparatorChar)
    $fullPath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Locked file is missing: $($file.Name)"
    }

    $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedHash = [string]$file.Value
    if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
        throw "Locked file drift detected: $($file.Name)"
    }
}

if (-not (Test-Path -LiteralPath $registryPath -PathType Leaf)) {
    throw "Skill registry was not found: $registryPath"
}

$registry = Get-Content -LiteralPath $registryPath -Raw
if ([regex]::Matches($registry, '(?m)^\| `[^`]+` \|').Count -ne $expectedSkills.Count) {
    throw "REGISTRY.md must contain exactly $($expectedSkills.Count) skill entries."
}

foreach ($skill in $lock.skills) {
    $skillPath = Join-Path $skillsRoot $skill.name
    $skillManifest = Join-Path $skillPath "SKILL.md"
    if (-not (Test-Path -LiteralPath $skillManifest -PathType Leaf)) {
        throw "Skill manifest was not found: $skillManifest"
    }

    $manifest = Get-Content -LiteralPath $skillManifest -Raw
    if ($manifest -notmatch "(?m)^name:\s+$([regex]::Escape($skill.name))\s*$") {
        throw "Skill name does not match its directory: $($skill.name)"
    }

    if ($registry -notmatch [regex]::Escape("| ``$($skill.name)`` |")) {
        throw "Skill is missing from REGISTRY.md: $($skill.name)"
    }

}

foreach ($managedFile in Get-ChildItem -LiteralPath $skillsRoot -Recurse -File) {
    $relativePath = $managedFile.FullName.Substring($repositoryRoot.Length + 1).Replace("\", "/")
    if ($lock.files.PSObject.Properties.Name -notcontains $relativePath) {
        throw "Untracked file found in canonical skill store: $relativePath"
    }
}

$managedFileCount = @(Get-ChildItem -LiteralPath $skillsRoot -Recurse -File).Count
if (@($lock.files.PSObject.Properties).Count -ne $managedFileCount) {
    throw "Locked file inventory does not match the canonical skill store."
}

if (-not (Test-Path -LiteralPath $discoveryPath)) {
    throw "OpenCode discovery junction is missing. Run .\.harness\setup.ps1."
}

$discovery = Get-Item -LiteralPath $discoveryPath -Force
if ($discovery.LinkType -ne "Junction") {
    throw "OpenCode discovery path is not a Windows junction: $discoveryPath"
}

$actualTargetValue = [string]($discovery.Target | Select-Object -First 1)
$actualTarget = [IO.Path]::GetFullPath($actualTargetValue).TrimEnd("\")
$expectedTarget = (Resolve-Path -LiteralPath $skillsRoot).Path.TrimEnd("\")
if ($actualTarget -ne $expectedTarget) {
    throw "OpenCode discovery junction has an unexpected target: $actualTarget"
}

$placeholderFiles = @(
    (Join-Path $repositoryRoot "AGENTS.md"),
    (Join-Path $PSScriptRoot "README.md")
)
foreach ($placeholderFile in $placeholderFiles) {
    if ((Get-Content -LiteralPath $placeholderFile -Raw) -match "\{\{[^}]+\}\}") {
        throw "Unresolved placeholder found in: $placeholderFile"
    }
}

Write-Output "Harness health check passed for $($lock.skills.Count) skills."
