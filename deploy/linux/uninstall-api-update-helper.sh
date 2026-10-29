#!/bin/bash
# Removes the manually installed Linux API update helper.
# The API installation is left in place. Pass --remove-staging to delete staged packages.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run this uninstaller as root." >&2
  exit 1
fi

REMOVE_STAGING=0
STAGING_PATH=/var/lib/sw-license-watcher/api-update
UPDATE_UNIT=sw-license-watcher-api-update.service

while [ "$#" -gt 0 ]; do
  case "$1" in
    --remove-staging) REMOVE_STAGING=1; shift ;;
    --staging-path) STAGING_PATH="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

systemctl stop "$UPDATE_UNIT" >/dev/null 2>&1 || true
rm -f "/etc/systemd/system/$UPDATE_UNIT"
rm -f /etc/sudoers.d/sw-license-watcher-api-update
rm -f /usr/local/lib/sw-license-watcher/apply-api-update.sh
rmdir /usr/local/lib/sw-license-watcher 2>/dev/null || true
rm -f /etc/sw-license-watcher/update-helper.conf
rmdir /etc/sw-license-watcher 2>/dev/null || true
systemctl daemon-reload

if [ "$REMOVE_STAGING" -eq 1 ]; then
  rm -rf "$STAGING_PATH"
fi

echo "OK: Update helper removed. The API installation was not changed."
