# Changelog

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
