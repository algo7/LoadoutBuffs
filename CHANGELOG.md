# Changelog

## 1.3.0

- New stat: Parry bonus (On parry group, Melee / Ranged / Shield), added to the parry multiplier of what you parry
  with (+2 turns a buckler's 2.5x into 4.5x; up to +20).
- Clearer slots: Armor, Fall damage, Regen and Resistances are offered only on armor (helmet, chest, legs, cape);
  Block armor and Block force only on Melee, Ranged and Shield.
- Block armor goes up to 1000 in the window (was 200).
- Buffs window: the selected slot has a gold frame.
- Buffs window: Shift + click on − / + changes a value ten steps at a time.
- New icon, matching my other mods; the buff on your HUD shows just the shield.

## 1.2.3

- The Abyssal Harpoon is excluded from the Melee slot, like pickaxes and other tools, so dragging tamed animals
  no longer kills them.

## 1.2.2

- Buffs window: cut-off text and values now show in full (e.g. "Slightly resistant"); damage rows name just the type
  ("Lightning").

## 1.2.1

- Staff of Protection and Northern Vengeance are excluded from the Ranged slot to prevent friendly fire.

## 1.2.0

- Clearer slots: On parry (heal, stamina, bubble, reach) is offered only on Melee, Ranged and Shield, the gear you
  parry with, not on armor or the cape.

## 1.1.0

- New stat: Eitr cost, a percent of every spell and staff charge (−50 = half the eitr). It's in the window's Costs
  group (formerly Stamina costs).
- Classes on the ranged slot now work: with Woodcutter or Miner there, your arrows, bolts and staff spells (their blasts
  too) fell any tree or break any rock or ore.
- Clearer slots: Damage %, Added damage and Woodcutter / Miner are offered only on Melee and Ranged, the weapons they
  act on, not on armor or the shield.

## 1.0.0

First public release, as **LoadoutBuffs** (developed privately as ItemStatOverrides).

- Buffs: one extra effect and/or custom stats per equipment slot (helmet, chest, legs, cape, melee, ranged, shield),
  picked in the BUFFS tab of the inventory or edited in `LoadoutBuffs.buffs.yaml`.
- Stats: movement, carry weight, armor, block armor, block force, fall damage, regen, stamina costs, resistances,
  damage %, added damage per type, skills, raw `fields:`.
- On parry: heal / stamina, set separately for you and for other players; heal for tamed creatures; and the Staff of
  Protection's bubble (its health and time) for you and everyone in reach. Works for players without the mod.
- Classes: Woodcutter (melee hits fell any tree), Miner (melee hits break any rock or ore).
- Console: `lb_reload`, `lb_buffs`, `lb_stats`.
