#!/bin/bash
# Builds wgsidecar for every platform Jellyfin runs on, into ../dist-sidecar/, one file per platform named
# wgsidecar-{os}-{arch}, with a SHA256SUMS file beside them. Run it from anywhere: sidecar/build.sh
set -e
cd "$(dirname "$0")"
out_dir="../dist-sidecar"
rm -rf "$out_dir"
mkdir -p "$out_dir"
for target in linux/amd64 linux/arm64 linux/arm darwin/arm64 darwin/amd64 windows/amd64 windows/arm64; do
    os=${target%/*}; arch=${target#*/}
    ext=""; [[ $os == windows ]] && ext=".exe"
    out="$out_dir/wgsidecar-$os-$arch$ext"
    CGO_ENABLED=0 GOOS=$os GOARCH=$arch go build -trimpath -ldflags="-s -w" -o "$out" .
    echo "$out $(du -h "$out" | cut -f1)"
done
cd "$out_dir"
if command -v sha256sum >/dev/null; then sha256sum wgsidecar-* > SHA256SUMS; else shasum -a 256 wgsidecar-* > SHA256SUMS; fi
