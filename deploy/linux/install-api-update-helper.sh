#!/bin/bash
# Manually installs the Linux API update helper.
# Does not install or replace the API service. Run once as root when an administrator
# wants /admin to apply staged server packages.
set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
  echo "Run this installer as root." >&2
  exit 1
fi

INSTALL_PATH=/opt/sw-license-watcher
SERVICE_NAME=sw-license-watcher-api
SERVICE_USER=swlw
SERVICE_GROUP=swlw
STAGING_PATH=/var/lib/sw-license-watcher/api-update
HEALTH_URL=
UPDATE_UNIT=sw-license-watcher-api-update.service
HELPER_SCRIPT=/usr/local/lib/sw-license-watcher/apply-api-update.sh
CONFIG=/etc/sw-license-watcher/update-helper.conf

while [ "$#" -gt 0 ]; do
  case "$1" in
    --install-path) INSTALL_PATH="$2"; shift 2 ;;
    --service-name) SERVICE_NAME="$2"; shift 2 ;;
    --service-user) SERVICE_USER="$2"; shift 2 ;;
    --service-group) SERVICE_GROUP="$2"; shift 2 ;;
    --staging-path) STAGING_PATH="$2"; shift 2 ;;
    --health-url) HEALTH_URL="$2"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

case "$INSTALL_PATH" in
  /|/usr|/etc|/var|/bin|/sbin|/opt|/home)
    echo "Refusing install path $INSTALL_PATH." >&2
    exit 1
    ;;
esac

if [ ! -x "$INSTALL_PATH/SwLicenseWatcher.Api" ] && [ ! -f "$INSTALL_PATH/SwLicenseWatcher.Api" ]; then
  echo "Install path does not contain SwLicenseWatcher.Api." >&2
  exit 1
fi

if [[ ! "$SERVICE_NAME" =~ ^[A-Za-z0-9._-]+$ || ! "$SERVICE_USER" =~ ^[A-Za-z0-9._-]+$ || ! "$SERVICE_GROUP" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "Service name and account must be simple tokens." >&2
  exit 1
fi

SOURCE_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ ! -f "$SOURCE_DIR/apply-api-update.sh" ]; then
  echo "apply-api-update.sh was not found next to this installer." >&2
  exit 1
fi

install -d -m 755 /usr/local/lib/sw-license-watcher /etc/sw-license-watcher
install -m 755 -o root -g root "$SOURCE_DIR/apply-api-update.sh" "$HELPER_SCRIPT"
install -d -m 750 -o "$SERVICE_USER" -g "$SERVICE_GROUP" "$STAGING_PATH"

cat > "$CONFIG" <<EOF
HostKind=linux
InstallPath=$INSTALL_PATH
ServiceName=$SERVICE_NAME
AppPoolName=
StagingPath=$STAGING_PATH
HealthUrl=$HEALTH_URL
ServiceUser=$SERVICE_USER
ServiceGroup=$SERVICE_GROUP
TaskName=SwLicenseWatcher Api Update
UpdateUnitName=$UPDATE_UNIT
EOF
chmod 644 "$CONFIG"

cat > "/etc/systemd/system/$UPDATE_UNIT" <<EOF
[Unit]
Description=Apply a staged SwLicenseWatcher API package
After=network.target

[Service]
Type=oneshot
User=root
ExecStart=$HELPER_SCRIPT
EOF

SYSTEMCTL="$(command -v systemctl)"
SUDOERS=/etc/sudoers.d/sw-license-watcher-api-update
cat > "$SUDOERS" <<EOF
$SERVICE_USER ALL=(root) NOPASSWD: $SYSTEMCTL start $UPDATE_UNIT
EOF
chmod 440 "$SUDOERS"
visudo -cf "$SUDOERS" >/dev/null

systemctl daemon-reload
echo "OK: Update helper installed for $INSTALL_PATH."
echo "The API service installer does not register this helper. Remove it with uninstall-api-update-helper.sh."
