$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$publishDir = Join-Path $root 'publish\win-x64'
$serverProject = Join-Path $root 'GreenSwamp.Alpaca.Server\GreenSwamp.Alpaca.Server.csproj'
$serverPubxml = Join-Path $root 'GreenSwamp.Alpaca.Server\Properties\PublishProfiles\win-x64.pubxml'
$installerProject = Join-Path $root 'GreenSwamp.Alpaca.Installer\GreenSwamp.Alpaca.Installer.wixproj'

Write-Host 'Publishing GreenSwamp.Alpaca.Server for win-x64...'
msbuild '/t:restore;clean;build' cd  `
  /p:Configuration=Release `
  /p:DeployOnBuild=true `
  /p:PublishProfile=$serverPubxml
#dotnet publish $serverProject `
#  -c Release `
#  -r win-x64 `
#  --self-contained false `
#  -p:PublishSingleFile=true `
#  -p:PublishTrimmed=false `
#  -o $publishDir

$exePath = Join-Path $publishDir 'GreenSwamp.Alpaca.Server.exe'
if (-not (Test-Path $exePath)) {
  throw "Published EXE not found: $exePath"
}

# Example input: 1.2.3-beta-xxxxxx or 1.2.3-beta+xxxxxx
# MSI version must be 3-part: 1.2.3
$productVersion = (Get-Item $exePath).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($productVersion)) {
  throw "Unable to read ProductVersion from: $exePath"
}

if ($productVersion -match '^(\d+)\.(\d+)\.(\d+)') {
  $msiVersion = "$($matches[1]).$($matches[2]).$($matches[3])"
}
else {
  throw "Unable to normalize MSI version from ProductVersion: $productVersion"
}

Write-Host "EXE ProductVersion: $productVersion"
Write-Host "MSI Version: $msiVersion"

# Set working directory to installer project for relative paths in .wixproj
Set-Location (Join-Path $root 'GreenSwamp.Alpaca.Installer')

#Write-Host 'Building MSI...'
#dotnet build $installerProject `
#  -c Release `
#  -p:Platform=x64 `
#  -p:UpgradeCode='FFFFFFFF-4475-4BFA-A8A5-1F26CD982B1C' `
#  "-p:PublishDir=$publishDir\" `
#  "-p:AppVersion=$msiVersion"

Write-Host 'Done.'