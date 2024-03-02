# WslNotifyd

WslNotifyd is an implementation of the [Desktop Notifications Specification](https://specifications.freedesktop.org/notification-spec/notification-spec-latest.html) using Windows native functionality.

## Requirements
- WSL2 (WSL1 is not confirmed to work)
- [WSL2 settings](https://learn.microsoft.com/en-us/windows/wsl/wsl-config)
    - [systemd](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#systemd-support) enabled
    - [localhostForwarding](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#main-wsl-settings) enabled
    - [Windows interop](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#interop-settings) enabled
- can connect D-Bus user session (check `$DBUS_SESSION_BUS_ADDRESS`)

## Usage

1. [Install .NET sdk](https://learn.microsoft.com/en-us/dotnet/core/install/linux) on WSL2
    - .NET 8 is confirmed to work
2. Clone the repo
    ```sh
    git clone https://github.com/ultrabig/WslNotifyd.git
    ```
3. Build the app
    ```sh
    dotnet publish WslNotifyd -o out && dotnet publish WslNotifydWin --runtime win-x64 -o out/WslNotifydWin --self-contained
    ```
4. Run the app
    ```sh
    ./out/WslNotifyd
    ```
5. Send notifications from any app!
    ```sh
    notify-send 'It works!'
    ```

## Supported features

- Wait dismiss/action
    ```sh
    notify-send -w foo
    ```
- Actions
    ```sh
    notify-send -w -A action1=aaa foo
    ```
- Urgency (critical only)
    ```sh
    notify-send -u critical foo
    ```
- Custom icons
    ```sh
    notify-send -i firefox foo
    ```
- Replace existing notifications
    ```sh
    $ notify-send -p foo
    1
    $ notify-send -r 1 bar
    ```

## Integration with systemd/D-Bus

1. [Build the app](#usage) first
2. Run the installer script
    ```sh
    ./install.sh
    ```
    - This script installs following files into `~/.local`
        - D-Bus session service file
            - `~/.local/share/dbus-1/services/org.freedesktop.Notifications.service`
        - systemd user unit service file
            - `~/.local/share/systemd/user/WslNotifyd.service`
        - WslNotifyd
            - `~/.local/lib/WslNotifyd`
3. Sending notification automatically runs WslNotifyd by systemd/D-Bus
    ```sh
    notify-send foo
    ```
    - Maybe you need to restart WSL
4. Use `systemctl` command to stop WslNotifyd
    ```sh
    systemctl --user stop WslNotifyd
    ```

## Todo

- Custom icons and images
    - app_icon is supported
- Custom sounds

## Known issues
- WSL2 won't shut down while WslNotifyd is running

## Uninstall

1. Remove `out` directory
    - If you used `install.sh`, then remove following files/dirs
        - `~/.local/share/dbus-1/services/org.freedesktop.Notifications.service`
        - `~/.local/share/systemd/user/WslNotifyd.service`
        - `~/.local/lib/WslNotifyd`
2. Delete the registry key `HKCU\Software\Classes\AppUserModelId\WslNotifyd`
    ```sh
    reg.exe delete 'HKCU\Software\Classes\AppUserModelId\WslNotifyd'
    ```
    Remove quotes when you want to use cmd.exe

## Limitations

- Markup is not supported
- Expiration timeout is not respected much
