#!/bin/bash
set -e
set -o pipefail

# C:\Users\<user>\AppData\Local\Temp
# `< /dev/null` is required to prevent cmd.exe from stealing stdin
windows_tmpdir="$(/mnt/c/Windows/System32/cmd.exe /u /c 'echo %TEMP%' < /dev/null 2> /dev/null | iconv -f UTF-16 -t UTF-8 | sed -e 's/\r$//')"
if [[ -z $windows_tmpdir ]]; then
    echo 'failed to get %TEMP%' >&2
    exit 1
fi

# /mnt/c/Users/<user>/AppData/Local/Temp
windows_tmpdir_wsl="$(wslpath -u "${windows_tmpdir}")"

printf '%s\n' "${windows_tmpdir_wsl}"
