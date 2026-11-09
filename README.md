<p align="center"><img src="docs/icon.png" width="96" alt="icon"></p>

# SA Skin Manager

**English** · [Português](README.pt-BR.md)

Launcher and manager for swapping character skins in **GTA San Andreas (PC, classic version)**.
It lists every character in the game, lets you pick "replace this character with this skin" and installs it for you,
with one-click undo. The program is available in English and Portuguese.

## How it works

- **First run:** the program looks for the game folder (Steam, Rockstar, common paths). You confirm it or pick the folder.
  With *"Load every NPC model now"* checked, it reads `data/peds.ide`, `models/gta3.img` and `models/cutscene.img`
  and builds the full character list with thumbnails. After that everything is cached.
- **Characters only:** characters are recognized by their skeleton (32 bones or more). Animated cutscene objects and
  cars are left out.
- **Skin library:** import `.dff` + `.txd` files, a folder or a `.zip` (or drop them on the list).
  You can search, sort (newest, name, most used), rename (`F2`, double-click or right-click) and see how many characters
  use each skin. The **⋯** menu exports the whole library to a backup `.zip` and restores it later.
- **Swap:** select the character on the left, the skin on the right and click *Swap to this skin*.
  Use `Ctrl`/`Shift` to select several characters and apply the same skin to all of them.
- **Cutscenes and variants:** when you swap a story character (e.g. Big Smoke), its cutscene version (`cssmoke`) and
  mission variants (`smokev`…) are swapped too. You can uncheck the ones you don't want.
- **Install modes:**
  - **Mod Loader** (recommended, used automatically when `modloader.asi` exists): writes to
    `modloader\SaSkinManager\<model>\`. The original `.img` files are not changed.
  - **Direct IMG:** replaces the entries inside the `.img`. Before the first change it makes a full backup
    (`gta3.img.skinmanager.bak`) and keeps the original of every replaced file for *Undo*.
- **Active swaps:** grouped by character, with *Undo all*, the list of models in each swap and *Restore all*.
- **Filters:** *All*, *Swapped*, *Not swapped* and *★ Favorites* (mark them with the star).
- **Shortcuts:** `Ctrl+F` search, `Enter` swap, `Ctrl+Z` undo the last swap.
- **Settings:** language (English / Português), accent color (orange, green, yellow, purple, blue, pink),
  default install mode, grid or list, thumbnail size, show cutscene versions in the list,
  change the game folder and clear the cache.

### CJ

CJ is not a regular character: his body is built from clothing parts in `models/player.img`.
Import "CJ replacement" packs with the **+ CJ pack** button: the files keep their original names and go to
`modloader\SaSkinManager\cj\player.img\` or into `player.img`.
To play as a regular NPC instead of CJ, use a mod such as Skin Selector (CLEO).

## Install

Download `SaSkinManager.exe` from the [Releases](../../releases) page and run it. No .NET install needed.

> Recommended: use a **copy** of the game folder for your first test.

## Requirements

- Windows 10/11
- Classic GTA San Andreas for PC (1.0, Steam or "downgraded"). The *Definitive Edition* uses another format and is not supported.
- Optional: [Mod Loader](https://github.com/thelink2012/modloader)

Program data lives in `%AppData%\SaSkinManager` (settings, library, cache, per-entry backups).

## 🎨 Icon wanted!

The current icon (the cheetah) is only a **placeholder**. If you'd like to make the official SA Skin Manager icon,
open an [Issue](../../issues) with your suggestion (square PNG, ideally 256×256 or larger).
Suggestions are very welcome!

## Build

```bash
dotnet build
dotnet test
dotnet publish src/SaSkinManager.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Layout

- `src/SaSkinManager.Core`: file formats (IMG VER2, IDE, TXD/DXT, DFF), character catalog, library and swap logic. No WPF, testable.
- `src/SaSkinManager.App`: WPF user interface (.NET 8).
- `tests/SaSkinManager.Core.Tests`: xUnit tests with synthetic IMG/TXD files.

## Roadmap

- v2: 3D model preview (`.dff` + `.txd`), original vs. new side by side, grid of rotating models; swap profiles.
- v3: vehicle swaps (cars, bikes, boats, planes, helicopters…) via `data/vehicles.ide`.

## License

[GPL-3.0](LICENSE). No game files (Rockstar Games) are distributed with this project.
