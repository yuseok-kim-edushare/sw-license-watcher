#!/bin/bash
# Applies a staged API package. The systemd oneshot runs this script as root.
# Install path and service name come from /etc/sw-license-watcher/update-helper.conf.
set -euo pipefail

CONFIG=/etc/sw-license-watcher/update-helper.conf

declare -A CONF

read_conf() {
  local file="$1" line key value
  while IFS= read -r line || [ -n "$line" ]; do
    line="${line%$'\r'}"
    case "$line" in
      ''|\#*) continue ;;
    esac
    case "$line" in
      *=*) ;;
      *) echo "Invalid helper setting: $line" >&2; exit 1 ;;
    esac
    key="${line%%=*}"
    value="${line#*=}"
    CONF["$key"]="$value"
  done < "$file"
}

is_preserved() {
  local name="${1,,}"
  [[ "$name" == appsettings*.json || "$name" == device-ca*.json ]]
}

copy_payload() {
  local src="$1" dest="$2" entry base
  mkdir -p "$dest"
  shopt -s nullglob dotglob
  for entry in "$src"/*; do
    base="$(basename "$entry")"
    if [ "$base" = "update-helper" ]; then
      continue
    fi
    if [ -f "$entry" ] && is_preserved "$base"; then
      continue
    fi
    if [ -d "$entry" ]; then
      copy_payload "$entry" "$dest/$base" || return 1
    else
      cp -a "$entry" "$dest/$base" || return 1
    fi
  done
  shopt -u dotglob
}

write_result() {
  printf 'Status=%s\nVersion=%s\nMessage=%s\n' "$1" "$2" "$3" > "$RESULT"
}

if [ ! -f "$CONFIG" ]; then
  echo "Missing $CONFIG. Run install-api-update-helper.sh first." >&2
  exit 1
fi

read_conf "$CONFIG"
HOST_KIND="${CONF[HostKind]:-}"
INSTALL_PATH="${CONF[InstallPath]:-}"
STAGING_PATH="${CONF[StagingPath]:-}"
SERVICE_NAME="${CONF[ServiceName]:-}"
SERVICE_USER="${CONF[ServiceUser]:-}"
SERVICE_GROUP="${CONF[ServiceGroup]:-}"
HEALTH_URL="${CONF[HealthUrl]:-}"

if [ "$HOST_KIND" != "linux" ]; then
  echo "helper.conf HostKind must be linux." >&2
  exit 1
fi

case "$INSTALL_PATH" in
  /|/usr|/etc|/var|/bin|/sbin|/opt|/home)
    echo "Refusing to update $INSTALL_PATH." >&2
    exit 1
    ;;
esac

REQUEST="$STAGING_PATH/request.conf"
RESULT="$STAGING_PATH/result.conf"
PAYLOAD="$STAGING_PATH/payload"
BACKUP="$STAGING_PATH/backup"

if [ ! -f "$REQUEST" ]; then
  echo "Missing $REQUEST." >&2
  exit 1
fi

read_conf "$REQUEST"
VERSION="${CONF[Version]:-}"
if [[ ! "$VERSION" =~ ^[A-Za-z0-9._-]{1,32}$ || "$VERSION" == "." || "$VERSION" == ".." ]]; then
  echo "request.conf Version is not safe." >&2
  exit 1
fi

if [ ! -d "$PAYLOAD" ]; then
  echo "Staged payload is missing." >&2
  exit 1
fi

if [ -n "$HEALTH_URL" ] && ! command -v curl >/dev/null 2>&1; then
  echo "curl is required when HealthUrl is set." >&2
  exit 1
fi

sleep 3
if ! systemctl stop "$SERVICE_NAME"; then
  write_result "failed" "$VERSION" "Could not stop $SERVICE_NAME."
  exit 1
fi

rollback() {
  if [ -d "$BACKUP" ]; then
    find "$INSTALL_PATH" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    cp -a "$BACKUP"/. "$INSTALL_PATH"/
    if [ -n "$SERVICE_USER" ] && [ -n "$SERVICE_GROUP" ]; then
      chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_PATH"
    fi
    systemctl start "$SERVICE_NAME" || true
  fi
}

fail() {
  local message="$1"
  rollback
  write_result "failed" "$VERSION" "$message"
  exit 1
}

rm -rf "$BACKUP"
mkdir -p "$BACKUP"
if ! cp -a "$INSTALL_PATH"/. "$BACKUP"/; then
  systemctl start "$SERVICE_NAME" || true
  write_result "failed" "$VERSION" "Could not back up $INSTALL_PATH."
  exit 1
fi

if ! copy_payload "$PAYLOAD" "$INSTALL_PATH"; then
  fail "The staged package could not be copied."
fi

if [ -n "$SERVICE_USER" ] && [ -n "$SERVICE_GROUP" ]; then
  if ! chown -R "$SERVICE_USER:$SERVICE_GROUP" "$INSTALL_PATH"; then
    fail "Could not restore ownership of $INSTALL_PATH."
  fi
fi

if ! systemctl start "$SERVICE_NAME"; then
  fail "Could not start $SERVICE_NAME."
fi

healthy=0
for _ in $(seq 1 30); do
  if systemctl is-active --quiet "$SERVICE_NAME"; then
    if [ -z "$HEALTH_URL" ]; then
      healthy=1
      break
    fi
    code="$(curl -k -s -o /dev/null -w "%{http_code}" --max-time 5 "$HEALTH_URL" || true)"
    if [ -n "$code" ] && [ "$code" != "000" ]; then
      healthy=1
      break
    fi
  fi
  sleep 2
done

if [ "$healthy" -ne 1 ]; then
  fail "The API did not accept connections after the update."
fi

write_result "succeeded" "$VERSION" "Applied $VERSION."
