# The Sunken Bellfoundry (dungeon 2, D65/D66/D67)

**Theme.** Below the Hollow Vault lies the foundry where the Choir's bells were cast. The sea broke in long ago, and
the founders never stopped pouring. Bronze hisses into brine, the furnaces breathe steam through flooded halls, and the
bells they cast still ring under the water and call the drowned back to their stations. Every pack is a crew at work.
Rams haul slag through the tank and out the other side. Thralls stoke their cores until they burst. Hounds scald
themselves into a frenzy. Menders ward the line with bronze, and drowned Bells toll the rest of the crew on. The run is
three halls, each with its own master: Foundry-Master Ommet at the pour, the Brine Mother in the flooded quench, and
Old Sorrow, the great bell that drowned, at the bottom.

**Shape.** Seeded, `tutorial: false`. Three segments (each a fork into two branches that meet at its boss), par 24000
ticks until the pacing lane measures. The data is `sunken-bellfoundry.json`, the 11 roster packs are in `../roster/`,
and the kits are in `../enemies/`, `../abilities/` and `../effects/bellfoundry.json`.

| New kind | The one new mechanic |
|---|---|
| Slag Ram | Molten Charge: a line through its target and on (stand beside the tank, never behind it) |
| Drowned Bell | Resonant Toll: stacks +20% damage on every enemy in its 6 m ring until it is killed (a totem) |
| Furnace Thrall | Stoke Core is a kick. If it lands, the Thrall bursts in Slag Burn where it dies |
| Scald Hound | Below 30% it boils over (Boiling Blood, an enrage: +50% damage, +30% haste) |
| Brine Mender | Bronze Ward, a kick: its pack takes half damage for 10 s |

Reused kinds: Bellows Keeper, Drowned Chorister, Vault Brute, Ash Hexer and Hollow Hound.

| Boss | Mechanics (each answered at least twice at key 2) |
|---|---|
| Foundry-Master Ommet | Pour Slag (a cone the tank turns away), Temper (a self-heal; kick it) |
| The Brine Mother | Undertow drags everyone in, then Boiling Surge (get out); below 60%, Drowning Hymn (kick it) |
| Old Sorrow, the Drowned Bell | Clangour (a cone), the Iron Clappers (a Break bar two seats stagger), Still the Bell (a Lock) |

**Chain pulls** (huge pulls are fun): Slag Haulers + Pour Line, Tolling Shift + Scald Kennel, Furnace Stokers +
Bellows Shift, Brine Chapel + Drowned Choir. Each pack's `chainNote` says what the pair trades.
