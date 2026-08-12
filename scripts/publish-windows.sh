#!/usr/bin/env bash
# Cross-builds a self-contained, single-file Windows .exe from Linux/macOS.
#
# The apt-packaged `dotnet-sdk-8.0` on Ubuntu strips out the Windows Desktop
# (WinForms/WPF) build support, so a plain `dotnet build`/`publish` on Linux
# fails with MSB4019 ("Microsoft.NET.Sdk.WindowsDesktop.targets ... was not
# found"). Microsoft's own SDK tarball (from dotnet-install.sh / dot.net)
# *does* include it. This script downloads that official SDK into a local,
# gitignored cache (once) and uses it to publish.
#
# Output: publish/win-x64/HamstuffAgcGuard.exe - a single portable exe that
# bundles the .NET 8 runtime, so the target Windows machine needs nothing
# installed.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

SDK_DIR="${AGCGUARD_DOTNET_SDK_DIR:-$HOME/tools/dotnet-win-sdk}"
RID="${1:-win-x64}"

if [ ! -x "$SDK_DIR/dotnet" ]; then
  echo "Official .NET SDK not found at $SDK_DIR - installing..."
  mkdir -p "$SDK_DIR"
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 8.0 --install-dir "$SDK_DIR"
fi

export DOTNET_ROOT="$SDK_DIR"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

OUT="publish/$RID"
rm -rf "$OUT"

"$SDK_DIR/dotnet" publish -c Release -r "$RID" \
  -p:EnableWindowsTargeting=true \
  -p:SelfContained=true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:SatelliteResourceLanguages=en \
  -p:DebugType=none \
  -o "$OUT"

echo
echo "Built: $OUT/HamstuffAgcGuard.exe"
du -h "$OUT/HamstuffAgcGuard.exe"
