using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Lock is theoretically winnable (STATE.md D17; design/ART-PLAN.md section 8 lane 9; BIBLE s9 "The Lock").
/// A deterministic scripted party plays the real boss fight on the real run root and closes the first Lock with a
/// Setup then a Payoff from another class inside the Window.
/// <para>
/// What is real: the hollow-vault k2 oracle setup (the shipped content's abilities, effects, combos and the
/// Tempo-scaled boss pull and script, content/data as tuned by D14), the host's three seats (Warden 1, Chanter 2,
/// Cinder 3: game/src/Host/HostRunSetup.cs Roster), the host's <c>--pull 4</c> slice
/// (game/src/Host/SimDriver.cs StartRun: <c>Pulls.Skip(StartPull - 1)</c>), the host's boss arming
/// (<see cref="BossRules.Arm"/> with a config built from content/data/enemies/hollow-warden.json exactly as
/// HostRunSetup.BuildBossConfig builds it) and the host's step (<see cref="Backfill.Step"/>, SimDriver.StepOnce).
/// Nothing in boss or encounter state is set by hand: the Lock opens because the party brought the boss to 50%.
/// </para>
/// <para>
/// What is not the host: the NAV-CORE layout is not applied (its adapter, Keyrun.Content NavLayouts, is not referenced
/// by this test project), so the boss pull stands at its flat-arena position and spawns at creation, as a
/// <c>--pull 4</c> start spawns it on entry into its trigger. The Lock rules read no layout. The party is driven by
/// <see cref="Policy"/> below in place of the bots (the bot party is not required to break the Lock, D17).
/// </para>
/// </summary>
public sealed class LockWinnabilityTests
{
    /// <summary>Run seed (the HudSceneTest run C seed).</summary>
    public const long Seed = 1;

    /// <summary>Key level: the k2 oracle setup's key.</summary>
    public const int Key = 2;

    /// <summary>1-based start pull, as <c>--pull 4</c>: the Hollow Warden.</summary>
    public const int StartPull = 4;

    private const int Warden = 1;
    private const int Chanter = 2;
    private const int Cinder = 3;
    private const int Boss = 900;
    private const int MaxTicks = 6000;

    /// <summary>Melee standoff the Warden walks to (Strike and Reckoning range 3000).</summary>
    private const long MeleeMm = 2500;

    /// <summary>
    /// Where the Warden holds the boss: 9 m and more from the casters' seats (x -2100 to -700, z 4000), beyond the
    /// cleave's 4 m cone, inside the Payoff's and Cadence's 25 m range.
    /// </summary>
    private static readonly Vec3 TankSpot = new(9000, 0, 0);

    private readonly ITestOutputHelper _out;

    public LockWinnabilityTests(ITestOutputHelper output) => _out = output;

    [Fact(DisplayName = "Lock winnable: a Warden Setup then a Cinder Payoff inside the Window breaks the first Lock")]
    public void Setup_then_payoff_inside_the_window_breaks_the_lock()
    {
        var played = Play(payoffInLock: true);
        Report(played);

        var events = played.Events;
        var open = events.FirstOrDefault(e => e.Type == "lock_open");
        Assert.True(open is not null, "no lock_open: the scripted party never brought the boss to 50%. " + Tail(events));
        var openTick = open!.Tick;
        var endsTick = IntOf(open, "endsTick");
        Assert.Equal(1, IntOf(open, "lockIndex"));

        var wipeBefore = events.FirstOrDefault(e => e.Type == "run_end" && e.Tick <= endsTick);
        Assert.True(wipeBefore is null, "the run ended before the first Lock closed: " + Tail(events));

        var broken = events.FirstOrDefault(e => e.Type == "lock_broken");
        Assert.True(broken is not null, $"lock_broken missing: Lock 1 opened at tick {openTick} (ends {endsTick}) and was not broken. " + Tail(events));
        Assert.Equal(1, IntOf(broken!, "lockIndex"));
        Assert.Equal(Warden, IntOf(broken, "setupActorId"));
        Assert.Equal("warden", StrOf(broken, "setupClass"));
        Assert.Equal(Cinder, IntOf(broken, "payoffActorId"));
        Assert.Equal("cinder", StrOf(broken, "payoffClass"));
        var comboTick = IntOf(broken, "comboTick");
        Assert.InRange(comboTick, openTick, endsTick - 1);
        Assert.DoesNotContain(events, e => e.Type == "lock_failed" && IntOf(e, "lockIndex") == 1);

        // The Setup that tagged the Lock is the Warden's Sundered on the boss, opened inside the Window, and no Sundered
        // from before the Window was still open on the boss when it opened (so the Combo closed an in-Window Setup).
        Assert.DoesNotContain(events, e => e.Type == "window_opened" && IntOf(e, "targetId") == Boss &&
            e.Tick < openTick && IntOf(e, "endsTick") > openTick);
        Assert.Contains(events, e => e.Type == "window_opened" && StrOf(e, "windowId") == "sundered" &&
            IntOf(e, "targetId") == Boss && IntOf(e, "setupActorId") == Warden && e.Tick >= openTick && e.Tick < endsTick);
        Assert.Contains(events, e => e.Type == "combo" && StrOf(e, "comboId") == "shatter" && IntOf(e, "targetId") == Boss && e.Tick == comboTick);
        Assert.Equal(1, played.Final.Encounter.Boss!.LocksBroken);
    }

    [Fact(DisplayName = "Lock winnable control: the same stream with no Payoff in the Window fails the first Lock")]
    public void Without_the_payoff_the_lock_fails()
    {
        var played = Play(payoffInLock: false);
        var events = played.Events;
        var open = events.FirstOrDefault(e => e.Type == "lock_open");
        Assert.True(open is not null, "no lock_open in the control run. " + Tail(events));
        Assert.DoesNotContain(events, e => e.Type == "lock_broken" && IntOf(e, "lockIndex") == 1);
        Assert.Contains(events, e => e.Type == "lock_failed" && IntOf(e, "lockIndex") == 1);
    }

    private sealed record Played(List<SimEvent> Events, List<string> Commands, Run Final);

    // SimDriver.StartRun for `--autorun --key 2 --seed 1 --pull 4` (request null), then StepOnce until the first Lock
    // has resolved (broken or failed) plus a margin, or the run ends.
    private static Played Play(bool payoffInLock)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        Assert.Equal(Key, setup.Content.KeyLevel);
        var party = setup.Party.Take(3).ToList();
        Assert.Equal(new[] { "warden", "chanter", "cinder" }, party.Select(p => p.ClassId));
        var dungeon = setup.Dungeon with { Pulls = setup.Dungeon.Pulls.Skip(StartPull - 1).ToList() };
        Assert.Equal("boss", dungeon.Pulls[0].Id);
        var created = RunRoot.CreateRun(Seed, party, dungeon, setup.Content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, BossConfigFromContent(setup.Content, Key)) };

        var events = new List<SimEvent>();
        var commands = new List<string>();
        var book = BackfillState.Empty;
        int? resolvedTick = null;
        for (var i = 0; i < MaxTicks && run.RunEnd.Result is null; i++)
        {
            var cmds = Policy(run, setup.Content, payoffInLock);
            foreach (var c in cmds.OfType<CastCommand>())
            {
                commands.Add(string.Create(CultureInfo.InvariantCulture, $"{c.Tick} actor={c.ActorId} cast {c.AbilityId} -> {c.TargetId?.ToString(CultureInfo.InvariantCulture) ?? "self"}"));
            }
            var step = Backfill.Step(book, run, cmds, Array.Empty<QueueRequest>());
            book = step.Backfill;
            run = step.Step.Run;
            events.AddRange(step.Step.Events);
            if (resolvedTick is null && step.Step.Events.Any(e => e.Type is "lock_broken" or "lock_failed"))
            {
                resolvedTick = run.State.Tick;
            }
            if (resolvedTick is { } r && run.State.Tick >= r + 20)
            {
                break;
            }
        }
        return new Played(events, commands, run);
    }

    /// <summary>
    /// The scripted player, a pure function of the visible state (deterministic). Warden: walk to melee, Strike on
    /// cooldown (Reckoning at 100 Momentum) until the Lock opens, then one Strike inside the Window (the Setup).
    /// Cinder: Ember Bolt from range until the Lock opens; once the Warden's Setup has tagged the Lock, Cold Snap on the
    /// boss (the Payoff; <paramref name="payoffInLock"/> false withholds it, the mutation). Chanter: Cadence on the
    /// most injured ally. No floor Windows are used, so Payoff.cs:153's field self-hit never applies.
    /// </summary>
    private static IReadOnlyList<Command> Policy(Run run, RunContent content, bool payoffInLock)
    {
        var state = run.State;
        var tick = state.Tick;
        var cmds = new List<Command>();
        if (!state.Actors.TryGetValue(Boss, out var boss) || !boss.Alive)
        {
            return cmds;
        }
        var lockWindow = run.Encounter.Boss?.Active;
        var tagged = lockWindow?.SetupActorId is not null;

        if (state.Actors.TryGetValue(Warden, out var w) && w.Alive)
        {
            var dx = boss.Pos.X - w.Pos.X;
            var dz = boss.Pos.Z - w.Pos.Z;
            var d = Movement.Isqrt((dx * dx) + (dz * dz));
            if (w.Health * 10 < w.MaxHealth * 7 && Ready(w, content, "warden.brace", tick))
            {
                // Off the GCD: the defensive goes out alongside anything else this tick.
                cmds.Add(new CastCommand(tick, Warden, "warden.brace", null));
            }
            if (d <= MeleeMm && boss.TargetId is { } bt && bt != Warden && Ready(w, content, "warden.taunt", tick))
            {
                // Off the GCD: pull the boss back off a softer seat.
                cmds.Add(new CastCommand(tick, Warden, "warden.taunt", Boss));
            }
            // Tank the boss (it chases its threat target) at a spot away from the casters, clear of its cleave.
            var sx = TankSpot.X - w.Pos.X;
            var sz = TankSpot.Z - w.Pos.Z;
            if (Movement.Isqrt((sx * sx) + (sz * sz)) > 300)
            {
                cmds.Add(new MoveCommand(tick, Warden, Toward(sx, sz), w.Yaw));
            }
            if (d > MeleeMm)
            {
                // Out of reach: keep walking; the boss closes on its own.
            }
            else if (lockWindow is not null)
            {
                if (!tagged && Ready(w, content, "warden.strike", tick))
                {
                    cmds.Add(new CastCommand(tick, Warden, "warden.strike", Boss));
                }
            }
            else if (NearLock(run, boss))
            {
                // The Lock is one Ember Bolt away: hold the GCD so the Setup goes out the tick the Window opens.
            }
            else if (w.Resource >= 50 && w.Health * 10 < w.MaxHealth * 6 && Ready(w, content, "warden.vigil", tick))
            {
                cmds.Add(new CastCommand(tick, Warden, "warden.vigil", null));
            }
            else if (w.Resource >= 100 && Ready(w, content, "warden.reckoning", tick))
            {
                cmds.Add(new CastCommand(tick, Warden, "warden.reckoning", Boss));
            }
            else if (Ready(w, content, "warden.strike", tick))
            {
                cmds.Add(new CastCommand(tick, Warden, "warden.strike", Boss));
            }
        }

        if (state.Actors.TryGetValue(Cinder, out var c) && c.Alive && c.CastId is null)
        {
            if (c.Health * 2 < c.MaxHealth && Ready(c, content, "cinder.ashenSkin", tick))
            {
                cmds.Add(new CastCommand(tick, Cinder, "cinder.ashenSkin", null));
            }
            if (lockWindow is not null)
            {
                if (tagged && payoffInLock && Ready(c, content, "cinder.coldSnap", tick))
                {
                    cmds.Add(new CastCommand(tick, Cinder, "cinder.coldSnap", Boss));
                }
            }
            else if (Ready(c, content, "cinder.emberBolt", tick))
            {
                cmds.Add(new CastCommand(tick, Cinder, "cinder.emberBolt", Boss));
            }
        }

        if (state.Actors.TryGetValue(Chanter, out var h) && h.Alive && h.CastId is null && Ready(h, content, "chanter.cadence", tick))
        {
            // The tank first while it is below 90%, else the most injured ally.
            var hurt = state.Actors.Values
                .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Health < a.MaxHealth)
                .OrderBy(a => a.Id == Warden && a.Health * 10 < a.MaxHealth * 9 ? 0 : 1)
                .ThenBy(a => a.Health * 10000 / Math.Max(1, a.MaxHealth))
                .ThenBy(a => a.Id)
                .FirstOrDefault();
            if (hurt is not null)
            {
                cmds.Add(new CastCommand(tick, Chanter, "chanter.cadence", hurt.Id));
            }
        }
        return cmds;
    }

    // A move direction inside Commands.Validate's [-1000, 1000] per axis (the sim normalises its length).
    private static Dir2 Toward(long dx, long dz)
    {
        var m = Math.Max(Math.Abs(dx), Math.Abs(dz));
        return m == 0 ? new Dir2(0, 0) : new Dir2(IntMath.TruncDiv(dx * 1000, m), IntMath.TruncDiv(dz * 1000, m));
    }

    // True before the first Lock while the boss is within 6% of the 50% gate (a player reading the boss frame). Long
    // enough that every pre-Lock Sundered has expired when the Window opens, so only an in-Window Setup can be closed.
    private static bool NearLock(Run run, ActorState boss) =>
        run.Encounter.Boss is { LocksOpened: 0 } b &&
        boss.Health * 10000 <= boss.MaxHealth * (b.Config.LockThresholdBp + 600);

    private static bool Ready(ActorState actor, RunContent content, string abilityId, int tick) =>
        actor.CastId is null && Cooldowns.IsAbilityReady(actor, content.Abilities[abilityId], tick) &&
        (content.Abilities[abilityId].Cost.Resource == "none" || actor.Resource >= content.Abilities[abilityId].Cost.Amount);

    /// <summary>
    /// game/src/Host/HostRunSetup.cs BuildBossConfig over content/data/enemies/hollow-warden.json: the boss's one Break
    /// bar is the Twins' bar, the two enemies naming it are the Twins (ids 901, 902 in id order, Tempo-scaled health
    /// and scripts, at +/-4000 mm x), and the boss ability tagged <c>setup:lock</c> gives the Window length, the
    /// shield effect and the suppressed cast, which the boss casts before each Window opens (CastLock, as the host
    /// arms it since LOCK-TELEGRAPH). The 50%, 30 s and three-fail structure are the BossConfig defaults.
    /// </summary>
    private static BossConfig BossConfigFromContent(RunContent content, int keyLevel)
    {
        var enemies = ((JsArray)JsValue.Parse(File.ReadAllText(RepoFile("content", "data", "enemies", "hollow-warden.json")))).Items
            .Cast<JsObject>().ToList();
        var bossDef = enemies.Single(e => StrIn(e, "id") == "hollowWarden.boss");
        var bar = (JsObject)((JsArray)bossDef.Get("breakBars")!).Items.Single();
        var barId = StrIn(bar, "id");
        var twinDefs = enemies.Where(e => e.Get("breakBarId") is JsString s && s.Value == barId)
            .OrderBy(e => StrIn(e, "id"), StringComparer.Ordinal).ToList();
        Assert.Equal(2, twinDefs.Count);
        var twins = twinDefs.Select((def, i) => new BossTwinSpec(
            Boss + 1 + i,
            StrIn(def, "id"),
            Tempo.ScaleHealth(((JsInt)def.Get("maxHealth")!).Value, keyLevel),
            new Vec3(i == 0 ? 4000 : -4000, 0, 0),
            Tempo.ScaleScript(EncounterJson.ParseScript(def.Get("script")!), keyLevel))).ToList();
        var onEmpty = ((JsArray)bar.Get("onEmpty")!).Items.Cast<JsObject>()
            .Select(op => new BreakBarOnEmptyEffect(StrIn(op, "effectId"), (int)((JsInt)op.Get("durationTicks")!).Value, ((JsInt)op.Get("stacks")!).Value))
            .ToList();
        var twinsBar = new BreakBarSource(barId, twins.Select(t => t.ActorId).ToList(),
            ((JsInt)bar.Get("max")!).Value, ((JsInt)bar.Get("drainPerInterrupt")!).Value, ((JsInt)bar.Get("drainPerControl")!).Value, onEmpty);

        var lockAbility = content.Abilities.Values.Single(a => a.Tags is not null && a.Tags.Contains("setup:lock"));
        var window = lockAbility.Effects.OfType<WindowOp>().Single();
        var shield = lockAbility.Effects.OfType<ApplyEffectOp>().Single();
        return new BossConfig(Boss, twins, twinsBar)
        {
            LockWindowTicks = window.DurationTicks,
            ShieldEffectId = shield.EffectId,
            SuppressedBossCasts = new[] { lockAbility.Id },
            CastLock = true,
        };
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(OracleFixtures.Directory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("not found above the oracle fixtures: " + string.Join("/", parts));
    }

    private static string StrIn(JsObject o, string key) => ((JsString)o.Get(key)!).Value;

    private static long IntOf(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static string? StrOf(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    private static readonly string[] TailTypes =
    {
        "lock_open", "lock_broken", "lock_failed", "boss_enrage", "boss_add_spawn", "window_opened", "combo", "combo_denied", "death", "run_end",
    };

    private static string Tail(IEnumerable<SimEvent> events) =>
        "Event tail: " + string.Join(" | ", events.Where(e => Array.IndexOf(TailTypes, e.Type) >= 0).TakeLast(14).Select(e => e.ToCanonicalJson()));

    private void Report(Played played)
    {
        // The whole cast stream (moves are omitted: the Warden walks to TankSpot from tick 0), the damage the party took,
        // the Lock-relevant events and the final seats.
        foreach (var line in played.Commands)
        {
            _out.WriteLine("CMD " + line);
        }
        foreach (var e in played.Events.Where(e => e.Type == "damage" && IntOf(e, "targetId") is Warden or Chanter or Cinder))
        {
            _out.WriteLine("HIT " + e.ToCanonicalJson());
        }
        foreach (var e in played.Events.Where(e => Array.IndexOf(TailTypes, e.Type) >= 0))
        {
            _out.WriteLine("EVENT " + e.ToCanonicalJson());
        }
        foreach (var a in played.Final.State.Actors.Values.Where(a => a.Kind == ActorKind.Player || a.Id == Boss))
        {
            _out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"ACTOR {a.Id} {a.ClassId} hp={a.Health}/{a.MaxHealth} alive={a.Alive} pos={a.Pos} yaw={a.Yaw} target={a.TargetId}"));
        }
    }
}
