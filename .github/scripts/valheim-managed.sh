#!/usr/bin/env bash
# Valheim's game DLLs for CI, from the free dedicated server (Steam app 896660, anonymous login). Nothing is committed.
#   valheim-managed.sh buildid        print the public branch's build id (the cache key)
#   valheim-managed.sh fetch <dir>    download the server and copy its valheim_server_Data/Managed to <dir>
set -euo pipefail

APP=896660
WORK="${RUNNER_TEMP:-${TMPDIR:-/tmp}}"
STEAMCMD_DIR="$WORK/steamcmd"

steamcmd() {
  if [ ! -x "$STEAMCMD_DIR/steamcmd.sh" ]; then
    mkdir -p "$STEAMCMD_DIR"
    curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar xz -C "$STEAMCMD_DIR"
  fi
  # SteamCMD updates itself on its first run and can exit non-zero then: retry.
  for attempt in 1 2 3; do
    if "$STEAMCMD_DIR/steamcmd.sh" "$@"; then return 0; fi
    echo "steamcmd failed (attempt $attempt), retrying" >&2
  done
  return 1
}

case "${1:-}" in
  buildid)
    steamcmd +login anonymous +app_info_update 1 +app_info_print "$APP" +quit > "$WORK/appinfo.txt"
    id=$(awk '/"branches"/{b=1} b&&/"public"/{p=1} p&&/"buildid"/{gsub(/"/,"",$2); print $2; exit}' "$WORK/appinfo.txt")
    if [ -z "$id" ]; then echo "no public build id in the app info" >&2; exit 1; fi
    echo "$id"
    ;;
  fetch)
    dest="${2:?usage: valheim-managed.sh fetch <dir>}"
    server="$WORK/valheim-server"
    steamcmd +force_install_dir "$server" +login anonymous +app_update "$APP" +quit
    mkdir -p "$dest"
    cp -r "$server/valheim_server_Data/Managed/." "$dest/"
    test -f "$dest/assembly_valheim.dll"
    ;;
  *)
    echo "usage: $0 buildid | fetch <dir>" >&2
    exit 2
    ;;
esac
