# tests/client/e2e - headless browser specs

## What this folder holds
Five `*.e2e.ts` specs that drive the real browser playable in headless Chromium:
- `first-run-readability.e2e.ts`
- `run-end-overlay.e2e.ts`
- `run-readout.e2e.ts`
- `status-net.e2e.ts`
- `window-ping.e2e.ts`

They are NOT part of `npm test`. Run the whole set with `npm run test:e2e`.

## Run one spec locally
    node --test tests/client/e2e/window-ping.e2e.ts

Swap the file name for any other spec in this folder.

## Flake fixes
A flake fix is proven only by 5 consecutive green local runs of the changed spec, before push. Run npm run typecheck before every push that touches this folder. Do not write a FIX REPORT as green until the post-push CI run for that exact sha has concluded. If waiting is blocked, report the sha as UNCONFIRMED and do not claim it fixed.

Read HUD state in one page.evaluate / waitForFunction pass; a second DOM read races the per-frame HUD clear (window-ping, PRs #110 #111).
