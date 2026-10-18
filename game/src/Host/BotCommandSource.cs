using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// The bot seats' command source (lane 3H): the C# port of the frozen TS client's bot half of
/// client/src/sim-host/sim-runtime.ts step() with bot-commands.ts computeBotCommands. Every tick it builds one
/// <see cref="BotView"/> from the pre-step run (encounter telegraphs through <see cref="BotViews.ToBotTelegraph"/>,
/// the content kits through <see cref="KitRegistry"/>) and calls <see cref="Bot.RunBots"/> once for every bot seat,
/// threading the memories and the bot PRNG from tick to tick. Godot-free; the bot code stays in Keyrun.Bots and out
/// of Keyrun.Sim.
/// </summary>
/// <remarks>
/// Determinism: the bots are pure functions of the view, their memory and the PRNG, and the PRNG is seeded from the
/// run seed (sim-runtime.ts <c>seedPrng(SEED)</c>), so the same seed yields the same command stream.
/// <see cref="Fingerprint"/> is a running FNV-1a 64 hash over every command issued, for the headless proof line.
/// </remarks>
public sealed class BotCommandSource : ICommandSource
{
    /// <summary>sim-runtime.ts constructor: every bot starts from <c>defaultMemory(80)</c>.</summary>
    public const int DefaultSkillDial = 80;

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly Dictionary<int, IBot> _bots;
    private readonly KitRegistry _kits;
    private readonly int _skillDial;
    private readonly long _seed;
    private IReadOnlyDictionary<int, BotMemory> _memories = new SortedDictionary<int, BotMemory>();
    private BotPrngState _prng;

    /// <summary>Builds the source for <paramref name="botActorIds"/>, each a <see cref="Bot.MakeBot"/> over <see cref="RoleSet.Default"/>.</summary>
    /// <param name="botActorIds">The bot seats (the party minus the human).</param>
    /// <param name="kits">The content-backed kits (<see cref="KitRegistry.From"/>).</param>
    /// <param name="seed">The run seed; the bot PRNG is seeded from it.</param>
    /// <param name="skillDial">Every bot's skill dial (0..100).</param>
    public BotCommandSource(IEnumerable<int> botActorIds, KitRegistry kits, long seed, int skillDial = DefaultSkillDial)
    {
        ArgumentNullException.ThrowIfNull(botActorIds);
        ArgumentNullException.ThrowIfNull(kits);
        if (skillDial is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(skillDial), skillDial, "skill dial is 0..100");
        }
        var bot = Bot.MakeBot(RoleSet.Default);
        _bots = botActorIds.Distinct().ToDictionary(id => id, _ => bot);
        _kits = kits;
        _skillDial = skillDial;
        _seed = seed;
        Reset();
    }

    /// <summary>The bot seats driven, ascending.</summary>
    public IReadOnlyList<int> BotActorIds => _bots.Keys.OrderBy(id => id).ToList();

    /// <summary>Commands issued since the last <see cref="Reset"/>.</summary>
    public long CommandCount { get; private set; }

    /// <summary>FNV-1a 64 over every command issued since the last <see cref="Reset"/>, in issue order.</summary>
    public ulong Fingerprint { get; private set; } = FnvOffset;

    /// <summary>The headless proof line: "BOTS done commands=N hash=0123456789abcdef".</summary>
    /// <returns>The line.</returns>
    public string ProofLine() => FormattableString.Invariant($"BOTS done commands={CommandCount} hash={Fingerprint:x16}");

    /// <summary>A fresh run: default memories, the PRNG reseeded from the run seed, the fingerprint cleared.</summary>
    public void Reset()
    {
        var memories = new SortedDictionary<int, BotMemory>();
        foreach (var id in _bots.Keys)
        {
            memories[id] = BotMemory.Default(_skillDial);
        }
        _memories = memories;
        _prng = Skill.SeedPrng(_seed);
        CommandCount = 0;
        Fingerprint = FnvOffset;
    }

    /// <inheritdoc />
    public void Sample(int tick, Run run, List<Command> into)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(into);
        if (_bots.Count == 0)
        {
            return;
        }
        var view = BotViews.BuildBotView(
            run.State,
            run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph),
            ClassKitView.Empty,
            _kits);
        var batch = Bot.RunBots(view, _bots, _memories, _prng);
        _memories = batch.Memories;
        _prng = batch.Prng;
        foreach (var command in batch.Commands)
        {
            into.Add(command);
            Absorb(command);
        }
    }

    private void Absorb(Command command)
    {
        CommandCount++;
        var hash = Fingerprint;
        foreach (var b in Encoding.UTF8.GetBytes(command.ToString() ?? string.Empty))
        {
            hash = unchecked((hash ^ b) * FnvPrime);
        }
        Fingerprint = unchecked((hash ^ 0x0A) * FnvPrime);
    }
}
