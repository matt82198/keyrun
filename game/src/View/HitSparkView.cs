using Godot;
using Keyrun.Content;

namespace Keyrun.View;

/// <summary>
/// R-ANIM-1: a health drop on any actor (a party seat or an enemy) spawns one small bright burst at the target's chest
/// height. The burst is its own MeshInstance3D child, so the actor body's material and modulate are never touched
/// (ENEMY-BODY-STABLE). It scales up and fades over <see cref="LifeSeconds"/>, then frees itself. The shape (a growing
/// sphere) reads the hit without colour alone. Each drop prints one <c>VIEW hit actor=N tick=T</c> line through GD.Print.
/// </summary>
public static class HitSparkView
{
    /// <summary>How long the burst lives, from spawn to free, in seconds.</summary>
    public const float LifeSeconds = 0.15f;

    private const float MmToM = 0.001f;
    private const float StartScale = 0.3f;
    private const float Radius = 0.14f;

    /// <summary>
    /// Spawns the burst as a child of <paramref name="parent"/> (the actor's view, rooted at its feet) at chest height,
    /// then prints the receipt line for this drop.
    /// </summary>
    public static void Spawn(Node3D parent, int actorId, int simTick)
    {
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.95f, 0.6f, 1f),
        };
        var spark = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2f },
            MaterialOverride = material,
            Position = new Vector3(0f, (float)PartyFade.BodyCentreMm * MmToM, 0f),
            Scale = Vector3.One * StartScale,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(spark);

        var tween = spark.CreateTween();
        tween.TweenProperty(spark, "scale", Vector3.One, LifeSeconds);
        tween.Parallel().TweenProperty(material, "albedo_color", new Color(1f, 0.95f, 0.6f, 0f), LifeSeconds);
        tween.TweenCallback(Callable.From(spark.QueueFree));

        GD.Print(System.FormattableString.Invariant($"VIEW hit actor={actorId} tick={simTick}"));
    }
}
