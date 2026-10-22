using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    private void ProofKeyBand()
    {
        if (_lobby?.Key is not { } s)
        {
            return;
        }
        ProofDungeon(s);
        Proof(FormattableString.Invariant($"SCREEN key level={s.KeyLevel} locked={(s.Locked ? 1 : 0)}"));
        Proof(FormattableString.Invariant($"SCREEN affixes key={s.KeyLevel} line=\"{AffixLine(s.KeyLevel)}\""));
        Proof(FormattableString.Invariant($"SCREEN readiness line=\"{s.ReadinessLine}\""));
        // R1 (2026-10-21, BIBLE s8): the key sets the Clause; the band shows it as text, read-only, one line per key
        // (lane U2 shows it; no pick exists).
        var clause = s.KeyClause;
        var (name, text) = clause is null ? (string.Empty, string.Empty) : ClauseText(clause);
        Proof(FormattableString.Invariant($"SCREEN clause key={s.KeyLevel} id={clause ?? "none"} name=\"{name}\" startKey={s.ClauseStartKey}"));
        // The text as laid out at the design viewport (1280x720), with the same wrap the Key band draws.
        var box = ClauseTextWidth(LayoutFor(DesignSize()).Key);
        var lines = Wrap(text, box, SmallFont, 2);
        var widest = lines.Count == 0 ? 0f : lines.Max(line => HudNodes.Width(line, SmallFont));
        Proof(FormattableString.Invariant($"SCREEN fit clause lines={lines.Count} widest={Math.Ceiling(widest)} box={Math.Floor(box)}"));
        // The private last-three-runs strip: the same lines the band draws, from the saved history.
        var runs = RunStripText.Lines(_progression.History);
        if (runs.Count == 0)
        {
            Proof("SCREEN runs none");
        }
        for (var i = 0; i < runs.Count; i++)
        {
            Proof(FormattableString.Invariant($"SCREEN runs n={i + 1} \"{runs[i]}\""));
        }
    }

    // D65 / f-61f01ca3: the pre-run dungeon pick (Dark and Darker style: the map is picked before Queue). The Key band's
    // last row; Up and Down on the Key tab, or a click on its left or right half, cycle the registry's dungeon ids.
    private static Rect2 DungeonRow(Rect2 key) => new(key.Position.X + 14, key.Position.Y + 310, ClauseTextWidth(key), 36);

    private void ProofDungeon(ScreenState s)
    {
        if (_driver?.Registry is not { } reg || !reg.Dungeons.ContainsKey(s.DungeonId))
        {
            return;
        }
        var (name, bosses) = DungeonPick.Describe(reg, s.DungeonId);
        Proof(FormattableString.Invariant($"SCREEN dungeon id={s.DungeonId} name=\"{name}\" bosses={bosses} map={(DungeonPick.HasMap(reg, s.DungeonId) ? 1 : 0)} locked={(s.Locked ? 1 : 0)}"));
    }

    private void CycleDungeon(int delta)
    {
        if (_lobby is not { } lobby || _driver?.Registry is not { } reg)
        {
            return;
        }
        _status = lobby.Locked ? "The dungeon is locked in." : string.Empty;
        _lobby = lobby.WithDungeon(DungeonPick.Cycle(reg, lobby.Key.DungeonId, delta));
        if (_lobby.Key is { } s)
        {
            ProofDungeon(s);
        }
    }

    private IReadOnlyList<string> ClauseIds() =>
        _driver?.Registry is { } reg ? reg.Clauses.Select(kv => kv.Key).ToList() : Array.Empty<string>();

    // Key and Queue tabs, left: who is queueing and what the last run moved.
    private void DrawSummary(Rect2 r)
    {
        Band(r, "Character");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 58;
        HudNodes.Text(this, new Vector2(x, y), ClassName(), 20, Parchment);
        y += 22;
        var picks = _lobby?.Talents;
        HudNodes.Text(this, new Vector2(x, y), FormattableString.Invariant($"Talents {picks?.Count ?? 0}/{picks?.ForkCount ?? 0}    Gear {_progression.Equipped.Count}/{Rewards.GearSlots.Count}"), SmallFont, Dim);
        y += 26;
        var since = _progression.LastRun?.Text() ?? "First run";
        foreach (var line in Wrap("Since last run: " + since, r.Size.X - 28, BodyFont, 3))
        {
            HudNodes.Text(this, new Vector2(x, y), line, BodyFont, Calm);
            y += 18;
        }
        y += 12;
        foreach (var (keyLabel, name) in Abilities())
        {
            DrawRect(new Rect2(x, y - 15, 22, 20), CardBg);
            HudNodes.Text(this, new Vector2(x + 6, y), keyLabel, BodyFont, Gold);
            HudNodes.Text(this, new Vector2(x + 32, y), Clip(name, r.Size.X - 60, BodyFont), BodyFont, Parchment);
            y += 24;
        }
    }

    // D65: "< The Sunken Bellfoundry, 3 bosses >" under the run strip; "no map yet" when the content lays out none.
    private void DrawDungeonRow(Rect2 band, ScreenState state, float inner)
    {
        if (_driver?.Registry is not { } reg || !reg.Dungeons.ContainsKey(state.DungeonId))
        {
            return;
        }
        var row = DungeonRow(band);
        DrawRect(row, new Color(CardBg, state.Locked ? 0.4f : 1f));
        DrawRect(row, BronzeDim, filled: false, width: 1f);
        var (name, bosses) = DungeonPick.Describe(reg, state.DungeonId);
        var tail = DungeonPick.HasMap(reg, state.DungeonId) ? string.Empty : ", no map yet";
        HudNodes.Text(this, row.Position + new Vector2(10, 24), "<", 20, Parchment);
        HudNodes.Text(this, row.Position + new Vector2(row.Size.X - 24, 24), ">", 20, Parchment);
        HudNodes.Text(this, row.Position + new Vector2(34, 24), Clip(FormattableString.Invariant($"{name}, {bosses} boss{(bosses == 1 ? string.Empty : "es")}{tail}"), inner - 70, BodyFont), BodyFont, Gold);
        HudNodes.Text(this, row.Position + new Vector2(0, row.Size.Y + 14), "Dungeon: Up and Down or click", SmallFont, Dim);
    }

    // Key tab, right: what Enter does at this key, so the Key tab says the next step without the Queue tab open.
    private void DrawKeyNote(Rect2 r, ScreenState state)
    {
        Band(r, "Next");
        var inner = r.Size.X - 28;
        var y = r.Position.Y + 58;
        var text = state.Locked
            ? FormattableString.Invariant($"Queued at key {state.KeyLevel}.")
            : FormattableString.Invariant($"Enter queues at key {state.KeyLevel}. The Queue tab (F5) shows the seats it fills.");
        foreach (var line in Wrap(text, inner, BodyFont, 4))
        {
            HudNodes.Text(this, new Vector2(r.Position.X + 14, y), line, BodyFont, state.Locked ? Calm : Parchment);
            y += 18;
        }
    }

    private void DrawKey(Rect2 r, ScreenState state)
    {
        Band(r, "Key");
        var l = Layout();
        foreach (var (rect, glyph) in new[] { (l.KeyDown, "<"), (l.KeyUp, ">") })
        {
            DrawRect(rect, new Color(CardBg, state.Locked ? 0.4f : 1f));
            DrawRect(rect, BronzeDim, filled: false, width: 1f);
            HudNodes.Text(this, rect.Position + new Vector2(14, 27), glyph, 20, Parchment);
        }
        HudNodes.Text(this, new Vector2(r.Position.X + 74, r.Position.Y + 76), FormattableString.Invariant($"Key {state.KeyLevel}"), TitleFont, Parchment);
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 98), AffixLine(state.KeyLevel), SmallFont, Calm);
        var inner = ClauseTextWidth(r);
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 118), Clip(state.ReadinessLine, inner, BodyFont),
            BodyFont, state.Locked ? Calm : Good);
        // The Clause row: the one Clause the Queue hands the run, shown as text and never picked. R1 (BIBLE s8): the key
        // sets it (ClauseRotation.ForKey, read-only), and the row says so below the start key.
        var (label, text) = state.KeyClause is { } clause
            ? ("Clause: " + ClauseText(clause).Name, ClauseText(clause).Text)
            : (FormattableString.Invariant($"Clause: none below key {state.ClauseStartKey}"), string.Empty);
        HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 144), Clip(label, inner, BodyFont), BodyFont, Gold);
        var clauseLines = Wrap(text, inner, SmallFont, 2);
        for (var j = 0; j < clauseLines.Count; j++)
        {
            HudNodes.Text(this, new Vector2(r.Position.X + 14, r.Position.Y + 164 + (j * 14)), clauseLines[j], SmallFont, Colors.White);
        }
        // The private last-three-runs strip under the Clause row (BIBLE s10): the saved history, nothing else.
        RunStrip.Draw(this, new Rect2(r.Position.X + 14, r.Position.Y + 206, inner, 90), _progression.History);
        DrawDungeonRow(r, state, inner);
    }

    /// <summary>
    /// Plan M5 (BIBLE s8): the key's affixes as the run will be built with them, "Affixes: Fortified" or "Affixes: none",
    /// from content run.json and the setup's rotation index (the same <see cref="Affixes.AffixesFor"/> the host scales
    /// spawns with), so the screen never shows an affix the run does not carry.
    /// </summary>
    /// <param name="keyLevel">The key on the band.</param>
    /// <returns>The line.</returns>
    public string AffixLine(int keyLevel)
    {
        if (_driver?.Registry is not { } reg)
        {
            return "Affixes: none";
        }
        var affixes = Affixes.AffixesFor(HostRunSetup.AffixTableOf(reg.Run.Affixes), keyLevel, _driver.Setup?.RotationIndex ?? 0);
        return affixes.Names.Count == 0 ? "Affixes: none" : "Affixes: " + string.Join(" + ", affixes.Names);
    }

    private void DrawQueue(Rect2 r, ScreenState state)
    {
        DrawRect(r, state.Locked ? new Color(0.16f, 0.22f, 0.15f) : new Color(0.3f, 0.2f, 0.08f));
        DrawRect(r, Gold, filled: false, width: 2f);
        DrawRect(new Rect2(r.Position + new Vector2(4, 4), r.Size - new Vector2(8, 8)), BronzeDim, filled: false, width: 1f);
        HudNodes.Text(this, r.Position + new Vector2(18, 48), state.Locked ? "QUEUED" : "QUEUE", 28, Gold);
        var clauseName = state.KeyClause is { } clause ? ClauseText(clause).Name : "no Clause";
        HudNodes.Text(this, r.Position + new Vector2(18, 80), Clip(FormattableString.Invariant($"{ClassName()}, key {state.KeyLevel} with {clauseName}"), r.Size.X - 36, BodyFont), BodyFont, Parchment);
        HudNodes.Text(this, r.Position + new Vector2(18, 102), "Enter or click", SmallFont, Dim);
        var y = r.End.Y + 24;
        foreach (var line in CaptionLines(r))
        {
            HudNodes.Text(this, new Vector2(r.Position.X, y), line, SmallFont, Calm);
            y += SmallFont + 4;
        }
    }
}
