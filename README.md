# YSM Installer
### [YSM Community](https://discord.gg/XmbhaSRqfZ)
### [YSM Repository](https://github.com/Yokaiste/YSM)

## Features
- Finds local `Warno.exe` installations automatically
- Supports manual `Warno.exe` selection if auto-detection fails
- Installs [YSM](https://github.com/Yokaiste/YSM) and **YSM x WiF** for detected WARNO versions
- Shows all detected WARNO versions with quick "Show more" expansion
- Uses latest compatible mod package when game version is newer than supported catalog
- Supports catalog source selection in-app:
  - Official `mods-list.json`
  - Yokaiste GitHub releases (with automatic fallback to official source)
- Checks for installer updates from GitHub releases on startup
- Performs safe install workflow with:
  - WARNO process close before install
  - Existing config/mod backup
  - Rollback on failure

## Requirements
- Windows 10 or higher
- Internet connection

## Behavior
The application uses the internet to:
- fetch supported mod metadata from selected catalog source (`mods-list.json` or Yokaiste releases)
- download mod archives from [YSM_Archives](https://github.com/dary1337/YSM_Archives) or [YSM](https://github.com/Yokaiste/YSM)
- check and download installer updates from GitHub releases
