#!/usr/bin/env bash
# Builds the slim ffmpeg that Akses ships, cross-compiled for Windows x64 with mingw-w64.
#
# Why: the general-purpose GPL build (BtbN) is 183 MB because it carries every encoder and library
# there is (AV1/HEVC encoders, rav1e, SVT, x265, libass, …). Akses needs every *decoder* (so any file
# opens) but only the encoders of the formats it offers. This build keeps:
#   decoders, demuxers, parsers, bitstream filters: all of them (+ dav1d for AV1)
#   encoders: x264 (MP4/MOV/MKV), libvpx VP9/VP8 (WebM), AAC, LAME MP3, Opus, Vorbis, FLAC, PCM,
#             ALAC, MPEG-4 Part 2 (AVI), GIF, PNG/MJPEG (frames), subtitles (SRT/VTT/ASS/mov_text)
#   muxers:   the containers Akses writes; filters: the ones MediaEngine.cs uses
# Result: ffmpeg.exe + ffprobe.exe + shared DLLs in tools\ffmpeg (~35 MB instead of 183 MB).
#
# Run inside WSL (or any Linux) with mingw-w64-gcc, nasm, meson, ninja, cmake, autotools:
#   wsl -d Arch -u root -- bash installer/build-ffmpeg.sh
# Builds in ~/akses-ffmpeg (Linux file system: much faster than /mnt/c) and copies the result into
# tools/ffmpeg of this repo. When changing MediaEngine.cs (a new encoder, muxer or filter), add it
# below and rebuild. Every source is pinned to a release tag.
set -euo pipefail

REPO="$(cd "$(dirname "$0")/.." && pwd)"
WORK="${HOME}/akses-ffmpeg"
PREFIX="${WORK}/prefix"
HOST=x86_64-w64-mingw32
JOBS="$(nproc)"

FFMPEG_TAG=n9.0.2
X264_COMMIT=b35605ace3ddf7c1a5d67a2eb553f034aef41d55   # x264 "stable" branch
VPX_TAG=v1.17.0
DAV1D_TAG=1.5.4
OPUS_TAG=v1.6.1
OGG_TAG=v1.3.6
VORBIS_TAG=v1.3.7
ZLIB_TAG=v1.3.2
LAME_URL=https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz
LAME_SHA256=ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e

mkdir -p "$WORK/src" "$PREFIX"
cd "$WORK/src"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig"
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"

clone() { # url tag dir
    [ -d "$3" ] || git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$2" "$1" "$3"
}

echo "==> zlib"
clone https://github.com/madler/zlib.git "$ZLIB_TAG" zlib
(cd zlib && make -s -f win32/Makefile.gcc PREFIX="$HOST-" -j"$JOBS" libz.a \
    && install -Dm644 libz.a "$PREFIX/lib/libz.a" && install -Dm644 zlib.h zconf.h -t "$PREFIX/include")

echo "==> x264"
if [ ! -d x264 ]; then git clone --quiet https://code.videolan.org/videolan/x264.git x264 && git -C x264 checkout --quiet "$X264_COMMIT"; fi
(cd x264 && ./configure --host="$HOST" --cross-prefix="$HOST-" --prefix="$PREFIX" --enable-static --disable-cli \
    --enable-win32thread --disable-opencl >/dev/null && make -s -j"$JOBS" && make -s install)

echo "==> lame"
if [ ! -d lame-3.100 ]; then
    curl -sSL -o lame.tar.gz "$LAME_URL"
    echo "$LAME_SHA256  lame.tar.gz" | sha256sum -c -
    tar xzf lame.tar.gz
fi
(cd lame-3.100 && ./configure --host="$HOST" --prefix="$PREFIX" --enable-static --disable-shared --disable-frontend \
    --disable-decoder --disable-gtktest >/dev/null && make -s -j"$JOBS" && make -s install)

echo "==> ogg + vorbis + opus"
clone https://github.com/xiph/ogg.git "$OGG_TAG" ogg
clone https://github.com/xiph/vorbis.git "$VORBIS_TAG" vorbis
clone https://github.com/xiph/opus.git "$OPUS_TAG" opus
for lib in ogg vorbis opus; do
    (cd "$lib" && rm -rf build && cmake -S . -B build -G Ninja -DCMAKE_SYSTEM_NAME=Windows -DCMAKE_C_COMPILER="$HOST-gcc" \
        -DCMAKE_CXX_COMPILER="$HOST-g++" \
        -DCMAKE_RC_COMPILER="$HOST-windres" -DCMAKE_INSTALL_PREFIX="$PREFIX" -DCMAKE_PREFIX_PATH="$PREFIX" \
        -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTING=OFF -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
        -DOPUS_BUILD_PROGRAMS=OFF -DOPUS_BUILD_TESTING=OFF -DINSTALL_DOCS=OFF >/dev/null \
        && cmake --build build -j "$JOBS" >/dev/null && cmake --install build >/dev/null)
done

echo "==> libvpx"
clone https://chromium.googlesource.com/webm/libvpx "$VPX_TAG" libvpx
(cd libvpx && CROSS="$HOST-" ./configure --target=x86_64-win64-gcc --prefix="$PREFIX" --enable-static --disable-shared \
    --disable-examples --disable-tools --disable-docs --disable-unit-tests --enable-vp9-highbitdepth \
    --as=nasm >/dev/null && make -s -j"$JOBS" && make -s install)

echo "==> dav1d"
clone https://code.videolan.org/videolan/dav1d.git "$DAV1D_TAG" dav1d
cat > "$WORK/cross.meson" <<EOF
[binaries]
c = '$HOST-gcc'
ar = '$HOST-ar'
strip = '$HOST-strip'
windres = '$HOST-windres'
nasm = 'nasm'
[host_machine]
system = 'windows'
cpu_family = 'x86_64'
cpu = 'x86_64'
endian = 'little'
EOF
(cd dav1d && meson setup build --cross-file "$WORK/cross.meson" --prefix="$PREFIX" --libdir=lib --default-library=static \
    --buildtype=release -Denable_tools=false -Denable_tests=false >/dev/null && ninja -C build >/dev/null && ninja -C build install >/dev/null)

echo "==> ffmpeg $FFMPEG_TAG"
clone https://git.ffmpeg.org/ffmpeg.git "$FFMPEG_TAG" ffmpeg
cd ffmpeg
make -s distclean >/dev/null 2>&1 || true
./configure --prefix="$PREFIX/ffmpeg" --arch=x86_64 --target-os=mingw32 --cross-prefix="$HOST-" \
    --pkg-config=pkg-config --pkg-config-flags=--static \
    --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib -static-libgcc" \
    --enable-gpl --enable-version3 --enable-shared --disable-static \
    --disable-debug --disable-doc --disable-ffplay --disable-autodetect --enable-w32threads \
    --disable-network --disable-devices --disable-hwaccels \
    --disable-protocols --enable-protocol=file,pipe,concat \
    --enable-zlib --enable-libx264 --enable-libmp3lame --enable-libopus --enable-libvorbis --enable-libvpx --enable-libdav1d \
    --disable-encoders --enable-encoder=libx264,libvpx_vp8,libvpx_vp9,aac,libmp3lame,libopus,libvorbis,flac,alac,pcm_s16le,pcm_s16be,pcm_s24le,mpeg4,gif,png,mjpeg,subrip,srt,webvtt,ass,ssa,mov_text \
    --disable-muxers --enable-muxer=mp4,mov,ipod,matroska,webm,avi,gif,mp3,wav,flac,ogg,opus,aiff,adts,image2,srt,webvtt,ass,null \
    --disable-filters --enable-filter=scale,format,fps,split,palettegen,paletteuse,trim,atrim,setpts,asetpts,concat,aformat,aresample,showwavespic,null,anull,copy,acopy,pad,crop,transpose,hflip,vflip \
    >/dev/null
make -s -j"$JOBS"
make -s install

echo "==> copying into tools/ffmpeg"
OUT="$REPO/tools/ffmpeg"
rm -rf "$OUT" && mkdir -p "$OUT"
cp "$PREFIX/ffmpeg/bin/"*.exe "$PREFIX/ffmpeg/bin/"*.dll "$OUT/"
$HOST-strip --strip-unneeded "$OUT/"*.exe "$OUT/"*.dll
# libvpx/x264 threads pull in mingw's winpthreads (~60 KB, MIT/BSD-style license): ship it alongside.
for f in "$OUT"/*.dll "$OUT"/*.exe; do
    if $HOST-objdump -p "$f" | grep -q "libwinpthread-1.dll"; then
        cp "$(dirname "$(command -v $HOST-gcc)")/../$HOST/bin/libwinpthread-1.dll" "$OUT/" 2>/dev/null \
            || cp /usr/$HOST/bin/libwinpthread-1.dll "$OUT/"
        break
    fi
done
cp COPYING.GPLv3 "$OUT/LICENSE.txt"
cat > "$OUT/BUILD.txt" <<EOF
Slim ffmpeg for Akses, built by installer/build-ffmpeg.sh ($(date -u +%Y-%m-%d)).
ffmpeg $FFMPEG_TAG (GPL v3) with x264 $X264_COMMIT, libvpx $VPX_TAG, dav1d $DAV1D_TAG, opus $OPUS_TAG,
libogg $OGG_TAG, libvorbis $VORBIS_TAG, LAME 3.100, zlib $ZLIB_TAG.
Source code of all of them: the tags above, or ask info@mtre-consulting.it.
EOF
# Every DLL the result imports must be a Windows system DLL or one of ours.
for f in "$OUT"/*.exe "$OUT"/*.dll; do
    $HOST-objdump -p "$f" | awk '/DLL Name/ {print $3}'
done | sort -u | grep -viE '^(av|sw|postproc)|^libwinpthread-1\.dll$|^(kernel32|user32|bcrypt|advapi32|ole32|shell32|msvcrt|ws2_32|shlwapi|gdi32|psapi|secur32|crypt32|api-ms-win-.*|ucrtbase)\.dll$' \
    && echo "!! unexpected DLL dependencies above" || echo "   only system DLLs imported"
du -sh "$OUT"
