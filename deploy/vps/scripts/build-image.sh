#!/usr/bin/env sh
# MOTOR: build the image on this server from a git ref, for hosts that do not pull
# from a registry (e.g. an ARM server: the registry image is amd64 only).
#
#   deploy/vps/scripts/build-image.sh origin/preview   # prints the tag, e.g. sha-1a2b3c4
#   deploy/vps/scripts/deploy.sh sha-1a2b3c4
#
# Needs MOTOR_IMAGE_PULL=never in .env so deploy.sh uses the local image.
# The build context is "git archive" of the ref: only committed files, never .env,
# secrets or local App_Data.
set -eu

cd "$(dirname "$0")/.."
[ -f .env ] || { echo "build-image: .env missing" >&2; exit 1; }
image=$(sed -n 's/^MOTOR_IMAGE=//p' .env | tail -n 1)
[ -n "$image" ] || { echo "build-image: MOTOR_IMAGE missing in .env" >&2; exit 1; }

ref=${1:-HEAD}
git fetch --quiet origin
rev=$(git rev-parse "$ref^{commit}")
tag="sha-$(git rev-parse --short=7 "$rev")"

git archive --format=tar "$rev" | \
    docker build --quiet -t "$image:$tag" --build-arg MOTOR_REVISION="$rev" - >&2

echo "$tag"
