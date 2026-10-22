using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Host;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

public partial class Screen
{
    // The seats the next Queue fills (CharacterSheet.Seats over the party SimDriver builds: the human's roster seat and,
    // unless --solo, a bot in every other seat), empty without content.
    private IReadOnlyList<SeatRow> QueueSeats() =>
        _driver?.Registry is { } reg && _lobby is { } lobby
            ? CharacterSheet.Seats(reg, lobby.ClassId, withBots: !_driver.Options.Solo)
            : Array.Empty<SeatRow>();

    private void ProofSeats()
    {
        var seats = QueueSeats();
        if (seats.Count == 0)
        {
            return;
        }
        var human = seats.FirstOrDefault(s => s.Human)?.ClassId ?? "none";
        var bots = seats.Where(s => !s.Human).Select(s => s.ClassId).ToList();
        Proof(FormattableString.Invariant(
            $"SCREEN seats total={seats.Count} human={human} bots={(bots.Count == 0 ? "none" : string.Join(',', bots))}"));
    }

    // Queue tab, middle: one row per seat (the human's first), then what pressing Queue does with them. While queued the
    // live role readout and the ready check are the Queue node's (BIBLE s10), drawn over the HUD once the screen closes.
    private void DrawSeats(Rect2 r, ScreenState state)
    {
        Band(r, "Party");
        var x = r.Position.X + 14;
        var y = r.Position.Y + 60;
        var inner = r.Size.X - 28;
        var seats = QueueSeats();
        for (var i = 0; i < seats.Count; i++)
        {
            var s = seats[i];
            var box = new Rect2(x, y - 20, inner, 44);
            DrawRect(box, s.Human ? TabOn : CardBg);
            DrawRect(box, s.Human ? Gold : BronzeDim, filled: false, width: s.Human ? 2f : 1f);
            var who = s.Human ? "You" : "Bot";
            HudNodes.Text(this, new Vector2(x + 10, y), FormattableString.Invariant($"Seat {i + 1}"), SmallFont, Dim);
            HudNodes.Text(this, new Vector2(x + 70, y), Clip($"{who}  -  {ClassName(s.ClassId)}", inner - 170, BodyFont), BodyFont, s.Human ? Gold : Parchment);
            var role = s.Role.ToString();
            HudNodes.Text(this, new Vector2(box.End.X - 10 - HudNodes.Width(role, SmallFont), y), role, SmallFont, Calm);
            y += 52;
        }
        var bots = seats.Count(s => !s.Human);
        var line = state.Locked
            ? "Queued: the seats fill when the group is found."
            : bots == 0
                ? "Queue starts a solo run."
                : FormattableString.Invariant($"Queue fills {bots} seats with bots at key {state.KeyLevel}.");
        foreach (var l in Wrap(line, inner, BodyFont, 3))
        {
            HudNodes.Text(this, new Vector2(x, y), l, BodyFont, state.Locked ? Good : Calm);
            y += 18;
        }
    }
}
