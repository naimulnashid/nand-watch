#!/bin/sh
# Off Windows only: a stand-in for mt.exe, the Windows SDK's manifest tool,
# which the Windows App SDK's self-contained build runs to merge manifests.
# It copies the generated manifest to -out:, so the app's C# can be compiled
# (type-checked) on Linux. Builds on Windows never use it.
in=""; out=""
while [ $# -gt 0 ]; do
  case "$1" in
    -manifest) shift; in="$1" ;;
    -out:*) out="${1#-out:}" ;;
  esac
  shift
done
cp "$in" "$out"
