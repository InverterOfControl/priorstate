#!/bin/sh
#
# One-time bootstrap for the bundled Garage node: assign a layout, create the bucket, and import
# the access key from .env so the API and worker can use fixed credentials.
#
# Garage cannot be fully configured from its config file, and an evaluator should not have to run
# admin commands by hand before the system works. Everything here is idempotent, so the container
# can be restarted without consequence.

set -eu

GARAGE="garage -c /etc/garage.toml"
BUCKET="${GARAGE_BUCKET:-priorstate}"

echo "garage-init: waiting for the node to accept connections"
until $GARAGE status >/dev/null 2>&1; do
  sleep 1
done

NODE_ID=$($GARAGE node id -q | cut -d@ -f1)

if $GARAGE layout show 2>/dev/null | grep -q "$NODE_ID"; then
  echo "garage-init: layout already assigned"
else
  echo "garage-init: assigning layout to $NODE_ID"
  $GARAGE layout assign -z priorstate -c 10G "$NODE_ID"
  $GARAGE layout apply --version 1
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
  $GARAGE key import --yes -n priorstate "$GARAGE_ACCESS_KEY" "$GARAGE_SECRET_KEY"
fi

$GARAGE bucket allow --read --write --owner "$BUCKET" --key priorstate

echo "garage-init: done"
