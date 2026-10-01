param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$ValidationFile,
    [Parameter(Mandatory=$true)][string]$StartupValidationFile,
    [string]$ReloadValidationFile,
    [string]$ChartFile,
    [Parameter(Mandatory=$true)][string]$ReportFile
)
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path -Parent $PSScriptRoot
$validationText=Get-Content -LiteralPath $ValidationFile -Raw
if ($validationText -notmatch '(?m)^PASS assertions=\d+') {throw 'A passed runtime report is required'}
$startupText=Get-Content -LiteralPath $StartupValidationFile -Raw
if ($startupText -notmatch '(?m)^VALIDATION_PASS independent=4 disabled=true unknown_hash=true current_hashes=true\r?$') {
    throw 'Fresh independent loading, disabled and unknown-build validation is required'
}
$reloadText=$null
if ($ReloadValidationFile) {
    $reloadText=Get-Content -LiteralPath $ReloadValidationFile -Raw
    foreach ($mode in @('False','True')) {
        if ($reloadText -notmatch ('(?m)^RELOAD_PASS transitions=3 phases=4 optimizers='+$mode+' assertions=\d+\r?$')) {
            throw 'Both baseline and optimizer reload validation are required when provided'
        }
    }
}
$names=@('Spatial','Wrecks','Navigation','Diagnostics')
$dlls=@()
foreach ($name in $names) {
    $path=Join-Path $sourceRoot "src/NOPerf.$name/bin/Release/net48/NOPerf.$name.dll"
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected='(?m)^PLUGIN_SHA256 '+[regex]::Escape("NOPerf.$name.dll")+'='+$hash+'\r?$'
    if ($validationText -notmatch $expected) {throw "Runtime validation does not match current NOPerf.$name.dll"}
    if ($startupText -notmatch $expected) {throw "Startup validation does not match current NOPerf.$name.dll"}
    if ($reloadText -and $reloadText -notmatch $expected) {throw "Reload validation does not match current NOPerf.$name.dll"}
    $dlls+=@{Name="NOPerf.$name.dll";Path=$path;SHA256=$hash}
}
$commit=& git -C $sourceRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) {throw 'Source commit unavailable'}
$status=& git -C $sourceRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $status) {throw 'Commit the reviewed source checkpoint before packaging'}
$stage=Join-Path $sourceRoot ('artifacts/package-'+[guid]::NewGuid().ToString('N'))
$plugins=Join-Path $stage 'BepInEx/plugins'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
foreach ($dll in $dlls) {Copy-Item -LiteralPath $dll.Path -Destination $plugins}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'README.md') -Destination $stage
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LICENSE') -Destination $stage
Copy-Item -LiteralPath (Join-Path $sourceRoot 'docs') -Destination (Join-Path $stage 'docs') -Recurse
Copy-Item -LiteralPath $ReportFile -Destination (Join-Path $stage 'Report.md')
if ($ChartFile) {Copy-Item -LiteralPath $ChartFile -Destination (Join-Path $stage 'Mission-aging.png')}
Copy-Item -LiteralPath $ValidationFile -Destination (Join-Path $stage 'RuntimeValidation.txt')
Copy-Item -LiteralPath $StartupValidationFile -Destination (Join-Path $stage 'StartupValidation.txt')
if ($ReloadValidationFile) {Copy-Item -LiteralPath $ReloadValidationFile -Destination (Join-Path $stage 'ReloadValidation.txt')}
$manifest=@{SourceCommit=$commit;GameSteamBuild='24724541';GameAssemblySHA256='df5bed594dd84912efb3e57faa75b37d7e327bf4c8f5418f50411ad0ff46e24a';Plugins=@($dlls | ForEach-Object {@{Name=$_.Name;SHA256=$_.SHA256}})}
$manifest.RuntimeValidationSHA256=(Get-FileHash -LiteralPath $ValidationFile -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest.StartupValidationSHA256=(Get-FileHash -LiteralPath $StartupValidationFile -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ReloadValidationFile) {$manifest.ReloadValidationSHA256=(Get-FileHash -LiteralPath $ReloadValidationFile -Algorithm SHA256).Hash.ToLowerInvariant()}
if ($ChartFile) {$manifest.ChartSHA256=(Get-FileHash -LiteralPath $ChartFile -Algorithm SHA256).Hash.ToLowerInvariant()}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stage 'Manifest.json') -Encoding utf8
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$pluginZip=Join-Path $OutputDirectory 'NOPerf-0.1.0-build24724541.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $pluginZip -Force
$sourceZip=Join-Path $OutputDirectory 'NOPerf-source.zip'
& git -C $sourceRoot archive --format=zip "--output=$sourceZip" HEAD
if ($LASTEXITCODE -ne 0) {throw 'Source archive failed'}
Write-Output "Packaged validated plugin DLLs from $commit"
