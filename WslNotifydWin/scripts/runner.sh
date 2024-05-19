#!/bin/bash
set -e
set -o pipefail

copy_src="$1"
if ! [[ -d $copy_src ]]; then
    echo "${copy_src} is not a directory" >&2
    exit 1
fi
shift

windows_tmpdir_wsl="$(./scripts/get-windows-tmpdir.sh)"
dst="${windows_tmpdir_wsl}/WslNotifydWin"

rm -rf "${dst}"
cp -r "${copy_src}" "${dst}"
exec "${dst}/WslNotifydWin.exe" "$@"
