# Keyrun.Bots guide

## Playtester guides (G2.4 K3b)

A guided playtester plays a per-class guide as data (design/PLAYTESTER-GUIDE.md section 4), so its balance evidence
comes from the intended rotation, not from a blind learner's cheese.

- **Location:** `game/content/data/guides/<classId>.json` (C#-only data; embedded under `content/data/guides/`).
  Only the Warden guide ships so far: `game/content/data/guides/warden.json`.
- **Loader:** `Keyrun.Bots.Guides.GuideSet.Parse(json, abilityIds)`. It fails closed with `InvalidOperationException`
  naming the guide and rule on any unknown key, unknown ability, unknown condition or malformed value.
  `GuideSet.Require(classId)` throws, naming the class, when a guided seat has no guide.
- **Schema:** `{ "classId", "version": 1, "rotation": [ { "id", "press", "when", "reason" } ] }`. The bot plays the
  first entry whose `when` holds and whose press is castable and affordable. Its receipt reason is
  `guide:<classId>:<id>` with the guide's quoted `reason`.
- **Condition vocabulary (implemented subset):** `all`, `any`, `not`, `resourceAtLeast`, `healthBelowPct`,
  `attackersOnSelfAtLeast`, `abilityReady`, `kickDuty`. Other keys from section 4 (`effectNotUp`, `targetWindowNot`,
  `castTicksLeftAtMost`, `insideTelegraph`, ...) are not implemented and fail at load.
- **Wiring:** `RoleSet.Guided(guides)` replaces the Tank slot with `Guides.GuidedTankRole`. The tank's kick-ledger,
  floor-escape and engage-walk answers still run first. `BotCommandSource(..., guides)` opts in: a Guided run plays the
  guide, and a Blind run keeps the scripted roster byte-for-byte. Without guides nothing changes.
- **Not yet:** the kick and telegraph sections of the guide, the Chanter and Cinder guides, and host wiring that loads
  the guide for a playtest run.
