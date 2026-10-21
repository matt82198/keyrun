using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// TELEGRAPH-EVERY-ABILITY, the code form of design/BIBLE.md section 3: "Every dangerous enemy action is a cast with a
/// visible bar and a floor telegraph (circle, cone, line) drawn ahead of resolution. Shape plus colour, never colour
/// alone." Every enemy in content/data/enemies/*.json (the Choir, the Pair, the Seed, the Hollow Warden and its
/// Twins; the optional packs in game/content/data/layout.json are built from these same enemies, checked below) is
/// enumerated, and every ability in its kit that deals damage or puts an effect on someone other than its caster (a
/// control effect: tag <c>control</c>, or an <c>applyEffect</c> whose shape is not <c>self</c>) must:
/// <list type="bullet">
/// <item>be a cast with a bar: content <c>castTicks</c> above zero, and in play the caster holds the cast for that
/// many ticks before it lands;</item>
/// <item>be scripted with a floor telegraph of a real shape (circle with a radius, cone with a radius and an angle,
/// line with a length and a width);</item>
/// <item>in play, at keys 2, 10 and 20 (Tempo-scaled as the host scales scripts), on every landing: a telegraph of
/// that shape resolving on the landing tick was drawn no later than the tick the bar started, is on the floor on
/// every tick of the bar, and was drawn at least <see cref="MinLeadTicks"/> ticks before it resolves.</item>
/// </list>
/// Only auto-attacks are exempt, by name (<see cref="AutoAttacks"/>), and each exempt entry must itself look like an
/// auto-attack (instant, single-target, melee range, damage only) so the list cannot hide a slam. A heal, a self
/// shield or a Window the party uses is not a threat to the player and needs no floor telegraph.
/// </summary>
public sealed class EveryEnemyAbilityTelegraphedTests
{
    /// <summary>
    /// N: the minimum ticks between a telegraph appearing and resolving. BIBLE s9.4 "the 10-tick lead floor holds"
    /// (Tempo.LeadFloorTicks, tempo.ts:19: 0.5 s at 20 Hz, BIBLE s3). Restated as a literal on purpose: lowering the
    /// sim's floor must turn this test red, not move it.
    /// </summary>
    public const int MinLeadTicks = 10;

    /// <summary>The only exempt abilities: enemy auto-attacks (instant melee swings with no cast bar).</summary>
    public static readonly IReadOnlyDictionary<string, string> AutoAttacks = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["choir.meleeSwing"] = "Choir Acolyte / Choir Cantor melee auto-attack",
        ["pair.meleeSwing"] = "Paired Warrior melee auto-attack",
    };

    private const int AutoAttackMaxRangeMm = 3000;
    private const int Ticks = 3000;
    private const int SoloActor = 500;
    private static readonly int[] Keys = { 2, 10, 20 };

    private static string Root => ContentRoots.RepoRootAbove(OracleFixtures.Directory);

    private static JsArray ParseArray(string path) => (JsArray)JsValue.Parse(File.ReadAllText(path));

    private static string Id(JsObject o) => ((JsString)o.Get("id")!).Value;

    private static string? StrOf(JsValue? v) => v is JsString s ? s.Value : null;

    private static long? IntOf(JsValue? v) => v is JsInt i ? i.Value : null;

    /// <summary>Every enemy definition in content/data/enemies/*.json (both content roots), by id.</summary>
    private static Dictionary<string, JsObject> Enemies() =>
        ContentRoots.KindFiles(Root, "enemies")
            .SelectMany(f => ParseArray(f).Items.Cast<JsObject>())
            .ToDictionary(Id, e => e, StringComparer.Ordinal);

    /// <summary>Every ability in content/data/abilities/enemies.json, by id.</summary>
    private static Dictionary<string, JsObject> EnemyAbilities() =>
        ParseArray(Path.Combine(Root, "content", "data", "abilities", "enemies.json")).Items.Cast<JsObject>()
            .ToDictionary(Id, a => a, StringComparer.Ordinal);

    private static IEnumerable<string> Kit(JsObject enemy)
    {
        var listed = enemy.Get("abilities") is JsArray a ? a.Items.Select(v => ((JsString)v).Value) : Enumerable.Empty<string>();
        var scripted = ScriptSteps(enemy).Select(s => StrOf(s.Get("cast"))!).Where(c => c is not null);
        return listed.Concat(scripted).Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<JsObject> ScriptSteps(JsObject enemy) =>
        enemy.Get("script") is JsArray s ? s.Items.Cast<JsObject>() : Enumerable.Empty<JsObject>();

    private static IEnumerable<JsObject> Effects(JsObject ability) =>
        ability.Get("effects") is JsArray e ? e.Items.Cast<JsObject>() : Enumerable.Empty<JsObject>();

    /// <summary>Deals damage, or applies an effect to anyone but its caster (a control effect).</summary>
    private static bool IsThreat(JsObject ability)
    {
        var tags = ability.Get("tags") is JsArray t ? t.Items.Select(v => StrOf(v)).ToList() : new List<string?>();
        var selfShape = StrOf((ability.Get("shape") as JsObject)?.Get("kind")) == "self";
        return tags.Contains("control")
            || Effects(ability).Any(op => StrOf(op.Get("op")) == "damage")
            || (!selfShape && Effects(ability).Any(op => StrOf(op.Get("op")) == "applyEffect"));
    }

    private static int CastTicks(JsObject ability) => (int)(IntOf(ability.Get("castTicks")) ?? 0);

    /// <summary>The Pair's mark rules as content/data's run.json pairMarks ships them: read from the file, no second copy of the numbers.</summary>
    private static Keyrun.Sim.Combat.MarkRules ShippedMarks()
    {
        var run = (JsObject)JsValue.Parse(File.ReadAllText(Path.Combine(Root, "game", "content", "data", "run.json")));
        var m = (JsObject)run.Get("pairMarks")!;
        return new Keyrun.Sim.Combat.MarkRules(
            StrOf(m.Get("spreadEffectId"))!,
            IntOf(m.Get("spreadRadiusMm"))!.Value,
            IntOf(m.Get("spreadDamage"))!.Value,
            StrOf(m.Get("stackEffectId"))!,
            IntOf(m.Get("stackRadiusMm"))!.Value,
            IntOf(m.Get("stackDamage"))!.Value);
    }

    /// <summary>A Pair mark ability: an applyEffect of the spread or stack mark. Its floor shape is the ring on the marked player.</summary>
    private static bool IsMarkAbility(JsObject ability, Keyrun.Sim.Combat.MarkRules marks) =>
        Effects(ability).Any(op => StrOf(op.Get("op")) == "applyEffect" && StrOf(op.Get("effectId")) is { } id && (id == marks.SpreadEffectId || id == marks.StackEffectId));

    private static HashSet<string> MarkAbilityIds(Dictionary<string, JsObject> abilities, Keyrun.Sim.Combat.MarkRules marks) =>
        abilities.Where(kv => IsMarkAbility(kv.Value, marks)).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>A telegraph shape the player can read with the colour removed, with a nonzero size.</summary>
    private static string? ShapeProblem(string? shape, long? radius, long? angle, long? length, long? width) => shape switch
    {
        "circle" => radius is > 0 ? null : $"circle radius {radius}",
        "cone" => radius is > 0 && angle is > 0 ? null : $"cone radius {radius} angle1024 {angle}",
        "line" => length is > 0 && width is > 0 ? null : $"line length {length} width {width}",
        _ => $"shape '{shape}' is not circle, cone or line",
    };

    /// <summary>(enemy id, threat ability id) for every threat in every enemy kit; the gap list this test guards.</summary>
    public static IEnumerable<(string Enemy, string Ability)> Threats()
    {
        var abilities = EnemyAbilities();
        foreach (var (enemyId, enemy) in Enemies().OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var id in Kit(enemy))
            {
                if (abilities.TryGetValue(id, out var a) && IsThreat(a) && !AutoAttacks.ContainsKey(id))
                {
                    yield return (enemyId, id);
                }
            }
        }
    }

    [Fact(DisplayName = "Every enemy the dungeon and its optional packs spawn is a content enemy whose kit this test enumerates")]
    public void Every_spawned_enemy_is_enumerated()
    {
        var enemies = Enemies();
        var abilities = EnemyAbilities();
        var spawned = new List<(string Where, string EnemyId)>();
        foreach (var file in ContentRoots.KindFiles(Root, "dungeons"))
        {
            var d = (JsObject)JsValue.Parse(File.ReadAllText(file));
            foreach (var pull in ((JsArray)d.Get("pulls")!).Items.Cast<JsObject>())
            {
                spawned.AddRange(((JsArray)pull.Get("enemies")!).Items.Cast<JsObject>().Select(e => ($"pull {Id(pull)}", StrOf(e.Get("enemyId"))!)));
            }
            spawned.Add(("boss", StrOf(((JsObject)d.Get("boss")!).Get("enemyId"))!));
        }
        var layout = (JsObject)JsValue.Parse(File.ReadAllText(Path.Combine(Root, "game", "content", "data", "layout.json")));
        if (layout.Get("packs") is JsArray packs)
        {
            foreach (var pack in packs.Items.Cast<JsObject>())
            {
                spawned.AddRange(((JsArray)pack.Get("enemies")!).Items.Cast<JsObject>().Select(e => ($"pack {Id(pack)}", StrOf(e.Get("enemyId"))!)));
            }
        }
        Assert.Contains(spawned, s => s.Where.StartsWith("pack ", StringComparison.Ordinal));
        var missing = spawned.Where(s => !enemies.ContainsKey(s.EnemyId)).Select(s => $"{s.Where}: {s.EnemyId}").ToList();
        Assert.True(missing.Count == 0, "spawned enemies with no content definition: " + string.Join(", ", missing));
        var unknown = enemies.Values.SelectMany(e => Kit(e).Select(a => (Enemy: Id(e), Ability: a))).Where(p => !abilities.ContainsKey(p.Ability)).ToList();
        Assert.True(unknown.Count == 0, "kit abilities missing from abilities/enemies.json: " + string.Join(", ", unknown));
        foreach (var expected in new[] { "choir.melee", "choir.lieutenant", "pair.melee", "seed.elite", "hollowWarden.boss", "hollowWarden.twinA", "hollowWarden.twinB" })
        {
            Assert.True(enemies.ContainsKey(expected), $"{expected} is not in content/data/enemies");
        }
    }

    [Fact(DisplayName = "Only auto-attacks are exempt, and every exempt ability really is an instant melee swing")]
    public void Exempt_list_holds_only_auto_attacks()
    {
        var abilities = EnemyAbilities();
        foreach (var id in AutoAttacks.Keys)
        {
            Assert.True(abilities.TryGetValue(id, out var a), $"exempt {id} is not an enemy ability");
            Assert.True(CastTicks(a!) == 0, $"exempt {id} has a {CastTicks(a!)}-tick cast: a cast is not an auto-attack");
            Assert.Equal("target", StrOf(((JsObject)a!.Get("shape")!).Get("kind")));
            Assert.True(IntOf(a.Get("range")) is { } r && r <= AutoAttackMaxRangeMm, $"exempt {id} reaches {IntOf(a.Get("range"))} mm, beyond melee");
            Assert.True(Effects(a).All(op => StrOf(op.Get("op")) == "damage"), $"exempt {id} does more than damage");
            Assert.True(a.Get("tags") is not JsArray t || t.Items.Count == 0, $"exempt {id} carries tags");
        }
    }

    [Fact(DisplayName = "Every enemy ability that deals damage or controls has a cast bar and a scripted floor telegraph with a shape")]
    public void Every_threat_has_a_bar_and_a_shaped_telegraph_in_content()
    {
        var abilities = EnemyAbilities();
        var enemies = Enemies();
        var threats = Threats().ToList();
        Assert.True(threats.Count >= 6, $"only {threats.Count} threats enumerated: the classifier is not reading the content");
        var markIds = MarkAbilityIds(abilities, ShippedMarks());
        var gaps = new List<string>();
        foreach (var (enemyId, abilityId) in threats)
        {
            var a = abilities[abilityId];
            if (CastTicks(a) <= 0)
            {
                gaps.Add($"{enemyId} {abilityId}: castTicks {CastTicks(a)}, no cast bar");
            }
            var steps = ScriptSteps(enemies[enemyId]).Where(s => StrOf(s.Get("cast")) == abilityId).ToList();
            if (steps.Count == 0)
            {
                gaps.Add($"{enemyId} {abilityId}: in the kit but no script step casts it, so it has no telegraph");
            }
            if (markIds.Contains(abilityId))
            {
                // A mark's floor shape is the ring on the marked player (Mark_abilities_have_no_caster_centred_telegraph and
                // Every_mark_is_a_ring_on_the_marked_player_until_it_resolves), not a script telegraph around the caster.
                continue;
            }
            foreach (var s in steps)
            {
                if (s.Get("telegraph") is not JsObject tg)
                {
                    gaps.Add($"{enemyId} {abilityId} (at {s.Get("at")}): no floor telegraph");
                    continue;
                }
                if (ShapeProblem(StrOf(tg.Get("shape")), IntOf(tg.Get("radius")), IntOf(tg.Get("angle1024")), IntOf(tg.Get("length")), IntOf(tg.Get("width"))) is { } bad)
                {
                    gaps.Add($"{enemyId} {abilityId}: telegraph {bad}");
                }
                // TELEGRAPH-TARGET (BIBLE s3: the floor shape is the area that will be hit): seed.lash is a single-target
                // cast at range, so its telegraph is a line from the caster, as long as its range, to its target, never a
                // caster-centred circle.
                if (abilityId == "seed.lash" && (StrOf(tg.Get("shape")) != "line" || IntOf(tg.Get("length")) != IntOf(a.Get("range"))))
                {
                    gaps.Add($"{enemyId} {abilityId}: telegraph must be a line of length range {IntOf(a.Get("range"))} from caster to target, is {StrOf(tg.Get("shape"))} length {IntOf(tg.Get("length"))}");
                }
                if (IntOf(tg.Get("leadTicks")) is not { } lead || lead < MinLeadTicks)
                {
                    gaps.Add($"{enemyId} {abilityId}: leadTicks {IntOf(tg.Get("leadTicks"))} below {MinLeadTicks}");
                }
            }
        }
        Assert.True(gaps.Count == 0, $"{gaps.Count} untelegraphed enemy threats (BIBLE s3):\n" + string.Join("\n", gaps));
    }

    /// <summary>One played run: its events, each tick's pending telegraphs, and each tick's enemy cast bars.</summary>
    private sealed record Played(
        List<SimEvent> Events,
        Dictionary<int, IReadOnlyList<Telegraph>> Pending,
        Dictionary<(int Actor, string Cast, int Lands), int> BarStarts,
        Run Run);

    private static Played Play(Run run, IReadOnlyCollection<int> watched)
    {
        var events = new List<SimEvent>();
        var pending = new Dictionary<int, IReadOnlyList<Telegraph>>();
        var bars = new Dictionary<(int, string, int), int>();
        while (run.State.Tick < Ticks)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            pending[tick] = run.Encounter.Telegraphs;
            foreach (var id in watched)
            {
                if (run.State.Actors.TryGetValue(id, out var a) && a is { CastId: { } c, CastStartTick: { } s, CastEndsTick: { } e })
                {
                    bars.TryAdd((id, c, e), s);
                }
            }
        }
        return new Played(events, pending, bars, run);
    }

    private static IReadOnlyList<ScriptStep> Scaled(JsObject enemy, int key) =>
        Tempo.ScaleScript(EncounterJson.ParseScript(enemy.Get("script")!), key);

    private static RunContent BaseContent(RunSetup setup, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts) =>
        setup.Content with
        {
            Bounds = new Keyrun.Sim.Combat.Bounds(-20000, 20000, -20000, 20000),
            EnemyScripts = scripts,
            BreakBarSources = null,
            KeyLevel = null,
            ParTicks = null,
            Clauses = null,
        };

    private static IReadOnlyList<PartyMemberSpec> Party(long z) => new[]
    {
        RunTestKit.Member(BossTestKit.Warden, "warden", ActorRole.Tank, 0, z, BossTestKit.PartyHealth, 100),
        RunTestKit.Member(BossTestKit.Chanter, "chanter", ActorRole.Healer, 500, z, BossTestKit.PartyHealth, 1),
        RunTestKit.Member(BossTestKit.Cinder, "cinder", ActorRole.Damage, -500, z, BossTestKit.PartyHealth, 100),
    };

    /// <summary>One enemy at the origin, the party 1 m away and unkillable, no party commands.</summary>
    private static Played PlaySolo(string enemyId, JsObject enemy, int key)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(SoloActor, enemyId, new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = BaseContent(setup, new Dictionary<int, IReadOnlyList<ScriptStep>> { [SoloActor] = Scaled(enemy, key) });
        return Play(RunRoot.CreateRun(BossTestKit.Seed, Party(1000), dungeon, content), new[] { SoloActor });
    }

    /// <summary>
    /// The armed Hollow Warden fight (BossTestKit's Twins and Break bar), the boss and both Twins scripted from
    /// content/data/enemies/hollow-warden.json at <paramref name="key"/>. Nobody presses anything, so every Lock fails
    /// and the boss enrages inside the run: the enraged cadence is judged too.
    /// </summary>
    private static Played PlayBoss(Dictionary<string, JsObject> enemies, int key)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("boss", new[] { new PullSpawn(BossTestKit.Boss, "hollowWarden.boss", new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = BaseContent(setup, new Dictionary<int, IReadOnlyList<ScriptStep>> { [BossTestKit.Boss] = Scaled(enemies["hollowWarden.boss"], key) });
        var run = RunRoot.CreateRun(BossTestKit.Seed, Party(1500), dungeon, content);
        var kit = BossTestKit.Config();
        var config = kit with
        {
            Twins = new[]
            {
                kit.Twins[0] with { Script = Scaled(enemies["hollowWarden.twinA"], key) },
                kit.Twins[1] with { Script = Scaled(enemies["hollowWarden.twinB"], key) },
            },
        };
        run = run with { Encounter = BossRules.Arm(run.Encounter, config) };
        return Play(run, new[] { BossTestKit.Boss, BossTestKit.TwinA, BossTestKit.TwinB });
    }

    private static long Int(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : long.MinValue;

    private static long? OptInt(SimEvent e, string key) => e.Get(key) is JsInt v ? v.Value : null;

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>
    /// Every landing of every threat ability by <paramref name="actor"/>: a bar of its content castTicks, a shaped
    /// telegraph resolving on the landing tick drawn no later than the bar's start and at least
    /// <see cref="MinLeadTicks"/> ahead, on the floor for the whole bar. Returns the landings judged.
    /// </summary>
    private static int JudgeLandings(Played p, int actor, string enemyId, int key, IReadOnlyCollection<string> threats, Dictionary<string, JsObject> abilities, List<string> gaps, IReadOnlySet<string> ringTelegraphed)
    {
        var judged = 0;
        foreach (var done in p.Events.Where(e => e.Type == "cast_complete" && Int(e, "actorId") == actor))
        {
            var cast = Str(done, "abilityId")!;
            if (!threats.Contains(cast))
            {
                continue;
            }
            judged++;
            var lands = done.Tick;
            var where = $"key {key} {enemyId} ({actor}) {cast} landing on {lands}";
            var castTicks = CastTicks(abilities[cast]);
            var simCastTicks = p.Run.Content.Abilities[cast].CastTicks;
            if (simCastTicks != castTicks)
            {
                gaps.Add($"{where}: the sim plays a {simCastTicks}-tick cast, content says {castTicks}");
            }
            if (!p.BarStarts.TryGetValue((actor, cast, lands), out var barStart) || lands - barStart <= 0)
            {
                gaps.Add($"{where}: no cast bar (the caster never held {cast} ending on {lands})");
                barStart = lands;
            }
            else if (lands - barStart != castTicks)
            {
                gaps.Add($"{where}: bar ran {lands - barStart} ticks, content castTicks {castTicks}");
            }

            if (ringTelegraphed.Contains(cast))
            {
                // A Pair mark: its telegraph is the ring on the marked player, judged by the mark rule, not a bar-start telegraph.
                continue;
            }
            var tg = p.Events.FirstOrDefault(e => e.Type == "telegraph" && Int(e, "actorId") == actor && Str(e, "cast") == cast && Int(e, "resolvesTick") == lands);
            if (tg is null)
            {
                gaps.Add($"{where}: no floor telegraph resolves on the landing tick");
                continue;
            }
            if (ShapeProblem(Str(tg, "shape"), OptInt(tg, "radius"), OptInt(tg, "angle1024"), OptInt(tg, "length"), OptInt(tg, "width")) is { } bad)
            {
                gaps.Add($"{where}: telegraph {bad}");
            }
            if (tg.Tick > barStart)
            {
                gaps.Add($"{where}: telegraph drawn on {tg.Tick}, after the bar started on {barStart}");
            }
            if (lands - tg.Tick < MinLeadTicks)
            {
                gaps.Add($"{where}: telegraph drawn on {tg.Tick} leads by {lands - tg.Tick}, under {MinLeadTicks}");
            }
            for (var t = Math.Max(barStart, tg.Tick); t < lands; t++)
            {
                if (!p.Pending.TryGetValue(t, out var floor) || !floor.Any(f => f.SourceId == actor && f.AbilityId == cast && f.ResolvesTick == lands))
                {
                    gaps.Add($"{where}: telegraph off the floor on tick {t}, {lands - t} ticks before it lands");
                    break;
                }
            }
        }
        return judged;
    }

    public static IEnumerable<object[]> Keyed() => Keys.Select(k => new object[] { k });

    [Theory(DisplayName = "In play, every damaging or controlling enemy cast shows its telegraph at cast start, 10+ ticks before it lands")]
    [MemberData(nameof(Keyed))]
    public void Every_threat_landing_was_telegraphed_from_cast_start(int key)
    {
        var enemies = Enemies();
        var abilities = EnemyAbilities();
        var threats = Threats().ToList();
        var gaps = new List<string>();
        var landed = new Dictionary<(string, string), int>();
        var markIds = MarkAbilityIds(abilities, ShippedMarks());

        void Judge(Played p, int actor, string enemyId)
        {
            var mine = threats.Where(t => t.Enemy == enemyId).Select(t => t.Ability).ToList();
            JudgeLandings(p, actor, enemyId, key, mine, abilities, gaps, markIds);
            foreach (var a in mine)
            {
                landed[(enemyId, a)] = p.Events.Count(e => e.Type == "cast_complete" && Int(e, "actorId") == actor && Str(e, "abilityId") == a);
            }
        }

        var bossSide = new[] { "hollowWarden.boss", "hollowWarden.twinA", "hollowWarden.twinB" };
        foreach (var (enemyId, enemy) in enemies.Where(kv => !bossSide.Contains(kv.Key) && kv.Value.Get("script") is JsArray))
        {
            Judge(PlaySolo(enemyId, enemy, key), SoloActor, enemyId);
        }
        var boss = PlayBoss(enemies, key);
        Judge(boss, BossTestKit.Boss, "hollowWarden.boss");
        Judge(boss, BossTestKit.TwinA, "hollowWarden.twinA");
        Judge(boss, BossTestKit.TwinB, "hollowWarden.twinB");

        // Not vacuous: every enumerated threat was seen landing at least twice in its run.
        foreach (var (enemyId, ability) in threats)
        {
            var n = landed.TryGetValue((enemyId, ability), out var c) ? c : 0;
            Assert.True(n >= 2, $"key {key}: {enemyId} {ability} landed {n} times in {Ticks} ticks, too few to judge");
        }
        Assert.True(gaps.Count == 0, $"key {key}: {gaps.Count} untelegraphed landings (BIBLE s3):\n" + string.Join("\n", gaps.Take(20)));
    }

    [Fact(DisplayName = "A Pair mark ability draws no caster-centred telegraph: the ring on the marked player is its telegraph")]
    public void Mark_abilities_have_no_caster_centred_telegraph()
    {
        var marks = ShippedMarks();
        var abilities = EnemyAbilities();
        var found = new List<string>();
        var gaps = new List<string>();
        foreach (var (enemyId, enemy) in Enemies().OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (var id in Kit(enemy))
            {
                if (!abilities.TryGetValue(id, out var a) || !IsMarkAbility(a, marks))
                {
                    continue;
                }
                found.Add(id);
                foreach (var step in ScriptSteps(enemy).Where(st => StrOf(st.Get("cast")) == id))
                {
                    if (step.Get("telegraph") is JsObject tg)
                    {
                        gaps.Add($"{enemyId} {id} (at {step.Get("at")}): a {StrOf(tg.Get("shape"))} of radius {IntOf(tg.Get("radius"))} drawn around the caster, where no mark lands");
                    }
                }
            }
        }
        Assert.True(found.Contains("pair.spreadMark") && found.Contains("pair.stackCall"), $"the mark classifier missed the Pair: {string.Join(", ", found)}");
        Assert.True(gaps.Count == 0, $"{gaps.Count} mark casts still draw a caster-centred telegraph (BIBLE s3, s9 The Pair):\n" + string.Join("\n", gaps));
    }

    [Theory(DisplayName = "In play, every Pair mark is a ring on the marked player from the tick it lands until it resolves, at the detonation radius")]
    [MemberData(nameof(Keyed))]
    public void Every_mark_is_a_ring_on_the_marked_player_until_it_resolves(int key)
    {
        var marks = ShippedMarks();
        var enemies = Enemies();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), BossTestKit.Seed);
        var dungeon = new DungeonDef("hollow-vault", new[]
        {
            new PullDef("solo", new[] { new PullSpawn(SoloActor, "pair.melee", new Vec3(0, 0, 0), 512, 100_000_000) }),
        });
        var content = BaseContent(setup, new Dictionary<int, IReadOnlyList<ScriptStep>> { [SoloActor] = Scaled(enemies["pair.melee"], key) }) with { Marks = marks };
        var run = RunRoot.CreateRun(BossTestKit.Seed, Party(1000), dungeon, content);
        var lands = new Dictionary<(int Actor, string Effect, int EndsTick), int>();
        var gaps = new List<string>();
        while (run.State.Tick < Ticks)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            var now = run.State.Tick;
            var rings = Keyrun.Sim.Combat.PairMarks.Rings(run.State, marks);
            foreach (var actor in run.State.Actors.Values.Where(a => a.Alive))
            {
                foreach (var (effectId, kind, radius) in new[]
                {
                    (marks.SpreadEffectId, Keyrun.Sim.Combat.MarkKind.Spread, marks.SpreadRadiusMm),
                    (marks.StackEffectId, Keyrun.Sim.Combat.MarkKind.Stack, marks.StackRadiusMm),
                })
                {
                    var live = actor.Effects.Where(e => e.EffectId == effectId && e.EndsTick >= now).ToList();
                    if (live.Count == 0)
                    {
                        continue;
                    }
                    foreach (var e in live)
                    {
                        lands.TryAdd((actor.Id, effectId, e.EndsTick), now);
                    }
                    var earliest = live.Min(e => e.EndsTick);
                    var ring = rings.FirstOrDefault(r => r.ActorId == actor.Id && r.Kind == kind);
                    if (ring is null || ring.EndsTick != earliest)
                    {
                        gaps.Add($"tick {now}: {effectId} on actor {actor.Id} resolves on {earliest} with no ring until then");
                    }
                    else if (ring.RadiusMm != radius)
                    {
                        gaps.Add($"tick {now}: {effectId} ring on actor {actor.Id} has radius {ring.RadiusMm}, detonation radius {radius}");
                    }
                }
            }
        }
        var spread = lands.Keys.Count(k => k.Effect == marks.SpreadEffectId);
        var stack = lands.Keys.Count(k => k.Effect == marks.StackEffectId);
        Assert.True(spread >= 2 && stack >= 2, $"key {key}: {spread} spread and {stack} stack marks landed in {Ticks} ticks, too few to judge");
        Assert.True(gaps.Count == 0, $"key {key}: {gaps.Count} marks without their ring (BIBLE s3, s9 The Pair):\n" + string.Join("\n", gaps.Take(20)));
    }
}
