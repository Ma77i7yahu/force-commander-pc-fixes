# Star Wars: Force Commander – Mod Installer

A small installer that applies optional fixes and improvements to Force Commander. It only changes
bytes in the game's own files, contains none of the game's files, and keeps your originals, so you
can change or remove any option later.

## What it can install

| Option | What it does | Status |
|---|---|---|
| **Widescreen 3D view** | The game only knows 4:3 screen modes. dgVoodoo is told to render at your real resolution, so the picture is sharp and fills the screen, and the 3D view is corrected so units and terrain keep their proper shape. You see more of the battlefield at the sides. The in-mission HUD is still stretched sideways (see below). | Tested at 1920x1080. Other aspect ratios use the same calculation but haven't been played |
| **Keep menus in 4:3** | Without this, the front-end menus and the Esc/Save/Load screens are spread across the wide screen, so text and pictures drift out of their frames. Everything 2D is now drawn at 4:3 unless a mission is actually running. Needs the widescreen option. | Tested at 1920x1080 |
| **Fix Rebel mines doing no damage** | The official 1.1 update broke the Rebel mine: it homes in on a target variable that nothing ever sets, so it stays where it is and explodes out of range. The mine now homes in on the unit that triggered it, as in version 1.0. | Tested |
| **Scrolling save list** | The game has 32 save slots, but its Save and Load screens can only show 9, so later saves can't be reached. The lists now scroll with the mouse wheel, Up/Down or PgUp/PgDn, on the in-game screens and the main menu's Load Saved screen. | Tested |
| **Better unit movement around buildings** | Big units wobble, weave and take long detours next to buildings, or drive through them. Steering no longer overshoots. Vehicles route around the real outline of most Rebel and Imperial buildings rather than a big circle, and stop at the edge if ordered into one. Infantry still walk in and out, aircraft fly over, vehicles no longer veer away from landers high above them, and turrets can still be placed right next to buildings. | Tested in both campaigns |
| **Smarter computer-controlled units** | Units shot at by something now go after the attacker. Computer units in Aggressive or Guard stance use Defensive, the stance that closes with enemies it sees, with a larger acquire range. Computer infantry capture the command centres and turret controls they would otherwise only shoot. Rebel infantry see twice as far, including your own in the Rebel campaign. This makes the Imperial campaign harder, so it pairs with the next option. | Tested in the early Imperial missions |
| **Imperial hospital and repair bay from the first mission** | These normally aren't available until late in the campaign. They can now be bought in the 7 missions from Ruul up to Abridon. | Tested |

All the options together produce exactly the files that were played and tested. Other combinations
are checked to build correctly and load in Windows, but they haven't been played one by one.

### Things to know

- **Multiplayer:** anything that changes `focom.exe` or the game's scripts may go out of sync with
  players who don't have the same options. All players should use the same set.
- **Saved games:** saves made with a different set of options may not load, so finish your current
  mission before you change them. Your campaign progress isn't affected. The 1.1 update itself doesn't
  load saves from version 1.0.
- **HUD:** the in-mission HUD is still stretched on a wide screen. It's made of 3D meshes in the
  game's data and shares its camera with the menus, so it can't be squeezed separately. It does
  squeeze while the Esc menu is open. The mouse and click areas line up with it either way.
- **Movement limits:** a big vehicle can clip a building's corner slightly, and mission-specific
  buildings still use the original circles.

## Requirements

- Force Commander for PC, installed in full from the CD, with the **official 1.1 update**:
  - `Resource\focom.exe`: MD5 `3159FFE0CFFC10F895E00C461503AF6B`
  - `Resource\forcecommand\fcv1p1.rpk`: MD5 `B587A80AB1C5E4B05855D3D4C7CC6524`
  - `Resource\forcecommand\forcecommand.rpk`: MD5 `CEF2423DA209F63C74157274A057BF54` (the 274 MB
    file, only needed for the hospital and repair bay option)

  If a file is different or missing, the installer says so and leaves it alone. Options that don't
  need that file still work.
- **dgVoodoo 2** for the widescreen and menu options: `ddraw.dll`, `D3DImm.dll` and `dgVoodoo.conf`
  next to `focom.exe`. If you have no `dgVoodoo.conf`, run `dgVoodooCpl.exe` from that folder once and
  click Apply. Tested with dgVoodoo 2.55.
- Keep the game's own screen setting at **1280x960**. That's the mode these patches were tested with,
  and dgVoodoo turns it into your real resolution.
- Windows with .NET Framework 4 (included in Windows 8, 10 and 11).

## How to install

1. Close the game.
2. Run `ForceCommanderModInstaller.exe`. It finds the game by itself when it can. If not, click
   **Browse...** and pick the game folder or its `Resource` folder.
3. Tick the options you want. Point at an option to see what it does. Choose your screen resolution.
4. Click **Install selected**.

To change options later, run the installer again: it shows what's installed now and rebuilds the
files from the backups. **Restore original game** removes every mod.

### What it keeps

| File | How it's kept |
|---|---|
| `focom.exe` | Original saved as `focom_original.exe` |
| `forcecommand\fcv1p1.rpk` | Original saved as `forcecommand\fcv1p1_original.rpk` |
| `dgVoodoo.conf` | Original saved as `dgVoodoo_original.conf`, and put back if you remove the widescreen option. Other changes you've made to the file since installing are lost at that point |
| `forcecommand\forcecommand.rpk` | No copy, because it's 274 MB and only 13 bytes change. The installer holds the original bytes and puts them back |

If Windows refuses access to the game folder (for example under `C:\Program Files`), right-click the
installer and choose **Run as administrator**.

## Troubleshooting

- **"Isn't the version 1.1 file".** Install the official 1.1 update, or put your original file back.
  Files changed by other mods can't be patched.
- **The hospital and repair bay option is greyed out.** A smaller install can leave
  `forcecommand.rpk` on the CD. A full install copies it to the hard drive.
- **The picture has the wrong shape.** Run the installer again and choose the resolution your screen
  really uses.
- **The Display options are greyed out.** dgVoodoo wasn't found. See Requirements.

## For the curious

`PATCHES.txt` lists the changes file by file, with offsets and the new bytes.

## Credits

Patches made by reverse engineering `focom.exe` and the game's script archives. Thanks to Dege
(dgVoodoo 2), whose work makes the game run well on modern Windows.

Source code and full patch details: https://github.com/Ma77i7yahu/force-commander-pc-fixes
