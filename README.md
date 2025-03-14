# WslNotifyd

WslNotifyd is an implementation of the [Desktop Notifications Specification](https://specifications.freedesktop.org/notification-spec/latest/) using Windows native functionality.

## Requirements
- WSL2 (WSL1 is not confirmed to work)
- [WSL2 settings](https://learn.microsoft.com/en-us/windows/wsl/wsl-config)
    - [systemd](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#systemd-support) enabled
    - [localhostForwarding](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#main-wsl-settings) enabled
    - [Windows interop](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#interop-settings) enabled
- can connect D-Bus user session (check `$DBUS_SESSION_BUS_ADDRESS`)
- `iconv` is installed
- `gtk3` is installed if you want to display images

## Usage

1. [Install .NET sdk](https://learn.microsoft.com/en-us/dotnet/core/install/linux) on WSL2
    - .NET 8 is confirmed to work
2. Clone the repo
    ```sh
    git clone https://github.com/ultrabig/WslNotifyd.git
    ```
3. Build the app
    ```sh
    cd WslNotifyd
    dotnet publish WslNotifyd -o out && dotnet publish WslNotifydWin --runtime win-x64 -o out/WslNotifydWin --self-contained
    ```

   If you're building from a source tarball or in an environment where git is not available, you can specify a custom git hash:
   ```sh
   dotnet publish WslNotifyd -p:CustomGitHash="$(date +%Y%m%d%H%M%S)" -o out && dotnet publish WslNotifydWin --runtime win-x64 -o out/WslNotifydWin --self-contained
   ```
4. Run the app
    ```sh
    ./out/WslNotifyd
    ```
5. Send notifications from any app!
    ```sh
    notify-send 'Hello' 'World!'
    ```
    ![hello-world](https://github.com/ultrabig/WslNotifyd/assets/161245554/6363a0f1-368b-4f8f-bf9d-2bba14324ec5)


## Supported features
There are usage examples with images on the [Wiki](https://github.com/ultrabig/WslNotifyd/wiki/Supported-features) with images.
- Wait dismiss/action
    ```sh
    notify-send -w foo
    ```
- Actions
    ```sh
    notify-send -A 'action1=aaa' foo
    # with icons
    notify-send -h 'boolean:action-icons:true' -A 'firefox=Firefox' foo
    ```
- Urgency (critical only)
    ```sh
    notify-send -u critical foo
    ```
- Custom icons
    ```sh
    notify-send -i firefox foo
    ```
- Custom images
    ```sh
    # 1*1 blue pixel (width, height, rowstride, has_alpha, bits_per_sample, channels, rgb data array)
    notify-send -h 'variant:image-data:(int32 1, int32 1, int32 3, false, int32 8, int32 3, [byte 0, 0, 255])' foo
    # specify custom image path
    notify-send -h 'string:image-path:/path/to/image.png' foo
    ```
- Custom sounds
    - You need to choose from the [predefined sounds](https://learn.microsoft.com/en-us/uwp/schemas/tiles/toastschema/element-audio) for the `sound-name`
    ```sh
    notify-send -h 'string:sound-name:ms-winsoundevent:Notification.Reminder' foo
    ```
- Suppress sounds
    ```sh
    notify-send -h 'boolean:suppress-sound:true' foo
    ```
- Replace existing notifications
    ```sh
    $ notify-send -p foo
    1
    $ notify-send -r 1 bar
    ```
- Add an inline reply textbox (non-standard KDE feature)
    ```sh
    notify-send -h 'string:x-kde-reply-placeholder-text:AAA' -A inline-reply='Reply' foo
    ```
    Use the following script to monitor replies.
    ```sh
    busctl --user \
        --match="type='signal',
            interface='org.freedesktop.Notifications',
            path='/org/freedesktop/Notifications',
            member='NotificationReplied'" \
        monitor
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
- Map [sound naming specification](https://specifications.freedesktop.org/sound-naming-spec/sound-naming-spec-latest.html) `sound-name` to `ms-winsoundevent` name

## Uninstall

1. Remove `out` directory
    - If you used `install.sh`, then remove following files/dirs
        - `~/.local/share/dbus-1/services/org.freedesktop.Notifications.service`
        - `~/.local/share/systemd/user/WslNotifyd.service`
        - `~/.local/lib/WslNotifyd`
2. Remove WslNotifydWin in Windows TEMP directory
    - `%TEMP%\WslNotifydWin`
3. Delete the registry key `HKCU\Software\Classes\AppUserModelId\WslNotifyd`
    ```sh
    reg.exe delete 'HKCU\Software\Classes\AppUserModelId\WslNotifyd'
    ```
    Remove quotes when you want to use cmd.exe

## Known issues

- It is not expected to run on multiple WSL instances simultaneously

## Limitations

- Markup is not supported
- Expiration timeout is not respected much
- You cannot use arbitary audio files as notification sounds
