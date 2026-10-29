# TF2 Weapon Crosshairs

A small Windows app that gives every Team Fortress 2 weapon its own crosshair.
It works like the [cfg.tf weapon-specific crosshairs tool](https://cfg.tf/tools/crosshairs/),
but covers every weapon script the game has (the cfg.tf list stopped being updated years ago),
lets you set the crosshair size per weapon, previews the result over a game screenshot,
and can write the files straight into your TF2 folder.

![screenshot](docs/screenshot.png)

## Using it

1. Download an exe from the Releases page (or build it, see below). There are two flavours of the
   same app:
   - `TF2Crosshairs.exe` (about 50 MB) is self-contained and needs nothing installed on a 64-bit
     Windows 10 or 11 machine. This is the one to send to friends.
   - `TF2Crosshairs-slim.exe` (about 1 MB) needs the
     [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0); if it is missing,
     Windows shows a prompt with the download link.
2. Run it. It finds your TF2 install through Steam and shows the `tf\custom` folder it will install
   into. If it cannot find it, click **Browse...** and pick
   `Steam\steamapps\common\Team Fortress 2\tf\custom`.
3. Select one or more weapons on the left (Ctrl/Shift-click for several, or click a class name such
   as **Scout** to select every weapon of that class), then click a crosshair on the right. It is
   assigned immediately and shown in the preview.
   - **Size** changes how big the crosshair is drawn in game (64 px is what cfg.tf used).
   - **Explosion** lets explosive weapons use one of the smoke-free explosion effects instead of
     the stock one, the same five choices cfg.tf offers.
   - Weapons you leave alone keep their stock crosshair. **Reset selected weapons to stock** under the
     preview un-assigns the selection; **Reset ALL weapons** at the bottom clears everything so you can
     start over.
4. Click **Install into TF2**. The app writes `tf\custom\crosshairs` and removes what it wrote last
   time, so un-assigning a weapon really puts it back to stock. Restart TF2 if it is running.
5. In TF2, set the crosshair to **None** in the options (that is `cl_crosshair_file ""`),
   otherwise the game keeps drawing that one. The crosshair colour still comes from TF2's normal
   crosshair colour settings.

Your setup is saved automatically and restored the next time you open the app. The app starts in dark
mode; the **Dark mode** checkbox at the top right switches to a light theme and is remembered too.

**Sharing with friends:** either send them the exe, or click **Save ZIP...** and have them extract
it into `Team Fortress 2\tf\custom` so they end up with `tf\custom\crosshairs`.

**Your own crosshairs:** click **Add custom crosshair (.vtf)...**. 64x64 textures work best. The
matching `.vmt` material is generated for you and the file is copied into the package.

## How it works

TF2 reads a weapon's settings from `scripts/tf_weapon_<name>.txt`. The game ships those scripts
encrypted (`.ctx`), but the engine looks for a plain `.txt` in `tf/custom` first and only then falls
back to the encrypted file. The app takes a copy of each stock script, replaces its `crosshair`
texture block with one that points at `materials/vgui/replay/thumbnails/<crosshair>` and writes it
into `tf/custom/crosshairs/scripts`. The crosshair textures go into that `replay/thumbnails` folder
because the game does not validate it under `sv_pure`, which is the same trick cfg.tf and the
original [weapon-specific crosshair guide](https://www.teamfortress.tv/30866/guide-weapon-specific-custom-crosshairs)
rely on. Test on the servers you play on; a strict community server can still block custom files.

The explosion options rewrite the `ExplosionEffect`, `ExplosionPlayerEffect` and
`ExplosionWaterEffect` lines of the script with the particle names cfg.tf uses. If you already use a
"no explosion smoke" mod, remove it and pick the effect here instead, otherwise the two will fight
over the same script files.

## Weapon list

[`assets/weapons.tsv`](assets/weapons.tsv) maps each script to the weapons that use it. It was built
from the game's own `items_game.txt` (every item's `item_class`) plus the per-class translation TF2
applies in its source code (`tf_weapon_shotgun` becomes `tf_weapon_shotgun_soldier` for Soldier,
all-class melee weapons use each class's stock melee script, and so on). Things worth knowing:

- The Gas Passer uses `tf_weapon_jar_gas`, not the Jarate/Mad Milk scripts cfg.tf listed it under.
- The Pain Train on Demoman uses the Bottle script; on Soldier the Shovel script.
- The B.A.S.E. Jumper is `tf_weapon_parachute_secondary` on Soldier and `tf_weapon_parachute_primary`
  on Demoman.
- All-class melee weapons (Frying Pan, Saxxy, Conscientious Objector, ...) use the class's stock melee
  script, except on Heavy where the game uses the Pyro Fire Axe script.
- Grappling Hook, PASS Time JACK, Spellbook and the Spy's watches have their own scripts and are listed
  under "All classes" / Spy.

## Building

You need the .NET 10 SDK, which comes with Visual Studio 2026 (or install it on its own). The project
targets `net10.0-windows` and opens in Visual Studio as a normal WinForms project.

```bat
build.cmd
```

That publishes both exes into `dist\` (self-contained and framework-dependent, single file each). The
GitHub Actions workflow does the same on every push and attaches both to a release when you push a
`v*` tag.

## Repository layout

| Path | What it is |
| --- | --- |
| `src/TF2Crosshairs/` | C# WinForms app on .NET 10. `Vtf.cs` decodes textures for the preview, `Packager.cs` writes the package, `Tf2Locator.cs` finds the game, `MainForm.cs` is the UI, `Gallery.cs` the hand-painted crosshair grid, `Theme.cs` the dark/light palettes. |
| `assets/crosshairs/*.vtf` | The 98 crosshair textures (Leth's pack, Tob's black brackets, xhairshadow, CPMA, Quake Live) from the cfg.tf repository. |
| `assets/crosshairs.tsv` | Which pack each texture belongs to (anything unlisted shows up under "Other"). |
| `assets/scripts/*.txt` | The stock weapon scripts, decrypted from the game's `.ctx` files, used as the base for patching. |
| `assets/weapons.tsv` | Class, script and weapon names shown in the list. |
| `assets/preview_bg.jpg` | The game screenshot behind the preview (from cfg.tf). |

To add a crosshair to the bundle, drop the `.vtf` into `assets/crosshairs`, add a line to
`assets/crosshairs.tsv` and rebuild; everything in `assets` is embedded into the exe.

## Credits

- [cfg.tf](https://cfg.tf) by mkrl (MIT) for the original tool, the crosshair packs it collects and the preview image.
- The crosshair authors: Leth (xhair pack), Tob (black brackets), xhairshadow, and whoever ported the CPMA and Quake Live sets.
- The weapon-specific crosshair method from the teamfortress.tv guide linked above.
- Valve for Team Fortress 2 and for publishing the game's source, which settled the per-class script names.
