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
    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey { Pressed: true, Echo: false } key && HandleKey(key.Keycode))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }
        var l = Layout();
        if (l.Tabs.Select((r, i) => (r, i)).FirstOrDefault(t => t.r.HasPoint(click.Position)) is { r.Size.X: > 0 } hit)
        {
            SelectTab(Lobby.Tabs[hit.i]);
        }
        else if (Tab == LobbyTab.Character)
        {
            var ids = ClassIds();
            for (var i = 0; i < ids.Count && i < l.ClassCards.Count; i++)
            {
                if (l.ClassCards[i].HasPoint(click.Position))
                {
                    PickClass(ids[i]);
                }
            }
        }
        else if (Tab == LobbyTab.Play && l.Queue.HasPoint(click.Position))
        {
            Queue();
        }
        else if (Tab == LobbyTab.Play && l.KeyDown.HasPoint(click.Position))
        {
            StepKey(-1);
        }
        else if (Tab == LobbyTab.Play && l.KeyUp.HasPoint(click.Position))
        {
            StepKey(1);
        }
        AcceptEvent();
    }

    /// <summary>
    /// Opens a tab. Leaving or entering the Build tab is the talent panel closing or opening, so it prints the same
    /// "SCREEN talents open=..." line the T key always has.
    /// </summary>
    /// <param name="tab">The tab.</param>
    public void SelectTab(LobbyTab tab)
    {
        if (!IsOpen || _lobby is not { } lobby)
        {
            return;
        }
        var next = lobby.SelectTab(tab);
        if (ReferenceEquals(next, lobby))
        {
            return;
        }
        var wasBuild = lobby.Tab == LobbyTab.Build;
        _lobby = next;
        Proof(FormattableString.Invariant($"SCREEN tab {next.Tab.ToString().ToLowerInvariant()} class={next.ClassId}"));
        if (wasBuild == (next.Tab == LobbyTab.Build))
        {
            return;
        }
        var picks = next.Talents;
        TalentFork = Math.Clamp(TalentFork, 0, Math.Max(0, picks.ForkCount - 1));
        Proof(FormattableString.Invariant(
            $"SCREEN talents open={(TalentsOpen ? 1 : 0)} class={picks.ClassId} forks={picks.ForkCount} picks={picks.Count} applied={AppliedCount(TalentLoadouts())}"));
        if (TalentsOpen)
        {
            ProofTalents();
        }
    }

    /// <summary>Opens the Build tab, or goes back to Play from it (T). Only while the screen is up.</summary>
    public void ToggleTalents() => SelectTab(Tab == LobbyTab.Build ? LobbyTab.Play : LobbyTab.Build);

    // Returns true when the key was the screen's (Enter is not consumed, so the host's restart still sees it).
    private bool HandleKey(Key key)
    {
        if (TabKey(key))
        {
            return true;
        }
        if (TalentsOpen)
        {
            return HandleTalentKey(key);
        }
        switch (key)
        {
            case Key.T:
                ToggleTalents();
                return true;
            case Key.Up when Tab == LobbyTab.Character:
                CycleClass(-1);
                return true;
            case Key.Down when Tab == LobbyTab.Character:
                CycleClass(1);
                return true;
            case Key.Left or Key.Minus or Key.KpSubtract:
                StepKey(-1);
                return true;
            case Key.Right or Key.Equal or Key.Plus or Key.KpAdd:
                StepKey(1);
                return true;
            case Key.Tab:
                CycleSelected();
                return true;
            case Key.R:
                Reroll();
                return true;
            case Key.Enter or Key.KpEnter:
                Queue();
                return false;
            default:
                return false;
        }
    }

    // The tab bar's keys work on every tab: Q and E step along the bar, F1-F3 jump to a tab.
    private bool TabKey(Key key)
    {
        if (_lobby is not { } lobby)
        {
            return false;
        }
        switch (key)
        {
            case Key.Q:
                SelectTab(lobby.CycleTab(-1).Tab);
                return true;
            case Key.E:
                SelectTab(lobby.CycleTab(1).Tab);
                return true;
            case Key.F1:
                SelectTab(LobbyTab.Character);
                return true;
            case Key.F2:
                SelectTab(LobbyTab.Build);
                return true;
            case Key.F3:
                SelectTab(LobbyTab.Play);
                return true;
            default:
                return false;
        }
    }

    private void StepKey(int delta)
    {
        if (_lobby is not { } lobby)
        {
            return;
        }
        _status = lobby.Locked ? "The key is locked in." : string.Empty;
        _lobby = lobby.StepKey(delta);
        ProofKeyBand();
    }
}
