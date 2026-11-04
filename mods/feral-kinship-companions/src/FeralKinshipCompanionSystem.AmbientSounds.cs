#nullable enable
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

[ProtoContract]
public sealed class CompanionAmbientSoundSettingsPacket
{
    [ProtoMember(1)] public float ChanceMultiplier;
    [ProtoMember(2)] public float MinCooldownSeconds;
    [ProtoMember(3)] public float MaxCooldownSeconds;
}

public sealed partial class FeralKinshipCompanionSystem
{
    private readonly ConditionalWeakTable<Entity, AmbientIdleSoundBudget> sourceAmbientSoundBudgets = new();
    private readonly ConditionalWeakTable<Entity, AmbientIdleSoundBudget> nativeAmbientSoundBudgets = new();
    private AmbientIdleSoundSettings clientAmbientSoundSettings = AmbientIdleSoundSettings.Defaults;
    private Harmony? nativeAmbientSoundHarmony;

    internal AmbientIdleSoundSettings SourceAmbientSoundSettings => AmbientIdleSoundSettings.Normalize(
        companionConfig.AmbientIdleSoundChanceMultiplier,
        companionConfig.AmbientIdleSoundMinCooldownSeconds,
        companionConfig.AmbientIdleSoundMaxCooldownSeconds);

    internal void PrepareSourceAmbientSound(Entity entity)
    {
        AmbientIdleSoundSettings settings = SourceAmbientSoundSettings;
        sourceAmbientSoundBudgets.GetValue(entity, static _ => new AmbientIdleSoundBudget()).Prepare(entity.World.ElapsedMilliseconds,
            () => settings.ChooseDelayMs(entity.World.Rand));
    }

    internal bool TryPlaySourceAmbientSound(Entity entity)
    {
        if (entity.Api.Side != EnumAppSide.Server || IsFoxAwayFromWorld(entity)) return false;
        AmbientIdleSoundSettings settings = SourceAmbientSoundSettings;
        return sourceAmbientSoundBudgets.GetValue(entity, static _ => new AmbientIdleSoundBudget()).TryConsume(entity.World.ElapsedMilliseconds,
            () => settings.ChooseDelayMs(entity.World.Rand));
    }

    internal bool CanStartSourceAmbientSound(Entity entity)
    {
        PrepareSourceAmbientSound(entity);
        return sourceAmbientSoundBudgets.GetValue(entity, static _ => new AmbientIdleSoundBudget()).IsEligible(entity.World.ElapsedMilliseconds);
    }

    internal bool TryPlayNativeAmbientSound(Entity entity)
    {
        if (IsFoxAwayFromWorld(entity) || IsNativeFoxSleepAnimationActive(entity)) return false;
        AmbientIdleSoundSettings settings = clientAmbientSoundSettings;
        AmbientIdleSoundBudget budget = nativeAmbientSoundBudgets.GetValue(entity, static _ => new AmbientIdleSoundBudget());
        budget.Prepare(entity.World.ElapsedMilliseconds, () => settings.ChooseDelayMs(entity.World.Rand));
        if (settings.ChanceMultiplier <= 0 || entity.World.Rand.NextDouble() >= settings.ChanceMultiplier) return false;
        return budget.TryConsume(entity.World.ElapsedMilliseconds, () => settings.ChooseDelayMs(entity.World.Rand));
    }

    private static bool IsNativeFoxSleepAnimationActive(Entity entity)
    {
        CompanionSpeciesProfile fox = CompanionSpeciesCatalog.Fox;
        AssetLocation? code = entity.Code;
        if (entity is EntityPlayer || code == null
            || !string.Equals(code.Domain, fox.TameCodeDomain, StringComparison.OrdinalIgnoreCase)
            || !(string.Equals(code.Path, fox.TameCodePathBase, StringComparison.OrdinalIgnoreCase)
                || code.Path.StartsWith(fox.TameCodePathBase + "-", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Native Kinship has distinct sleep, awake lie/sit and wounded codes.
        // The animation dictionary is keyed by clip, so wounded aliases can
        // also occupy "sleep". Require the canonical task metadata code too.
        // External providers may use these codes differently; leave them alone.
        string? sleep = fox.SleepAnimationCode;
        var active = entity.AnimManager?.ActiveAnimationsByAnimCode;
        return sleep != null && active != null
            && active.TryGetValue(sleep, out AnimationMetaData? metadata)
            && string.Equals(metadata?.Code, sleep, StringComparison.OrdinalIgnoreCase);
    }

    private void OnAmbientSoundSettings(CompanionAmbientSoundSettingsPacket packet)
        => clientAmbientSoundSettings = AmbientIdleSoundSettings.Normalize(
            packet.ChanceMultiplier, packet.MinCooldownSeconds, packet.MaxCooldownSeconds);

    private CompanionAmbientSoundSettingsPacket AmbientSoundSettingsPacket()
    {
        AmbientIdleSoundSettings settings = SourceAmbientSoundSettings;
        return new CompanionAmbientSoundSettingsPacket {
            ChanceMultiplier = settings.ChanceMultiplier,
            MinCooldownSeconds = settings.MinSeconds,
            MaxCooldownSeconds = settings.MaxSeconds
        };
    }
}
