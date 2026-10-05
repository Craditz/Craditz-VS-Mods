using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PetAI;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FeralKinship;

/// <summary>
/// Replaces PetAI 5.1.1's unsafe growth postfix while preserving its intended
/// domestication and name transfer behavior. Kinship-tamed babies are also
/// redirected to the matching Kinship tame-adult definition when they grow.
/// </summary>
internal static class PetAiGrowthCompatibility
{
    private const string PetAiHarmonyId = "gerste.petai";
    private const string FeralHarmonyId = "feralkinship.petai-compat";
    private const int DeferredVerificationDelayMs = 1;
    private const string UnsafePetAiPostfixTypeName = "PetAI.EntityBehaviorGrowBecomeAdultPatch";
    private static readonly Harmony Harmony = new(FeralHarmonyId);

    public static void Install(ICoreServerAPI api)
    {
        MethodInfo? method = typeof(EntityBehaviorGrow).GetMethod(
            "BecomeAdult",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        MethodInfo? postfix = typeof(PetAiGrowthCompatibility).GetMethod(
            nameof(Postfix),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? prefix = typeof(PetAiGrowthCompatibility).GetMethod(
            nameof(Prefix),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? tameableDeath = typeof(EntityBehaviorTameable).GetMethod(
            nameof(EntityBehaviorTameable.OnEntityDeath),
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo? tameableDeathPrefix = typeof(PetAiGrowthCompatibility).GetMethod(
            nameof(SuppressGrowthDeathNotification),
            BindingFlags.Static | BindingFlags.NonPublic
        );
        MethodInfo? unsafePostfixSuppressor = typeof(PetAiGrowthCompatibility).GetMethod(
            nameof(SuppressUnsafePetAiPostfix),
            BindingFlags.Static | BindingFlags.NonPublic
        );

        if (method == null
            || prefix == null
            || postfix == null
            || tameableDeath == null
            || tameableDeathPrefix == null
            || unsafePostfixSuppressor == null)
        {
            api.Logger.Warning(
                "[feralkinship] Could not install the PetAI growth compatibility guard; the expected growth method was not found."
            );
            return;
        }

        try
        {
            int removedAtStartup = RemoveUnsafePetAiPostfixes(method);
            EnsureReplacementInstalled(method, prefix, postfix);
            bool deathGuardInstalled = Harmony.GetPatchInfo(tameableDeath)?.Prefixes.Any(
                patch => string.Equals(patch.owner, FeralHarmonyId, StringComparison.Ordinal)
            ) == true;
            if (!deathGuardInstalled)
            {
                Harmony.Patch(
                    tameableDeath,
                    prefix: new HarmonyMethod(tameableDeathPrefix)
                );
            }

            int remainingAtStartup = GetUnsafePetAiPostfixes(method).Length;
            if (remainingAtStartup == 0)
            {
                api.Logger.Notification(
                    "[feralkinship] Installed the PetAI 5.1.1 growth compatibility guard; removed {0} unsafe postfix(es).",
                    removedAtStartup
                );
            }
            else
            {
                api.Logger.Warning(
                    "[feralkinship] PetAI growth compatibility startup check still found {0} unsafe postfix(es); deferred repair is scheduled.",
                    remainingAtStartup
                );
            }

            api.Event.RegisterCallback(_ =>
            {
                try
                {
                    int unsafeBeforeRepair = GetUnsafePetAiPostfixes(method).Length;
                    if (unsafeBeforeRepair == 0) return;

                    int removedDeferred = RemoveUnsafePetAiPostfixes(method);
                    Patch[] unsafeAfterRepair = GetUnsafePetAiPostfixes(method);
                    if (unsafeAfterRepair.Length == 0)
                    {
                        api.Logger.Notification(
                            "[feralkinship] Deferred PetAI growth verification removed {0} late unsafe postfix(es).",
                            removedDeferred
                        );
                        return;
                    }

                    int suppressed = 0;
                    foreach (MethodInfo unsafePatchMethod in unsafeAfterRepair
                        .Select(patch => patch.PatchMethod)
                        .Distinct())
                    {
                        bool alreadySuppressed = Harmony.GetPatchInfo(unsafePatchMethod)?.Prefixes.Any(
                            patch => string.Equals(patch.owner, FeralHarmonyId, StringComparison.Ordinal)
                        ) == true;
                        if (alreadySuppressed) continue;

                        Harmony.Patch(
                            unsafePatchMethod,
                            prefix: new HarmonyMethod(unsafePostfixSuppressor)
                        );
                        suppressed++;
                    }

                    api.Logger.Warning(
                        "[feralkinship] Could not detach {0} PetAI growth postfix(es); directly suppressed {1} unsafe method(s) instead.",
                        unsafeAfterRepair.Length,
                        suppressed
                    );
                }
                catch (Exception exception)
                {
                    api.Logger.Warning(
                        "[feralkinship] Deferred PetAI growth compatibility verification failed: {0}",
                        exception.Message
                    );
                }
            }, DeferredVerificationDelayMs);
        }
        catch (Exception exception)
        {
            api.Logger.Warning(
                "[feralkinship] Could not install the PetAI growth compatibility guard: {0}",
                exception.Message
            );
        }
    }

    private static Patch[] GetUnsafePetAiPostfixes(MethodInfo method)
    {
        return Harmony.GetPatchInfo(method)?.Postfixes
            .Where(patch => string.Equals(patch.owner, PetAiHarmonyId, StringComparison.Ordinal)
                || patch.PatchMethod.DeclaringType?.FullName == UnsafePetAiPostfixTypeName)
            .ToArray()
            ?? Array.Empty<Patch>();
    }

    private static int RemoveUnsafePetAiPostfixes(MethodInfo method)
    {
        Patch[] unsafePostfixes = GetUnsafePetAiPostfixes(method);
        foreach (Patch patch in unsafePostfixes)
        {
            Harmony.Unpatch(method, patch.PatchMethod);
        }

        Harmony.Unpatch(method, HarmonyPatchType.Postfix, PetAiHarmonyId);
        return unsafePostfixes.Length;
    }

    private static void EnsureReplacementInstalled(
        MethodInfo method,
        MethodInfo prefix,
        MethodInfo postfix)
    {
        Patches? patchInfo = Harmony.GetPatchInfo(method);
        bool prefixInstalled = patchInfo?.Prefixes.Any(
            patch => string.Equals(patch.owner, FeralHarmonyId, StringComparison.Ordinal)
                && patch.PatchMethod == prefix
        ) == true;
        bool postfixInstalled = patchInfo?.Postfixes.Any(
            patch => string.Equals(patch.owner, FeralHarmonyId, StringComparison.Ordinal)
                && patch.PatchMethod == postfix
        ) == true;

        Harmony.Patch(
            method,
            prefix: prefixInstalled ? null : new HarmonyMethod(prefix),
            postfix: postfixInstalled ? null : new HarmonyMethod(postfix)
        );
    }

    private static bool SuppressUnsafePetAiPostfix()
    {
        return false;
    }

    private static void Prefix(EntityBehaviorGrow __instance, ref Entity adult)
    {
        Entity? child = __instance?.entity;
        if (child == null
            || adult == null
            || !FeralKinshipSystem.IsMarkedBabyTamingTarget(child))
        {
            return;
        }

        EntityBehaviorTameable? tameable = child.GetBehavior<EntityBehaviorTameable>();
        if (tameable == null
            || tameable.DomesticationLevel != DomesticationLevel.DOMESTICATED
            || string.IsNullOrWhiteSpace(tameable.OwnerId)
            || !FeralKinshipSystem.TryGetTamedAdultCode(adult, out AssetLocation adultCode))
        {
            return;
        }

        EntityProperties? adultType = child.Api.World.GetEntityType(adultCode);
        Entity? replacement = adultType == null
            ? null
            : child.Api.World.ClassRegistry.CreateEntity(adultType);
        if (replacement == null) return;

        replacement.Pos.SetFrom(adult.Pos);
        replacement.PositionBeforeFalling.Set(replacement.Pos.X, replacement.Pos.Y, replacement.Pos.Z);
        if (child.WatchedAttributes.GetTreeAttribute("domesticationstatus") is ITreeAttribute status)
        {
            replacement.WatchedAttributes["domesticationstatus"] = status.Clone();
        }
        if (child.WatchedAttributes.GetTreeAttribute("nametag") is ITreeAttribute nameTag)
        {
            replacement.WatchedAttributes["nametag"] = nameTag.Clone();
        }

        adult = replacement;
    }

    private static void Postfix(EntityBehaviorGrow __instance, Entity adult)
    {
        Entity? entity = __instance?.entity;
        if (entity == null || adult == null) return;

        EntityBehaviorTameable? adultTameable =
            adult.GetBehavior<EntityBehaviorTameable>();
        EntityBehaviorTameable? entityTameable =
            entity.GetBehavior<EntityBehaviorTameable>();

        // Companions owns this handoff. A juvenile from a source definition
        // that also has PetAI's tameable behavior may still report WILD here;
        // copying that tree would overwrite the companion's persisted owner
        // and turn the newly grown adult hostile.
        if (!IsCompanionGrowthChild(entity)
            && adultTameable != null
            && entityTameable != null)
        {
            adultTameable.DomesticationStatus = entityTameable.DomesticationStatus;
        }

        if (entityTameable != null && FeralKinshipSystem.IsMarkedBabyTamingTarget(entity))
        {
            adult.GetBehavior<EntityBehaviorTameable>()?.DomesticationStatus?.SetBool(
                FeralKinshipSystem.BabyTamingMarkerKey,
                false
            );
        }

        adult.GetBehavior<EntityBehaviorNameTag>()?.SetName(
            entity.GetBehavior<EntityBehaviorNameTag>()?.DisplayName
        );
    }

    private static bool SuppressGrowthDeathNotification(EntityBehaviorTameable __instance)
    {
        return !IsCompanionGrowthChild(__instance?.entity);
    }

    private static bool IsCompanionGrowthChild(Entity? entity)
    {
        ITreeAttribute? status = entity?.WatchedAttributes.GetTreeAttribute("domesticationstatus");
        return status?.GetBool("feralKinshipJuvenile", false) == true
            || status?.GetBool("feralKinshipGrowthInProgress", false) == true;
    }
}
