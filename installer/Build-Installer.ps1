param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$NoNativeAot,
    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $true

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = Join-Path $repoRoot "artifacts\installer"
$payloadDir = Join-Path $artifactsRoot "payload"
$setupDir = Join-Path $artifactsRoot "setup"
$publishRoot = Join-Path $artifactsRoot "publish"
$cliPublishDir = Join-Path $publishRoot "cli"
$lspPublishDir = Join-Path $publishRoot "language-server"
$setupPublishDir = Join-Path $publishRoot "setup"
$payloadZip = Join-Path $repoRoot "src\Pscp.Installer\payload.zip"
$verifyRoot = Join-Path $artifactsRoot "verify"
$verifyInstallDir = Join-Path $verifyRoot "install"
$verifySource = Join-Path $verifyRoot "multiline-collection.pscp"

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

function Get-PscpToolVersion {
    $syntaxPath = Join-Path $repoRoot "src\Pscp.Transpiler\Syntax.cs"
    $match = Select-String -Path $syntaxPath -Pattern 'ToolVersion\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($null -eq $match) {
        throw "Could not locate ToolVersion in $syntaxPath"
    }

    return $match.Matches[0].Groups[1].Value
}

function Remove-ProjectIntermediates {
    param([string[]]$ProjectDirectories)

    foreach ($projectDirectory in $ProjectDirectories) {
        foreach ($folderName in @("bin", "obj")) {
            Remove-Item -Recurse -Force (Join-Path $projectDirectory $folderName) -ErrorAction SilentlyContinue
        }
    }
}

function Invoke-PscpPublish {
    param(
        [string]$Project,
        [string]$OutputDir,
        [string]$Label,
        [bool]$PreferNativeAot = $true,
        [string]$Version
    )

    Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue
    $baseArgs = @(
        "publish",
        $Project,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "-o", $OutputDir,
        "/t:Rebuild",
        "/p:InvariantGlobalization=true",
        "/p:Version=$Version",
        "/p:InformationalVersion=$Version"
    )

    if ($PreferNativeAot -and -not $NoNativeAot) {
        try {
            Write-Host "Publishing $Label with Native AOT..."
            dotnet @baseArgs "/p:PublishAot=true" | Out-Host
            return "NativeAot"
        }
        catch {
            Write-Warning "Native AOT publish failed for $Label. Falling back to ReadyToRun self-contained publish. $($_.Exception.Message)"
            Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Publishing $Label as ReadyToRun self-contained..."
    dotnet @baseArgs "/p:PublishReadyToRun=true" "/p:PublishSingleFile=true" "/p:SelfContained=true" | Out-Host
    return "ReadyToRun"
}

function Verify-Setup {
    param(
        [string]$SetupExe,
        [string]$PayloadExe,
        [string]$ExpectedVersion
    )

    Remove-Item -Recurse -Force $verifyRoot -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $verifyRoot | Out-Null

    $sample = @"
let l = [
1,
2,
3
]

+= l
"@
    [System.IO.File]::WriteAllText($verifySource, $sample, [System.Text.UTF8Encoding]::new($false))

    try {
        Write-Host "Verifying setup.exe against the staged payload..."
        & $SetupExe --install-dir $verifyInstallDir --no-ui --no-integrate | Out-Host

        $installedExe = Join-Path $verifyInstallDir "pscp.exe"
        if (!(Test-Path $installedExe)) {
            throw "Verification install did not produce $installedExe"
        }

        $payloadVersion = (& $PayloadExe version).Trim()
        $installedVersion = (& $installedExe version).Trim()
        if ($payloadVersion -ne $installedVersion) {
            throw "Installed version mismatch. Payload: '$payloadVersion' Installed: '$installedVersion'"
        }

        if ($installedVersion -notmatch [regex]::Escape($ExpectedVersion)) {
            throw "Installed version '$installedVersion' does not contain expected tool version '$ExpectedVersion'"
        }

        $payloadHash = (Get-FileHash $PayloadExe).Hash
        $installedHash = (Get-FileHash $installedExe).Hash
        if ($payloadHash -ne $installedHash) {
            throw "Installed pscp.exe hash does not match the staged payload."
        }

        $diagnostics = (& $installedExe check $verifySource).Trim()
        if ($diagnostics -ne "No diagnostics.") {
            throw "Verification parser check failed.`n$diagnostics"
        }

        Write-Host "Setup verification passed."
    }
    finally {
        Remove-Item -Recurse -Force $verifyRoot -ErrorAction SilentlyContinue
    }
}

$toolVersion = Get-PscpToolVersion
Write-Host "Preparing installer for PSCP $toolVersion"

Remove-ProjectIntermediates -ProjectDirectories @(
    (Join-Path $repoRoot "src\Pscp.Transpiler"),
    (Join-Path $repoRoot "src\Pscp.Cli"),
    (Join-Path $repoRoot "src\Pscp.LanguageServer"),
    (Join-Path $repoRoot "src\Pscp.Installer")
)

Remove-Item -Recurse -Force $artifactsRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payloadDir, $setupDir, $publishRoot | Out-Null

$cliMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.Cli\Pscp.Cli.csproj") -OutputDir $cliPublishDir -Label "pscp CLI" -Version $toolVersion
$lspMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.LanguageServer\Pscp.LanguageServer.csproj") -OutputDir $lspPublishDir -Label "PSCP language server" -Version $toolVersion

Copy-Item (Join-Path $cliPublishDir "pscp.exe") $payloadDir
Copy-Item (Join-Path $lspPublishDir "Pscp.LanguageServer.exe") $payloadDir

$installedReadme = @"
PSCP SDK installed files

- pscp.exe: CLI and SDK entrypoint ($cliMode)
- Pscp.LanguageServer.exe: language server used by `pscp lsp` ($lspMode)
- uninstall.exe: generated during installation
"@
Set-Content -Encoding UTF8 (Join-Path $payloadDir "README.txt") $installedReadme

Remove-Item -Force $payloadZip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $payloadZip

$payloadVersion = (& (Join-Path $payloadDir "pscp.exe") version).Trim()
if ($payloadVersion -notmatch [regex]::Escape($toolVersion)) {
    throw "Staged payload version '$payloadVersion' does not contain expected tool version '$toolVersion'"
}

$setupMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.Installer\Pscp.Installer.csproj") -OutputDir $setupPublishDir -Label "setup" -PreferNativeAot $false -Version $toolVersion
Copy-Item (Join-Path $setupPublishDir "PscpSetup.exe") (Join-Path $setupDir "setup.exe") -Force

if (-not $SkipVerification) {
    Verify-Setup -SetupExe (Join-Path $setupDir "setup.exe") -PayloadExe (Join-Path $payloadDir "pscp.exe") -ExpectedVersion $toolVersion
}

Write-Host ""
Write-Host "Setup created:"
Write-Host "  $setupDir\setup.exe"
Write-Host ""
Write-Host "Payload staged at:"
Write-Host "  $payloadDir"
Write-Host ""
Write-Host "Publish modes:"
Write-Host "  CLI: $cliMode"
Write-Host "  Language server: $lspMode"
Write-Host "  Installer: $setupMode"
Write-Host "  Tool version: $toolVersion"
