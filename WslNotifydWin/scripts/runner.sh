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

hash="$1"
shift

do_copy=1
if [[ $hash != development && -f $dst/__hash__ ]]; then
    hash_on_disk="$(<"${dst}/__hash__")"
    if [[ $hash_on_disk = $hash ]]; then
        do_copy=0
    fi
fi
if [[ $do_copy = 1 ]]; then
    echo "copy WslNotifyd to ${dst}"
    rm -rf "${dst}"
    cp -r "${copy_src}" "${dst}"
fi

exec "${dst}/WslNotifydWin.exe" "$@"
