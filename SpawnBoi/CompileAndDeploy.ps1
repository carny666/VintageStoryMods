param(
    [switch]$Quick,                # Quick mode: auto-yes to all prompts (except launch & clipboard which follow defaults)
    [switch]$AutoYes,              # Alias: force yes to all confirmations (takes precedence over defaults)
    [switch]$NoVersionIncrement    # Skip version increment even if default says yes
)

# Clean and compile in Release, then increment version, then package & deploy with user confirmations.
$ErrorActionPreference = 'Stop'

# =========================
# Configuration Variables
# =========================
$UserName               = 'b_e_c'            # Username previously hardcoded in paths
$ModId                  = 'spawnboi'         # Base Mod ID / domain / output folder name
$AssemblyName           = $ModId             # If assembly name differs, change here
$ProjectName            = $ModId             # Project (csproj) name
$Configuration          = 'Release'          # Build configuration
$KeepDeployedVersions   = 0                  # How many deployed zip versions to keep in destination BEFORE new deploy
$DefaultKeepProjectZips = 5                  # (Reserved for future) how many to keep locally if added
$AutoCleanupDestination = $true              # Always cleanup destination old zips prior to deploy
$DefaultIncrementPatch  = $true              # Default choice when asked to increment version
$DefaultRunClean        = $true              # Default choice for clean
$DefaultRunBuild        = $true              # Default choice for build
$DefaultCopyModInfo     = $true              # Default choice to copy modinfo into output
$DefaultCreatePackage   = $true              # Default choice to package
$DefaultDeploy          = $true              # Default choice to deploy
$DefaultClipboardHelper = $false             # Default choice to copy helper command
$DefaultLaunchGame      = $false             # Default choice to launch game

if ($NoVersionIncrement) { $DefaultIncrementPatch = $false }

# Derived user paths
$UserRoot    = Join-Path 'C:\Users' $UserName
$Destination = Join-Path $UserRoot 'AppData\Roaming\VintagestoryData\Mods'
$GameExe     = Join-Path $UserRoot 'AppData\Roaming\Vintagestory\Vintagestory.exe'

# Derived / path variables (do not normally edit)
$ProjectDir   = $PSScriptRoot
$ProjectFile  = Join-Path $ProjectDir ("$ProjectName.csproj")
$BuildOutRoot = Join-Path $ProjectDir "bin\$Configuration\Mods"
$Source       = Join-Path $BuildOutRoot $ModId
$ModInfoPath  = Join-Path $ProjectDir 'modinfo.json'

# Placeholder; set after reading modinfo
$FileName     = "$ModId-unknownversion.zip"
$ZipPath      = Join-Path $ProjectDir $FileName

function Confirm-Action {
    param(
        [string]$Message,
        [switch]$DefaultYes
    )
    if ($script:Quick -or $script:AutoYes) { return $true }
    $suffix = if ($DefaultYes) { '[Y/n]' } else { '[y/N]' }
    while ($true) {
        $ans = Read-Host "${Message} $suffix"
        if ([string]::IsNullOrWhiteSpace($ans)) { return [bool]$DefaultYes }
        switch ($ans.ToLower()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default { Write-Host 'Please answer y or n.' -ForegroundColor Yellow }
        }
    }
}

function Cleanup-OldZips {
    param(
        [string]$Folder,
        [string]$ModId,
        [int]$Keep = 2
    )
    if (-not (Test-Path $Folder)) { return }
    $pattern = "^$([regex]::Escape($ModId))-([0-9]+\.[0-9]+\.[0-9]+)\.zip$"
    $files = Get-ChildItem -Path $Folder -Filter "$ModId-*.zip" -ErrorAction SilentlyContinue | Where-Object { $_.Name -match $pattern } | ForEach-Object {
        $verString = [regex]::Match($_.Name, $pattern).Groups[1].Value
        try { [pscustomobject]@{ File = $_; Version = [version]$verString } } catch { $null }
    } | Where-Object { $_ -ne $null }
    if (-not $files) { Write-Host "No matching $ModId zip files to consider in $Folder" -ForegroundColor DarkGray; return }
    $ordered = $files | Sort-Object Version -Descending
    $toRemove = $ordered | Select-Object -Skip $Keep
    if (-not $toRemove) { Write-Host "Cleanup ($Folder): nothing to delete (already <= $Keep versions)" -ForegroundColor DarkGray; return }
    foreach ($entry in $toRemove) {
        try { Remove-Item $entry.File.FullName -Force; Write-Host "Removed old package: $($entry.File.Name)" -ForegroundColor DarkYellow }
        catch { Write-Host "Failed to remove $($entry.File.FullName): $_" -ForegroundColor Red }
    }
}

trap {
    Write-Host "`n=== FATAL ERROR ===" -ForegroundColor Red
    Write-Host ($_.Exception.Message) -ForegroundColor Red
    if ($_.Exception.InnerException) { Write-Host "Inner: $($_.Exception.InnerException.Message)" -ForegroundColor DarkRed }
    Write-Host "`nSTACK TRACE:" -ForegroundColor Yellow
    Write-Host $_.Exception.ToString() -ForegroundColor DarkGray
    Write-Host "`n(Press Enter to close)" -ForegroundColor Cyan
    [void](Read-Host)
    exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found on PATH. Install .NET 8 SDK." }
if (-not (Test-Path $ProjectFile)) { throw "Project file not found at $ProjectFile" }

Write-Host "=== Combined Build & Deploy Script (Mod: $ModId) ===" -ForegroundColor Cyan

# Prompt user up-front to skip all following prompts and auto-answer Yes to all
if (-not $Quick -and -not $AutoYes) {
    $resp = Read-Host 'Skip all following prompts and auto-answer Yes to all? [y/N]'
    if ($resp -and ($resp.Trim().ToLower() -in @('y','yes'))) {
        $script:Quick = $true
        $script:AutoYes = $true
        Write-Host 'Auto-Yes mode enabled for this run.' -ForegroundColor Yellow
    }
}

if ($Quick -or $AutoYes) { Write-Host 'Running in QUICK/AUTO mode (prompts auto-accepted).' -ForegroundColor Yellow }
if ($NoVersionIncrement) { Write-Host 'Version increment disabled by parameter.' -ForegroundColor Yellow }

# CLEAN
if (Confirm-Action 'Run dotnet clean?' -DefaultYes:([bool]$DefaultRunClean)) {
    Write-Host '=== Cleaning ===' -ForegroundColor Cyan
    & dotnet clean $ProjectFile -c $Configuration | Out-Host
} else { Write-Host 'Skipped clean' -ForegroundColor Yellow }

# BUILD
if (Confirm-Action 'Run dotnet build?' -DefaultYes:([bool]$DefaultRunBuild)) {
    Write-Host '=== Building (compile) ===' -ForegroundColor Cyan
    & dotnet build $ProjectFile -c $Configuration --nologo | Out-Host
} else { Write-Host 'Skipped build (existing artifacts will be used if present)' -ForegroundColor Yellow }

# VERSION INCREMENT
$didVersionIncrement = $false
$json = $null
if (Test-Path $ModInfoPath) {
    $json = Get-Content $ModInfoPath | ConvertFrom-Json
    if (Confirm-Action 'Increment patch version in modinfo.json?' -DefaultYes:([bool]$DefaultIncrementPatch)) {
        Write-Host '=== Incrementing mod version ===' -ForegroundColor Cyan
        $oldVersion = $json.version
        $versionParts = $json.version.Split('.')
        if ($versionParts.Length -lt 3) { throw "Version format unexpected: $($json.version)" }
        $versionParts[2] = ([int]$versionParts[2]) + 1
        $json.version = "$(($versionParts[0])).$(($versionParts[1])).$(($versionParts[2]))"
        $json | ConvertTo-Json -Depth 10 | Set-Content $ModInfoPath -Encoding UTF8
        Write-Host "Version: $oldVersion -> $($json.version)" -ForegroundColor Green
        $didVersionIncrement = $true
        $json = Get-Content $ModInfoPath | ConvertFrom-Json
    } else { Write-Host 'Skipped version increment' -ForegroundColor Yellow }
} else { Write-Host 'modinfo.json not found; skip version increment' -ForegroundColor Red }

# Derive file name with version
if ($json -ne $null) {
    $idFromJson = $json.modid; if (-not $idFromJson) { $idFromJson = $json.ModID }
    if ($idFromJson) { $ModId = $idFromJson }
    $FileName = "$ModId-$($json.version).zip"
} else { $FileName = "$ModId-unknownversion.zip" }
$ZipPath = Join-Path $ProjectDir $FileName
Write-Host "Package file will be: $FileName" -ForegroundColor Cyan

if (-not (Test-Path $Source)) { Write-Host "Warning: build output folder not found yet: $Source" -ForegroundColor Yellow }

# Ensure modinfo.json resides in SAME folder as the dll for packaging
$hasOutput = Test-Path $Source
$hasModInfo = Test-Path $ModInfoPath
if ($hasOutput -and $hasModInfo) {
    if (Confirm-Action 'Copy modinfo.json into build output?' -DefaultYes:([bool]$DefaultCopyModInfo)) {
        Copy-Item $ModInfoPath (Join-Path $Source 'modinfo.json') -Force
    } else { Write-Host 'Skipped copying modinfo.json (zip may miss it)' -ForegroundColor Yellow }
}

# PACKAGE
$doPackage = Confirm-Action 'Create zip package?' -DefaultYes:([bool]$DefaultCreatePackage)
if ($doPackage) {
    Write-Host '=== Packaging ===' -ForegroundColor Cyan
    if (-not $hasOutput) { throw "Cannot package: build output folder missing: $Source" }
    $dllPath = Join-Path $Source ("$AssemblyName.dll")
    if (-not (Test-Path $dllPath)) { Write-Host 'Warning: DLL missing; proceeding may create incomplete zip.' -ForegroundColor Yellow }
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    Push-Location $Source
    try {
        $items = Get-ChildItem -Force
        if ($items.Count -eq 0) { throw "No build artifacts in $Source" }
        Compress-Archive -Path * -DestinationPath $ZipPath -Force
    } finally { Pop-Location }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zf = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        $rootEntries = $zf.Entries | Where-Object { $_.FullName -notmatch '/' } | Select-Object -ExpandProperty FullName
        if (("$AssemblyName.dll") -notin $rootEntries) { Write-Host "Warning: $AssemblyName.dll not at zip root." -ForegroundColor Yellow }
        if ('modinfo.json' -notin $rootEntries) { Write-Host 'Warning: modinfo.json not at zip root.' -ForegroundColor Yellow }
    } finally { $zf.Dispose() }
    Write-Host "Zip creation complete: $FileName" -ForegroundColor Green
} else { Write-Host 'Skipped packaging' -ForegroundColor Yellow }

# DEPLOY (cleanup destination beforehand)
$zipExists = (Test-Path $ZipPath)
$doDeploy = $doPackage -and $zipExists -and (Confirm-Action "Deploy $FileName to Mods folder?" -DefaultYes:([bool]$DefaultDeploy))
if ($doDeploy) {
    if ($AutoCleanupDestination) {
        Write-Host '=== Pre-deploy cleanup of older deployed versions ===' -ForegroundColor Cyan
        Cleanup-OldZips -Folder $Destination -ModId $ModId -Keep $KeepDeployedVersions
    }
    Write-Host '=== Deploying ===' -ForegroundColor Cyan
    if (-not (Test-Path $Destination)) { New-Item -ItemType Directory -Path $Destination | Out-Null }
    Copy-Item -Path $ZipPath -Destination (Join-Path $Destination $FileName) -Force
    Write-Host "Deployed to $Destination" -ForegroundColor Green
} else { Write-Host 'Skipped deployment' -ForegroundColor Yellow }

# OPTIONAL: VIEW COMMAND CLIPBOARD
if ($doPackage -and $zipExists -and (Confirm-Action 'Copy zip inspection helper command to clipboard?' -DefaultYes:([bool]$DefaultClipboardHelper))) {
    $DisplayCommand = @"
powershell -NoLogo -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; `$zip='$FileName'; `$z=[IO.Compression.ZipFile]::OpenRead(`$zip); `$e=`$z.Entries | Where-Object { `$_.FullName -ieq 'modinfo.json' }; if (`$e) { `$sr=New-Object IO.StreamReader(`$e.Open()); `$c=`$sr.ReadToEnd(); `$sr.Dispose(); `$z.Dispose(); Write-Host `$c } else { Write-Host 'modinfo.json not found in zip.' }"
"@.Trim()
    try {
        if (Get-Command Set-Clipboard -ErrorAction SilentlyContinue) { Set-Clipboard -Value $DisplayCommand; Write-Host 'Helper command copied to clipboard.' -ForegroundColor Yellow }
        elseif (Get-Command clip.exe -ErrorAction SilentlyContinue) { $DisplayCommand | clip.exe; Write-Host 'Helper command copied to clipboard (clip.exe).' -ForegroundColor Yellow }
    } catch { Write-Host "Clipboard set failed: $_" -ForegroundColor Yellow }
}

# LAUNCH GAME
$canLaunch = Test-Path $GameExe
if ($canLaunch -and (Confirm-Action 'Launch Vintage Story now?' -DefaultYes:([bool]$DefaultLaunchGame))) {
    try { Write-Host 'Launching Vintage Story...' -ForegroundColor Cyan; Start-Process -FilePath $GameExe -WorkingDirectory (Split-Path $GameExe) }
    catch { Write-Host "Failed to launch game: $_" -ForegroundColor Red }
} else { Write-Host 'Skipped game launch' -ForegroundColor Yellow }

Write-Host 'Script completed.' -ForegroundColor Green
if ($didVersionIncrement) { Write-Host 'Remember to commit updated modinfo.json.' -ForegroundColor DarkCyan }
