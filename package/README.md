# LoadoutBuffs

Client-side mod: **buffs** of extra "while worn" effects and stats for your equipment slots, picked in an in-game
window. No items are created, cloned or changed, so other players (modded or not) see your gear normally, removing the
mod leaves nothing behind, and nothing is written to your save files.

## Buffs

A buff gives each slot (helmet, chest, legs, cape, melee, ranged, shield) one extra effect and/or custom stats, on
while something is equipped there. Melee means swords, knives, clubs, atgeirs, spears, axes and fists; ranged means
bows, crossbows and magic staffs (pickaxes, the fishing rod, torches and tools count as neither). Swap your chest piece
and the chest slot stays; your gear keeps its own effects, and the same effect from two sources counts once. Weapon and
shield slots are off while sheathed.

Open the **BUFFS** tab next to CRAFT in your inventory (or `lb_buffs` in the console). Create a buff, click a
slot, point at an effect to read what it does and click it to use it; press **Use** to turn the buff on. Changes are
saved and applied immediately. Starter buffs (Warrior, Hunter, Explorer, Homesteader) are included, none active.

Effects available are the ones gear has in the game (set bonuses and equipment effects, including other mods' gear).
One buff is active at a time, for every character in the profile.

**Stats** (the window's Stats tab, − / + buttons):

- General: movement speed, carry weight, armor, **block armor** and **block force** (added to whatever you block with:
  your shield, else your weapon), fall damage.
- Regen (health / stamina / eitr), stamina costs (run, jump, attack, block, dodge), resistances, damage % per type
  (the list shows the types your weapon deals: a % of a type it doesn't deal would multiply zero).
- **Added damage** per type (e.g. +30 spirit on the weapon you attack with, scaled like the weapon's own damage).
  Fire, poison and spirit arrive as damage over time after the hit, like the game's own.
- Skill levels.
- **On parry**: heal / stamina for you, heal / stamina for other players, heal for tamed creatures, a **bubble** (the
  Staff of Protection's Magic barrier: its health, i.e. the damage it absorbs, and its time in minutes) for all of you, and their reach (default
  10 m). Other players don't need the mod. Anyone who already has a bubble keeps theirs; a broken or expired one is
  replaced on your next parry.
- **Class**: *Woodcutter* lets your melee weapon fell any tree (and logs and stumps), *Miner* lets it break any rock or
  ore deposit (black marble included; digging the ground stays a pickaxe thing).

The buff in use shows on your HUD with its name ("N stats" under it when it has custom stats); the stats of the slots
you're wearing add up, and the Compendium's Active effects page lists its effects and stats. The bottom of the window shows the buff's effects and custom stats
for what you wear now.

Buffs live in `BepInEx/config/LoadoutBuffs.buffs.yaml`, which you can also edit by hand (code or in-game
effect names) and reload with `lb_reload`:

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

Percent stats are written in percent (`10` = +10 %). `fields:` inside `stats` sets any other SE_Stats field by its
code name (raw game value). Mistakes never block the rest: a bad entry is skipped with a warning in the window and in
`BepInEx/LogOutput.log`.

## Console commands

F5 console; the game needs the `-console` launch option. Not cheats, so no `devcommands` needed.

- `lb_reload`: re-read the buffs file and apply it
- `lb_buffs`: open the Buffs window
- `lb_stats`: your current totals: armor, movement and run speed, stamina modifiers, resistances, weapon and what
  you block with, buff, active effects

The buffs file is also re-read on every world load.

## Notes

- Your stats and the damage you deal are computed on your own client, so buffs apply in multiplayer too. Other
  players aren't affected (except by your parry help), and servers don't need the mod.
- Requires Jötunn (installed automatically by r2modman) for the Buffs window.
