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
    /// <summary>Picks side <paramref name="option"/> (0-based) of fork <paramref name="fork"/>; out-of-range input is ignored.</summary>
    /// <param name="fork">The fork index.</param>
    /// <param name="option">The option index.</param>
    public void PickTalent(int fork, int option)
    {
        if (!TalentsOpen || _lobby is not { } lobby || lobby.Talents is not { } picks || fork < 0 || fork >= picks.ForkCount ||
            option < 0 || option >= picks.Forks[fork].Options.Count)
        {
            return;
        }
        _lobby = lobby.WithTalents(picks.Pick(fork, option));
        TalentFork = fork;
        var f = _lobby.Talents.Forks[fork];
        Proof(FormattableString.Invariant(
            $"SCREEN talent pick fork={fork + 1} ability={f.AbilityId} option={f.Options[option].Id} picks={_lobby.Talents.Count}"));
        ProofTalentMarks(fork);
        SaveTalents();
    }

    /// <summary>Clears every talent pick for free (BIBLE s6 "Respec is free on the screen, always"): Ash is untouched.</summary>
    public void RespecTalents()
    {
        if (!TalentsOpen || _lobby is not { } lobby)
        {
            return;
        }
        var ash = _progression.AshTotal;
        _lobby = lobby.WithTalents(lobby.Talents.Respec());
        Proof(FormattableString.Invariant($"SCREEN talent respec picks={_lobby.Talents.Count} ash={ash} ash_after={_progression.AshTotal}"));
        ProofTalentMarks();
        SaveTalents();
    }

    // TALENT-SAVE (f-83c5e369): the build rides the progression, so a pick or a respec is saved like a reroll is. Only
    // this class's forks are written; the other classes' saved builds are kept (Lobby.SaveTalents).
    private void SaveTalents()
    {
        if (_lobby is not { } lobby)
        {
            return;
        }
        var picks = lobby.Talents;
        _progression = lobby.SaveTalents(_progression);
        SaveProgression("talent");
        if (_store is not null)
        {
            Proof(FormattableString.Invariant($"SCREEN talents saved picks={picks.Count} choices={ChoicesText(picks)}"));
        }
    }

    // "warden.strike:rend,..." in fork order, or "none".
    private static string ChoicesText(Keyrun.Content.TalentPicks picks) =>
        picks.Count == 0 ? "none" : string.Join(",", picks.Choices.Select(kv => kv.Key + ":" + kv.Value));

    /// <summary>The human class's display name (the talent panel's title).</summary>
    /// <returns>The name.</returns>
    public string TalentClassName() => ClassName();

    /// <summary>An ability's display name (the talent panel's fork rows).</summary>
    /// <param name="abilityId">The ability id.</param>
    /// <returns>The name.</returns>
    public string TalentAbilityName(string abilityId) => _driver?.AbilityName(abilityId) ?? abilityId;

    /// <summary>The display name and description of a Clause.</summary>
    /// <param name="clauseId">The id.</param>
    /// <returns>Name and text.</returns>
    public (string Name, string Text) ClauseText(string clauseId) =>
        _driver?.Registry is { } reg && reg.Clauses.TryGetValue(clauseId, out var c) ? (c.Name, c.Description) : (clauseId, string.Empty);

    // The Build tab's keys (the talent panel; and Enter, which still queues).
    private bool HandleTalentKey(Key key)
    {
        switch (key)
        {
            case Key.T or Key.Escape:
                ToggleTalents();
                return true;
            case Key.Up:
                TalentFork = Math.Max(0, TalentFork - 1);
                ProofTalentMarks();
                return true;
            case Key.Down:
                TalentFork = Math.Min(Math.Max(0, (_lobby?.Talents.ForkCount ?? 1) - 1), TalentFork + 1);
                ProofTalentMarks();
                return true;
            case Key.Key1 or Key.Kp1:
                PickTalent(TalentFork, 0);
                return true;
            case Key.Key2 or Key.Kp2:
                PickTalent(TalentFork, 1);
                return true;
            case Key.Key3 or Key.Kp3:
                PickTalent(TalentFork, 2);
                return true;
            case Key.Backspace or Key.Delete:
                RespecTalents();
                return true;
            case Key.Enter or Key.KpEnter:
                Queue();
                return false;
            default:
                return true;
        }
    }

    private static Rect2 TalentsFor(Vector2 size) => LayoutFor(size).Talents;

    // f-0a21646d: one "SCREEN talent mark fork=<n> cursor=<ability> picked=<option> marker=<cards drawn PICKED>" line per
    // fork (or just `fork`), from the live build and cursor through TalentPanelMarks, the state the panel draws from.
    private void ProofTalentMarks(int? fork = null)
    {
        if (!TalentsOpen || _lobby?.Talents is not { } picks)
        {
            return;
        }
        for (var f = 0; f < picks.ForkCount; f++)
        {
            if (fork is null || fork == f)
            {
                Proof(Keyrun.Content.TalentPanelMarks.ProofLine(picks, f, TalentFork));
            }
        }
    }

    // One "SCREEN talent fork=..." line per fork and a fit line per option card, laid out at the design viewport.
    private void ProofTalents()
    {
        if (_lobby?.Talents is not { } picks)
        {
            return;
        }
        for (var f = 0; f < picks.ForkCount; f++)
        {
            var fork = picks.Forks[f];
            Proof(FormattableString.Invariant(
                $"SCREEN talent fork={f + 1} ability={fork.AbilityId} options={string.Join(',', fork.Options.Select(o => o.Id))} picked={picks.PickedOption(f) ?? "-"}"));
        }
        var panel = TalentsFor(DesignSize());
        var design = DesignSize();
        foreach (var card in TalentPanel.Cards(panel.Size, picks))
        {
            var text = picks.Forks[card.Fork].Options[card.Option].Text;
            var lines = TalentPanel.TextLines(text, card.Rect);
            var widest = lines.Count == 0 ? 0f : lines.Max(line => HudNodes.Width(line, TalentPanel.TextFont));
            var clipped = lines.Count > 0 && lines[^1].EndsWith("...", StringComparison.Ordinal) && !text.EndsWith("...", StringComparison.Ordinal) ? 1 : 0;
            var bottom = panel.Position.Y + card.Rect.Position.Y + TalentPanel.TextBottom(Math.Max(1, lines.Count));
            Proof(FormattableString.Invariant(
                $"SCREEN fit talent fork={card.Fork + 1} option={card.Option + 1} lines={lines.Count} widest={Math.Ceiling(widest)} box={Math.Floor(TalentPanel.TextWidthFor(card.Rect.Size.X))} clipped={clipped} text_bottom={Math.Ceiling(bottom)} card_bottom={Math.Floor(panel.Position.Y + card.Rect.End.Y)}"));
        }
        // f-0a21646d: the PICKED tag every card can carry stays right of the option name and above the description.
        foreach (var card in TalentPanel.Cards(panel.Size, picks))
        {
            var name = FormattableString.Invariant($"{card.Option + 1}  {picks.Forks[card.Fork].Options[card.Option].Name}");
            var nameRight = card.Rect.Position.X + Keyrun.Content.TalentCardLayout.TextInset + HudNodes.Width(name, TalentPanel.NameFont);
            var tag = TalentPanel.TagFor(card.Rect);
            Proof(FormattableString.Invariant(
                $"SCREEN fit talent mark fork={card.Fork + 1} option={card.Option + 1} name_right={Math.Ceiling(nameRight)} tag_left={Math.Floor(tag.Position.X)} tag_bottom={Math.Ceiling(tag.End.Y)} desc_top={Math.Floor(card.Rect.Position.Y + Keyrun.Content.TalentCardLayout.TextTop - TalentPanel.TextFont)}"));
        }
        ProofTalentMarks();
        Proof(FormattableString.Invariant(
            $"SCREEN fit talents right={Math.Ceiling(panel.End.X)} bottom={Math.Ceiling(panel.End.Y)} viewport={Math.Floor(design.X)}x{Math.Floor(design.Y)}"));
    }
}
