param(
  [Parameter(Mandatory = $true)]
  [ValidatePattern('^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$')]
  [string]$Version,
  [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$windowsOutput = Join-Path $artifactsRoot 'windows'
$project = Join-Path $projectRoot 'src\Dirvexa.App\Dirvexa.App.csproj'
$installerScript = Join-Path $projectRoot 'packaging\windows\Dirvexa.nsi'

New-Item -ItemType Directory -Force -Path $windowsOutput | Out-Null

if (-not $SkipTests) {
  dotnet test (Join-Path $projectRoot 'Dirvexa.slnx') -c Release
  if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$versionNumbers = $Version.Split('-')[0].Split('.')
$fileVersion = "$($versionNumbers[0]).$($versionNumbers[1]).$($versionNumbers[2]).0"
$runtimes = @('win-x64', 'win-arm64')
$portableFiles = @()

foreach ($runtime in $runtimes) {
  $publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$runtime"))
  if (-not $publishDirectory.StartsWith($artifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe publish directory: $publishDirectory"
  }
  if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
  }

  dotnet publish $project -c Release -r $runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $publishDirectory
  if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

  $portable = Join-Path $windowsOutput "Dirvexa-$Version-$runtime-portable.exe"
  Copy-Item -LiteralPath (Join-Path $publishDirectory 'Dirvexa.exe') -Destination $portable -Force
  $portableFiles += $portable
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
    $portable = Join-Path $windowsOutput "Dirvexa-$Version-$runtime-portable.exe"
    $setup = Join-Path $windowsOutput "Dirvexa-Setup-$Version-$runtime.exe"
    & $makensisPath /V2 "/DAPP_VERSION=$Version" "/DAPP_FILE_VERSION=$fileVersion" `
      "/DAPP_ARCH=$architecture" "/DAPP_EXE=$portable" "/DOUTPUT_FILE=$setup" $installerScript
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
  Set-Content -LiteralPath "$file.sha256" -Value "$hash  $([System.IO.Path]::GetFileName($file))" -Encoding ascii
}

Write-Output "Artifacts: $windowsOutput"
Get-ChildItem -LiteralPath $windowsOutput -File | Select-Object Name, Length
