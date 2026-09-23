#Requires -Version 5.1
<#
.SYNOPSIS
Validates and previews mission packs, publishing only with -Publish.
.DESCRIPTION
Targets an explicitly selected, existing SQLite World .db file. Builds the
already-restored MissionTool project and preserves its validation/publication
rules. Stop or drain Game before publishing. No database is created or reset.
.PARAMETER WorldDatabasePath
Required physical SQLite World filename, including its lowercase .db suffix.
Relative paths resolve from the caller's working directory.
.PARAMETER PackDirectory
Release directory containing packs and client-bindings.json. Defaults to the
repository's content/missions/bootcamp directory, regardless of caller location.
.PARAMETER Publish
Apply the release after validation and preview. Without this flag, no release
is published. No interactive confirmation is used.
.EXAMPLE
.\scripts\Update-MissionPacks.ps1 -WorldDatabasePath D:\RasaData\rasaworld.db
.EXAMPLE
.\scripts\Update-MissionPacks.ps1 -WorldDatabasePath D:\RasaData\rasaworld.db -Publish
#>
[CmdletBinding()]
param(
    [string] $WorldDatabasePath,
    [string] $PackDirectory,
    [switch] $Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$locationPushed = $false

function Invoke-MissionToolCommand {
    param([string] $Stage, [string[]] $Arguments)

    & $dotnetCommand @Arguments
    if ($LASTEXITCODE -ne 0) {
        $message = "$Stage failed with exit code $LASTEXITCODE. See the tool output above."
        if ($Stage -eq 'Build') {
            $message += ' Restore repository dependencies first if build assets are missing.'
        }
        $failure = [InvalidOperationException]::new($message)
        $failure.Data['NativeExitCode'] = $LASTEXITCODE
        throw $failure
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($WorldDatabasePath)) {
        throw 'Specify -WorldDatabasePath with the existing SQLite World .db file. No database is inferred from Game configuration.'
    }
    $database = Get-Item -LiteralPath $WorldDatabasePath
    if ($database -isnot [System.IO.FileInfo] -or $database.Extension -cne '.db') {
        throw '-WorldDatabasePath must be an existing file with a lowercase .db extension.'
    }
    $databaseBase = $database.FullName.Substring(0, $database.FullName.Length - 3)
    $repository = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $toolProject = [System.IO.Path]::Combine($repository, 'src', 'Rasa.MissionTool', 'Rasa.MissionTool.csproj')
    if (-not (Test-Path -LiteralPath $toolProject -PathType Leaf)) {
        throw "MissionTool project not found at $toolProject."
    }
    if ($PSBoundParameters.ContainsKey('PackDirectory')) {
        if ([string]::IsNullOrWhiteSpace($PackDirectory)) {
            throw '-PackDirectory cannot be empty.'
        }
        $packs = Get-Item -LiteralPath $PackDirectory
    }
    else {
        $packs = Get-Item -LiteralPath ([System.IO.Path]::Combine($repository, 'content', 'missions', 'bootcamp'))
    }
    if ($packs -isnot [System.IO.DirectoryInfo]) {
        throw '-PackDirectory must be an existing directory.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $packs.FullName 'client-bindings.json') -PathType Leaf)) {
        throw "Missing client-bindings.json in $($packs.FullName)."
    }
    $packFiles = @(Get-ChildItem -LiteralPath $packs.FullName -Filter '*.json' -File |
        Where-Object { $_.Name -ine 'client-bindings.json' -and $_.Name -inotlike '*.schema.json' } |
        Sort-Object Name)
    if ($packFiles.Count -eq 0) {
        throw "No mission packs found directly in $($packs.FullName)."
    }
    $dotnetCommand = (Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1).Source

    Write-Host "World database: $($database.FullName)"
    Write-Host "Mission packs: $($packs.FullName)"
    Write-Host ("Mode: " + $(if ($Publish) { 'Publish (Game must be stopped or drained)' } else { 'Preview only' }))

    Push-Location -LiteralPath $repository
    $locationPushed = $true
    Invoke-MissionToolCommand -Stage 'Build' -Arguments @(
        'build', $toolProject, '--configuration', 'Release', '--no-restore', '--nologo', '--verbosity', 'quiet')
    $run = @('run', '--project', $toolProject, '--configuration', 'Release', '--no-build', '--no-restore', '--')
    $target = @('--database', $databaseBase, '--directory', $packs.FullName)

    Invoke-MissionToolCommand -Stage 'Validation' -Arguments ($run + @('validate') + $target)
    $release = (Get-Content -LiteralPath $packFiles[0].FullName -Raw | ConvertFrom-Json).release
    Write-Host "Release: $release"
    Invoke-MissionToolCommand -Stage 'Diff' -Arguments ($run + @('diff') + $target)
    if ($Publish) {
        Invoke-MissionToolCommand -Stage 'Publication' -Arguments ($run + @('publish') + $target)
        Write-Host 'Publication completed. Start or restart Game to load the selected release.'
    }
    else {
        Write-Host 'Preview completed; no release was published. Use -Publish to apply the release explicitly.'
    }
}
catch {
    [Console]::Error.WriteLine("Mission pack update failed: $($_.Exception.Message)")
    if ($_.Exception.Data.Contains('NativeExitCode')) {
        exit [int]$_.Exception.Data['NativeExitCode']
    }
    exit 1
}
finally {
    if ($locationPushed) {
        Pop-Location
    }
}

exit 0
