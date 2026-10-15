# End It (RimWorld 1.7)

A Brutality mod.

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
Select a colonist and right-click a **prisoner** or a **downed enemy** to get *Execute (name) with (weapon)*.
- **Gun:** the colonist walks up, aims for the execution aim time, and shoots them in the head.
- **Knife, sword or other blade:** the colonist walks up, raises the blade over them, holds it there with shaking hands for the execution aim time, drives it in, then pulls it out. Executions cut the throat (vanilla's execution cut); mercy kills stab the heart.
- Needs a gun or blade equipped (the same weapons that count for End it; clubs and other blunt weapons don't). Without one, or if the colonist is incapable of violence or can't reach the target, the option is greyed out with the reason.
- Prisoners: same thoughts, tale and history event as a vanilla prisoner execution.
- Downed enemies: just a kill, with no execution thoughts.
- Works regardless of the "Executioners with a gun shoot the prisoner" setting.

### Mercy kill
Select a colonist with a gun or blade and right-click a **downed friendly pawn** (a colonist, an ally, a visitor or a tame animal) to get *Mercy kill (name) with (weapon)*. The option only appears while they're suffering: in **medium pain or worse**, as the health tab shows it, or **unconscious and bleeding**. It works like the right-click execution, with a gun (one shot to the head) or a blade (a stab to the heart).
- For your own colonists and animals you're asked to confirm first (turn this off with the "Ask for confirmation" setting).
- It isn't an execution: no execution thoughts, and the death is announced as "(name) was given a merciful death." The usual thoughts about someone dying still apply.
- If the pawn gets back on their feet, or stops suffering (pain drops below medium, and they aren't unconscious and bleeding), before the kill, the order is called off.

### Russian roulette (mental break)
An extreme mental break that can only happen to a pawn carrying a **revolver**, either equipped or in their inventory. Any gun with "revolver" in its name counts.
- The pawn equips the revolver if it's in their inventory, and puts whatever they were holding into their inventory.
- **One bullet, six chambers, one spin.** They spin the cylinder once at chest height, then pull the trigger three times in a row. Each round they raise the revolver to their temple, hesitate with trembling hands, and pull.
- **Each pull is riskier than the last,** like the real thing: 1 in 6 on the first pull, 1 in 5 on the second, 1 in 4 on the third. That's a 50% chance the bullet comes up in the three pulls.
  - **Empty chamber:** a *click*, a startled twitch, and the revolver comes down for the next round.
  - **Live round:** the same as a pistol End it, so whether it kills follows the pistol death chance. If they survive the shot, the break ends.
- After three empty chambers the break ends with a "pulled the trigger 3 times and lived" message.

### Gallows (hanging)
- **Research:** *Gallows* (medieval, 300 points).
- **Build:** *Gallows* in the **Security** tab: 60 wood and 15 cloth, construction skill 3. It takes one cell; the frame and noose stand tall above it. Pawns can walk through it.
- **Use:** in a prisoner's **Prisoner** tab, set the interaction mode to **Hang**. A warden who's allowed to do violence carries the prisoner to the nearest free gallows, stands them on the trapdoor, puts the noose on, then drops them.
- **Hanging:** the prisoner kicks and struggles, goes limp and sways, and dies of strangulation about 8 seconds later at normal speed.
- **Consequences:** same mood effects, history event and tale as a vanilla execution, and no death letter.
- If there's no free gallows, the warden won't start, and the job shows "No free gallows". Changing the prisoner's mode before they're on the gallows calls it off.

### Torture (the Bad Chair)
- **Research:** *Interrogation chair* (medieval, 500 points).
- **Build:** the *Bad Chair* in the **Security** tab: 50 wood and 25 steel, construction skill 4. It's rotatable; the torturer works from the cell in front of it.
- **Use:** select the chair and press **Torture...**. Pick the **prisoner**, the **torturer** (anyone who can do violence and use their hands), the **method**, the **reason**, the **duration** (1 to 8 hours) and whether to **stop before it kills them** (on by default).
- The torturer carries the prisoner to the chair, straps them in, steps to the front and works on them in rounds (about every 4 seconds at normal speed) until the time is up. A progress bar shows how far along it is. **Stop torture** on the chair ends it early. Afterwards the torturer takes the prisoner back to their bed.

**Methods**

| Method | Animation | During | After |
|---|---|---|---|
| Beating | Punches with a lunge; the prisoner recoils | Bruises, sometimes a cracked rib | Heal normally, can scar |
| Waterboarding | Cloth over the face, bucket tipped over it, water splashing; the prisoner thrashes | *Near drowning* (breathing and consciousness down, painful); pours pause while they're close to drowning | Clears over a few hours |
| Branding | Red-hot iron pressed on, smoke and glow | Burns | Heal, can scar |
| Electric shock | Prod with sparks; the prisoner convulses | *Electric shock* (pain, spasms, a struggling heart) and small burns | Wears off over about a day |
| Thumbscrews | Iron vice on the hand, twisted | Crushed fingers | Heal, or are lost for good if badly crushed |
| Cutting | Knife dragged along an arm, blood | Shallow cuts that bleed | Heal, can scar |
| Foot whipping | Cane raised and whipped across the soles | Bruised feet | Heal |
| Ice water | Buckets of water poured over; the prisoner shivers | Vanilla *hypothermia* | Fades once warm |

Electric shock needs the Electricity research. Every method also adds *being tortured* (extra pain) while the session lasts.

**Reasons**
- **Get intel:** at the end, a chance they break and talk (higher with the torturer's Social skill, more hours, and harsher methods; waterboarding is best). If they do, you get a real opportunity on the world map: an enemy camp or a hidden item stash. Each prisoner only talks once (*interrogated*).
- **Break their will:** wears down resistance and will twice as fast, so they're much easier to recruit or enslave.
- **For fun:** no questions. Bloodlust pawns love it and get recreation from it.

**Afterwards**
- The prisoner gets *torture trauma* (consciousness down a little, phantom pain at higher severity), which fades over about three weeks, and a "I was tortured" memory (-25 mood).
- Their resistance and will drop, more the longer it went on.
- The torturer feels bad about it (-8), unless they're a psychopath. Bloodlust pawns enjoy it (+6, or +10 for fun).
- Other colonists get "prisoner tortured" (-3), except psychopaths and bloodlust pawns.
- A non-hostile prisoner's faction loses 15 goodwill.
- With "stop before it kills them" on, the torturer holds back near the limits and stops if the prisoner nears death (heavy blood loss, near drowning, heart arrhythmia, deep hypothermia, very poor health). With it off, the prisoner can die.

### Suicidal (mental break)
A pawn who turns suicidal broods for **5 hours**, wandering aimlessly but still eating and sleeping. Then they try to end it **on their own**:
- with **End it**, if there's a gun or blade on them or within reach (the same rules as the player's End it: equipped, inventory, then the floor nearby), with all its animations and outcomes;
- otherwise by walking to a free **gallows** and hanging themself (noose, drop and strangling as with the gallows execution).
- With neither, they look again every hour; after about a day the break wears off.
- If they survive an attempt, the break ends.

It's triggered by:
- **Brainwashing:** when the catatonia after a session lifts, a 10% chance.
- **Rim Job World** (optional, only when RJW is loaded): after a forced encounter, a 10% chance for the victim. It's detected from RJW's victim memories (`GotRaped`, `GotAnalRaped`, `GotAnalRapedByFemale`, `GotRapedUnconscious`, and any later negative `Got...Raped...` memory), rolled at most once an hour per pawn. Masochists who enjoyed it, and the social opinion memories, don't count.

Both chances can be changed in the mod settings. Prisoners and slaves can have the break too.

### Brainwashing station
- **Research:** *Mind conditioning* (industrial, 1200 points).
- **Build:** the *brainwashing station* in the **Security** tab: 60 steel, 4 components and 20 cloth, construction skill 6. It needs **250 W of power**. It's 1x2: a strap chair with a head clamp facing an old television. Rotation is the way the subject faces; facing north, you see the hypnotic spiral spinning on the screen during a session.
- **Use:** select it and press **Brainwash...**. Pick the **subject** (a prisoner or slave), the colonist who **straps them in**, and the **goals**:
  - **Convert to an ideoligion** of your choice (any on the map; needs Ideology, not classic mode). Their certainty is set high.
  - **Break their unwavering loyalty**, for prisoners who have it, so they can be recruited.
- The colonist carries them over, straps them in and switches it on. The session runs for **4 hours of powered time** (it pauses without power). **Stop brainwashing** releases them with nothing changed.
- **During:** *being brainwashed* (dazed: consciousness and talking reduced) and a slow, dazed sway.
- **After:** the changes apply, they're *catatonic* for **5 hours** (unconscious), and they get a "mind rewritten" memory (-12 mood for 10 days). When the catatonia lifts, there's a 10% chance they turn **suicidal**.

## Combat Extended
End It works with or without [Combat Extended](https://github.com/CombatExtended-Continued/CombatExtended). It doesn't depend on it; the support switches on by itself when CE is loaded.
- CE's guns are recognised as guns, so End it, gun executions, right-click Execute and Mercy kill, and Russian roulette all work with them. Launchers, grenades and bows are still excluded.
- With CE's ammo system on, a gun has to have something to fire: a round in the magazine, or ammo on hand for guns without one. An empty gun is treated as no gun, and the option says "(name)'s gun is out of ammo".
- Each shot (End it, executions, a live round in Russian roulette) takes one round out of the magazine.
- Knives, swords, seppuku and the gallows work the same with or without CE.

## Building
```
cd Source/EndIt
dotnet build -c Release
```
This needs the .NET 10 SDK. The reference assemblies come from NuGet (`Krafs.Rimworld.Ref`), so the game doesn't need to be installed. The output goes to `1.7/Assemblies/EndIt.dll`.
