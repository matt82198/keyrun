using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>Builders shared by the encounter tests (the TS tests' local actor/state helpers).</summary>
internal static class EncounterTestKit
{
    /// <summary>An actor shaped like the TS tests' literal ActorState (enemy: classId boss, role none).</summary>
    public static ActorState Actor(int id, ActorKind kind, Vec3? pos = null, bool alive = true, long health = 1000) => new()
    {
        Id = id,
        Kind = kind,
        ClassId = kind == ActorKind.Enemy ? "boss" : "warden",
        Role = kind == ActorKind.Enemy ? ActorRole.None : ActorRole.Tank,
        Pos = pos ?? Vec3.Zero,
        Yaw = 0,
        Health = health,
        MaxHealth = 1000,
        Resource = 0,
        MaxResource = 0,
        Alive = alive,
    };

    /// <summary>A SimState at <paramref name="tick"/> with prng [1,2,3,4] (the TS tests' literal).</summary>
    public static SimState State(int tick, params ActorState[] actors) => State(tick, new PrngState(1, 2, 3, 4), actors);

    /// <summary>A SimState at <paramref name="tick"/> with an explicit prng.</summary>
    public static SimState State(int tick, PrngState prng, params ActorState[] actors)
    {
        var s = new SimState { Tick = tick, Seed = 1, Prng = prng, RunTimerTicks = tick, Phase = "combat" };
        foreach (var a in actors)
        {
            s.Actors[a.Id] = a;
        }
        return s;
    }

    /// <summary>An event parsed from a JSON object literal, for equality via canonical JSON.</summary>
    public static string Ev(string json) => SimEvent.FromJson(JsonDocument.Parse(json).RootElement).ToCanonicalJson();

    /// <summary>Canonical JSON of each event.</summary>
    public static List<string> Json(IEnumerable<SimEvent> events) => events.Select(e => e.ToCanonicalJson()).ToList();

    /// <summary>An emit sink.</summary>
    public static Action<SimEvent> Sink(List<SimEvent> into) => into.Add;

    /// <summary>A scripts map with one actor.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts(int actorId, params ScriptStep[] steps) =>
        new Dictionary<int, IReadOnlyList<ScriptStep>> { [actorId] = steps };

    /// <summary>The base encounter literal the TS tests use (pullIndex 0, given slack, key 1, no clause).</summary>
    public static EncounterState BaseEncounter(int slack = 0, int par = 0, int keyLevel = 1) =>
        EncounterState.Create(keyLevel, par, Clauses.DefaultEncounterRules(keyLevel)) with { SlackTicks = slack };

    /// <summary>
    /// Repo root (the directory holding content/data and game/content/data), found by walking up from the oracle
    /// fixtures through <see cref="ContentRoots.RepoRootAbove"/>, so a game/content/data/enemies folder cannot capture it.
    /// </summary>
    public static string RepoRoot() => ContentRoots.RepoRootAbove(OracleFixtures.Directory);

    /// <summary>A setup fixture parsed as a JsObject.</summary>
    public static JsObject Setup(string fileName) =>
        (JsObject)JsValue.Parse(File.ReadAllText(OracleFixtures.PathOf(fileName)));
}
