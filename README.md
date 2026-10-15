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
  - **Sword (seppuku):** the pawn turns the sword around, holds it in both hands with the tip at the belly, drives it in, then drags it slowly across from their left to right before letting go. Swords are recognised by name (sword, katana, wakizashi, tanto, gladius, zweihander, saber, rapier, scimitar, cutlass).

  Each one has a hesitation beat and trembling hands. Guns also show a muzzle flash and smoke, play the weapon's own shot sound and recoil. The animation still works while the pawn is lying downed.
- **Seppuku outcome:** never an instant death. The stab cuts the stomach; the drag cuts the liver, kidneys and torso. Every part is left with a couple of hit points so nothing is destroyed outright. The pawn goes down from the pain and bleeds to death within a few hours unless someone tends them. If the pawn passes out before the drag finishes, the cut is still applied in full. The blade death-chance setting doesn't apply to seppuku.
- **Outcome (other weapons):** the wound is always severe. Whether it kills depends on the death chance in the mod settings (defaults: pistol 80%, rifle 90%, blade 60%). Survivors take a jaw, eye, skull, brain, ear or throat wound and may still bleed out.

### Gun executions
When the executioner has a gun equipped, prisoner, slave and guilty-colonist executions become a gunshot to the head instead of a throat cut. The executioner aims at the victim first; the aim time is set in the mod settings. Thoughts, records and tales are the same as vanilla. Executions without a gun stay vanilla.

### Right-click executions
Select a colonist and right-click a **prisoner** or a **downed enemy** to get *Execute (name) with (gun)*. The colonist walks up, aims for the execution aim time, and shoots them in the head.
- Needs a gun equipped (the same guns that count for End it). Without one, or if the colonist is incapable of violence or can't reach the target, the option is greyed out with the reason.
- Prisoners: same thoughts, tale and history event as a vanilla prisoner execution.
- Downed enemies: just a kill, with no execution thoughts.
- Works regardless of the "Executioners with a gun shoot the prisoner" setting.

### Russian roulette (mental break)
An extreme mental break that can only happen to a pawn carrying a **revolver**, either equipped or in their inventory. Any gun with "revolver" in its name counts.
- The pawn equips the revolver if it's in their inventory, and puts whatever they were holding into their inventory.
- **One bullet, six chambers, one spin.** They spin the cylinder once at chest height, then pull the trigger three times in a row. Each round they raise the revolver to their temple, hesitate with trembling hands, and pull.
- **Each pull is riskier than the last,** like the real thing: 1 in 6 on the first pull, 1 in 5 on the second, 1 in 4 on the third. That's a 50% chance the bullet comes up in the three pulls.
  - **Empty chamber:** a *click*, a startled twitch, and the revolver comes down for the next round.
  - **Live round:** the same as a pistol End it, so whether it kills follows the pistol death chance. If they survive the shot, the break ends.
- After three empty chambers the break ends with a "pulled the trigger 3 times and lived" message.

## Building
```
cd Source/EndIt
dotnet build -c Release
```
This needs the .NET 10 SDK. The reference assemblies come from NuGet (`Krafs.Rimworld.Ref`), so the game doesn't need to be installed. The output goes to `1.7/Assemblies/EndIt.dll`.
