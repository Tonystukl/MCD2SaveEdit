# Minecraft Dungeons II Save Editor

A native Windows save editor for Minecraft Dungeons II, with an illustrated inventory, four-piece armor loadout, enchantment editing, and interactive Overworld and Sift maps. Built with C# and Windows Forms. Runs locally; the application has no network features.

**Version 0.3.1.** Inventory and Xbox save transactions have automated coverage. Quest completion and local fog alignment are experimental and have not been verified in a live game session.

## Download

Get **MCD2SaveEdit.exe** or the Windows ZIP from the [latest release](https://github.com/Tonystukl/MCD2SaveEdit/releases). The portable Windows x64 EXE includes its .NET runtime. Extract the ZIP into a writable folder and double-click the EXE. No Python installation is needed.

## Features

- Illustrated inventory and equipment cards with game display names and icons.
- Catalogue of 296 launch-build gear definitions: 80 weapons, 152 armor pieces, 40 artifacts and 24 talismans.
- Separate helmet, chest, leggings and boots slots.
- Gear power, rarity, original power, gear level, XP, stack count and equipment editing.
- 32 named enchantments, compatible-slot filtering, tiers I–III and editable strengths.
- Emeralds, Echo Shards, enchantment points, character level and other saved attributes.
- Compatible gear exposes Sharpness bonus and Protection strength as percentages.
- Original Overworld and Sift map artwork with quest markers, area labels, zoom and pan.
- Details and published reward pools for 42 quests. Available/active native quest records can be marked completed and awarded XP, emeralds and generated gear in one undoable draft change.
- Region-wide fog revealing, plus an explicitly experimental local fog preview and area reveal.
- Dark/light themes, searchable pickers, undo/redo, item import/export, JSON editing and change review.
- Xbox save discovery, active-revision tracking, whole-account backups and write verification.

## Quick start

1. Close Minecraft Dungeons II completely.
2. Open the editor. A single detected Xbox character opens automatically; **Find Xbox saves** lets you choose or reload another.
3. Select a gear card, edit its fields, and click **Apply changes**. Currency controls apply directly to the editor's draft.
4. Use **Enchant…** to choose a compatible enchantment and tier. **Advanced…** exposes the full saved item.
5. Review your changes and click **Save to game**. The editor creates a verified backup before writing the new active revision. **Export copy** creates a separate JSON file.

**Import character** opens an exported character JSON for saving back to its matching Xbox character. If the game has saved newer progress since you opened the character, the editor asks whether to replace it. The game must stay closed until writing finishes.

## Quests and maps

Open **Quests / Map**, then choose **Overworld** or **The Sift**. Click a yellow/blue quest marker or choose a quest in the searchable list. Details show its saved state, description, objectives and rewards.

**Complete quest** completes the quest's existing task records and grants published XP, randomly rolled emerald drops and native gear rewards. Choose reward gear power from 1–100. For repeatable quests, check **First time** only if you have never finished that quest before: the observed save has no reliable first-completion ledger. Already-completed quests cannot claim rewards again. Unavailable or uninitialized quests must first be started through the game; the editor never invents missing task IDs. Unlisted saved quests remain visible but cannot claim unknown rewards.

Quest XP can raise your character level and updates the matching metadata. Rewards use locally generated rolls from published fixed/weighted pools, rather than the game's exact random sequence. No Echo Shards are invented as quest rewards.

**Map areas** lets you select an area and reveal the entire selected region's saved fog grid. Enable **Fog preview (experimental)** to inspect approximate local coverage and reveal fog near one area. The texture and quest marker coordinates come from game-file data, but fog-grid orientation and named-area boundaries are inferred. Local revealing may affect a different nearby section. Whole-region revealing does not depend on that alignment.

**Quest scripts, physical story gates, cinematic triggers and live reward acceptance have not been verified.** Fog revealing does not unlock those gates or activate minecart stations. Completing saved task states may not replace every in-game script event. Both actions stay in your draft, support Undo, and require **Save to game**.

## Compatibility

Supports the observed Xbox/Game Pass format from game 1.1.1.0: `FCharacterSaveV1`, internal version 0, soft version 5. Unknown JSON fields and exact 64-bit integers are preserved. Other platforms/builds are not verified.

The catalogue covers published launch-build records, rather than guaranteeing future DLC support. All 296 generated gear definitions and all 42 quest reward definitions pass structural save tests. Tests are performed on copied or synthetic fixtures; they do not prove every combination is accepted by the game.

Equipment power and character level are different quantities. Setting power to thousands is not a verified way to get unlimited damage. Above-normal effect strengths are experimental. Normal Sharpness tiers are 10/20/30%, and normal Protection tiers are 10/15/20%. Talisman saved levels 0–2 correspond to tiers I–III.

Twenty-five enchantments have numeric presets inferred from published tier descriptions. Seven require manual strengths for newly chosen tiers: Chain Reaction, Cow Stampede, Crash Landing, Gravity Pulse, Shockwave, Thundering and Tumbleshot. Existing strengths are retained.

The observed character blob is JSON. Its GUID filename is an Xbox storage identifier, not an encrypted weapon code. The editor writes native item tags and generator/effect records; it does not decrypt installed game archives.

## Backups and recovery

Backups are in **Backups** beside the EXE. Xbox backups contain the whole account save folder and `restore-info.json`; ordinary JSON edits receive a single-file backup. Xbox writes stage a fresh blob and container table, publish their index reference, verify the result and attempt rollback on caught failures. A power loss between writes can still require restoration. Cloud synchronization can affect which revision the game loads.

To restore, close the game, pause cloud synchronization, keep a copy of the current account folder, then restore the backup's complete `original` contents to its recorded `sourceRoot`. Keep indexes and blobs from the same backup together. Backups can contain account-related data: keep them private.

## Build from source

Requires Windows and the .NET 10 SDK. The complete source archive attached to the release includes every asset. When cloning this repository, first unpack `assets.zip` into `Source` (the ZIP contains only the `Data` folder), or run `Build.ps1`, which does that for you. The compressed artwork keeps the repository compact; all C# code and catalogue JSON are directly browsable.

```powershell
Expand-Archive -LiteralPath ./assets.zip -DestinationPath ./Source -Force
dotnet build Source/MCD2SaveEdit.csproj -c Release
dotnet publish Source/MCD2SaveEdit.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

`--self-test <copied-character-path> <log-path>` runs save transactions, catalogue/reward tests and UI layout checks. It refuses the normal live Xbox save directory. Never publish a character fixture or full Xbox backup with your source.

## Data sources and credits

- [Dungeons Tools](https://www.dungeons.tools/2/weapons): gear names/icons, enchantments, [quest rewards](https://www.dungeons.tools/2/quests), [Overworld map](https://www.dungeons.tools/2/map), [Sift map](https://www.dungeons.tools/2/map/the-sift) and [XP thresholds](https://www.dungeons.tools/2/hero-level).
- [McFun game-file database](https://www.mcshuo.com/minecraft-dungeons-2/database/): native item/effect templates, talisman tiers and area loot weights.
- [Dungeons II Wiki research](https://dungeons2wiki.com/wiki/research/weapon-power-and-base-damage/): weapon-power and damage behavior.

Code is available under the MIT license. Game artwork, names and extracted game data retain their original owners' rights and are not covered by that code license. Unofficial fan utility; not affiliated with Mojang or Microsoft.
