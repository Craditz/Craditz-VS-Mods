using System;
using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimalicaCore;

/// <summary>
/// Suppresses Animalica animal arm/clothing meshes in ordinary first person and
/// provides a stable camera-space held-item base for FotSA models.
/// </summary>
public sealed class AnimalicaFirstPersonHandsSystem : ModSystem
{
    private const string HarmonyId = "craditz.animalicacore.firstpersonhands";
    private const float FirstPersonItemCameraYOffset = -0.10f;
    private const float FirstPersonItemCameraZOffset = -1.00f;
    private static readonly FieldInfo? EntityField = AccessTools.Field(typeof(EntityRenderer), "entity");
    private static readonly FieldInfo? RenderModeField = AccessTools.Field(typeof(EntityPlayerShapeRenderer), "renderMode");
    private static readonly MethodInfo? RenderItemMethod = AccessTools.Method(typeof(EntityShapeRenderer), "RenderItem");
    private static readonly string[] AnimalModelTokens =
    {
        "feralfox", "feralwolf", "spottedhyena", "feralhyena",
        "wolftaming-hunting", "wolftaming-shepherd", "wolftaming-corgi",
        "blackbear", "brownbear", "polarbear", "giantpanda", "sunbear",
        "feraldeer-caribou", "deer-", "gazelle-thomson", "goat-", "pig-", "sheep-",
        "feralfish", "chicken-", "hare-", "feralraccoon", "pantherinae-",
        "cats-adult-cat", "cats-kitten",
        "caninae-", "capreolinae-", "casuariidae-", "machairodontinae-",
        "vombatidae-", "elephantidae-", "felinae-", "spheniscidae-",
        "dinornithidae-", "manidae-", "rhinocerotidae-", "viverridae-",
        "bovinae-", "chelonioidea-", "thylacinidae-", "iniidae-",
        "cervinae-", "meiolaniidae-", "sirenia-",
        "dinosaur-", "hieronymus-", "feverstone-", "jimothy-",
        "feraldrifter", "feralshiver", "feralbowtorn", "ferallocust"
    };
    private static readonly string[] FotsaModelTokens =
    {
        "pantherinae-", "sirenia-", "caninae-", "capreolinae-",
        "casuariidae-", "machairodontinae-", "vombatidae-", "elephantidae-",
        "felinae-", "spheniscidae-", "dinornithidae-", "manidae-",
        "rhinocerotidae-", "viverridae-", "bovinae-", "chelonioidea-",
        "thylacinidae-", "iniidae-", "cervinae-", "meiolaniidae-"
    };
    private static readonly ConcurrentDictionary<string, FirstPersonModelPolicy> ModelPolicyCache =
        new(StringComparer.OrdinalIgnoreCase);

    [ThreadStatic]
    private static float[]? cameraSpaceBaseBuffer;

    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        MethodInfo? target = AccessTools.Method(
            typeof(EntityShapeRenderer),
            nameof(EntityShapeRenderer.DoRender3DOpaqueBatched),
            new[] { typeof(float), typeof(bool) }
        );
        MethodInfo? prefix = AccessTools.Method(
            typeof(AnimalicaFirstPersonHandsSystem),
            nameof(BeforeRenderBatched)
        );

        MethodInfo? renderItemPrefix = AccessTools.Method(
            typeof(AnimalicaFirstPersonHandsSystem),
            nameof(BeforeRenderItem)
        );

        if (target == null
            || prefix == null
            || renderItemPrefix == null
            || RenderItemMethod == null
            || EntityField == null
            || RenderModeField == null)
        {
            api.Logger.Warning("[AnimalicaCore] Could not install the Animalica first-person hand-mesh filter.");
            return;
        }

        harmony = new Harmony(HarmonyId);
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        harmony.Patch(
            RenderItemMethod,
            prefix: new HarmonyMethod(renderItemPrefix),
            postfix: new HarmonyMethod(typeof(AnimalicaFirstPersonHandsSystem), nameof(AfterRenderItem))
        );
        api.Logger.Notification("[AnimalicaCore] Animal-model first-person mesh filter and FotSA camera-space held-item correction enabled.");
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        harmony = null;
        ModelPolicyCache.Clear();
    }

    private static bool BeforeRenderBatched(EntityShapeRenderer __instance, bool isShadowPass)
    {
        if (isShadowPass || __instance is not EntityPlayerShapeRenderer playerRenderer)
        {
            return true;
        }

        if (RenderModeField?.GetValue(playerRenderer) is not RenderMode.FirstPerson)
        {
            return true;
        }

        if (EntityField?.GetValue(__instance) is not Entity entity)
        {
            return true;
        }

        string model = entity.WatchedAttributes?.GetString("skinModel", string.Empty) ?? string.Empty;
        return !ShouldHideFirstPersonArms(model);
    }

    private static bool ShouldHideFirstPersonArms(string model)
    {
        return GetModelPolicy(model).HideFirstPersonArms;
    }

    private static void BeforeRenderItem(
        EntityShapeRenderer __instance,
        bool isShadowPass,
        ref AttachmentPointAndPose apap,
        out float[]? __state
    )
    {
        __state = null;

        if (isShadowPass
            || __instance is not EntityPlayerShapeRenderer playerRenderer
            || RenderModeField?.GetValue(playerRenderer) is not RenderMode.FirstPerson
            || EntityField?.GetValue(__instance) is not Entity entity)
        {
            return;
        }

        string model = entity.WatchedAttributes?.GetString("skinModel", string.Empty) ?? string.Empty;
        if (!ShouldHideFirstPersonArms(model))
        {
            return;
        }

        float scale = entity.Properties?.Client?.Size ?? 1f;
        if (!float.IsFinite(scale) || scale <= 0.0001f)
        {
            scale = 1f;
        }

        if (ShouldUseFloatingFirstPersonItems(model))
        {
            // EntityShapeRenderer.RenderItem multiplies attachment coordinates
            // after the per-item HandTp scale. PlayerModelLib independently
            // changes both the camera eye height and entity render scale, so an
            // attachment-space eye-height formula cannot be model- or item-
            // invariant. Start the held item in camera space instead:
            //
            // view * inverse(view) * fixedOffset * HandTpTransform
            //
            // This retains each item's normal HandTp transform while removing
            // every FotSA bone, eye-height, and body-scale dependency.
            float[] originalModelMat = (float[])__instance.ModelMat.Clone();
            if (TrySetCameraSpaceItemBase(__instance, entity))
            {
                __state = originalModelMat;
                apap = CreateCameraSpaceAttachment(apap.AttachPoint?.Code == "LeftHand");
                return;
            }

            // The camera matrix should always be invertible during rendering.
            // If it is not, retain the previous model-space fallback instead of
            // making the held item disappear for the frame.
            apap = CreateEyeLevelAttachment(entity, scale, apap.AttachPoint?.Code == "LeftHand");
        }

        if (Math.Abs(scale - 1f) <= 0.0001f)
        {
            return;
        }

        // EntityPlayerShapeRenderer appends the animal's body scale and then
        // the model-origin translation to ModelMat. Undo those two operations
        // around the origin so the held item is rendered at normal player-hand
        // scale without changing the third-person model.
        __state = (float[])__instance.ModelMat.Clone();
        Mat4f.Translate(__instance.ModelMat, __instance.ModelMat, 0.5f, 0f, 0.5f);
        Mat4f.Scale(__instance.ModelMat, __instance.ModelMat, 1f / scale, 1f / scale, 1f / scale);
        Mat4f.Translate(__instance.ModelMat, __instance.ModelMat, -0.5f, 0f, -0.5f);
    }

    private static void AfterRenderItem(EntityShapeRenderer __instance, float[]? __state)
    {
        if (__state == null)
        {
            return;
        }

        Array.Copy(__state, __instance.ModelMat, __state.Length);
    }

    private static bool ShouldUseFloatingFirstPersonItems(string model)
    {
        return GetModelPolicy(model).UseFloatingItems;
    }

    private static FirstPersonModelPolicy GetModelPolicy(string model)
    {
        return string.IsNullOrEmpty(model)
            ? default
            : ModelPolicyCache.GetOrAdd(model, static value => ClassifyModel(value));
    }

    private static FirstPersonModelPolicy ClassifyModel(string model)
    {
        bool hideFirstPersonArms = ContainsAnyToken(model, AnimalModelTokens);
        bool useFloatingItems = ContainsAnyToken(model, FotsaModelTokens);
        return new FirstPersonModelPolicy(hideFirstPersonArms, useFloatingItems);
    }

    private static bool ContainsAnyToken(string model, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (model.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct FirstPersonModelPolicy(
        bool HideFirstPersonArms,
        bool UseFloatingItems
    );

    private static bool TrySetCameraSpaceItemBase(EntityShapeRenderer renderer, Entity entity)
    {
        if (entity.Api is not ICoreClientAPI clientApi)
        {
            return false;
        }

        float[] viewMatrix = clientApi.Render.CameraMatrixOriginf;
        if (viewMatrix == null || viewMatrix.Length < 16)
        {
            return false;
        }

        float[] cameraSpaceBase = cameraSpaceBaseBuffer ??= Mat4f.Create();
        if (Mat4f.Invert(cameraSpaceBase, viewMatrix) == null)
        {
            return false;
        }

        Mat4f.Translate(
            cameraSpaceBase,
            cameraSpaceBase,
            0f,
            FirstPersonItemCameraYOffset,
            FirstPersonItemCameraZOffset
        );
        Array.Copy(cameraSpaceBase, renderer.ModelMat, cameraSpaceBase.Length);
        return true;
    }

    private static AttachmentPointAndPose CreateCameraSpaceAttachment(bool leftHand)
    {
        return new AttachmentPointAndPose
        {
            AttachPoint = new AttachmentPoint
            {
                Code = leftHand ? "LeftHand" : "RightHand",
                PosX = 0.0,
                PosY = 0.0,
                PosZ = 0.0,
                RotationX = 0.0,
                RotationY = -180.0,
                RotationZ = 0.0,
            },
        };
    }

    private static AttachmentPointAndPose CreateEyeLevelAttachment(Entity entity, float scale, bool leftHand)
    {
        double eyeHeight = entity.LocalEyePos?.Y ?? 0.0;
        if (!double.IsFinite(eyeHeight) || eyeHeight < 0.0)
        {
            eyeHeight = entity.Properties?.EyeHeight ?? 1.0;
            scale = 1f;
        }

        double unscaledEyeHeight = eyeHeight / scale;

        return new AttachmentPointAndPose
        {
            AttachPoint = new AttachmentPoint
            {
                Code = leftHand ? "LeftHand" : "RightHand",
                PosX = 8.0,
                PosY = (unscaledEyeHeight + FirstPersonItemCameraYOffset) * 16.0,
                PosZ = 8.0,
                RotationX = 0.0,
                RotationY = -180.0,
                RotationZ = 0.0,
            },
        };
    }
}
