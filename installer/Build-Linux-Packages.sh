#!/usr/bin/env bash
# Builds Linux SDK distribution artifacts without changing the Windows installer flow.
set -euo pipefail

configuration=Release
runtime_identifier=""
no_native_aot=false
skip_verification=false

usage() {
    printf '%s\n' \
        'Usage: installer/Build-Linux-Packages.sh [options]' \
        '' \
        'Options:' \
        '  --runtime <linux-x64|linux-arm64>  Target runtime (default: current Linux architecture)' \
        '  --configuration <Debug|Release>    Build configuration (default: Release)' \
        '  --no-native-aot                    Use single-file self-contained publish directly' \
        '  --skip-verification                Do not run the staged CLI smoke test' \
        '  -h, --help                         Show this help'
}

while (($#)); do
    case "$1" in
        --runtime) runtime_identifier=${2:?"--runtime requires a value"}; shift 2 ;;
        --configuration) configuration=${2:?"--configuration requires a value"}; shift 2 ;;
        --no-native-aot) no_native_aot=true; shift ;;
        --skip-verification) skip_verification=true; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

case "$(uname -s)" in Linux) ;; *) echo "This script must run on Linux." >&2; exit 1 ;; esac
if [[ -z "$runtime_identifier" ]]; then
    case "$(uname -m)" in
        x86_64|amd64) runtime_identifier=linux-x64 ;;
        aarch64|arm64) runtime_identifier=linux-arm64 ;;
        *) echo "Unsupported host architecture '$(uname -m)'. Pass --runtime explicitly." >&2; exit 1 ;;
    esac
fi
case "$runtime_identifier" in linux-x64) deb_architecture=amd64 ;; linux-arm64) deb_architecture=arm64 ;; *) echo "Only linux-x64 and linux-arm64 are currently packaged." >&2; exit 2 ;; esac

command -v dotnet >/dev/null || { echo "dotnet SDK 10 or newer is required." >&2; exit 1; }
command -v dpkg-deb >/dev/null || { echo "dpkg-deb is required to create the .deb package." >&2; exit 1; }
command -v tar >/dev/null || { echo "tar is required to create the portable archive." >&2; exit 1; }

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/.." && pwd)
artifact_root="$repo_root/artifacts/linux/$runtime_identifier"
publish_root="$artifact_root/publish"
cli_publish_dir="$publish_root/cli"
lsp_publish_dir="$publish_root/language-server"
package_root="$artifact_root/package-root"
portable_root="$artifact_root/portable/pscp-sdk"
tool_version=$(sed -nE 's/.*ToolVersion = "([^"]+)".*/\1/p' "$repo_root/src/Pscp.Transpiler/Syntax.cs" | head -n 1)
[[ -n "$tool_version" ]] || { echo "Could not determine PSCP tool version." >&2; exit 1; }

export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_CLI_HOME="$repo_root/.dotnet"
rm -rf -- "$artifact_root"
mkdir -p "$cli_publish_dir" "$lsp_publish_dir"

publish_project() {
    local project=$1 output=$2 label=$3 mode_variable=$4
    local -a base_args=(publish "$project" -c "$configuration" -r "$runtime_identifier" -o "$output" /t:Rebuild "/p:InvariantGlobalization=true" "/p:Version=$tool_version" "/p:InformationalVersion=$tool_version")
    if [[ "$no_native_aot" != true ]]; then
        echo "Publishing $label with Native AOT..."
        if dotnet "${base_args[@]}" /p:PublishAot=true; then
            printf -v "$mode_variable" '%s' NativeAot
            return
        fi
        echo "Native AOT publish failed for $label; falling back to self-contained single-file publish." >&2
        rm -rf -- "$output"
        mkdir -p "$output"
    fi
    echo "Publishing $label as self-contained single file..."
    dotnet "${base_args[@]}" /p:PublishReadyToRun=true /p:PublishSingleFile=true /p:SelfContained=true
    printf -v "$mode_variable" '%s' ReadyToRun
}

cli_mode=""
lsp_mode=""
publish_project "$repo_root/src/Pscp.Cli/Pscp.Cli.csproj" "$cli_publish_dir" "pscp CLI" cli_mode
publish_project "$repo_root/src/Pscp.LanguageServer/Pscp.LanguageServer.csproj" "$lsp_publish_dir" "PSCP language server" lsp_mode
cli_executable="$cli_publish_dir/pscp"
lsp_executable="$lsp_publish_dir/Pscp.LanguageServer"
[[ -x "$cli_executable" ]] || { echo "Expected CLI executable not found: $cli_executable" >&2; exit 1; }
[[ -x "$lsp_executable" ]] || { echo "Expected language-server executable not found: $lsp_executable" >&2; exit 1; }

mkdir -p "$package_root/DEBIAN" "$package_root/usr/lib/pscp" "$package_root/usr/bin"
install -m 0755 "$cli_executable" "$package_root/usr/lib/pscp/pscp"
install -m 0755 "$lsp_executable" "$package_root/usr/lib/pscp/Pscp.LanguageServer"
ln -s ../lib/pscp/pscp "$package_root/usr/bin/pscp"
ln -s ../lib/pscp/Pscp.LanguageServer "$package_root/usr/bin/pscp-lsp"
printf 'Package: pscp\nVersion: %s\nSection: devel\nPriority: optional\nArchitecture: %s\nMaintainer: PSCP contributors <noreply@pscp.dev>\nDescription: PSCP SDK command-line tools\n PSCP transpiler CLI and language server for competitive programming.\n' "$tool_version" "$deb_architecture" > "$package_root/DEBIAN/control"
deb_path="$artifact_root/pscp_${tool_version}_${deb_architecture}.deb"
dpkg-deb --build --root-owner-group "$package_root" "$deb_path" >/dev/null

mkdir -p "$portable_root/bin" "$portable_root/lib/pscp"
install -m 0755 "$cli_executable" "$portable_root/lib/pscp/pscp"
install -m 0755 "$lsp_executable" "$portable_root/lib/pscp/Pscp.LanguageServer"
ln -s ../lib/pscp/pscp "$portable_root/bin/pscp"
ln -s ../lib/pscp/Pscp.LanguageServer "$portable_root/bin/pscp-lsp"
printf 'PSCP SDK %s (%s)\n\nRun without installing:\n  ./bin/pscp version\n\nTo make the commands available in the current shell:\n  export PATH="$PWD/bin:$PATH"\n\nFor system installation on Debian/Ubuntu, prefer the accompanying .deb package:\n  sudo apt install ./%s\n' "$tool_version" "$runtime_identifier" "$(basename "$deb_path")" > "$portable_root/README.txt"
tar_path="$artifact_root/pscp-sdk_${tool_version}_${runtime_identifier}.tar.gz"
tar -C "$artifact_root/portable" -czf "$tar_path" pscp-sdk

if [[ "$skip_verification" != true ]]; then
    staged_version=$($package_root/usr/lib/pscp/pscp version)
    [[ "$staged_version" == *"$tool_version"* ]] || { echo "Staged CLI version mismatch: $staged_version" >&2; exit 1; }
    verification_root="$artifact_root/verify"
    mkdir -p "$verification_root"
    printf '+= "Linux package verification"\n' > "$verification_root/main.pscp"
    diagnostics=$($package_root/usr/lib/pscp/pscp check "$verification_root/main.pscp")
    [[ "$diagnostics" == "No diagnostics." ]] || { echo "CLI verification failed: $diagnostics" >&2; exit 1; }
fi

echo
echo "Linux artifacts created for PSCP $tool_version ($runtime_identifier):"
echo "  $deb_path"
echo "  $tar_path"
echo "Publish modes: CLI=$cli_mode, language-server=$lsp_mode"
