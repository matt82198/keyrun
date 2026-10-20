using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>One coloured segment of a Window ring's band.</summary>
/// <param name="ClassId">The class whose Payoff can close the Window.</param>
/// <param name="Color">Segment colour, 0xRRGGBB (<see cref="WindowBandRule.ClassColor"/>).</param>
/// <param name="StartPermille">Where the segment starts along the ring, 0..1000.</param>
/// <param name="EndPermille">Where it ends, 0..1000 (the next segment's start).</param>
public sealed record WindowBandSegment(string ClassId, int Color, int StartPermille, int EndPermille);

/// <summary>
/// The view-only colour band of a Window ring (BIBLE s4: "The Window ring shows which classes' Payoffs can close it by
/// colour band"). A class can close a Window when it is not the Window's setup class (the sim denies a self-combo), it
/// owns at least one Payoff ability (an ability carrying a <see cref="PayoffOp"/> or facet <see cref="ComboOp"/>), and
/// the combo table holds its (setupClass, payoffClass) cell: the exact row for a target/self Window, the <c>"*"</c>
/// field row for a floor Window. These are the same cells Keyrun.Sim's Payoff.TryClosePayoff and
/// TryClosePayoffOnFloor look up (HostRunSetup.ToComboTables builds them from the same rows). Read-only over content;
/// pure and integer-only (game/CLAUDE.md purity rule). src/View/WindowRingView.cs draws one segment per closer.
/// </summary>
public static class WindowBandRule
{
    /// <summary>The field-row payoff class: any class other than the setup class.</summary>
    public const string AnyClass = "*";

    /// <summary>Colour for a class this rule has no colour for.</summary>
    public const int UnknownClassColor = 0xB0B0B0;

    /// <summary>The colour a class's segment draws in: Warden steel blue, Cinder ember orange, Chanter hymn gold.</summary>
    /// <param name="classId">Class id.</param>
    /// <returns>0xRRGGBB.</returns>
    public static int ClassColor(string classId) => classId switch
    {
        "warden" => 0x4A9EFF,
        "cinder" => 0xFF6A2A,
        "chanter" => 0xF2D45C,
        _ => UnknownClassColor,
    };

    /// <summary>The classes, in content class order, whose Payoffs can close a Window opened by <paramref name="setupClass"/>.</summary>
    /// <param name="registry">Loaded content.</param>
    /// <param name="setupClass">The Window's setup class.</param>
    /// <param name="floor">True for a floor-patch Window (field row), false for a target/self Window (exact row).</param>
    /// <returns>The closing class ids.</returns>
    public static IReadOnlyList<string> ClosingClasses(ContentRegistry registry, string setupClass, bool floor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var payoffClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClassDef c in registry.Classes.Values)
        {
            if (SlotIds(c.Abilities).Any(id => registry.Abilities.TryGetValue(id, out AbilityDef? a) && IsPayoff(a)))
            {
                payoffClasses.Add(c.Id);
            }
        }
        return ClosingClasses(
            registry.Classes.Keys.ToList(),
            payoffClasses,
            registry.Combos.Values.Select(e => (e.SetupClass, e.PayoffClass)),
            setupClass,
            floor);
    }

    /// <summary>The closing-class rule over plain inputs.</summary>
    /// <param name="classIds">Playable classes, in display order.</param>
    /// <param name="payoffClasses">Classes that own at least one Payoff ability.</param>
    /// <param name="comboCells">Combo rows as (setupClass, payoffClass); payoffClass <see cref="AnyClass"/> is a field row.</param>
    /// <param name="setupClass">The Window's setup class.</param>
    /// <param name="floor">True for a floor-patch Window.</param>
    /// <returns>The closing class ids, in <paramref name="classIds"/> order.</returns>
    public static IReadOnlyList<string> ClosingClasses(
        IReadOnlyList<string> classIds,
        IReadOnlySet<string> payoffClasses,
        IEnumerable<(string SetupClass, string PayoffClass)> comboCells,
        string setupClass,
        bool floor)
    {
        ArgumentNullException.ThrowIfNull(classIds);
        ArgumentNullException.ThrowIfNull(payoffClasses);
        ArgumentNullException.ThrowIfNull(comboCells);
        var cells = comboCells.Where(c => string.Equals(c.SetupClass, setupClass, StringComparison.Ordinal)).ToList();
        bool field = cells.Any(c => c.PayoffClass == AnyClass);
        return classIds
            .Where(c => !string.Equals(c, setupClass, StringComparison.Ordinal) && payoffClasses.Contains(c))
            .Where(c => floor ? field : cells.Any(cell => string.Equals(cell.PayoffClass, c, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>The band: one equal, contiguous segment per closer covering the whole ring (empty for no closer).</summary>
    /// <param name="closers">Closing class ids, in order.</param>
    /// <returns>The segments, start to end.</returns>
    public static IReadOnlyList<WindowBandSegment> Band(IReadOnlyList<string> closers)
    {
        ArgumentNullException.ThrowIfNull(closers);
        var band = new List<WindowBandSegment>(closers.Count);
        for (int i = 0; i < closers.Count; i++)
        {
            band.Add(new WindowBandSegment(closers[i], ClassColor(closers[i]), i * 1000 / closers.Count, (i + 1) * 1000 / closers.Count));
        }
        return band;
    }

    private static bool IsPayoff(AbilityDef a) =>
        a.Effects.Any(op => op is PayoffOp or ComboOp)
        || (a.Variants?.Values.Any(v => v.Any(op => op is PayoffOp or ComboOp)) ?? false);

    private static IEnumerable<string> SlotIds(ClassAbilitySlots s) =>
        new[] { s.Rot1, s.Rot2, s.Rot3, s.Rot4, s.Rot5, s.Interrupt, s.Defensive, s.Utility, s.Movement };
}
