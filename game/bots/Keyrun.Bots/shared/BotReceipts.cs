using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// One issued bot command as a receipt line (G2.4, R-EVENTSTORE-2): the tick it was issued for, the bot seat (actor id),
/// the seat's class id, the ability (empty for a move, dash, target or stop command), the target (null when the command
/// names none) and the K1 reason phrase.
/// </summary>
/// <param name="Tick">The pre-step run tick the command addresses.</param>
/// <param name="Seat">The bot seat, the actor id.</param>
/// <param name="Class">The seat's class id.</param>
/// <param name="Ability">The ability id of a cast; empty otherwise.</param>
/// <param name="Target">The target actor id, or null.</param>
/// <param name="Reason">The K1 reason phrase for the command's rule id, or empty when the command was never tagged.</param>
public sealed record BotReceipt(
    [property: JsonPropertyName("tick")] int Tick,
    [property: JsonPropertyName("seat")] int Seat,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("ability")] string Ability,
    [property: JsonPropertyName("target")] int? Target,
    [property: JsonPropertyName("reason")] string Reason)
{
    /// <summary>Builds the receipt of one issued command.</summary>
    /// <param name="tick">The pre-step run tick.</param>
    /// <param name="classId">The seat's class id.</param>
    /// <param name="command">The command the bot issued.</param>
    /// <param name="reason">The K1 reason phrase of the command.</param>
    /// <returns>The receipt.</returns>
    public static BotReceipt Of(int tick, string classId, Command command, string reason)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (ability, target) = command switch
        {
            CastCommand cast => (cast.AbilityId, cast.TargetId),
            TargetCommand targeted => (string.Empty, targeted.TargetId),
            _ => (string.Empty, (int?)null),
        };
        return new BotReceipt(tick, command.ActorId, classId ?? string.Empty, ability, target, reason ?? string.Empty);
    }

    /// <summary>The receipt as one compact JSON object, without a line break.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJsonLine() => JsonSerializer.Serialize(this);
}

/// <summary>Receives one <see cref="BotReceipt"/> per issued bot command, in issue order (G2.4).</summary>
public interface IBotReceiptSink
{
    /// <summary>Records one receipt.</summary>
    /// <param name="receipt">The receipt.</param>
    void Record(BotReceipt receipt);
}

/// <summary>The default sink: drops every receipt, so a run with no sink attached is unchanged.</summary>
public sealed class NullBotReceiptSink : IBotReceiptSink
{
    /// <summary>The shared instance.</summary>
    public static NullBotReceiptSink Instance { get; } = new();

    /// <inheritdoc />
    public void Record(BotReceipt receipt)
    {
    }
}

/// <summary>
/// Appends each receipt as one JSON line to a file. The file is opened in append mode: an existing file keeps every line
/// it already has, and the sink never rewrites or truncates it. Each line is flushed as it is written.
/// </summary>
public sealed class JsonlBotReceiptSink : IBotReceiptSink, IDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>Opens <paramref name="path"/> for appending, creating it when it does not exist.</summary>
    /// <param name="path">The receipt file.</param>
    public JsonlBotReceiptSink(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    /// <inheritdoc />
    public void Record(BotReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _writer.Write(receipt.ToJsonLine());
        _writer.Write('\n');
    }

    /// <summary>Closes the file.</summary>
    public void Dispose() => _writer.Dispose();
}
