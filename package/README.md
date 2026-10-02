# LoadoutBuffs

Give each equipment slot extra **effects and stats**, picked in an in-game window: Megingjord's carry weight on your
chest, +30 spirit damage on your melee weapon, a protection bubble for you and your friends when you parry. Client-side:
servers and friends don't need the mod. No items are created or changed, and your saves are never touched.

![The Buffs window, opened from the BUFFS tab next to CRAFT](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/buffs-window.png)

## Quick start

1. Install with a mod manager (r2modman, Thunderstore Mod Manager, Gale): BepInEx and Jötunn come along automatically.
2. In game, open your inventory and click the **BUFFS** tab next to CRAFT.
3. Pick a starter buff (Warrior, Hunter, Explorer, Homesteader) or press **New**, then press **Use**.

Changes are saved and applied at once. One buff is in use at a time, for every character in your profile.

## Features

A buff gives each slot (helmet, chest, legs, cape, melee, ranged, shield) an extra **effect** and/or **custom stats**.

- **Effects:** any set bonus or equipment effect gear has in the game (Vanguard, Megingjord, Feather fall, Wisplight…,
  including other mods' gear). Point at one in the window to read what it does.
- **General:** movement speed, carry weight, armor, **block armor** and **block force** (added to what you block with:
  your shield, else your weapon), fall damage.
- **Regen and stamina:** health / stamina / eitr regen, run / jump / attack / block / dodge stamina cost.
- **Resistances** per damage type.
- **Damage:**
  - **Damage %** multiplies what your weapon deals; it scales with your weapon and covers the whole shot of a bow.
  - **Added damage** adds a fixed amount of any type, e.g. +30 spirit on a club that had none, scaled like the weapon's
    own damage.
- **Skills:** + or − levels for any skill.
- **On parry:** heal and stamina for you and other players, heal for tamed creatures, and a **bubble** (the Staff of
  Protection's Magic barrier) for all of you, with its health and time (1–10 minutes). Friends without the mod get it
  too.
- **Classes:** *Woodcutter* lets your melee weapon fell any tree (logs and stumps too), *Miner* break any rock or ore
  deposit (black marble included). The hit gets the highest tool tier, and chop / pickaxe damage equal to the weapon's
  physical damage, so even a club works. Digging the ground stays a pickaxe thing.

![Custom stats of a slot: General](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/stats-general.png)
![On parry: heal, stamina, bubble health and time, reach](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/stats-on-parry.png)

![The buff in use on the HUD, followed by its effects](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/hud-buff.png)

The buff in use shows on your HUD with its name ("N stats" under it when it has custom stats). The stats of all slots
you fill add up; the Compendium's Active effects page lists the buff's effects and stats, and the bottom of the window
shows them for what you wear now.

## How things behave

- **Slots follow your gear.** A slot counts while something is equipped there. Melee means swords, knives, clubs,
  atgeirs, spears, axes and fists; ranged means bows, crossbows and magic staffs; pickaxes, the fishing rod, torches and
  tools count as neither. Weapon and shield slots count only while that item is in your hands: sheathing turns them off.
- **Your gear keeps its own effects.** A buff only adds; the same effect from your gear and the buff counts once.
- **Parry, not block.** On-parry help needs a *perfect* block (the parry flash). A normal block never triggers it.
- **The bubble (if your buff has one).** The bubble is off unless you set *Bubble health* in a buff. Then a parry gives
  a bubble to everyone in reach who has none. A bubble that is still up (yours or from a real Staff of Protection) isn't
  refreshed: it stays until it breaks or runs out, and the next parry after that gives a new one. When a bubble breaks,
  the player in it gains a little Blood Magic skill, as with the staff.
- **Damage % follows your weapon:** it lists the types the weapon in your hand deals. Add a new type under *Added
  damage* and its % row appears.

## Multiplayer and fair play

LoadoutBuffs runs only on your client. Your stats and the damage you deal are computed by your own game, so buffs work
on any server, and other players see your gear as usual. Your parry help reaches friends whether they have the mod or
not.

LoadoutBuffs is a power mod that can make you a lot stronger, so some servers and players probably won't welcome it.

## Editing the file

Buffs live in `BepInEx/config/LoadoutBuffs.buffs.yaml`. You can edit it by hand (effect code names or in-game names)
and reload it with `lb_reload`; it's also re-read on every world load. The window rewrites the file, so comments you add
aren't kept.

```yaml
active: Warrior
buffs:
  Warrior:
    chest: SetEffect_DeepNorthMediumArmor   # Vanguard
    cape: Feather fall
    legs:
      effect: Sneaky
      stats: { movementSpeed: 10, armor: 15, resist: { Fire: Resistant }, skills: { Bows: 15 } }
    melee:
      stats: { addDamage: { spirit: 30 }, shieldOnParry: 500, shieldMinutes: 2 }
```

| Key | Value |
|---|---|
| `movementSpeed`, `fallDamage` | percent (`10` = +10 %) |
| `healthRegen`, `staminaRegen`, `eitrRegen` | percent |
| `runStamina`, `jumpStamina`, `attackStamina`, `blockStamina`, `dodgeStamina` | percent of the cost (negative = cheaper) |
| `carryWeight`, `armor`, `blockArmor`, `blockForce` | flat amount |
| `resist` | `{ Fire: Resistant }`: Normal, SlightlyResistant, Resistant, VeryResistant, Immune, SlightlyWeak, Weak, VeryWeak |
| `damage` | percent per type: `{ slash: 10 }` |
| `addDamage` | flat per type: `{ spirit: 30 }` (blunt, slash, pierce, fire, frost, lightning, poison, spirit) |
| `skills` | levels per skill: `{ Bows: 15 }` |
| `healOnParry`, `staminaOnParry` | for you, on every parry |
| `healAlliesOnParry`, `staminaAlliesOnParry`, `healTamedOnParry` | for other players / tamed creatures in reach |
| `shieldOnParry`, `shieldMinutes` | bubble health, and its time in minutes (default 1) |
| `parryRadius` | reach in metres (default 10) |
| `woodcutter`, `miner` | `true` |
| `fields` | any other `SE_Stats` field by its code name, raw game value |

Within a buff, the stats of all filled slots add up; reach and bubble time take the largest, resistances the most
protective. A bad entry is skipped with a warning in the window and in `BepInEx/LogOutput.log`; the rest still applies.

## Console commands

Open the console with F5 (the game needs the `-console` launch option). Not cheats, so no `devcommands` needed.

- `lb_buffs`: open the Buffs window
- `lb_reload`: re-read the buffs file and apply it
- `lb_stats`: your current totals: armor, movement and run speed, stamina modifiers, resistances, weapon and what you
  block with, buff, active effects

## Troubleshooting

- **There's no BUFFS tab.** Jötunn is missing or failed to load. Look for `[LoadoutBuffs]` lines in
  `BepInEx/LogOutput.log`.
- **My stats don't apply.** Is the buff *in use* (the window says "in use" next to it)? Is something equipped in that
  slot, and for weapons, in your hands?
- **No bubble when I parry.** Was it a parry, not a block? Is a bubble still up from an earlier parry?
- **A Damage % row is missing.** Your weapon doesn't deal that type; add it under Added damage first.
- **Console commands do nothing.** Add `-console` to the game's launch options.

## Compatibility

- Built and tested for Valheim 1.0 (Deep North). Tested on Linux; it should work on Windows and macOS (no OS-specific
  code), but hasn't been tested there yet. Reports welcome.
- If another mod changes the same game functions and one of LoadoutBuffs' hooks can't install, that feature turns off
  with a message in the log and the window; the rest keeps working.
- English only.

## Uninstalling

Nothing is stored in your characters or items: remove the mod and your gear is vanilla again. Delete
`BepInEx/config/LoadoutBuffs.buffs.yaml` if you don't want to keep your buffs.

## Links

- Source, bug reports and ideas: https://github.com/algo7/LoadoutBuffs (issues welcome)
- Changes: the Changelog tab
- Made with AI assistance.
- Built with [BepInEx](https://github.com/BepInEx/BepInEx) and [Jötunn](https://github.com/Valheim-Modding/Jotunn).
  MIT license.
