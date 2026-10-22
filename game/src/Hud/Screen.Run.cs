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
    private void OnStepped(int tick, IReadOnlyList<Keyrun.Sim.Core.SimEvent> events)
    {
        if (_banked || _driver?.Run?.RunEnd.Result is not { } result)
        {
            return;
        }
        _banked = true;
        RecordRating(_driver.Run, result);
        // f-275d47c9: the bank records the key the run was played at (KeyMove.From, the run-end panel's own key read)
        // and its result in Progression.History, so Ascension marks are counted from the saved data.
        var played = KeyMove.ForRun(_driver.Run)!.From;
        _progression = _progression.BankRun(result, played);
        Proof(FormattableString.Invariant($"SCREEN bank result={result.ToJs()} ash={Ash.ForResult(result)} total={_progression.AshTotal}"));
        Proof(AscensionReadout.LogLine("SCREEN", Ascension.Of(_progression)));
        SaveProgression("bank");

        // M1 TIMER-TIERS (BIBLE s8): a timed run raises the key by its tier (+1, +2 within 80% of par, +3 within 60%);
        // a depleted or abandoned run keeps the key at the level it was played at (Matt 2026-10-20: no drop).
        // KeyMove is the one read of that move, shared with the run-end panel's key line, so the panel and the reopened
        // screen never disagree about the level. It uses the same tick fallback (ResultTick ?? RunTimerTicks) as main's
        // tier read, and clamps the raise to the key range.
        // LOBBY-TABS: the lobby comes back on its Play tab at that key, class and build kept (Lobby.ReturnFrom); before the
        // first open (an --autorun run) there is no lobby yet and the key waits for it.
        var move = KeyMove.ForRun(_driver.Run)!;
        // R1: the band reads the Clause from the same rotation index and content clauseStartKey the run is built with,
        // as the Play tab does (Screen.cs Lobby.Open), so the reopened key never shows a Clause the run does not carry.
        var clauseStartKey = _driver.Registry is { } reg ? reg.Run.Affixes.ClauseStartKey : ScreenState.NoClauseStartKey;
        var to = ScreenState.Open(SimDriver.DungeonId, ClauseIds(), move.To, _driver.RotationIndex, clauseStartKey).KeyLevel;
        if (_lobby is { } lobby)
        {
            _lobby = lobby.ReturnFrom(_driver.Run);
            to = _lobby.Key.KeyLevel;
        }
        else
        {
            _pendingKey = to;
        }
        Proof(FormattableString.Invariant($"SCREEN key from={move.From} to={to} tier={move.Tier}"));
        if (_driver.Options.Autorun)
        {
            _autorunStep = 0;
            _autorunWait = 2;
        }
    }

    private void OnRunStarted()
    {
        _banked = false;
        Close();
        if (_lobby is { } lobby)
        {
            // A new run: the Key band unlocks for the next queue, keeping the key, class and build the party chose.
            _lobby = lobby.Reopen(lobby.Key.KeyLevel);
        }
    }

    private void RunAutorunScript()
    {
        if (_autorunStep < 0 || _autorunStep >= AutorunKeys.Length)
        {
            return;
        }
        if (_autorunWait > 0)
        {
            _autorunWait--;
            return;
        }
        var key = AutorunKeys[_autorunStep];
        GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        if (_autorunStep == 0 && !IsOpen)
        {
            Proof("SCREEN error the run-end take did not open the screen");
            _autorunStep = AutorunKeys.Length;
            return;
        }
        _autorunStep++;
    }

    private void Proof(string line)
    {
        if (_driver is not null)
        {
            HudNodes.Proof(_driver, line);
        }
    }

    // BIBLE s7 persistence (lane PROGRESSION-SAVE): the banked progression is loaded once per launch and saved after
    // every change that keeps (the bank, a reward take, a reroll). ProgressionSavePlace picks the directory: user://
    // in the interactive game, --progression-dir when given, and nowhere for a headless run without it (scene tests
    // and CI never touch the real save). With no store this is exactly the old Progression.Start, and nothing prints.
    private void LoadProgression(long seed)
    {
        var dir = Keyrun.Content.ProgressionSavePlace.Directory(
            OS.GetCmdlineUserArgs(), DisplayServer.GetName() == "headless", ProjectSettings.GlobalizePath("user://"));
        if (dir is null)
        {
            _progression = Progression.Start(seed);
            return;
        }
        _store = new ProgressionStore(dir);
        _progression = _store.Load(seed);
        _ratingStore = new PrivateRatingStore(dir);
        _rating = _ratingStore.Load();
        Proof(FormattableString.Invariant(
            $"SCREEN progression load store=1 ash={_progression.AshTotal} runs={_progression.Runs} items={EquippedText()} path=\"{_store.FilePath}\""));
    }

    // The private M+ rating (f-a3230c59): a timed run's result is read, never changed (the key level is not touched),
    // and the record is saved beside the progression save. Nothing about it is printed but LogLine (no value).
    private void RecordRating(Keyrun.Sim.Runs.Run run, RunResult result)
    {
        var timed = Keyrun.Content.PrivateRating.FromResult(
            result == RunResult.Timed, run.RunEnd.ResultTick, run.Dungeon.Id, run.Encounter.KeyLevel, run.Encounter.ParTicks);
        if (timed is null)
        {
            return;
        }
        var next = _rating.Add(timed);
        if (ReferenceEquals(next, _rating))
        {
            return;
        }
        _rating = next;
        _ratingStore?.Save(_rating);
    }

    private void SaveProgression(string reason)
    {
        if (_store is null)
        {
            return;
        }
        var ok = _store.Save(_progression);
        Proof(FormattableString.Invariant(
            $"SCREEN progression save reason={reason} ok={(ok ? 1 : 0)} ash={_progression.AshTotal} runs={_progression.Runs} items={EquippedText()}"));
    }
}
