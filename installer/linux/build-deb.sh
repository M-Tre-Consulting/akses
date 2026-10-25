#!/usr/bin/env bash
# Packages the Linux build of Akses (a `dotnet publish -r linux-x64` folder) into a .deb for Linux
# Mint, Ubuntu and Debian: double-click it and the Software Manager / apt installs it together with
# what it needs from the distribution (ffmpeg, poppler-utils, VLC's library), like any other app.
#
# Called by installer/build-linux.ps1 after the publish; by hand (WSL or any Linux with dpkg-deb):
#   bash installer/linux/build-deb.sh <publish folder> <version> <output folder>
#
# Layout:
#   /opt/akses/                          the app (self-contained .NET, no runtime needed)
#   /usr/bin/akses                       → /opt/akses/Akses
#   /usr/share/applications/akses.desktop   menu entry + "Apri con" for every type Akses reads
#   /usr/share/icons/hicolor/…/akses.*   icon
#   /usr/share/doc/akses/copyright       license (GPL v3) and attributions
# "Apri con Akses" in Nemo's right-click menu is written by the app itself on its first start
# (per user, src/Akses.Linux/ShellIntegration.cs): a package can't write in the users' homes.
set -euo pipefail

PUBLISH="${1:?publish folder}"
VERSION="${2:?version}"
OUT="${3:?output folder}"
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
PKG="$STAGE/akses"

install -d "$PKG/DEBIAN" "$PKG/opt/akses" "$PKG/usr/bin" "$PKG/usr/share/applications" \
    "$PKG/usr/share/icons/hicolor/scalable/apps" "$PKG/usr/share/icons/hicolor/512x512/apps" \
    "$PKG/usr/share/icons/hicolor/32x32/apps" "$PKG/usr/share/doc/akses"

cp -r "$PUBLISH/." "$PKG/opt/akses/"
# Debugger support of the .NET runtime: never used by the installed app.
rm -f "$PKG/opt/akses/libmscordaccore.so" "$PKG/opt/akses/libmscordbi.so" "$PKG/opt/akses/createdump"
# Files copied from Windows come without the right permissions: everything readable, the app runnable.
find "$PKG/opt/akses" -type d -exec chmod 755 {} +
find "$PKG/opt/akses" -type f -exec chmod 644 {} +
chmod 755 "$PKG/opt/akses/Akses"
ln -s /opt/akses/Akses "$PKG/usr/bin/akses"

install -m 644 "$HERE/akses.desktop" "$PKG/usr/share/applications/akses.desktop"
install -m 644 "$REPO/installer/art/icon-square.svg" "$PKG/usr/share/icons/hicolor/scalable/apps/akses.svg"
install -m 644 "$REPO/installer/art/akses-512.png" "$PKG/usr/share/icons/hicolor/512x512/apps/akses.png"
install -m 644 "$REPO/installer/art/akses-32.png" "$PKG/usr/share/icons/hicolor/32x32/apps/akses.png"

cat > "$PKG/usr/share/doc/akses/copyright" <<EOF
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: Akses
Upstream-Contact: M-Tre Consulting <info@mtre-consulting.it>
Source: https://github.com/M-Tre-Consulting/akses

Files: *
Copyright: $(date +%Y) M-Tre Consulting
License: GPL-3
 Akses is free software: you can redistribute it and/or modify it under the terms of the GNU
 General Public License version 3 as published by the Free Software Foundation.
 The full text is in /opt/akses/LICENSE (and /usr/share/common-licenses/GPL-3); third-party
 libraries and programs are listed in /opt/akses/NOTICE.md.
EOF

SIZE_KB="$(du -sk "$PKG/opt" "$PKG/usr" | awk '{s+=$1} END {print s}')"
cat > "$PKG/DEBIAN/control" <<EOF
Package: akses
Version: $VERSION
Architecture: amd64
Maintainer: M-Tre Consulting <info@mtre-consulting.it>
Installed-Size: $SIZE_KB
Section: graphics
Priority: optional
Homepage: https://mtre-consulting.it/progetti/akses
Depends: ffmpeg, poppler-utils, libvlc5, vlc-plugin-base, libfontconfig1, libice6, libsm6, libx11-6, libxrandr2, libxcursor1, libxi6, xdg-utils
Recommends: libreoffice-writer, libreoffice-impress, libreoffice-calc
Description: Give it a file: it shows everything you can do with it
 Akses converts photos, videos, audio, PDFs and documents between more than 170 formats
 (MKV to MP4, WEBP to JPG, HEIC, AVIF, FLAC, DOCX, Markdown…), trims audio and video in a
 second without re-encoding, crops, resizes and rotates images, and splits, merges and
 rotates PDFs. Everything happens on the computer: no accounts, no uploads.
 .
 Free software by M-Tre Consulting, GNU GPL v3.
EOF

# Menu and icon caches; the desktop database also refreshes the "Apri con" lists.
cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true; fi
exit 0
EOF
cp "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/postrm"
chmod 755 "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/postrm"

mkdir -p "$OUT"
DEB="$OUT/akses_${VERSION}_amd64.deb"
dpkg-deb --root-owner-group -Zxz --build "$PKG" "$DEB" >/dev/null
echo "$DEB ($(du -h "$DEB" | cut -f1))"
