# End It (RimWorld 1.7)

A standalone mod (separate from Rocket's Airstrike in the repo root). To install it, copy or symlink this `EndIt/` folder into `RimWorld/Mods/`. It needs Harmony.

## Features

### "End it"
- **Right-click:** select any colonist (or slave) and right-click on that pawn themself to get *End it*. This works whether they're healthy, hurt or downed. If they can't do it right now (no weapon in reach, unconscious, can't use their hands...), the option is shown greyed out with the reason.
- **Button:** a downed or injured colonist also gets an *End it* gizmo, shown when they're the only pawn selected.
- **Require injury (setting, off by default):** when on, only downed or injured pawns get the option.
- **Weapon search order:** the equipped weapon first, then the inventory, then the floor. A downed pawn can only reach adjacent cells. A pawn who can stand will walk up to 12 cells to pick a weapon up.
- **Usable weapons:**
  - Industrial or later guns that fire bullets. Launchers, grenades and bows don't count.
  - Melee weapons that can cut or stab. Clubs and maces don't count.
- **Animation by weapon type:**
  - **Pistol:** raised to the right temple.
  - **Rifle or long gun:** muzzle under the chin.
  - **Knife or blade:** edge against the throat, then a slash.

  Each one has a hesitation beat and trembling hands. Guns also show a muzzle flash and smoke, play the weapon's own shot sound and recoil. The animation still works while the pawn is lying downed.
- **Outcome:** the wound is always severe. Whether it kills depends on the death chance in the mod settings (defaults: pistol 80%, rifle 90%, blade 60%). Survivors take a jaw, eye, skull, brain, ear or throat wound and may still bleed out.

### Gun executions
When the executioner has a gun equipped, prisoner, slave and guilty-colonist executions become a gunshot to the head instead of a throat cut. The executioner aims at the victim first; the aim time is set in the mod settings. Thoughts, records and tales are the same as vanilla. Executions without a gun stay vanilla.

## Building
```
cd Source/EndIt
dotnet build -c Release
```
This needs the .NET 10 SDK. The reference assemblies come from NuGet (`Krafs.Rimworld.Ref`), so the game doesn't need to be installed. The output goes to `1.7/Assemblies/EndIt.dll`.
