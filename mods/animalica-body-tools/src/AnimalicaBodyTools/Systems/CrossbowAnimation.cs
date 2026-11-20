using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaBodyTools.Systems;

// Installed after the client's wearable behaviors so the final, possibly prefixed joint names are available.
public sealed class CrossbowAnimation : EntityBehavior
{
    private const string Draw = "abt-crossbow-draw";
    private const string Release = "abt-crossbow-release";
    private const string Duck = "abt-crossbow-duck";
    private readonly EntityPlayer wearer;
    private int lastShot;
    private bool drawing;
    private IAnimator? previousAnimator;

    private CrossbowAnimation(EntityPlayer player) : base(player)
    {
        wearer = player;
        lastShot = player.WatchedAttributes.GetInt(RangedCrossbowSystem.ShotCounter);
    }

    public override string PropertyName() => "animalicaCrossbowAnimation";

    internal static void Attach(Entity entity)
    {
        if (entity is EntityPlayer player && player.GetBehavior<CrossbowAnimation>() == null)
        {
            player.AddBehavior(new CrossbowAnimation(player));
            player.MarkShapeModified();
        }
    }

    public override void OnTesselation(ref Shape shape, string path, ref bool cloned, ref string[] disabled)
    {
        var stack = RangedCrossbowSystem.Equipped(wearer.Player)?.Itemstack;
        if (stack == null || shape?.Animations == null) return;
        var wing = shape.FindElement("*abt-wing-left");
        if (wing?.Name == null || shape.Animations.Any(a => a.Code == Draw)) return;
        if (!cloned) { shape = shape.Clone(); cloned = true; }
        string prefix = wing.Name[..^"abt-wing-left".Length];
        string pattern = stack.ItemAttributes["crossbowPattern"].AsString("")!;
        var asset = entity.Api.Assets.TryGet(new AssetLocation("animalicabodytools", $"config/ranged/{pattern}-motion.json"));
        if (asset == null) return;
        // Each animator resolves keyframe elements against its own copy of the player shape.
        var clips = asset.ToObject<Animation[]>();
        var skin = wearer.GetBehavior("skinnableplayercustommodel");
        var model = skin?.GetType().GetProperty("CurrentModelCode")?.GetValue(skin) as string;
        if (entity.Api.ModLoader.GetModSystem<RangedCrossbowSystem>().TryGetBackFit(entity.Api, model, pattern, out var fit))
            FitBackMount(shape, stack, prefix, model!, fit);
        FitToWornShape(shape, prefix, clips);
        foreach (var clip in clips)
        {
            clip.Version = shape.Animations.FirstOrDefault()?.Version ?? 1;
            if (prefix.Length == 0) continue;
            foreach (var frame in clip.KeyFrames)
                frame.Elements = frame.Elements.ToDictionary(e => prefix + e.Key, e => e.Value);
        }
        shape.Animations = [..shape.Animations, ..clips];
        if (entity.Api.ModLoader.GetModSystem<RangedCrossbowSystem>().UsesDuck(entity.Api, model, pattern))
        {
            var location = new AssetLocation("animalicabodytools", "config/ranged/ducks/" + new AssetLocation(model!).Path + ".json");
            var duck = entity.Api.Assets.Get(location).ToObject<Animation>();
            duck.Version = clips[0].Version;
            shape.Animations = [..shape.Animations, duck];
        }
    }

    // Bears face +Z in LowerTorso space. The birds face -X and use UpperTorso as their neck.
    private void FitBackMount(Shape shape, ItemStack stack, string prefix, string model, RangedCrossbowSystem.BackFit fit)
    {
        // The character editor can render the new rig before the server echoes skinModel.
        bool bear = model is "feralbear:blackbear" or "feralbear:brownbear" or "feralbear:polarbear"
            or "feralbear:giantpanda" or "feralbear:sunbear";
        bool bird = model is "feralpack:chicken-hen" or "feralpack:chicken-rooster";

        var back = shape.GetElementByName(bear || bird ? "LowerTorso" : "UpperTorso");
        var stock = shape.GetElementByName(prefix + "abt-oak-stock");
        var shoulder = stock?.ParentElement;
        if (back?.Children == null || shoulder?.Children == null || back.From == null || back.To == null
            || stock?.From == null || stock.To == null) return;
        var parts = shoulder.Children.Where(e => e.Name?.StartsWith(prefix + "abt-", StringComparison.Ordinal) == true).ToArray();
        double width = back.To[0] - back.From[0], height = back.To[1] - back.From[1], depth = back.To[2] - back.From[2];
        if (!bear && !bird)
        {
            // Center the stock on the torso. Move roots and pivots together so the draw stays aligned.
            double z = depth / 2 - (stock.From[2] + stock.To[2]) / 2;
            foreach (var part in parts)
            {
                foreach (var point in new[] { part.From, part.To, part.RotationOrigin })
                {
                    if (point == null) continue;
                    point[1] -= height * fit.Drop;
                    point[2] += z;
                }
                part.inverseModelTransform = null;
            }
            if (fit.Pitch > 0)
            {
                // Rotate the whole fitted assembly about the rear of the stock.
                var mountPivot = new ShapeElement {
                    Name = prefix + "abt-inclined-mount", From = [0, 0, 0], To = [0, 0, 0],
                    RotationOrigin = [stock.To[0], stock.From[1], depth / 2], RotationZ = -fit.Pitch,
                    FacesResolved = new ShapeElementFace[6], Children = parts, ParentElement = shoulder
                };
                shoulder.Children = [..shoulder.Children.Except(parts), mountPivot];
                foreach (var part in parts) part.ParentElement = mountPivot;
            }
            return;
        }
        var api = (Vintagestory.API.Client.ICoreClientAPI)wearer.Api;
        var original = api.TesselatorManager.GetCachedShape(stack.Item.Shape.Base)?.GetElementByName("abt-oak-stock");
        if (original?.From == null || original.To == null) return;

        double[] anchor = bear ? [width * .50, height * (1.055 - fit.Drop), depth * .43]
            : [width * (model == "feralpack:chicken-hen" ? .56 : .50), height * (1.08 - fit.Drop), depth * .50];
        double unitScale = bear ? depth * .12 : width * .085;
        double[] center = [(stock.From[0] + stock.To[0]) / 2, stock.From[1], (stock.From[2] + stock.To[2]) / 2];
        double[] scale = new double[3];
        for (int i = 0; i < 3; i++)
            scale[i] = unitScale * (original.To[i] - original.From[i]) / (stock.To[i] - stock.From[i]);

        var mount = new ShapeElement {
            Name = prefix + "abt-back-mount", From = anchor, To = (double[])anchor.Clone(),
            RotationOrigin = (double[])anchor.Clone(), RotationY = bear ? 90 : 0,
            FacesResolved = new ShapeElementFace[6], Children = parts, ParentElement = back
        };
        shoulder.Children = shoulder.Children.Except(parts).ToArray();
        back.Children = [..back.Children, mount];
        foreach (var part in parts)
        {
            part.ParentElement = mount;
            FitGeometry(part, scale, center);
        }
    }

    private static void FitGeometry(ShapeElement part, double[] scale, double[]? center)
    {
        // Only the roots are offset. Nested wing, string and arrow pivots are already parent-local.
        foreach (var point in new[] { part.From, part.To, part.RotationOrigin })
        {
            if (point == null) continue;
            for (int i = 0; i < 3; i++) point[i] = (point[i] - (center?[i] ?? 0)) * scale[i];
        }
        part.inverseModelTransform = null;
        if (part.Children != null)
            foreach (var child in part.Children) FitGeometry(child, scale, null);
    } 

    // PlayerModelLib fits clothing to each torso by scaling X, Y and Z independently.
    // Derive the cord angles and nock position from the fitted joints, in torso-local units.
    private static void FitToWornShape(Shape shape, string prefix, Animation[] clips)
    {
        var right = shape.GetElementByName(prefix + "abt-wing-right");
        var hinge = shape.GetElementByName(prefix + "abt-cord-hinge-right");
        var arrow = shape.GetElementByName(prefix + "abt-arrow");
        if (right?.From is not { Length: >= 3 } || hinge?.From is not { Length: >= 3 } || arrow?.From is not { Length: >= 3 }) return;
        double cordLength = right.From[2] + hinge.From[2] - arrow.From[2];
        if (cordLength <= 0) return;

        (double Angle, double NockX) Cord(double degrees)
        {
            double radians = degrees * Math.PI / 180;
            double x = hinge.From[0] * Math.Cos(radians) + hinge.From[2] * Math.Sin(radians);
            double z = right.From[2] - arrow.From[2] + hinge.From[2] * Math.Cos(radians) - hinge.From[0] * Math.Sin(radians);
            double angle = Math.Acos(Math.Clamp(z / cordLength, -1, 1));
            return (angle * 180 / Math.PI, right.From[0] + x + cordLength * Math.Sin(angle));
        }

        var seated = clips.First(clip => clip.Code == Draw).KeyFrames[^1].Elements;
        double fullDraw = seated["abt-wing-right"].RotationY!.Value;
        double templateX = seated["abt-arrow"].OffsetX!.Value;
        double templateY = seated["abt-arrow"].OffsetY!.Value;
        double travelX = Cord(fullDraw).NockX - arrow.From[0];
        double travelY = right.From[1] + hinge.From[1] - arrow.From[1];
        foreach (var clip in clips)
        foreach (var frame in clip.KeyFrames)
        {
            var parts = frame.Elements;
            double bend = parts["abt-wing-right"].RotationY!.Value;
            double turn = bend + Cord(bend).Angle;
            parts["abt-cord-hinge-right"].RotationY = -turn;
            parts["abt-cord-hinge-left"].RotationY = turn;
            var shaft = parts["abt-arrow"];
            shaft.OffsetX = shaft.OffsetX / templateX * travelX;
            shaft.OffsetY = shaft.OffsetY / templateY * travelY;
        }
    }

    public override void OnGameTick(float dt)
    {
        var attrs = wearer.WatchedAttributes;
        int phase = attrs.GetInt(RangedCrossbowSystem.DrawPhase);
        int shot = attrs.GetInt(RangedCrossbowSystem.ShotCounter);
        bool fired = shot != lastShot;
        lastShot = shot;
        var manager = wearer.TpAnimManager;
        // Start at the tucked pose on the shot event, then let the animal lift its head.
        if (fired && manager?.Animator?.GetAnimationState(Duck) != null)
            StartDuck(manager);
        if (phase == 0 && !drawing && !fired) return;
        var animator = manager?.Animator;
        if (animator?.GetAnimationState(Draw) is not { } draw) return;

        if (phase == 0)
        {
            manager!.StopAnimation(Draw);
            if (fired) manager.StartAnimationBase(Meta(Release, 1, 0));
            drawing = false;
            return;
        }

        if (!drawing || previousAnimator != animator || !manager!.ActiveAnimationsByAnimCode.ContainsKey(Draw))
        {
            var stack = RangedCrossbowSystem.Equipped(wearer.Player)?.Itemstack;
            if (stack == null) return;
            float speed = 2000f / stack.ItemAttributes["reloadMs"].AsInt();
            manager!.StopAnimation(Release);
            manager.StartAnimationBase(Meta(Draw, speed, (phase-1)*6));
            drawing = true;
            previousAnimator = animator;
        }
        else if (phase == 11 && draw.CurrentFrame < 60)
        {
            draw.CurrentFrame = 60;
        }
    }

    private static void StartDuck(PlayerAnimationManager manager)
    {
        var meta = Meta(Duck, 1, 4);
        // On the poultry rig UpperTorso is the neck. Its walk cycle swings it back by up to 42 degrees.
        if (manager.Animator?.GetAnimationState(Duck)?.Animation.KeyFrames[0].Elements.ContainsKey("UpperTorso") == true)
        {
            foreach (string joint in new[] { "UpperTorso", "Head" })
            {
                meta.ElementBlendMode[joint] = EnumAnimationBlendMode.AddAverage;
                meta.ElementWeight[joint] = 32;
            }
        }
        manager.StartAnimationBase(meta);
    }

    private static AnimationMetaData Meta(string code, float speed, float start) => new AnimationMetaData
    {
        Code = code, Animation = code, AnimationSpeed = speed, StartFrameOnce = start, ClientSide = true,
        BlendMode = EnumAnimationBlendMode.Add, EaseInSpeed = 1000, EaseOutSpeed = 1000
    }.Init();
}
