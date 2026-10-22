- fix/fastcheck-lanes-gate: tools/fastcheck.ps1 runs the lane row-file gate (npx vitest run tests/lanes) as CI does, so a malformed lane row fails locally (f-a6193186, keyrun #654)
BUILDLOG: fastcheck.ps1 lanes stage added (f-a6193186): local gate now matches CI lane row-file check; bite proof: a 3-line row fails locally
