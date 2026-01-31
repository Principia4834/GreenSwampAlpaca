# ============================================================================
# Revert Master to Working Baseline with Documentation Preserved
# ============================================================================
# This script:
# 1. Archives current broken refactor to a branch
# 2. Commits documentation files if not already committed
# 3. Resets master to working commit 0f10f0f6
# 4. Cherry-picks documentation back onto clean master
# 5. Force-pushes master (with safety checks)
#
# Usage:
#   .\revert-to-working-baseline.ps1           # Dry run (shows what would happen)
#   .\revert-to-working-baseline.ps1 -Execute  # Actually execute the revert
# ============================================================================

param(
    [switch]$Execute,
    [string]$WorkingCommit = "0f10f0f6",
    [string]$ArchiveBranchName = "archive/pulse-guide-refactor-attempt",
    [string]$DocsBranchName = "feature/pulse-guide-docs",
    [string]$DocsTagName = "docs/pulse-guide-analysis"
)

$ErrorActionPreference = "Stop"

# ANSI Colors for output
$Red = "`e[31m"
$Green = "`e[32m"
$Yellow = "`e[33m"
$Blue = "`e[34m"
$Cyan = "`e[36m"
$Reset = "`e[0m"

function Write-Header {
    param([string]$Message)
    Write-Host "`n$Cyan═══════════════════════════════════════════════════════════════$Reset" -ForegroundColor Cyan
    Write-Host "$Cyan  $Message$Reset" -ForegroundColor Cyan
    Write-Host "$Cyan═══════════════════════════════════════════════════════════════$Reset`n" -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "$Blue▶ $Message$Reset" -ForegroundColor Blue
}

function Write-Success {
    param([string]$Message)
    Write-Host "$Green✓ $Message$Reset" -ForegroundColor Green
}

function Write-Warning {
    param([string]$Message)
    Write-Host "$Yellow⚠ $Message$Reset" -ForegroundColor Yellow
}

function Write-Error-Message {
    param([string]$Message)
    Write-Host "$Red✗ $Message$Reset" -ForegroundColor Red
}

function Write-DryRun {
    param([string]$Command)
    Write-Host "$Yellow[DRY RUN] Would execute: $Command$Reset" -ForegroundColor Yellow
}

function Invoke-GitCommand {
    param(
        [string]$Command,
        [string]$Description,
        [switch]$AllowUpToDate
    )

    if ($Execute) {
        Write-Step "$Description..."
        Write-Host "  Command: git $Command" -ForegroundColor Gray

        # Execute git command - silence PowerShell's stderr handling
        $cmdParts = $Command.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
        $ErrorActionPreference = 'SilentlyContinue'
        $output = & git $cmdParts 2>&1
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'

        # Check exit code for actual errors
        if ($exitCode -ne 0) {
            Write-Error-Message "Failed: $Description (Exit code: $exitCode)"
            if ($output) {
                Write-Host ($output | Out-String) -ForegroundColor Red
            }
            throw "Git command failed: git $Command"
        }

        # Show output (convert error stream objects to strings)
        $outputStr = ($output | Out-String).Trim()
        if ($outputStr) {
            Write-Host "  $outputStr" -ForegroundColor Gray
        }

        Write-Success "$Description completed"
        return $outputStr
    }
    else {
        Write-DryRun "git $Command"
        Write-Host "  Purpose: $Description" -ForegroundColor Gray
    }
}

# ============================================================================
# PRE-FLIGHT CHECKS
# ============================================================================

Write-Header "PRE-FLIGHT SAFETY CHECKS"

# Check 1: Verify we're in a git repository
Write-Step "Checking git repository..."
try {
    $repoRoot = git rev-parse --show-toplevel 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Not a git repository"
    }
    Write-Success "Git repository found: $repoRoot"
}
catch {
    Write-Error-Message "Not in a git repository!"
    exit 1
}

# Check 2: Verify we're on master branch
Write-Step "Checking current branch..."
$currentBranch = git branch --show-current
if ($currentBranch -ne "master") {
    Write-Error-Message "Not on master branch! Currently on: $currentBranch"
    Write-Host "Please switch to master first: git checkout master" -ForegroundColor Yellow
    exit 1
}
Write-Success "On master branch"

# Check 3: Check for uncommitted changes
Write-Step "Checking for uncommitted changes..."
$status = git status --porcelain
if ($status) {
    Write-Warning "You have uncommitted changes:"
    Write-Host $status -ForegroundColor Yellow
    Write-Host ""
    
    $docFiles = @(
        "ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md",
        "COMBINED_PROPERTY_SUMMARY.md",
        "GOALS_ANALYSIS_AND_RECOMMENDATIONS.md"
    )
    
    $uncommittedDocs = $status | Where-Object { 
        $line = $_
        $docFiles | Where-Object { $line -match [regex]::Escape($_) }
    }
    
    if ($uncommittedDocs) {
        Write-Warning "Documentation files are uncommitted. Script will commit them."
    }
    
    Write-Host "Do you want to continue? The script will handle documentation files." -ForegroundColor Yellow
    if (-not $Execute) {
        Write-Host "Run with -Execute to proceed." -ForegroundColor Yellow
    }
}
else {
    Write-Success "Working directory is clean"
}

# Check 4: Verify working commit exists
Write-Step "Verifying working commit exists: $WorkingCommit..."
try {
    $commitInfo = git log -1 --oneline $WorkingCommit 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Commit not found"
    }
    Write-Success "Working commit found: $commitInfo"
}
catch {
    Write-Error-Message "Working commit $WorkingCommit not found!"
    exit 1
}

# Check 5: Verify documentation files exist
Write-Step "Checking documentation files..."
$docFiles = @(
    "ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md",
    "COMBINED_PROPERTY_SUMMARY.md",
    "GOALS_ANALYSIS_AND_RECOMMENDATIONS.md"
)

$missingDocs = @()
foreach ($doc in $docFiles) {
    if (Test-Path $doc) {
        Write-Success "Found: $doc"
    }
    else {
        $missingDocs += $doc
        Write-Error-Message "Missing: $doc"
    }
}

if ($missingDocs.Count -gt 0) {
    Write-Error-Message "Missing documentation files!"
    Write-Host "Please ensure all documentation files are created before running this script." -ForegroundColor Red
    exit 1
}

# Check 6: Verify remote connection
Write-Step "Checking remote connection..."
try {
    $remotes = git remote -v
    if ($remotes -match "origin") {
        Write-Success "Remote 'origin' configured"
    }
    else {
        Write-Warning "No 'origin' remote configured"
    }
}
catch {
    Write-Warning "Could not check remote configuration"
}

# Check 7: Fetch latest from remote
Write-Step "Fetching latest from remote..."
if ($Execute) {
    try {
        git fetch origin 2>&1 | Out-Null
        Write-Success "Fetched latest from origin"
    }
    catch {
        Write-Warning "Could not fetch from remote (continuing anyway)"
    }
}
else {
    Write-DryRun "git fetch origin"
}

# ============================================================================
# SHOW EXECUTION PLAN
# ============================================================================

Write-Header "EXECUTION PLAN"

Write-Host @"
This script will perform the following operations:

${Blue}1. CREATE ARCHIVE BRANCH${Reset}
   - Branch name: $ArchiveBranchName
   - Purpose: Preserve current refactor work
   - Will push to remote for backup

${Blue}2. COMMIT DOCUMENTATION${Reset}
   - Files:
     * ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md
     * COMBINED_PROPERTY_SUMMARY.md
     * GOALS_ANALYSIS_AND_RECOMMENDATIONS.md
   - Creates tag: $DocsTagName
   - Creates branch: $DocsBranchName

${Blue}3. RESET MASTER TO WORKING COMMIT${Reset}
   - Target commit: $WorkingCommit
   - Type: Hard reset (discards commits after $WorkingCommit)
   - ${Red}⚠ WARNING: This rewrites history!${Reset}

${Blue}4. CHERRY-PICK DOCUMENTATION${Reset}
   - Brings documentation commit onto clean master
   - Master will have: working code + docs

${Blue}5. FORCE PUSH MASTER${Reset}
   - Uses --force-with-lease (safer than --force)
   - ${Red}⚠ WARNING: Requires coordination if others use master!${Reset}

${Blue}6. VERIFY FINAL STATE${Reset}
   - Check commit history
   - Verify documentation files present
   - Run build to ensure code works

"@

if (-not $Execute) {
    Write-Warning "THIS IS A DRY RUN"
    Write-Host "To actually execute these changes, run:" -ForegroundColor Yellow
    Write-Host "  .\revert-to-working-baseline.ps1 -Execute" -ForegroundColor Cyan
    Write-Host ""
}

# Ask for confirmation if executing
if ($Execute) {
    Write-Host ""
    Write-Host "${Red}═══════════════════════════════════════════════════════════════${Reset}" -ForegroundColor Red
    Write-Host "${Red}  ⚠ WARNING: THIS WILL REWRITE MASTER HISTORY ⚠${Reset}" -ForegroundColor Red
    Write-Host "${Red}═══════════════════════════════════════════════════════════════${Reset}" -ForegroundColor Red
    Write-Host ""
    Write-Host "Are you absolutely sure you want to proceed?" -ForegroundColor Yellow
    Write-Host "Type 'YES' (in capitals) to confirm: " -NoNewline -ForegroundColor Yellow
    
    $confirmation = Read-Host
    
    if ($confirmation -ne "YES") {
        Write-Host "Aborted by user." -ForegroundColor Yellow
        exit 0
    }
    
    Write-Success "Confirmed. Proceeding with execution..."
    Start-Sleep -Seconds 2
}

# ============================================================================
# STEP 1: CREATE ARCHIVE BRANCH
# ============================================================================

Write-Header "STEP 1: CREATE ARCHIVE BRANCH"

Invoke-GitCommand `
    -Command "branch $ArchiveBranchName" `
    -Description "Create archive branch at current HEAD"

Invoke-GitCommand `
    -Command "push origin $ArchiveBranchName" `
    -Description "Push archive branch to remote"

# ============================================================================
# STEP 2: COMMIT DOCUMENTATION (IF NEEDED)
# ============================================================================

Write-Header "STEP 2: COMMIT DOCUMENTATION FILES"

# Check if docs are already committed
$docsStatus = git status --porcelain -- ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md COMBINED_PROPERTY_SUMMARY.md GOALS_ANALYSIS_AND_RECOMMENDATIONS.md

if ($docsStatus) {
    Write-Step "Documentation files need to be committed"
    
    Invoke-GitCommand `
        -Command "add ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md COMBINED_PROPERTY_SUMMARY.md GOALS_ANALYSIS_AND_RECOMMENDATIONS.md" `
        -Description "Stage documentation files"
    
    $commitMessage = @"
docs: Add comprehensive pulse guide architecture documentation

- ORIGINAL_PULSE_GUIDE_ARCHITECTURE.md: Complete analysis of working baseline (0f10f0f6)
- COMBINED_PROPERTY_SUMMARY.md: IsPulseGuiding combined property explanation
- GOALS_ANALYSIS_AND_RECOMMENDATIONS.md: SemaphoreSlim and instance architecture analysis

These documents analyze the original pulse guide implementation to inform future refactoring.
"@
    
    Invoke-GitCommand `
        -Command "commit -m `"$commitMessage`"" `
        -Description "Commit documentation files"
    
    Invoke-GitCommand `
        -Command "push origin master" `
        -Description "Push documentation commit to remote"
}
else {
    Write-Success "Documentation files already committed"
}

# Create tag and branch for documentation commit
Invoke-GitCommand `
    -Command "tag $DocsTagName" `
    -Description "Create tag for documentation commit"

Invoke-GitCommand `
    -Command "branch $DocsBranchName" `
    -Description "Create branch for documentation commit"

Invoke-GitCommand `
    -Command "push origin $DocsTagName" `
    -Description "Push documentation tag to remote"

Invoke-GitCommand `
    -Command "push origin $DocsBranchName" `
    -Description "Push documentation branch to remote"

# Save the documentation commit hash
if ($Execute) {
    $docsCommit = git rev-parse HEAD
    Write-Success "Documentation commit: $docsCommit"
}

# ============================================================================
# STEP 3: RESET MASTER TO WORKING COMMIT
# ============================================================================

Write-Header "STEP 3: RESET MASTER TO WORKING COMMIT"

Write-Warning "About to reset master to $WorkingCommit"
Write-Warning "All commits after $WorkingCommit will be removed from master"
Write-Warning "(But preserved in $ArchiveBranchName)"

if ($Execute) {
    Write-Host "Waiting 3 seconds before reset... (Ctrl+C to abort)" -ForegroundColor Yellow
    Start-Sleep -Seconds 3
}

Invoke-GitCommand `
    -Command "reset --hard $WorkingCommit" `
    -Description "Hard reset master to working commit"

# Verify we're at the right commit
if ($Execute) {
    $currentCommit = git rev-parse --short HEAD
    Write-Success "Master now at: $currentCommit"
    
    $logOutput = git log --oneline -5
    Write-Host "`nRecent commits:" -ForegroundColor Cyan
    Write-Host $logOutput -ForegroundColor Gray
}

# ============================================================================
# STEP 4: CHERRY-PICK DOCUMENTATION
# ============================================================================

Write-Header "STEP 4: CHERRY-PICK DOCUMENTATION BACK"

Invoke-GitCommand `
    -Command "cherry-pick $DocsTagName" `
    -Description "Cherry-pick documentation commit onto clean master"

# Verify documentation files exist
if ($Execute) {
    Write-Step "Verifying documentation files after cherry-pick..."
    
    foreach ($doc in $docFiles) {
        if (Test-Path $doc) {
            Write-Success "Present: $doc"
        }
        else {
            Write-Error-Message "Missing after cherry-pick: $doc"
            throw "Cherry-pick failed - documentation file missing"
        }
    }
}

# ============================================================================
# STEP 5: FORCE PUSH MASTER
# ============================================================================

Write-Header "STEP 5: FORCE PUSH MASTER TO REMOTE"

Write-Warning "About to force-push master (rewrites remote history)"
Write-Warning "Using --force-with-lease for safety"

if ($Execute) {
    Write-Host "Last chance to abort! Press Ctrl+C within 5 seconds..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
}

Invoke-GitCommand `
    -Command "push origin master --force-with-lease" `
    -Description "Force push master to remote (with lease protection)"

# ============================================================================
# STEP 6: VERIFY FINAL STATE
# ============================================================================

Write-Header "STEP 6: VERIFY FINAL STATE"

if ($Execute) {
    Write-Step "Checking commit history..."
    $logOutput = git log --oneline -10
    Write-Host $logOutput -ForegroundColor Gray
    
    Write-Step "Verifying documentation files..."
    foreach ($doc in $docFiles) {
        if (Test-Path $doc) {
            Write-Success "✓ $doc"
        }
        else {
            Write-Error-Message "✗ $doc"
        }
    }
    
    Write-Step "Checking git status..."
    $finalStatus = git status --porcelain
    if ($finalStatus) {
        Write-Warning "Working directory has changes:"
        Write-Host $finalStatus -ForegroundColor Yellow
    }
    else {
        Write-Success "Working directory is clean"
    }
    
    Write-Step "Running build to verify code..."
    try {
        $buildOutput = dotnet build --nologo --verbosity quiet 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Build successful!"
        }
        else {
            Write-Error-Message "Build failed!"
            Write-Host $buildOutput -ForegroundColor Red
        }
    }
    catch {
        Write-Warning "Could not run build (dotnet not in PATH?)"
    }
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Header "SUMMARY"

Write-Host @"
${Green}Operation completed successfully!${Reset}

${Cyan}Current State:${Reset}
  - Master branch: Working baseline ($WorkingCommit) + documentation
  - Archive branch: $ArchiveBranchName (preserved refactor attempt)
  - Docs branch: $DocsBranchName (clean docs for cherry-picking)
  - Tag: $DocsTagName (marks documentation commit)

${Cyan}Branch Structure:${Reset}
  
  ${Green}master${Reset} (current - working code + docs)
    └─ docs: Add pulse guide documentation  [cherry-picked]
    └─ $WorkingCommit (working baseline)
    └─ ... (older commits)

  ${Yellow}$ArchiveBranchName${Reset} (preserved for reference)
    └─ docs: Add pulse guide documentation
    └─ <broken controller commits>
    └─ $WorkingCommit (working baseline)
    └─ ... (older commits)

  ${Blue}$DocsBranchName${Reset} (clean docs only)
    └─ docs: Add pulse guide documentation

${Cyan}To Reference Archived Work:${Reset}
  # View archived commits
  git log $ArchiveBranchName --oneline

  # Compare archive to working baseline
  git diff $WorkingCommit..$ArchiveBranchName

  # Extract file from archive
  git show $ArchiveBranchName:path/to/file.cs > file_archived.cs

  # Create new branch from archive
  git checkout -b new-attempt $ArchiveBranchName

${Cyan}Next Steps:${Reset}
  1. ✓ Code is now at working baseline (all tests should pass)
  2. ✓ Documentation preserved (understand the original architecture)
  3. → Run ConformU tests to verify pulse guide works
  4. → Read documentation before attempting any refactoring
  5. → If refactoring again, use architecture principles from docs

"@

if (-not $Execute) {
    Write-Host ""
    Write-Warning "THIS WAS A DRY RUN - NO CHANGES MADE"
    Write-Host "To execute for real, run:" -ForegroundColor Yellow
    Write-Host "  .\revert-to-working-baseline.ps1 -Execute" -ForegroundColor Cyan
}

Write-Host ""
Write-Success "Script completed!"
