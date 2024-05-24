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
hash_path="${dst}/__hash__"
if [[ $hash != development && -f $hash_path ]]; then
    hash_on_disk="$(<"${hash_path}")"
    if [[ $hash_on_disk = $hash ]]; then
        do_copy=0
    fi
fi
if [[ $do_copy = 1 ]]; then
    echo "copy WslNotifyd to ${dst}"
    rm -rf "${dst}"
    cp -r "${copy_src}" "${dst}"
    printf '%s\n' "${hash}" > "${hash_path}"
fi

exec "${dst}/WslNotifydWin.exe" "$@"
