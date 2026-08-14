param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
  [string]$Version,
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$windowsOutput = Join-Path $artifactsRoot 'windows'
$installersOutput = Join-Path $windowsOutput 'Installers'
$portableOutput = Join-Path $windowsOutput 'Portable'
$checksumsOutput = Join-Path $windowsOutput 'Checksums'
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$publishRoot = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot "Dirvexa-build-$PID-$([System.Guid]::NewGuid().ToString('N'))"))
$project = Join-Path $projectRoot 'src\Dirvexa.App\Dirvexa.App.csproj'
$appIcon = Join-Path $projectRoot 'src\Dirvexa.App\Assets\Dirvexa.ico'
$installerScript = Join-Path $projectRoot 'packaging\windows\Dirvexa.nsi'

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$projectVersion = [string]$projectXml.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
  throw "Build version '$Version' does not match the project version '$projectVersion'."
}

if (-not (Test-Path -LiteralPath $appIcon)) {
  throw "The application icon is missing. Run scripts\Generate-AppIcon.ps1 first."
}

if (-not $publishRoot.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Unsafe temporary publish directory: $publishRoot"
}

New-Item -ItemType Directory -Force -Path $installersOutput, $portableOutput, $checksumsOutput, $publishRoot | Out-Null

if (-not $SkipTests) {
  dotnet test (Join-Path $projectRoot 'Dirvexa.slnx') -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$versionNumbers = $Version.Split('-')[0].Split('.')
$fileVersion = "$($versionNumbers[0]).$($versionNumbers[1]).$($versionNumbers[2]).0"
$runtimes = @('win-x64', 'win-arm64')
$portableFiles = @()

foreach ($runtime in $runtimes) {
  $publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $publishRoot $runtime))
  if (-not $publishDirectory.StartsWith($publishRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $publishDirectory"
  }

  dotnet publish $project -c Release -r $runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -p:FileVersion=$fileVersion `
    -p:InformationalVersion=$Version `
    -p:IncludeSourceRevisionInInformationalVersion=false `
    -o $publishDirectory
  if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

  $portable = Join-Path $portableOutput "Dirvexa-$Version-$runtime-portable.exe"
  Copy-Item -LiteralPath (Join-Path $publishDirectory 'Dirvexa.exe') -Destination $portable -Force
  $portableFiles += $portable
}

if (Test-Path -LiteralPath $publishRoot) {
  Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

$makensisCommand = Get-Command makensis.exe -ErrorAction SilentlyContinue
$makensisPath = if ($makensisCommand) { $makensisCommand.Source } else { $null }
if (-not $makensisPath) {
  $knownNsis = @(
    (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'),
    (Join-Path $env:ProgramFiles 'NSIS\makensis.exe')
  ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
  if ($knownNsis) { $makensisPath = $knownNsis }
}

$setupFiles = @()
if ($makensisPath) {
  foreach ($runtime in $runtimes) {
    $architecture = $runtime.Replace('win-', '')
    $portable = Join-Path $portableOutput "Dirvexa-$Version-$runtime-portable.exe"
    $setup = Join-Path $installersOutput "Dirvexa-Setup-$Version-$runtime.exe"
    & $makensisPath /V2 "/DAPP_VERSION=$Version" "/DAPP_FILE_VERSION=$fileVersion" `
      "/DAPP_ARCH=$architecture" "/DAPP_EXE=$portable" "/DAPP_ICON=$appIcon" `
      "/DOUTPUT_FILE=$setup" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "NSIS setup failed for $runtime." }
    $setupFiles += $setup
  }
} else {
  Write-Warning 'NSIS was not found. Portable builds were created; setup builds were skipped.'
}

$allFiles = @($portableFiles + $setupFiles)
if ($env:DIRVEXA_SIGN_CERT_THUMBPRINT) {
  $signtool = Get-Command signtool.exe -ErrorAction Stop
  foreach ($file in $allFiles) {
    & $signtool.Source sign /sha1 $env:DIRVEXA_SIGN_CERT_THUMBPRINT /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $file
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $file." }
  }
}

foreach ($file in $allFiles) {
  $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
  $checksum = Join-Path $checksumsOutput "$([System.IO.Path]::GetFileName($file)).sha256"
  Set-Content -LiteralPath $checksum -Value "$hash  $([System.IO.Path]::GetFileName($file))" -Encoding ascii
}

function ConvertTo-SemanticVersion {
  param([Parameter(Mandatory = $true)][string]$Value)

  $pattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
  if ($Value -notmatch $pattern) { throw "Invalid semantic version: $Value" }
  return [pscustomobject]@{
    Value = $Value
    Major = [uint64]$Matches[1]
    Minor = [uint64]$Matches[2]
    Patch = [uint64]$Matches[3]
    PreRelease = [string]$Matches[4]
  }
}

function Compare-SemanticVersion {
  param($Left, $Right)

  foreach ($property in @('Major', 'Minor', 'Patch')) {
    if ($Left.$property -lt $Right.$property) { return -1 }
    if ($Left.$property -gt $Right.$property) { return 1 }
  }
  if (-not $Left.PreRelease -and -not $Right.PreRelease) { return 0 }
  if (-not $Left.PreRelease) { return 1 }
  if (-not $Right.PreRelease) { return -1 }

  $leftIdentifiers = $Left.PreRelease.Split('.')
  $rightIdentifiers = $Right.PreRelease.Split('.')
  for ($index = 0; $index -lt [Math]::Min($leftIdentifiers.Length, $rightIdentifiers.Length); $index++) {
    $leftIdentifier = $leftIdentifiers[$index]
    $rightIdentifier = $rightIdentifiers[$index]
    $leftNumeric = $leftIdentifier -match '^\d+$'
    $rightNumeric = $rightIdentifier -match '^\d+$'
    if ($leftNumeric -and -not $rightNumeric) { return -1 }
    if (-not $leftNumeric -and $rightNumeric) { return 1 }
    if ($leftNumeric) {
      $leftNumber = [System.Numerics.BigInteger]::Parse($leftIdentifier)
      $rightNumber = [System.Numerics.BigInteger]::Parse($rightIdentifier)
      if ($leftNumber -lt $rightNumber) { return -1 }
      if ($leftNumber -gt $rightNumber) { return 1 }
    } else {
      $comparison = [string]::CompareOrdinal($leftIdentifier, $rightIdentifier)
      if ($comparison -ne 0) { return $comparison }
    }
  }
  return $leftIdentifiers.Length.CompareTo($rightIdentifiers.Length)
}

$currentSemanticVersion = ConvertTo-SemanticVersion $Version
$artifactPattern = '^Dirvexa-(?:Setup-)?(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?)-(?:win-(?:x64|arm64)(?:-portable)?)\.exe(?:\.sha256)?$'
$versionedArtifacts = Get-ChildItem -LiteralPath $windowsOutput -Recurse -File | ForEach-Object {
  if ($_.Name -match $artifactPattern) {
    [pscustomobject]@{ File = $_; Version = ConvertTo-SemanticVersion $Matches['version'] }
  }
}
$previousVersion = $null
foreach ($candidate in ($versionedArtifacts.Version | Sort-Object Value -Unique)) {
  if ((Compare-SemanticVersion $candidate $currentSemanticVersion) -ge 0) { continue }
  if ($null -eq $previousVersion -or (Compare-SemanticVersion $candidate $previousVersion) -gt 0) {
    $previousVersion = $candidate
  }
}
$versionsToKeep = @($Version)
if ($previousVersion) { $versionsToKeep += $previousVersion.Value }

foreach ($artifact in $versionedArtifacts) {
  $resolvedArtifact = [System.IO.Path]::GetFullPath($artifact.File.FullName)
  if (-not $resolvedArtifact.StartsWith($windowsOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe artifact cleanup target: $resolvedArtifact"
  }
  if ($artifact.Version.Value -notin $versionsToKeep) {
    Remove-Item -LiteralPath $resolvedArtifact -Force
    continue
  }

  $destinationDirectory = if ($artifact.File.Name.EndsWith('.sha256', [System.StringComparison]::OrdinalIgnoreCase)) {
    $checksumsOutput
  } elseif ($artifact.File.Name.StartsWith('Dirvexa-Setup-', [System.StringComparison]::OrdinalIgnoreCase)) {
    $installersOutput
  } else {
    $portableOutput
  }
  $destination = [System.IO.Path]::GetFullPath((Join-Path $destinationDirectory $artifact.File.Name))
  if ($resolvedArtifact.Equals($destination, [System.StringComparison]::OrdinalIgnoreCase)) { continue }
  if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $resolvedArtifact -Force
  } else {
    Move-Item -LiteralPath $resolvedArtifact -Destination $destination
  }
}

$guide = @(
  'DIRVEXA FOR WINDOWS',
  '',
  "Current version: $Version",
  '',
  'RECOMMENDED - INSTALL DIRVEXA',
  "Most Windows PCs: Installers\Dirvexa-Setup-$Version-win-x64.exe",
  "Windows on ARM:   Installers\Dirvexa-Setup-$Version-win-arm64.exe",
  '',
  'NO-INSTALL PORTABLE VERSION',
  "Most Windows PCs: Portable\Dirvexa-$Version-win-x64-portable.exe",
  "Windows on ARM:   Portable\Dirvexa-$Version-win-arm64-portable.exe",
  '',
  'Checksums contains optional SHA-256 files for download verification.',
  'Use the highest version number; the immediately previous version is kept only for rollback.'
)
if (-not $makensisPath) {
  $guide += ''
  $guide += 'Installers were not generated because NSIS was unavailable for this build.'
}
Set-Content -LiteralPath (Join-Path $windowsOutput 'README.txt') -Value $guide -Encoding utf8

Write-Output "Artifacts: $windowsOutput"
Write-Output "Retained versions: $($versionsToKeep -join ', ')"
Get-ChildItem -LiteralPath $windowsOutput -Recurse -File | Select-Object FullName, Length
