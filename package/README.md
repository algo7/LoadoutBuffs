# LoadoutBuffs

Give each equipment slot extra **effects and stats**, picked in an in-game window: Megingjord's carry weight on your
chest, +30 spirit damage on your melee weapon, a protection bubble for you and your friends when you parry. Client-side:
servers and friends don't need the mod. No items are created or changed, and your saves are never touched.

![The Buffs window, opened from the BUFFS tab next to CRAFT](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/buffs-window.png)

## Quick Start

1. Install with a mod manager (r2modman, Thunderstore Mod Manager, Gale): BepInEx and Jötunn come along automatically.
2. In game, open your inventory and click the **BUFFS** tab next to CRAFT.
3. Pick a starter buff (Warrior, Hunter, Explorer, Homesteader) or press **New**, then press **Use**.

Changes are saved and applied at once. One buff is in use at a time, for every character in your profile. Shift + click
on − / + changes a value ten steps at a time.

## Features

A buff gives each slot (helmet, chest, legs, cape, melee, ranged, shield) an extra **effect** and/or **custom stats**.

- **Effects:** any set bonus or equipment effect gear has in the game (Vanguard, Megingjord, Feather fall, Wisplight…,
  including other mods' gear). Point at one in the window to read what it does.
- **General:** movement speed, carry weight; on armor slots (helmet, chest, legs, cape) also armor and fall damage; on
  the melee, ranged and shield slots **block armor** and **block force** (added to what you block with: your shield,
  else your weapon).
- **Regen** (armor slots): health / stamina / eitr regen.
- **Costs:** run / jump / attack / block / dodge stamina cost; **eitr cost** (every spell and staff charge, e.g. −50 %
  for half the eitr). Even at −100 %, when spells cost nothing, you need eitr to cast: eat at least one eitr food.
- **Resistances** per damage type (armor slots).
- **Damage** (melee and ranged slots):
  - **Damage %** multiplies what your weapon deals; it scales with your weapon and covers the whole shot of a bow.
  - **Added damage** adds a fixed amount of any type, e.g. +30 spirit on a club that had none, scaled like the weapon's
    own damage.
- **Skills:** + or − levels for any skill.
- **Parry bonus** (melee, ranged and shield slots, in the window's On parry group): added to the parry multiplier of
  what you parry with, e.g. +2 turns a buckler's 2.5x into 4.5x (up to +20). The item's tooltip shows the new
  multiplier while it's in your hands.
- **On parry** (melee, ranged and shield slots): heal and stamina, set separately for you and for other players; heal
  for tamed creatures; and a **bubble** (the Staff of Protection's Magic barrier) for you and everyone in reach, with its
  health and time (1–10 minutes). Friends without the mod get it too.
- **Classes** (melee and ranged slots): *Woodcutter* lets that weapon fell any tree (logs and stumps too), *Miner*
  break any rock or ore deposit (black marble included). On the melee slot it's your swings; on the ranged slot your
  arrows, bolts and staff spells. The hit gets the highest tool tier, and chop / pickaxe damage equal to its physical
  damage (blunt, slash, pierce), so even a club or a bow works. A spell without physical damage (the Staff of Frost's,
  lightning) does nothing to wood or stone. To make it work, add blunt, slash or other physical damage under *Added
  damage* on the Ranged slot (e.g. +10 pierce). Digging the ground stays a pickaxe thing.

![Custom stats of an armor slot: General, Regen, Costs](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/stats-general.png)
![On parry: parry bonus, heal, stamina, bubble health and time, reach](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/stats-on-parry.png)

![The buff in use on the HUD (Warrior), next to its effects](https://raw.githubusercontent.com/algo7/LoadoutBuffs/main/images/hud-buff.png)

The buff in use shows on your HUD with its name ("N stats" under it when it has custom stats). The stats of all slots
you fill add up; the Compendium's Active effects page lists the buff's effects and stats, and the bottom of the window
shows them for what you wear now.

## How Things Behave

- **Slots follow your gear.** A slot counts while something is equipped there. Melee means swords, knives, clubs,
  atgeirs, spears, axes and fists; ranged means bows, crossbows and magic staffs; items that count as tools (below)
  are neither. Weapon and shield slots count only while that item is in your hands: sheathing turns them off.
- **Your gear keeps its own effects.** A buff only adds; the same effect from your gear and the buff counts once.
- **Parry, not block.** On-parry help needs a *perfect* block (the parry flash). A normal block never triggers it.
  Tower shields and the Serpentscale shield can't parry, so On parry does nothing with them.
- **On parry goes on what you parry with.** Its stats can only be set on Melee, Ranged or Shield, and count while that
  item is in your hands: holding a sword and a shield, a parry also uses the sword's on-parry stats. A parry with empty
  hands, a torch or a pickaxe gives none.
- **The bubble (if your buff has one).** The bubble is off unless you set *Bubble health* in a buff. Then a parry gives
  a bubble to everyone in reach who has none. A bubble that is still up (yours or from a real Staff of Protection) isn't
  refreshed: it stays until it breaks or runs out, and the next parry after that gives a new one. When a bubble breaks,
  the player in it gains a little Blood Magic skill, as with the staff.
- **Armor stats on armor, block stats in your hands.** Armor, Fall damage, Regen and Resistances can only be set on
  the helmet, chest, legs or cape. Block armor and Block force can only be set on Melee, Ranged or Shield, and count while
  that item is in your hands: holding a sword and a shield, a block also uses the sword's.
- **Damage and classes are for weapons.** Damage %, Added damage, Woodcutter and Miner can only be set on Melee or
  Ranged, and count only while that weapon is in your hands. A staff blast with Miner breaks every chunk of a deposit
  it reaches.
- **Damage % follows your weapon:** it lists the types the weapon in your hand deals. Add a new type under *Added
  damage* and its % row appears.

## Items That Count as Tools

These fill no slot: while one is in your hands, your Melee and Ranged slots' effects and stats don't apply.

- Pickaxes, the fishing rod, torches and tools (hammer, hoe, cultivator…).
- **Staff of Protection** and **Northern Vengeance**: their spells hit only you, other players and tamed creatures.
  With Added damage, instead of protecting your friends and your tamed animals, you'd be hurting or even killing them.
  For cheaper casts with them, put eitr cost on armor.
- **Abyssal Harpoon**: to avoid killing tamed animals with PvP on.

## Multiplayer and Fair Play

LoadoutBuffs runs only on your client. Your stats and the damage you deal are computed by your own game, so buffs work
on any server, and other players see your gear as usual. Your parry help reaches friends whether they have the mod or
not.

LoadoutBuffs is a power mod that can make you a lot stronger, so some servers and players probably won't welcome it.

## The Buffs File

Everything can be set in the Buffs window of the in-game UI. The file (`BepInEx/config/LoadoutBuffs.buffs.yaml`) is only useful to share
or back up your buffs, or for values beyond the window's limits and raw `fields:`. Effects can be written as code names or
in-game names. After editing, run `lb_reload` (or edit while the game is closed): the window saves what it has loaded,
so edits it hasn't loaded are overwritten the next time you change something in it. Comments and lines it can't read
are never kept.

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
      stats: { parryBonus: 2, shieldOnParry: 500, shieldMinutes: 2, addDamage: { spirit: 30 } }
```

| Key | Value |
|---|---|
| `movementSpeed` | percent (`10` = +10 %) |
| `fallDamage`, `healthRegen`, `staminaRegen`, `eitrRegen` | percent (helmet, chest, legs or cape slot only) |
| `runStamina`, `jumpStamina`, `attackStamina`, `blockStamina`, `dodgeStamina`, `eitrCost` | percent of the cost (negative = cheaper) |
| `carryWeight` | flat amount |
| `armor` | flat amount (helmet, chest, legs or cape slot only) |
| `blockArmor`, `blockForce` | flat amount, added to what you block with (melee, ranged or shield slot only) |
| `resist` | `{ Fire: Resistant }`: Normal, SlightlyResistant, Resistant, VeryResistant, Immune, SlightlyWeak, Weak, VeryWeak (helmet, chest, legs or cape slot only) |
| `damage` | percent per type: `{ slash: 10 }` (melee or ranged slot only) |
| `addDamage` | flat per type: `{ spirit: 30 }` (blunt, slash, pierce, fire, frost, lightning, poison, spirit; melee or ranged slot only) |
| `skills` | levels per skill: `{ Bows: 15 }` |
| `parryBonus` | added to the parry multiplier of what you parry with, up to 20 (`2` turns 2.5x into 4.5x; melee, ranged or shield slot only, like all on-parry keys) |
| `healOnParry`, `staminaOnParry` | for you, on every parry |
| `healAlliesOnParry`, `staminaAlliesOnParry`, `healTamedOnParry` | for other players / tamed creatures in reach |
| `shieldOnParry`, `shieldMinutes` | bubble health, and its time in minutes (default 1) |
| `parryRadius` | reach in metres (default 10) |
| `woodcutter`, `miner` | `true` (melee or ranged slot only) |
| `fields` | any other `SE_Stats` field by its code name, raw game value |

Within a buff, the stats of all filled slots add up (the parry bonus up to +20); reach and bubble time take the largest,
resistances the most protective. A bad entry is skipped with a warning in the window and in `BepInEx/LogOutput.log`; the rest still applies.

## Console Commands

In the F5 console. Not cheats, so no `devcommands` needed.

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
- **My Melee / Ranged stats don't count with the Staff of Protection, Northern Vengeance or the Abyssal Harpoon.**
  On purpose: they count as tools (see Items That Count as Tools), because Added damage on them would hurt or kill
  your friends and tamed animals.
- **My staff doesn't chop or mine with Woodcutter / Miner.** Its spell has no physical damage (Staff of Frost,
  lightning). Add some blunt, slash or pierce under Added damage on the Ranged slot.
- **A stat is gone from a slot after updating.** Since 1.1.0 Woodcutter, Miner, Damage % and Added damage go on Melee
  or Ranged only; since 1.2.0 on-parry stats go on Melee, Ranged or Shield only; since 1.3.0 armor, fall damage, regen
  and resistances go on the helmet, chest, legs or cape only, block armor and block force on Melee, Ranged or Shield
  only. On other slots they're skipped with a warning, and when that's the only problem in your buffs file, the mod
  removes them from it (the log says which). Set them on one of those slots again.

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
