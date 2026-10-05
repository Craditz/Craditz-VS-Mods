using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace AnimalicaCore;

/// <summary>
/// Keeps Dead's corpse renderer and animation state compatible with a
/// PlayerModelLib body copied from an Animalica player.
/// </summary>
public sealed class AnimalicaDeadCompatibilitySystem : ModSystem
{
    private const string HarmonyId = "craditz.animalicacore.deadcompat";

    private static readonly FieldInfo? EntityField = AccessTools.Field(typeof(EntityRenderer), "entity");

    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        Type? rendererType = AccessTools.TypeByName("Dead.Rendering.EntityDeadShapeRenderer");
        Type? corpseType = AccessTools.TypeByName("Dead.Entities.EntityPlayerCorpse");
        MethodInfo? rendererMethod = rendererType == null
            ? null
            : AccessTools.Method(rendererType, "GetTextureSource");
        MethodInfo? corpseMethod = corpseType == null
            ? null
            : AccessTools.Method(corpseType, "OnCorpseLoaded");
        MethodInfo? rendererPrefix = AccessTools.Method(
            typeof(AnimalicaDeadCompatibilitySystem),
            nameof(BeforeDeadTextureSource)
        );
        MethodInfo? corpsePostfix = AccessTools.Method(
            typeof(AnimalicaDeadCompatibilitySystem),
            nameof(AfterDeadCorpseLoaded)
        );
        MethodInfo? corpsePosePrefix = AccessTools.Method(
            typeof(AnimalicaDeadCompatibilitySystem),
            nameof(BeforeDeadCorpsePose)
        );

        if (rendererMethod == null || rendererPrefix == null || EntityField == null)
        {
            return;
        }

        harmony = new Harmony(HarmonyId);
        harmony.Patch(rendererMethod, prefix: new HarmonyMethod(rendererPrefix));

        if (corpseMethod != null && corpsePostfix != null)
        {
            harmony.Patch(corpseMethod, postfix: new HarmonyMethod(corpsePostfix));
        }

        if (corpseType != null && corpsePosePrefix != null)
        {
            PatchCorpsePose(harmony, corpseType, "StartDieAnim", corpsePosePrefix);
            PatchCorpsePose(harmony, corpseType, "StartRestAnim", corpsePosePrefix);
            PatchCorpsePose(harmony, corpseType, "StartDeadAnim", corpsePosePrefix);
            PatchCorpsePose(harmony, corpseType, "StartRemainsAnim", corpsePosePrefix);
            PatchCorpsePose(harmony, corpseType, "StartDragAnim", corpsePosePrefix);
        }

        api.Logger.Notification("[AnimalicaCore] Dead corpse texture compatibility enabled.");
    }

    private static void PatchCorpsePose(
        Harmony harmony,
        Type corpseType,
        string methodName,
        MethodInfo prefix
    )
    {
        MethodInfo? method = AccessTools.Method(corpseType, methodName);
        if (method != null)
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
        }
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
    }

    private static void BeforeDeadTextureSource(object __instance)
    {
        if (EntityField?.GetValue(__instance) is not Entity entity
            || !IsCopiedCustomModelCorpse(entity))
        {
            return;
        }

        IDictionary<string, CompositeTexture>? textures = entity.Properties?.Client?.Textures;
        if (textures == null)
        {
            return;
        }

        foreach (CompositeTexture? texture in textures.Values)
        {
            if (texture?.Baked != null)
            {
                continue;
            }

            try
            {
                texture?.Bake(entity.Api.Assets);
            }
            catch
            {
                // Dead still has its own guarded fallback for missing gear.
            }
        }
    }

    private static void AfterDeadCorpseLoaded(Entity __instance)
    {
        if (!IsCopiedCustomModelCorpse(__instance))
        {
            return;
        }

        // Dead can finish loading before PML has inserted the copied model's
        // generated textures. Re-tesselate once after both systems have settled.
        __instance.World.RegisterCallback(_ => __instance.MarkShapeModified(), 100);
    }

    private static bool BeforeDeadCorpsePose(object __instance)
    {
        if (__instance is not Entity entity || !IsCopiedCustomModelCorpse(entity))
        {
            return true;
        }

        if (entity.AnimManager == null)
        {
            return false;
        }

        // Dead's crawl/drag animations are authored for the vanilla Seraph
        // skeleton. Animalica shapes do not contain those animation codes.
        // Keep the compatible death pose active while the corpse itself is
        // moved by Dead's physics and rope mount. This prevents a transition
        // to the custom model's upright bind pose, which is what causes the
        // body to split apart or clip into the ground after dragging.
        StopDeadOnlyAnimations(entity);
        EnsureAnimalicaDeathPose(entity);
        return false;
    }

    private static void StopDeadOnlyAnimations(Entity entity)
    {
        entity.AnimManager.StopAnimation("deadcrawl");
        entity.AnimManager.StopAnimation("deadlimp");
        entity.AnimManager.StopAnimation("draggedback");
        entity.AnimManager.StopAnimation("draggedbackidle");
        entity.AnimManager.StopAnimation("draggedbackremains");
    }

    private static void EnsureAnimalicaDeathPose(Entity entity)
    {
        if (entity.AnimManager.IsAnimationActive("die"))
        {
            return;
        }

        entity.AnimManager.StartAnimation(new AnimationMetaData
        {
            Animation = "die",
            Code = "die",
            AnimationSpeed = 1f,
            EaseInSpeed = 100f,
            EaseOutSpeed = 100f,
            BlendMode = EnumAnimationBlendMode.Average,
            ClientSide = true
        }.Init());
    }

    private static bool IsCopiedCustomModelCorpse(Entity entity)
    {
        if (!string.Equals(entity.Code?.Domain, "dead", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entity.Code?.Path, "playercorpse", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? skinModel = entity.WatchedAttributes?.GetString("skinModel");
        return !string.IsNullOrWhiteSpace(skinModel)
            && !string.Equals(skinModel, "seraph", StringComparison.OrdinalIgnoreCase);
    }
}
