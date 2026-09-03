#!/bin/sh
#
# One-time bootstrap for the bundled Garage node: assign a cluster layout, create the bucket, and
# import the access key from .env so the API and worker can use fixed credentials.
#
# Garage cannot be fully configured from its config file, and an evaluator should not have to run
# admin commands by hand before the system works.
#
# Everything here is idempotent, because compose restarts this service on failure and runs it
# again on every `up`. Two things make that harder than it looks:
#
#   * Whether a layout exists is read from `garage status`, not from `layout show`. An unassigned
#     node reports NO ROLE ASSIGNED; once assigned it reports its zone and capacity.
#   * `layout apply` takes the version it is producing, not a constant. Applying version 1 twice
#     fails with "Invalid new layout version", so the next version is read from the current one.

set -eu

GARAGE="garage -c /etc/garage.toml"
BUCKET="${GARAGE_BUCKET:-priorstate}"
KEY_NAME="priorstate"

echo "garage-init: waiting for the node to accept connections"
until $GARAGE status >/dev/null 2>&1; do
  sleep 1
done

if $GARAGE status | grep -q 'NO ROLE ASSIGNED'; then
  NODE_ID=$($GARAGE node id -q | cut -d@ -f1)
  echo "garage-init: assigning layout to $NODE_ID"

  $GARAGE layout assign -z priorstate -c "${GARAGE_CAPACITY:-100G}" "$NODE_ID"

  CURRENT=$($GARAGE layout show | sed -n 's/^Current cluster layout version: *//p' | head -n 1)
  NEXT=$(( ${CURRENT:-0} + 1 ))

  echo "garage-init: applying layout version $NEXT"
  $GARAGE layout apply --version "$NEXT"
else
  echo "garage-init: layout already assigned"
fi

if $GARAGE bucket info "$BUCKET" >/dev/null 2>&1; then
  echo "garage-init: bucket $BUCKET already exists"
else
  echo "garage-init: creating bucket $BUCKET"
  $GARAGE bucket create "$BUCKET"
fi

if $GARAGE key info "$GARAGE_ACCESS_KEY" >/dev/null 2>&1; then
  echo "garage-init: access key already imported"
else
  echo "garage-init: importing access key"
  $GARAGE key import --yes -n "$KEY_NAME" "$GARAGE_ACCESS_KEY" "$GARAGE_SECRET_KEY"
fi

$GARAGE bucket allow --read --write --owner "$BUCKET" --key "$KEY_NAME" >/dev/null

echo "garage-init: done"
