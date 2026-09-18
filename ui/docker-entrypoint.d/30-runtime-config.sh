#!/bin/sh
# Writes the settings the app reads on start. nginx:alpine runs everything in
# /docker-entrypoint.d before starting the server, so this lands before the
# first request. Values are written with jq so a quote or a slash in a URL
# cannot produce invalid JSON.
set -eu

jq -n \
  --arg authority "${OIDC_AUTHORITY:-}" \
  --arg clientId "${OIDC_CLIENT_ID:-}" \
  '{oidcAuthority: $authority, oidcClientId: $clientId}' \
  > /usr/share/nginx/html/config.json

if [ -n "${OIDC_AUTHORITY:-}" ]; then
    echo "runtime config: signing in against ${OIDC_AUTHORITY}"
else
    echo "runtime config: no identity provider set, sign-in is off"
fi
