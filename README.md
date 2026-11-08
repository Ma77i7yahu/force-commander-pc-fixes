# Star Wars: Force Commander (PC) – Mod Installer

Optional fixes and improvements for **Star Wars: Force Commander** (PC, with the official 1.1 update): a widescreen 3D view with the menus kept at 4:3, Rebel mines that do damage again, a scrolling save list for all 32 slots, better unit movement around buildings, smarter computer-controlled units, and the Imperial hospital and repair bay from the first mission. Each option can be turned on or off on its own, and the installer keeps your original files so you can change or undo anything later.

**This is a patch only.** No game files are included. You need your own copy of the game.

## Download

Get `ForceCommanderMods-v1.0.zip` from the [**Releases**](https://github.com/Ma77i7yahu/force-commander-pc-fixes/releases/latest) page. It holds the installer and its README and `PATCHES.txt`. `SHA256SUMS.txt` on the release page lets you check your download.

Full instructions, requirements and troubleshooting: [docs/README-installer.md](docs/README-installer.md).

## The options

| Option | Status |
|---|---|
| Widescreen 3D view | Tested at 1920x1080 |
| Keep menus in 4:3 | Tested at 1920x1080 |
| Fix Rebel mines doing no damage | Tested |
| Scrolling save list | Tested |
| Better unit movement around buildings | Tested in both campaigns |
| Smarter computer-controlled units | Tested in the early Imperial missions |
| Imperial hospital and repair bay from the first mission | Tested |

The installer needs the full CD install with the official 1.1 update (`Resource\focom.exe` MD5 `3159FFE0CFFC10F895E00C461503AF6B`, `fcv1p1.rpk` MD5 `B587A80AB1C5E4B05855D3D4C7CC6524`) and dgVoodoo 2. All the options together produce exactly the files that were played and tested; other combinations are checked to build correctly but haven't been played one by one. All multiplayer players should use the same set.

## What's in this repo

- `docs/README-installer.md`: the README that comes with the installer.
- `PATCHES.txt`: every byte the installer changes, with its address and disassembly.
- `src/`: the installer's C# source and the patch tools. See [BUILDING.md](BUILDING.md).
- `test/`: the test harness used to check the installer's output.

## Licence

The code, patches and documentation here are under the [MIT licence](LICENSE): reuse or port them freely, with credit. The game itself isn't covered.

## Credits

Thanks to Dege (dgVoodoo 2), whose work makes the game run well on modern Windows.

## Disclaimer

Unofficial fan-made mods, not affiliated with or endorsed by Lucasfilm, LucasArts or Disney. Star Wars is a trademark of its owners. No game files are distributed here. Use at your own risk, and keep the backups the installer makes.
