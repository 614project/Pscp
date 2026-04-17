param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$NoNativeAot
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

$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

Remove-Item -Recurse -Force $artifactsRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $payloadDir, $setupDir, $publishRoot | Out-Null

function Invoke-PscpPublish {
    param(
        [string]$Project,
        [string]$OutputDir,
        [string]$Label,
        [bool]$PreferNativeAot = $true
    )

    Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue
    $baseArgs = @(
        "publish",
        $Project,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "-o", $OutputDir,
        "/p:InvariantGlobalization=true"
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

$cliMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.Cli\Pscp.Cli.csproj") -OutputDir $cliPublishDir -Label "pscp CLI"
$lspMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.LanguageServer\Pscp.LanguageServer.csproj") -OutputDir $lspPublishDir -Label "PSCP language server"

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

$setupMode = Invoke-PscpPublish -Project (Join-Path $repoRoot "src\Pscp.Installer\Pscp.Installer.csproj") -OutputDir $setupPublishDir -Label "setup" -PreferNativeAot $false
Copy-Item (Join-Path $setupPublishDir "PscpSetup.exe") (Join-Path $setupDir "setup.exe") -Force

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



