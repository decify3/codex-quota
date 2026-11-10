param([string]$Version = '0.1.0-preview', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[a-zA-Z0-9.-]+$') { throw 'Invalid version name.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (Test-Path -LiteralPath $sdk) {
    $env:DOTNET_ROOT = Join-Path $projectRoot '.tools/dotnet'
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
} else {
    $sdk = (Get-Command dotnet -ErrorAction Stop).Source
}
$output = Join-Path $projectRoot "release/CodexQuota-$Version-win-x64"
if (Test-Path -LiteralPath $output) { throw "Output already exists; choose a new version: $output" }
$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
& $sdk publish (Join-Path $projectRoot 'native/src/CodexGuardian.App/CodexGuardian.App.csproj') -c Release -r win-x64 --self-contained true --disable-build-servers -m:1 "-p:Version=$Version" "-p:InformationalVersion=$Version" -p:PublishSingleFile=false -p:PublishTrimmed=false -o $output @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; incomplete output retained for diagnosis.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'native/LICENSE') -Destination (Join-Path $output 'GUARDIAN-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'native/UPSTREAM.md') -Destination $output
Copy-Item -LiteralPath (Join-Path $projectRoot 'native/THIRD-PARTY-NOTICES.md') -Destination $output
$licenseOutput = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
$assets = Get-Content (Join-Path $projectRoot 'native/src/CodexGuardian.App/obj/project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
$runtimePackages = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.WindowsAppSDK.*/*' })
foreach ($packageRoot in $packageRoots) {
    foreach ($package in $runtimePackages) {
        $packagePath = Join-Path $packageRoot $package.ToLowerInvariant()
        if (!(Test-Path -LiteralPath $packagePath)) { continue }
        $destination = Join-Path $licenseOutput ($package -replace '/', '-')
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(license|notice)' } |
            Copy-Item -Destination $destination
    }
    foreach ($runtimeName in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $runtimeRoot = Join-Path $packageRoot $runtimeName
        if (!(Test-Path -LiteralPath $runtimeRoot)) { continue }
        foreach ($runtimeVersion in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
            $destination = Join-Path $licenseOutput "$runtimeName-$($runtimeVersion.Name)"
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Get-ChildItem -LiteralPath $runtimeVersion.FullName -File | Where-Object { $_.Name -match '^(license|third-party-notices)' } |
                Copy-Item -Destination $destination
        }
    }
}
if (!(Get-ChildItem -LiteralPath $licenseOutput -File -Recurse | Where-Object Name -Match 'license')) {
    throw 'Runtime license collection failed; do not distribute this output.'
}
@"
Codex Quota $Version — Windows x64 preview

Double-click CodexGuardian.App.exe to start. The binary retains the upstream assembly name.
No .NET SDK installation is required. Keep all files in this folder together.
Official Codex must already be installed and signed in for live quota reads.
Default language: Chinese. Change Chinese / English in Settings.
Settings and history: %USERPROFILE%\.codex-quota\data (unless CODEX_QUOTA_HOME is configured).
Active notifications, Windows startup and Follow Codex are off by default.
This preview does not include the optional Watcher executable.

Known limits:
- Account partition uses hashed metadata, not a guaranteed stable workspace ID. If workspace identity is missing, history/plan saving needs explicit Personal account only opt-in in Settings. Do not enable it for unnamed shared workspaces.
- Weekly scenarios need two complete days or two observed daily segments spanning at least four hours. Partial days cannot establish full-day sufficiency.
- Quota windows expire without automatically refilling; refresh is required.
- No X monitoring, subscription changes, reset-card redemption or cloud synchronization.
- Unsigned local preview; not an official OpenAI application.

源码及借鉴记录请参阅随包 UPSTREAM.md 和许可文件。
"@ | Set-Content (Join-Path $output 'START-HERE.txt') -Encoding utf8
Write-Output $output

