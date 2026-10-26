- fix/warden-frontal-dup: remove duplicate ApplyFrontalAnswer call in ScriptedWarden (no behaviour change)
BUILDLOG: Removed the second identical ApplyFrontalAnswer(CleaveAnswer(...)) line in ScriptedWarden.cs so the Cleave answer is computed and applied once per tick.
