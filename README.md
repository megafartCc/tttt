# RAMV2 Custom Release

Custom packaged build of Roblox Account Manager with RAMV2 changes.

## Included

- `source/` - source snapshot used for this custom build.
- `release/RAMV2_custom_20260321_230918/` - custom release files (trimmed package).
- `Joiner/updatedhopper.lua` - updated hopper script.

## Run

1. Open `release/RAMV2_custom_20260321_230918/`.
2. Run `Roblox Account Manager.exe`.

## Notes

- Runtime/user data files (`AccountData.json`, settings, logs) are intentionally not included.
- The bundled `.local-chromium` cache was removed to keep Git publishing reliable. Core manager binaries and CEF runtime folders are included.
- Base project attribution remains under `source/` (including original license/readme).
