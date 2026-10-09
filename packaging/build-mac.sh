#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
rid="${1:-osx-arm64}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo "Use osx-arm64 or osx-x64"; exit 1;; esac
rm -rf "$project_dir/dist/$rid/SMT Mac Beta.app"
dotnet publish "$project_dir/src/Smt.Desktop/Smt.Desktop.csproj" -c Release -r "$rid" --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=true -p:DebugType=none -p:UseAppHost=true -o "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/MacOS"
mkdir -p "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/Resources"
mv "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/MacOS/data" "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/Resources/data"
cp "$project_dir/packaging/Info.plist" "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/Info.plist"
cp "$project_dir/packaging/SMT.icns" "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/Resources/SMT.icns"
chmod +x "$project_dir/dist/$rid/SMT Mac Beta.app/Contents/MacOS/SmtMac"
if [[ "$(uname -s)" == Darwin ]]; then
  codesign --force --deep --sign - "$project_dir/dist/$rid/SMT Mac Beta.app"
fi
echo "Created $project_dir/dist/$rid/SMT Mac Beta.app"
