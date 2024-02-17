#!/bin/bash
set -e

dotnet build ./WslNotifydWin.csproj --runtime win-x64 --self-contained
exec ./bin/Debug/net8.0-windows10.0.19041.0/win-x64/WslNotifydWin.exe "$@"
