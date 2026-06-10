#!/usr/bin/env bash
# -----------------------------------------------------------------------------
# build-deb.sh — Build a .deb package for one RID
# -----------------------------------------------------------------------------
# Usage: ./build-deb.sh <RID> <VERSION>
#   e.g. ./build-deb.sh linux-arm64 1.2.3
#
# Expects the dotnet publish output to already exist at:
#   publish/<RID>/
# relative to the repository root (the current working directory when called
# from GitHub Actions or a local test).
# -----------------------------------------------------------------------------
set -euo pipefail

RID="${1:?First argument required: RID (linux-x64 | linux-arm64 | linux-arm)}"
VERSION="${2:?Second argument required: VERSION (e.g. 1.2.3)}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

case "$RID" in
  linux-x64)   DEB_ARCH="amd64"  ;;
  linux-arm64) DEB_ARCH="arm64"  ;;
  linux-arm)   DEB_ARCH="armhf"  ;;
  *)
    echo "ERROR: Unknown RID '$RID'. Expected linux-x64, linux-arm64, or linux-arm." >&2
    exit 1
    ;;
esac

PKG_NAME="greenswamp-alpaca-server_${VERSION}_${DEB_ARCH}"
PKG_ROOT="${PKG_NAME}"
INSTALL_DIR="${PKG_ROOT}/opt/greenswamp/alpaca-server"
DEBIAN_DIR="${PKG_ROOT}/DEBIAN"
SYSTEMD_DIR="${PKG_ROOT}/lib/systemd/system"
APPLICATIONS_DIR="${PKG_ROOT}/usr/share/applications"
ICONS_DIR="${PKG_ROOT}/usr/share/icons/hicolor/256x256/apps"

echo "==> Building ${PKG_NAME}.deb ..."
rm -rf "${PKG_ROOT}"

mkdir -p "${INSTALL_DIR}" "${DEBIAN_DIR}" "${SYSTEMD_DIR}" "${APPLICATIONS_DIR}" "${ICONS_DIR}"

# Stage publish output
cp -r "publish/${RID}/." "${INSTALL_DIR}/"
chmod +x "${INSTALL_DIR}/GreenSwamp.Alpaca.Server" 2>/dev/null || true

# Stage systemd unit file
cp "${SCRIPT_DIR}/systemd/greenswamp-alpaca.service" "${SYSTEMD_DIR}/"

# Stage XDG desktop entry (browser-launch menu item)
cp "${SCRIPT_DIR}/greenswamp-alpaca.desktop" "${APPLICATIONS_DIR}/"

# Stage application icon for desktop environments
cp "${SCRIPT_DIR}/greenswamp1.png" "${ICONS_DIR}/greenswamp-alpaca.png"

# Stage and chmod maintainer scripts
for script in postinst prerm postrm; do
  cp "${SCRIPT_DIR}/debian/${script}" "${DEBIAN_DIR}/"
  chmod 0755 "${DEBIAN_DIR}/${script}"
done

# Calculate installed size in KB (required by the Debian control spec)
INSTALLED_SIZE=$(du -sk "${INSTALL_DIR}" | cut -f1)

# Substitute tokens in the control template
sed \
  -e "s/@@VERSION@@/${VERSION}/g"               \
  -e "s/@@ARCH@@/${DEB_ARCH}/g"                 \
  -e "s/@@INSTALLED_SIZE@@/${INSTALLED_SIZE}/g" \
  "${SCRIPT_DIR}/debian/control.template" > "${DEBIAN_DIR}/control"

# Build the .deb
fakeroot dpkg-deb --build "${PKG_ROOT}" "${PKG_NAME}.deb"

echo "==> SUCCESS: ${PKG_NAME}.deb"