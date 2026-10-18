using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>bots/src/shared/windows.ts OpenWindow: an open Window effect on an alive enemy.</summary>
/// <param name="TargetId">Carrier enemy.</param>
/// <param name="WindowId">Window id (the effect id after "window:").</param>
/// <param name="SetupClass">Class of the actor that opened it (its <c>sourceId</c>), or null if that actor is gone.</param>
/// <param name="EndsTick">Tick the Window closes.</param>
public sealed record OpenWindow(int TargetId, string WindowId, string? SetupClass, int EndsTick);

/// <summary>
/// Port of bots/src/shared/windows.ts: finds open Windows on enemies and enforces The Lock (a class never closes a
/// Window its own class opened). A Window is an effect whose id starts with <see cref="Pings.WindowEffectPrefix"/>;
/// its setup class is the classId of the effect's source actor.
/// </summary>
public static class BotWindows
{
    /// <summary>windows.ts canClose: an unknown setup class, or a different class, may close it.</summary>
    /// <param name="windowSetupClass">Opening class, or null.</param>
    /// <param name="payoffClass">Closing class.</param>
    /// <returns>True when the Lock allows it.</returns>
    public static bool CanClose(string? windowSetupClass, string payoffClass) =>
        windowSetupClass is null || !string.Equals(windowSetupClass, payoffClass, StringComparison.Ordinal);

    /// <summary>windows.ts openWindowsOn: every still-open Window effect on an alive enemy, in actor then effect order.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tick">Current tick.</param>
    /// <returns>Open Windows.</returns>
    public static List<OpenWindow> OpenWindowsOn(SortedDictionary<int, ActorState> actors, int tick)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var output = new List<OpenWindow>();
        foreach (var actor in actors.Values)
        {
            if (actor.Kind != ActorKind.Enemy || !actor.Alive)
            {
                continue;
            }
            foreach (var effect in actor.Effects)
            {
                if (!effect.EffectId.StartsWith(Pings.WindowEffectPrefix, StringComparison.Ordinal) || effect.EndsTick <= tick)
                {
                    continue;
                }
                var setupClass = actors.TryGetValue(effect.SourceId, out var source) ? source.ClassId : null;
                output.Add(new OpenWindow(actor.Id, effect.EffectId.Substring(Pings.WindowEffectPrefix.Length), setupClass, effect.EndsTick));
            }
        }
        return output;
    }

    /// <summary>windows.ts findClosableWindow: the earliest-expiring Window <paramref name="payoffClass"/> may close, ties to the lowest target id.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="payoffClass">Closing class.</param>
    /// <returns>The Window, or null.</returns>
    public static OpenWindow? FindClosableWindow(SortedDictionary<int, ActorState> actors, int tick, string payoffClass) =>
        OpenWindowsOn(actors, tick)
            .Where(w => CanClose(w.SetupClass, payoffClass))
            .OrderBy(w => w.EndsTick)
            .ThenBy(w => w.TargetId)
            .FirstOrDefault();
}
