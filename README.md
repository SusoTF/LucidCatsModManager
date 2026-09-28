# Lucid Cats – Mod Manager

An in-game mod manager for **Lucid Cats**. See every mod you have installed, turn them on and off, change their settings with the game's own controls, save sets of mods as profiles and let the game recover on its own if a mod stops it from starting.

<img width="1917" height="1078" alt="image" src="https://github.com/user-attachments/assets/d83185c9-723a-4f9e-a21e-bb01a9e45b08" />

## Features

- **Mods** button in the top-right corner of the main menu, matching the game's own style, animations and sounds.
- **List of installed mods** with their status: Active, Disabled, Pending (waiting for a restart) or Failed (couldn't load). Scrollable if you have many.
- **Details of each mod**: version, author, description, file, what it requires and what needs it.
- **Turn mods on and off**, including a one-click **vanilla mode** (everything off except the Mod Manager) and a **Restart game now** button that closes the game and opens it again through Steam.
- **Settings editor** using the game's own Settings controls: toggles, sliders, dropdowns and text boxes. Hover over a setting to see what it does, its default value and its limits. Changes are saved straight away, with **Reset** per setting or for all of them.
- **Profiles**: save your current set of mods (for example "Solo" or "With friends") and switch between them.
- **Crash protection**: if a mod stops the game from reaching the main menu, the Mod Manager turns the likely culprit off on its own and tells you what happened.
- **Error detection**: mods that had errors this session are marked with **(!)**. You can read the errors and copy a ready-to-paste error report for the mod's author.
- **Quick access** to the mods folder, the log and each mod's settings file.
- Works with any BepInEx 5 mod, and alongside other mods that add things to the main menu: menu panels never overlap.

## Requirements

- Lucid Cats (Steam, Windows)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (tested with 5.4.23.5, x64)

## Installation

The Mod Manager comes in **two parts**, and both are needed:

| Part | Where it goes | What it does |
|---|---|---|
| **Mod Manager** | `BepInEx\plugins\LucidCatsModManager\LucidCatsModManager.dll` | The menu and everything you see in the game. |
| **Patcher** | `BepInEx\patchers\LucidCatsModManager\LucidCatsModManager.Patcher.dll` | Runs when the game starts, before any mod loads, to apply your changes and protect you from crashes. |

> **Why two parts?** While the game is running, Windows keeps the files of loaded mods locked, so they can't be switched on or off right away. The patcher applies those changes the next time the game starts, before the files are locked.

1. **Install BepInEx 5** (skip this if you already have it):
   - Download `BepInEx_win_x64_5.4.23.x.zip` from the [BepInEx releases page](https://github.com/BepInEx/BepInEx/releases). Use version 5, not 6.
   - Extract it into the game folder, next to `LucidCats.exe`.
   - Launch the game once and close it.
2. **Download the mod** from the [Releases](../../releases) page.
3. **Extract the BepInEx folder** from the zip into your game folder. If Windows asks to merge folders, click Yes. Both parts end up in the right place on their own.
4. Launch the game. The **Mods** button will be in the top-right corner of the main menu.

> **Where's the game folder?** In Steam, right-click Lucid Cats → Manage → Browse local files.

If **General** says the patcher isn't installed, check that `LucidCatsModManager.Patcher.dll` is in `BepInEx\patchers\LucidCatsModManager\`.

## How to use it

### Turning mods on and off
Select a mod and press **Disable** or **Enable**. The change is marked as **Pending** and applies the next time the game starts. Press **Restart game now** in **General** to do it straight away.

**Disable all mods (vanilla)** in **General** turns everything off except the Mod Manager, so you can turn your mods back on from the same place later.

### Changing settings
Select a mod and press **Settings** (only mods that are running and have settings). Changes are saved to the mod's settings file immediately. Most mods apply them right away; some only after restarting the game.

### Profiles
In **General**, press **Profiles**:
- **Save current**: saves the mods you have on and off right now as a new profile, and lets you name it.
- **Apply**: prepares the changes for the selected profile. Restart the game to use it.
- **Rename** and **Delete** (asks for confirmation).

The profile that matches your current setup is marked as **Current**. Mods installed after saving a profile keep their current state when you apply it.

### Crash protection
- If the game fails to reach the main menu **twice in a row**, the next time it starts the Mod Manager turns off the mods that were switched on or installed since the last time the game started correctly.
- If there are no clear suspects and it fails a **third** time, it starts in **safe mode**: every mod is turned off except the Mod Manager.
- When you reach the menu, the button shows **Mods (!)** and **General** explains what happened, with a button to turn those mods back on.
- Closing the game once while it's loading doesn't count.

### Reporting a mod's errors
Mods marked with **(!)** had errors this session. Select the mod, press **View errors**, then **Copy error report** to copy everything the mod's author needs (mod version, game version, BepInEx version and the errors) to your clipboard.

## For mod authors

The Mod Manager works with any BepInEx 5 mod, but a few small things make yours look better in it:

- **Description and author**: set them in your `.csproj`. They're read from your mod's file:
  ```xml
  <Description>What your mod does, in one sentence.</Description>
  <Company>YourName</Company>
  ```
- **Sliders**: give number settings a range, and they're shown with a slider instead of a text box:
  ```csharp
  Config.Bind("Section", "Speed", 25f, new ConfigDescription("What it does.", new AcceptableValueRange<float>(0f, 100f)));
  ```
- **Dropdowns**: settings that are an `enum`, or that use `AcceptableValueList`, are shown as a dropdown.
- **Toggles**: `bool` settings are shown as a toggle.
- The optional hints of the popular Configuration Manager mod are supported: `DispName`, `Order`, `Browsable` and `ReadOnly`.

## Files it creates

All in `BepInEx\config`:

| File | What it's for |
|---|---|
| `lucidcats.modmanager.pending.txt` | Mods to turn on or off the next time the game starts. |
| `lucidcats.modmanager.profiles.txt` | Your profiles. |
| `lucidcats.modmanager.known.txt` | The mods that were on the last time the game started correctly. |
| `lucidcats.modmanager.launch.txt` | Temporary, used by the crash protection while the game starts. |
| `lucidcats.modmanager.recovery.txt` | What the crash protection turned off, until you read the notice. |

Disabled mods are simply renamed from `.dll` to `.dll.disabled`, so nothing is ever deleted.

## Uninstall

1. **Turn your mods back on first** (or use a profile with everything on) and restart the game. Mods that are disabled stay renamed to `.dll.disabled` after uninstalling, so you'd have to rename them back by hand.
2. Delete `BepInEx\plugins\LucidCatsModManager` and `BepInEx\patchers\LucidCatsModManager`.
3. Optionally, delete the `lucidcats.modmanager.*` files from `BepInEx\config`.

## Questions that came to my mind

**Does it change the gameplay?**
No. It only manages your mods; the game plays exactly the same.

**Does it affect multiplayer?**
No. Everything happens on your own PC, and other players don't need it.

**Can I turn the Mod Manager off from its own menu?**
No, so you can always get back into it. To remove it, follow the uninstall steps.

**Is it safe?**
Yes. It only renames mod files inside `BepInEx\plugins` to switch them on and off, and the code is open source for anyone to check.

**What happens when the game updates?**
Turning mods on and off, profiles and the crash protection don't depend on the game, so updates don't affect them. If a future update completely redesigns the main menu, the Mods button might not appear until the Mod Manager is updated; the game keeps working normally either way.

## Building from source

The solution has two projects: `Plugin` (the Mod Manager) and `Patcher`.

1. Install BepInEx in your game folder (the projects use its files).
2. Install the [.NET SDK](https://dotnet.microsoft.com/download).
3. Set `<GameDir>` to your game folder in both `Plugin\LucidCatsModManager.csproj` and `Patcher\LucidCatsModManager.Patcher.csproj`.
4. Open `LucidCatsModManager.sln` and build in **Release**. Each part is copied to its BepInEx folder automatically.

## Changelog

### 1.0.0
- First release.

## More mods

- [Bestiary](https://github.com/SusoTF/LucidCatsBestiary): an in-game encyclopedia of every monster you've encountered.
- [Save Files](https://github.com/SusoTF/LucidCatsSaves): save your runs and continue them later, solo or in co-op.

## Credits

- The **BepInEx** and **HarmonyX** teams, for the modding tools that make this possible, and the **Mono.Cecil** project, used to read each mod's information.
- The developers of **Lucid Cats**, for the game.

## License

[MIT](LICENSE).

This is an unofficial fan-made mod. It is not affiliated with or endorsed by the developers of Lucid Cats.
