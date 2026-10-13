#nullable enable
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
        if (IsFoxAwayFromWorld(entity)) return false;
        AmbientIdleSoundSettings settings = clientAmbientSoundSettings;
        AmbientIdleSoundBudget budget = nativeAmbientSoundBudgets.GetValue(entity, static _ => new AmbientIdleSoundBudget());
        budget.Prepare(entity.World.ElapsedMilliseconds, () => settings.ChooseDelayMs(entity.World.Rand));
        if (settings.ChanceMultiplier <= 0 || entity.World.Rand.NextDouble() >= settings.ChanceMultiplier) return false;
        return budget.TryConsume(entity.World.ElapsedMilliseconds, () => settings.ChooseDelayMs(entity.World.Rand));
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
