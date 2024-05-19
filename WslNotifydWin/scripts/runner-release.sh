#!/bin/bash
set -e
set -o pipefail

copy_src=.

exec ./scripts/runner.sh "${copy_src}" "$@"
