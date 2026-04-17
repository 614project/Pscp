param(
    [string]$ExtensionDirectory = (Join-Path $PSScriptRoot 'pscp-vscode'),
    [string]$OutputDirectory = 'artifacts\vscode'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Content
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Normalize-TextFileNoBom {
    param([string]$Path)

    $extension = [System.IO.Path]::GetExtension($Path)
    if ($extension -notin '.json', '.js', '.md', '.xml', '.txt') {
        return
    }

    $content = [System.IO.File]::ReadAllText($Path)
    Write-Utf8NoBom -Path $Path -Content $content
}

$extensionDirectory = (Resolve-Path $ExtensionDirectory).Path
$repoRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}
$packageJsonPath = Join-Path $extensionDirectory 'package.json'

if (-not (Test-Path $packageJsonPath)) {
    throw "Could not find package.json at $packageJsonPath"
}

node --check (Join-Path $extensionDirectory 'extension.js') | Out-Host

$package = Get-Content -Path $packageJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($field in 'name', 'publisher', 'version', 'displayName', 'description') {
    if ([string]::IsNullOrWhiteSpace($package.$field)) {
        throw "Missing required package.json field '$field'."
    }
}

$vsixFileName = '{0}.{1}-{2}.vsix' -f $package.publisher, $package.name, $package.version
$vsixPath = Join-Path $outputDirectory $vsixFileName
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('pscp-vsix-' + [guid]::NewGuid().ToString('N'))
$extensionStaging = Join-Path $stagingRoot 'extension'
$zipPath = [System.IO.Path]::ChangeExtension($vsixPath, '.zip')

New-Item -ItemType Directory -Force $outputDirectory, $extensionStaging | Out-Null

try {
    $extensionPrefix = $extensionDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $files = Get-ChildItem -Path $extensionDirectory -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\node_modules\\' -and
        $_.Extension -ne '.vsix'
    }

    foreach ($file in $files) {
        $relative = if ($file.FullName.StartsWith($extensionPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $file.FullName.Substring($extensionPrefix.Length)
        }
        else {
            $file.Name
        }

        $destination = Join-Path $extensionStaging $relative
        New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        Normalize-TextFileNoBom -Path $destination
    }

    $contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="json" ContentType="application/json" />
  <Default Extension="js" ContentType="application/javascript" />
  <Default Extension="md" ContentType="text/markdown" />
  <Default Extension="xml" ContentType="application/xml" />
  <Default Extension="vsixmanifest" ContentType="text/xml" />
</Types>
"@
    Write-Utf8NoBom -Path (Join-Path $stagingRoot '[Content_Types].xml') -Content $contentTypes

    $identityId = [System.Security.SecurityElement]::Escape("$($package.publisher).$($package.name)")
    $version = [System.Security.SecurityElement]::Escape($package.version)
    $publisher = [System.Security.SecurityElement]::Escape($package.publisher)
    $displayName = [System.Security.SecurityElement]::Escape($package.displayName)
    $description = [System.Security.SecurityElement]::Escape($package.description)
    $engine = [System.Security.SecurityElement]::Escape($package.engines.vscode)
    $categories = if ($package.categories) { [System.Security.SecurityElement]::Escape(($package.categories -join ',')) } else { 'Programming Languages' }
    $tags = if ($package.keywords) { [System.Security.SecurityElement]::Escape(($package.keywords -join ';')) } else { 'pscp' }

    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Language="en-US" Id="$identityId" Version="$version" Publisher="$publisher" />
    <DisplayName>$displayName</DisplayName>
    <Description xml:space="preserve">$description</Description>
    <Tags>$tags</Tags>
    <Categories>$categories</Categories>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$engine" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionKind" Value="workspace" />
    </Properties>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" Version="$engine" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" />
    <Asset Type="Microsoft.VisualStudio.Services.Content.Details" Path="extension/README.md" />
  </Assets>
</PackageManifest>
"@
    Write-Utf8NoBom -Path (Join-Path $stagingRoot 'extension.vsixmanifest') -Content $manifest

    Remove-Item -LiteralPath $zipPath, $vsixPath -Force -ErrorAction SilentlyContinue
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $stagingPrefix = $stagingRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -Path $stagingRoot -Recurse -File) {
            $entryName = if ($file.FullName.StartsWith($stagingPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                $file.FullName.Substring($stagingPrefix.Length)
            }
            else {
                $file.Name
            }

            $entryName = $entryName -replace '\\', '/'
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    Move-Item -LiteralPath $zipPath -Destination $vsixPath -Force

    $archive = [System.IO.Compression.ZipFile]::OpenRead($vsixPath)
    try {
        $requiredEntries = @(
            '[Content_Types].xml',
            'extension.vsixmanifest',
            'extension/package.json',
            'extension/extension.js',
            'extension/language-configuration.json',
            'extension/syntaxes/pscp.tmGrammar.json',
            'extension/README.md'
        )

        foreach ($entryName in $requiredEntries) {
            if (-not ($archive.Entries | Where-Object FullName -eq $entryName)) {
                throw "VSIX is missing required entry '$entryName'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Host ''
    Write-Host 'VSIX created:'
    Write-Host "  $vsixPath"
}
finally {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
}