#!/bin/bash
set -e
set -o pipefail

dotnet build ./WslNotifydWin.csproj --runtime win-x64 --self-contained
copy_src=./bin/Debug/net8.0-windows10.0.19041.0/win-x64

exec ./scripts/runner.sh "${copy_src}" "$@"
