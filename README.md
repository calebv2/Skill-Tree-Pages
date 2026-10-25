# Skill Tree Pages

Configurable skill limits and paged shrine orb holders for A Township Tale.

- Set the maximum number of learned skill slots from 1 to 64.
- Browse eight orb holders per page.
- Navigation panels on profession shrines and the combat level shrine.
- Pressing arrow animations and controller vibration.

## Install

Download `RevampedSkills.dll` and place it in the MelonLoader `Mods` folder on the server and every client. Replace any previous RevampedSkills DLL, then restart. All players and the server must use the same build; unmodded clients are not compatible with the modified shrine messages.

Place `RevampedSkills.json` in the server's `UserData` folder:

```json
{
  "MaxOwnedSkills": 16
}
```

Change the number and restart the server to apply it. Keep your existing configuration when updating. The limit counts purchased skill-tree slots, including upgrades. Lowering it does not remove existing skills.

## Controls

Open the shrine and touch an arrow to change pages. Move your hand away before pressing again. Page changes are blocked while an orb is held. Skills on other pages remain active.

## Build

Requires a .NET SDK, .NET Framework 4.7.2 targeting pack, and local game and MelonLoader assemblies.

```powershell
dotnet build RevampedSkills.csproj -c Release -p:GamePath="PATH_TO_GAME" -p:MelonLoaderPath="PATH_TO_MELONLOADER/net35"
```

The shrine assets are embedded in the DLL. Game assemblies are not included.

Version 0.3.1. PC test build; VR and multiplayer validation is still required.
