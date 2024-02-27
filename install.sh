#!/bin/bash
set -ex

PREFIX="${HOME}/.local"
mkdir -p "${PREFIX}/share/dbus-1/services" "${PREFIX}/share/systemd/user" "${PREFIX}/lib"
sed "s!@INSTALL_PREFIX@!${PREFIX}!" resources/dbus-service/org.freedesktop.Notifications.service > "${PREFIX}/share/dbus-1/services/org.freedesktop.Notifications.service"
sed "s!@INSTALL_PREFIX@!${PREFIX}!" resources/systemd-user-units/WslNotifyd.service > "${PREFIX}/share/systemd/user/WslNotifyd.service"
[[ -d "${PREFIX}/lib/WslNotifyd" ]] && rm -rf "${PREFIX}/lib/WslNotifyd"
cp -a out "${PREFIX}/lib/WslNotifyd"
