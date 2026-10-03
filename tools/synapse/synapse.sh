#!/usr/bin/env bash
# Runs a local Synapse in podman for the integration tests, with rate limits turned off.
#
#   tools/synapse/synapse.sh start [version]   start Synapse (default: latest), e.g. "start v1.98.0"
#   tools/synapse/synapse.sh stop              stop and remove the container; data is kept
#   tools/synapse/synapse.sh status            show the running container and its version
#   tools/synapse/synapse.sh reset [version]   delete that version's data, then start it afresh
#
# Each version keeps its own data in .synapse/<version>/, because Synapse can migrate its database
# forwards but not back. Starting writes the test login to
# src/matrix.NET/matrix.NET.Tests/testsettings.local.json, which the tests use by default.
set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
container=matrixdotnet-synapse
# In rootless podman, root in the container is the host user, so the data stays owned by them
run_as=(--env UID=0 --env GID=0)
image=docker.io/matrixdotorg/synapse
port=8008
user=matrixdotnet
settings_file=$repo_root/src/matrix.NET/matrix.NET.Tests/testsettings.local.json

data_dir() { echo "$repo_root/.synapse/$1"; }

stop() {
    podman rm --force --ignore "$container" >/dev/null
}

# Appended once to the generated homeserver.yaml. Older versions ignore options they do not know.
write_overrides() {
    local config=$1
    grep -q '^# matrix.NET test overrides' "$config" && return
    {
        echo
        echo '# matrix.NET test overrides'
        local unlimited='{per_second: 1000000, burst_count: 1000000}'
        for option in rc_message rc_registration rc_registration_token_validity rc_admin_redaction \
            rc_joins_per_room rc_3pid_validation rc_third_party_invite rc_media_create \
            rc_delayed_event_mgmt rc_reports rc_room_creation rc_user_directory rc_profile; do
            echo "$option: $unlimited"
        done
        echo "rc_login: {address: $unlimited, account: $unlimited, failed_attempts: $unlimited}"
        echo "rc_joins: {local: $unlimited, remote: $unlimited}"
        echo "rc_invites: {per_room: $unlimited, per_user: $unlimited, per_issuer: $unlimited}"
        echo "rc_presence: {per_user: $unlimited}"
    } >>"$config"
}

wait_until_healthy() {
    for _ in $(seq 1 60); do
        if curl --silent --fail "http://127.0.0.1:$port/health" >/dev/null; then
            return
        fi
        sleep 1
    done
    echo "Synapse did not become healthy; see: podman logs $container" >&2
    exit 1
}

start() {
    local version=${1:-latest}
    local data
    data=$(data_dir "$version")
    mkdir -p "$data"

    podman pull --quiet "$image:$version" >/dev/null

    if [[ ! -f $data/homeserver.yaml ]]; then
        podman run --rm "${run_as[@]}" --volume "$data:/data:Z" \
            --env SYNAPSE_SERVER_NAME=localhost --env SYNAPSE_REPORT_STATS=no \
            "$image:$version" generate >/dev/null
    fi
    write_overrides "$data/homeserver.yaml"

    if [[ ! -f $data/test-password ]]; then
        (umask 077 && head -c 24 /dev/urandom | base64 | tr -d '/+=' >"$data/test-password")
    fi

    stop
    podman run --detach --name "$container" "${run_as[@]}" --volume "$data:/data:Z" \
        --publish "127.0.0.1:$port:8008" "$image:$version" >/dev/null
    wait_until_healthy

    # Fails harmlessly when the user already exists
    podman exec "$container" register_new_matrix_user --config /data/homeserver.yaml \
        --user "$user" --password "$(cat "$data/test-password")" --no-admin "http://localhost:8008" \
        >/dev/null 2>&1 || true

    (umask 077 && cat >"$settings_file" <<EOF
{
  "Homeserver": "http://127.0.0.1:$port/",
  "User": "$user",
  "Password": "$(cat "$data/test-password")",
  "DeviceId": "MATRIXDOTNETTEST"
}
EOF
    )
    echo "Synapse $(server_version) is running at http://127.0.0.1:$port/ (data: .synapse/$version)"
}

server_version() {
    curl --silent "http://127.0.0.1:$port/_matrix/federation/v1/version" |
        sed -n 's/.*"version":"\([^"]*\)".*/\1/p'
}

case ${1:-} in
    start) start "${2:-}" ;;
    stop) stop ;;
    status)
        if podman container exists "$container"; then
            podman ps --filter "name=^$container\$" --format '{{.Image}} {{.Status}}'
            echo "Synapse $(server_version)"
        else
            echo "Not running"
        fi
        ;;
    reset)
        stop
        rm -rf "$(data_dir "${2:-latest}")"
        start "${2:-}"
        ;;
    *)
        sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'
        exit 1
        ;;
esac
