# WslNotifyd

WslNotifyd is an implementation of the [Desktop Notifications Specification](https://specifications.freedesktop.org/notification-spec/notification-spec-latest.html) using Windows native functionality.

## Requirements

- [WSL2 settings](https://learn.microsoft.com/en-us/windows/wsl/wsl-config) (These settings are all enabled by default)
  - systemd enabled
  - localhostForwarding enabled
- can connect D-Bus user session (check `$DBUS_SESSION_BUS_ADDRESS`)

## Usage

1. [Install .NET sdk](https://learn.microsoft.com/en-us/dotnet/core/install/linux)
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
- Replace existing notifications
  ```sh
  $ notify-send -p foo
  1
  $ notify-send -r 1 bar
  ```

## Todo

- Custom icons and images
- Custom sounds

## Uninstall

1. Remove out dir
2. Delete registry key `HKCU\Software\Classes\AppUserModelId\WslNotifyd`
  ```
  reg.exe delete HKCU\Software\Classes\AppUserModelId\WslNotifyd
  ```
## Limitations

- Markup is not supported
- Expiration timeout is not respected much
