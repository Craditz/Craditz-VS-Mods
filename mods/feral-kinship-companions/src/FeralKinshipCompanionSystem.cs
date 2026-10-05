using System;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageStoryConfigMigration;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem : ModSystem
{
    internal const string SocialBehaviorCode = "feralkinshipfoxsocial";
    internal const string DamageGuardBehaviorCode = "feralkinshipdamageguard";
    internal const string CompanionWhistleBehaviorCode = "feralkinshipcompanionwhistle";
    internal const string CompanionIdleTestItemClass = "FeralKinshipCompanionIdleTester";
    internal const string CompanionPackLedgerItemClass = "FeralKinshipPackLedger";
    internal const string CompanionRecoveryLedgerItemClass = "FeralKinshipCompanionRecoveryLedger";
    internal const string CompanionDeveloperLedgerItemClass = "FeralKinshipDeveloperLedger";
    private static readonly AssetLocation CompanionWhistleSound =
        new("feralkinshipcompanions:sounds/commands/follow");

    private CompanionDialogueService? dialogueService;

    private const string NetworkChannelName = "feralkinshipcompanions:social";
    private const string CompanionTutorialConfigFileName = "feralkinshipcompanions-tutorial.json";
    private const int CompanionTutorialVersion = 2;
    private const int CompanionPackGuideVersion = 3;
    private const int CompanionPackCartGuideVersion = 1;
    private const int CompanionTalentGuideVersion = 2;
    private const string DomesticationStatusPath = "domesticationstatus";
    private const string FoxIdKey = "feralKinshipFoxId";
    private const string NumberKey = "feralKinshipNumber";
    private const string PersonalityKey = "feralKinshipPersonality";
    private const string EarlyWarningEndsUtcMsKey = "feralKinshipEarlyWarningEndsUtcMs";
    private const string RequestsGeneratedKey = "feralKinshipRequestsGenerated";
    private const string RequestsCompletedKey = "feralKinshipRequestsCompleted";
    private const string FoxPointsKey = "feralKinshipFoxPoints";
    private const string FoxLifetimePointsKey = "feralKinshipFoxLifetimePoints";
    internal const string DamagePerkRankKey = "feralKinshipDamagePerkRank";
    internal const string PerkTreeKey = "feralKinshipPerks";
    private const int MaximumDamagePerkRank = 3;
    private const float DamageBonusPerRank = 0.10f;
    private const string PackPointsKey = "feralKinshipPackPoints";
    private const string PackTalentSnapshotKey = "feralKinshipPackTalents";
    private const float PackTalentPercent = 0.10f;
    private const float PackTalentCooldownReduction = 0.20f;
    private const float PackTalentSharedRecoveryPercent = 0.20f;
    private const float PackTalentCargoCareReduction = 0.25f;
    private const float PackTalentScentTrailReduction = 0.15f;
    private const float PackTalentDutyScanMultiplier = 0.60f;
    private const float PackTalentDutyCooldownMultiplier = 0.70f;
    private const float PackTalentDutyRetryMultiplier = 0.50f;
    private const float PackTalentHarvestCadenceMultiplier = 0.70f;
    private const string ActiveRequestKey = "feralKinshipActiveRequest";
    private const string RequestProgressKey = "feralKinshipRequestProgress";
    private const string RequestQualifiedSinceUtcMsKey = "feralKinshipRequestQualifiedSinceUtcMs";
    private const string RequestExpiresUtcMsKey = "feralKinshipRequestExpiresUtcMs";
    private const string RequestIsDeveloperKey = "feralKinshipRequestIsDeveloper";
    private const string PredatorTargetKey = "feralKinshipPredatorTarget";
    private const string PredatorTargetClueKey = "feralKinshipPredatorTargetClue";
    private const string PredatorTargetKilledKey = "feralKinshipPredatorTargetKilled";
    private const string LastCompletedKey = "feralKinshipLastCompleted";
    private const string RequestStartXKey = "feralKinshipRequestStartX";
    private const string RequestStartYKey = "feralKinshipRequestStartY";
    private const string RequestStartZKey = "feralKinshipRequestStartZ";
    private const string RequestStartDayKey = "feralKinshipRequestStartDay";
    private const string RequestedItemCodeKey = "feralKinshipRequestedItemCode";
    private const string RequestedItemCountKey = "feralKinshipRequestedItemCount";
    private const string MoodKey = "feralKinshipMood";
    private const string MoodOverrideKey = "feralKinshipMoodOverride";
    private const string MoodOverrideRemainingKey = "feralKinshipMoodOverrideRemaining";
    private const string DeveloperMoodForcedKey = "feralKinshipDeveloperMoodForced";
    private const string PackMoodOverrideKey = "feralKinshipPackMoodOverride";
    private const string PackMoodOverrideRemainingKey = "feralKinshipPackMoodOverrideRemaining";
    private const string PackMoodOverrideEndsUtcMsKey = "feralKinshipPackMoodOverrideEndsUtcMs";
    private const string MoodBaseRegenSpeedKey = "feralKinshipMoodBaseRegenSpeed";
    private const string MoodRandomRemainingKey = "feralKinshipMoodRandomRemaining";
    private const string RequestCooldownRemainingKey = "feralKinshipRequestCooldownRemaining";
    private const string CancelCooldownRemainingKey = "feralKinshipCancelCooldownRemaining";
    private const string MoodOverrideEndsUtcMsKey = "feralKinshipMoodOverrideEndsUtcMs";
    private const string MoodRandomEndsUtcMsKey = "feralKinshipMoodRandomEndsUtcMs";
    private const string RequestCooldownEndsUtcMsKey = "feralKinshipRequestCooldownEndsUtcMs";
    private const string CancelCooldownEndsUtcMsKey = "feralKinshipCancelCooldownEndsUtcMs";
    private const string StabilizedWindowAppliedRankKey = "feralKinshipStabilizedWindowAppliedRank";
    private const string StabilizedWindowAdjustedStartHoursKey = "feralKinshipStabilizedWindowAdjustedStartHours";
    private const string DeathlessRecoveryAtHoursKey = "feralKinshipDeathlessRecoveryAtHours";
    private const string DeathlessFallbackAtHoursKey = "feralKinshipDeathlessFallbackAtHours";
    private const string ExpeditionStatusExpiresDayKey = "feralKinshipExpeditionStatusExpiresDay";
    private const string LastCombatUtcMsKey = "feralKinshipLastCombatUtcMs";
    private const string LastDamageUtcMsKey = "feralKinshipLastDamageUtcMs";
    private const string LastAttackUtcMsKey = "feralKinshipLastAttackUtcMs";
    private const string BattleRhythmEndsUtcMsKey = "feralKinshipBattleRhythmEndsUtcMs";
    private const string FreshMeatEndsUtcMsKey = "feralKinshipFreshMeatEndsUtcMs";
    private const string AdrenalineRushEndsUtcMsKey = "feralKinshipAdrenalineRushEndsUtcMs";
    private const string LastStandCooldownEndsUtcMsKey = "feralKinshipLastStandCooldownEndsUtcMs";
    private const string PanicSprintEndsUtcMsKey = "feralKinshipPanicSprintEndsUtcMs";
    private const string ExpeditionStatusKey = "feralKinshipExpeditionStatus";
    private const string RecruitmentArrivalPendingKey = "feralKinshipRecruitmentArrivalPending";
    internal const string ExpeditionAwayKey = "feralKinshipExpeditionAway";
    private const string SecondWindPendingHealthKey = "feralKinshipSecondWindPendingHealth";
    private const string EntityHealthStateKey = "healthState";
    private const int RecoveringHealthState = 1;
    private const int MortallyWoundedHealthState = 2;

    private const float PlayerRequestCooldownSeconds = 300f;
    private const float PlayerCancelCooldownSeconds = 300f;
    private const float ActiveRequestLifetimeSeconds = 1800f;
    private const float ExpeditionTestDurationSeconds = 30f;
    private const float RecruitmentChanceBonusPerRank = 0.20f;
    private const float RecruitmentBaseChanceScale = 0.80f;
    private const float RecruitmentBaseChanceCap = 0.60f;
    private const float ExpeditionHardinessRiskReductionPerRank = 0.10f;
    private const float MaximumExpeditionInjuryRiskReduction = 0.30f;
    private const float StabilizedRescueHoursPerRank = 12f;
    private const float MortallyWoundedRescueWindowHours = 24f;
    private const float DeathlessFallbackHours = 20f;
    private static readonly float[] DeathlessRecoveryHours = { 6f, 10f, 14f, 18f };
    private const double TransientExpeditionStatusDisplayDays = 1d;
    private const float MoodRandomIntervalMinimumSeconds = 120f;
    private const float MoodRandomIntervalMaximumSeconds = 240f;
    private const float PostRequestMoodDurationSeconds = 300f;
    private const float PackMoodOverrideDurationSeconds = 30f;
    private const float CombatRecoveryGraceSeconds = 10f;
    private const float CombatRecoveryBonusPerRank = 0.15f;
    private const float ThickBloodDurationSeconds = 10f;
    private const float ThickBloodBonusPerRank = 0.10f;
    private const float FreshMeatDurationSeconds = 60f;
    private const float FreshMeatBonusPerRank = 0.10f;
    private const float BattleRhythmDurationSeconds = 15f;
    private const float BattleRhythmBonusPerRank = 0.05f;
    private const float AdrenalineRushDurationSeconds = 5f;
    private const float AdrenalineRushMovementPerRank = 0.10f;
    private const float AdrenalineRushResistancePerRank = 0.10f;
    private const float LastStandCooldownSeconds = 300f;
    private const float PanicSprintDurationSeconds = 5f;
    private const float PanicSprintBonusPerRank = 0.30f;
    private const float DodgeChancePerRank = 0.15f;
    private const float NaturalResistancePerRank = 0.10f;
    private const float AlertSensesRangePerRank = 2f;
    private const float PredatorAwarenessRangePerRank = 8f;
    private const float FoxBaseMaxHealth = 10f;
    private const float ScentLedgerBaseRetentionDays = 0.25f;
    private const float ScentLedgerAdditionalRetentionDaysPerRank = 0.5f;
    private const float PackScentLedgerAdditionalRetentionDays = 1f;
    private const float PredatorSearchRange = 32f;
    private const float AlertPredatorRangeBonus = 8f;
    private const float PackMoodRange = 16f;
    private const float PredatorFarDistance = 32f;
    private const float StayCloseDistance = 10f;
    private const float StayAwayDistance = 16f;
    private const float TravelDistance = 16f;
    private const float HigherGround = 3f;
    private const float StillMotionThreshold = 0.03f;
    private const float ObjectProximityDistance = 10f;
    private const int LightSourceLevel = 8;
    private const float ColdTemperatureThreshold = 5f;
    private const long EnvironmentSnapshotLifetimeMs = 5000;
    private const long EarlyWarningScanIntervalMs = 3000;
    private const long BreedingPartnerScanIntervalMs = 5000;
    private const long BirthSafePositionRetryIntervalMs = 5000;
    private const long ProgressPacketIntervalMs = 2000;
    private const double SocialActionRangeSquared = 16d * 16d;
    private const long ExpeditionReturnStaggerMs = 250;
    private const long ExpeditionReturnRetryMs = 1000;
    private const long ExpeditionDepartureTimeoutMs = 24000;
    private const long SnowTargetReservationLifetimeMs = 60000;
    internal const float BaseCompanionCampRadius = 36f;
    internal const float BaseWorkCartRadius = BaseCompanionCampRadius * 0.75f;
    private const float PermanentRangeIncreasePerRank = 0.10f;
    private const string PackCairnBlockPath = "packcairn";
    private const string PackCartBlockPathPrefix = "packcart-";
    private const string WorkCartBlockPathPrefix = "workcart-";
    private const string FoxBedBlockPathPrefix = "foxbed-";
    private const string PackAmenityBlockPathPrefix = "packamenity-";
    private const string PackStorageBlockPathPrefix = "packstorage-";
    private const string FoxStorageCarryKey = "feralKinshipStorageCarry";
    private const string FoxStorageReturnSourceValidKey = "feralKinshipStorageReturnSourceValid";
    private const string FoxStorageReturnSourceXKey = "feralKinshipStorageReturnSourceX";
    private const string FoxStorageReturnSourceYKey = "feralKinshipStorageReturnSourceY";
    private const string FoxStorageReturnSourceZKey = "feralKinshipStorageReturnSourceZ";
    private const string FoxStorageReturnSourceDimensionKey = "feralKinshipStorageReturnSourceDimension";
    private const string FoxStorageFailedTargetsKey = "feralKinshipStorageFailedTargets";
    private const string FoxStorageCartPickupKey = "feralKinshipStorageCartPickup";
    private const string FoxStorageCommandedCarryKey = "feralKinshipStorageCommandedCarry";
    private const string WhistleHoldEndsUtcMsKey = "feralKinshipWhistleHoldEndsUtcMs";
    private const string ActivityModeKey = "feralKinshipActivityMode";
    private const string GroundCleanupEnabledKey = "feralKinshipGroundCleanupEnabled";
    private const string GroundDroppedItemsEnabledKey = "feralKinshipGroundDroppedItemsEnabled";
    private const string GroundCattailsEnabledKey = "feralKinshipGroundCattailsEnabled";
    private const string GroundFlintEnabledKey = "feralKinshipGroundFlintEnabled";
    private const string GroundSticksEnabledKey = "feralKinshipGroundSticksEnabled";
    private const string GroundBouldersEnabledKey = "feralKinshipGroundBouldersEnabled";
    private const string GroundRocksEnabledKey = "feralKinshipGroundRocksEnabled";
    private const string MowLawnEnabledKey = "feralKinshipMowLawnEnabled";
    private const string FinishedProductsEnabledKey = "feralKinshipFinishedProductsEnabled";
    private const string FinishedCropsEnabledKey = "feralKinshipFinishedCropsEnabled";
    private const string FinishedBerriesEnabledKey = "feralKinshipFinishedBerriesEnabled";
    private const string FinishedMushroomsEnabledKey = "feralKinshipFinishedMushroomsEnabled";
    private const string FlowerRemovalEnabledKey = "feralKinshipFlowerRemovalEnabled";
    private const string SnowShovelingEnabledKey = "feralKinshipSnowShovelingEnabled";
    private const string CharcoalShovelingEnabledKey = "feralKinshipCharcoalShovelingEnabled";
    private const string SnowballCollectionEnabledKey = "feralKinshipSnowballCollectionEnabled";
    private const string GeneralStorageSortingEnabledKey = "feralKinshipGeneralStorageSortingEnabled";
    private const string PackRangeTalentRankKey = "feralKinshipPackRangeTalentRank";
    private const string FollowDistanceKey = "feralKinshipFollowDistance";
    private const string CombatStyleKey = "feralKinshipCombatStyle";
    private const string RiskToleranceKey = "feralKinshipRiskTolerance";
    private const string TargetedAttackActiveKey = "feralKinshipTargetedAttackActive";
    private const string TargetedAttackTargetIdKey = "feralKinshipTargetedAttackTargetId";
    private const string TargetedAttackPreviousActivityKey = "feralKinshipTargetedAttackPreviousActivity";
    private const string TargetedAttackPreviousFollowDistanceKey = "feralKinshipTargetedAttackPreviousFollowDistance";
    private const string TargetedAttackPreviousCombatStyleKey = "feralKinshipTargetedAttackPreviousCombatStyle";
    private const string TargetedAttackPreviousRiskToleranceKey = "feralKinshipTargetedAttackPreviousRiskTolerance";
    private const float TargetedAttackOwnerDistance = 96f;
    private const string WhistleCommandKey = "feralKinshipWhistleCommand";
    private const string IdleTestCommandKey = "feralKinshipIdleTestCommand";
    private const string AutomaticRetreatActiveKey = "feralKinshipAutomaticRetreatActive";
    private const string AutomaticRetreatPreviousCombatStyleKey = "feralKinshipAutomaticRetreatPreviousCombatStyle";
    private const string AutomaticRetreatPreviousActivityKey = "feralKinshipAutomaticRetreatPreviousActivity";
    private const float AutomaticRetreatRecoveryHysteresis = 0.05f;
    private const string LastAnnouncedHealthStateKey = "feralKinshipLastAnnouncedHealthState";
    private const string ActivityStartedUtcMsKey = "feralKinshipActivityStartedUtcMs";
    private const string ActivityArrivedUtcMsKey = "feralKinshipActivityArrivedUtcMs";
    private const long CombatMemoryMs = 30000;
    private const long AssistMemoryMs = 20000;
    private const long MinimumCommandRestMs = 10000;
    private const long WhistleDropHoldMs = 10000;
    private const long CargoSoundCooldownMs = 700;
    private const int BrambleReadyDelayMs = 2500;
    private const int BrambleReadyRetryDelayMs = 1000;
    private const int BrambleReadyMaxAttempts = 60;

    private sealed class FotsaCombatStatProfile
    {
        public FotsaCombatStatProfile(
            Dictionary<string, float> maxHealthByType,
            Dictionary<string, float> damageByType)
        {
            MaxHealthByType = maxHealthByType;
            DamageByType = damageByType;
        }

        public Dictionary<string, float> MaxHealthByType { get; }
        public Dictionary<string, float> DamageByType { get; }
    }

    private static readonly Dictionary<string, FotsaCombatStatProfile> FotsaCombatStatsBySourceCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["caninae-canina"] = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-aenocyon-dirus"] = 15, ["*-female-canis-aureus"] = 4, ["*-male-canis-aureus"] = 5,
                    ["*-canis-familiaris"] = 15, ["*-canis-latrans"] = 5, ["*-canis-lupastor"] = 5,
                    ["*-female-canis-lupus"] = 10, ["*-male-canis-lupus"] = 11,
                    ["*-female-canis-lupusarctos"] = 12, ["*-male-canis-lupusarctos"] = 13,
                    ["*-female-canis-lupusdingo"] = 5, ["*-male-canis-lupusdingo"] = 6,
                    ["*-female-canis-lycaon"] = 7, ["*-male-canis-lycaon"] = 8,
                    ["*-female-canis-rufus"] = 7, ["*-male-canis-rufus"] = 8,
                    ["*-female-canis-simensis"] = 5, ["*-male-canis-simensis"] = 6,
                    ["*-female-cuon-alpinus"] = 5, ["*-male-cuon-alpinus"] = 6,
                    ["*-cynotherium-sardous"] = 5, ["*-lupulella-adusta"] = 5,
                    ["*-lupulella-mesomelas"] = 5, ["*-female-lycaon-pictus"] = 7,
                    ["*-male-lycaon-pictus"] = 9
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-aenocyon-dirus"] = 7, ["*-canis-aureus"] = 3, ["*-canis-familiaris"] = 6,
                    ["*-canis-latrans"] = 4, ["*-canis-lupastor"] = 3, ["*-canis-lupus"] = 6,
                    ["*-canis-lupusarctos"] = 7, ["*-canis-lupusdingo"] = 4,
                    ["*-female-canis-lycaon"] = 4, ["*-male-canis-lycaon"] = 5,
                    ["*-female-canis-rufus"] = 4, ["*-male-canis-rufus"] = 5,
                    ["*-canis-simensis"] = 4, ["*-cuon-alpinus"] = 4,
                    ["*-cynotherium-sardous"] = 4, ["*-lupulella-adusta"] = 4,
                    ["*-lupulella-mesomelas"] = 4, ["*-lycaon-pictus"] = 5
                }),
            ["caninae-cerdocyonina"] = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-atelocynus-microtis"] = 5, ["*-cerdocyon-thous"] = 4,
                    ["*-chrysocyon-brachyurus"] = 7, ["*-dusicyon-australis"] = 7,
                    ["*-female-dusicyon-avus"] = 8, ["*-male-dusicyon-avus"] = 10,
                    ["*-female-lycalopex-culpaeus"] = 4, ["*-male-lycalopex-culpaeus"] = 5,
                    ["*-lycalopex-fulvipes"] = 3, ["*-lycalopex-griseus"] = 3,
                    ["*-lycalopex-gymnocercus"] = 4, ["*-lycalopex-sechurae"] = 3,
                    ["*-lycalopex-vetulus"] = 3, ["*-female-speothos-pacivorus"] = 10,
                    ["*-male-speothos-pacivorus"] = 11, ["*-speothos-venaticus"] = 4
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-atelocynus-microtis"] = 3, ["*-cerdocyon-thous"] = 3,
                    ["*-chrysocyon-brachyurus"] = 4, ["*-dusicyon-australis"] = 4,
                    ["*-dusicyon-avus"] = 5, ["*-lycalopex-culpaeus"] = 3,
                    ["*-lycalopex-fulvipes"] = 2, ["*-lycalopex-griseus"] = 2,
                    ["*-lycalopex-gymnocercus"] = 3, ["*-lycalopex-sechurae"] = 2,
                    ["*-lycalopex-vetulus"] = 2, ["*-speothos-pacivorus"] = 5,
                    ["*-speothos-venaticus"] = 3
                }),
            ["caninae-urocyonini"] = new(
                new(StringComparer.OrdinalIgnoreCase) { ["*-urocyon-cinereoargenteus"] = 4, ["*-urocyon-littoralis"] = 3 },
                new(StringComparer.OrdinalIgnoreCase) { ["*-cinereoargenteus"] = 3, ["*-littoralis"] = 1 }),
            ["caninae-vulpini"] = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-nyctereutes-procyonoides"] = 3, ["*-nyctereutes-viverrinus"] = 3,
                    ["*-otocyon-megalotis"] = 4, ["*-vulpes-bengalensis"] = 3,
                    ["*-vulpes-cana"] = 3, ["*-vulpes-chama"] = 3, ["*-vulpes-corsac"] = 3,
                    ["*-vulpes-ferrilata"] = 4, ["*-vulpes-lagopus"] = 3, ["*-vulpes-macrotis"] = 3,
                    ["*-vulpes-pallida"] = 3, ["*-vulpes-rueppellii"] = 3, ["*-vulpes-velox"] = 3,
                    ["*-female-vulpes-vulpes"] = 4, ["*-male-vulpes-vulpes"] = 5, ["*-vulpes-zerda"] = 3
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-nyctereutes-procyonoides"] = 3, ["*-nyctereutes-viverrinus"] = 3,
                    ["*-otocyon-megalotis"] = 3, ["*-bengalensis"] = 2, ["*-cana"] = 1,
                    ["*-chama"] = 2, ["*-corsac"] = 1, ["*-ferrilata"] = 3,
                    ["*-lagopus"] = 2, ["*-macrotis"] = 1, ["*-pallida"] = 2,
                    ["*-rueppellii"] = 1, ["*-velox"] = 2, ["*-vulpes"] = 3, ["*-zerda"] = 1,
                    ["*"] = 4
                }),
            ["machairodontinae"] = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-female-smilodon-fatalis"] = 28, ["*-male-smilodon-fatalis"] = 42,
                    ["*-female-smilodon-populator"] = 42, ["*-male-smilodon-populator"] = 54,
                    ["*-female-homotherium-serum"] = 24, ["*-male-homotherium-serum"] = 38
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-female-smilodon-fatalis"] = 20, ["*-male-smilodon-fatalis"] = 24,
                    ["*-female-smilodon-populator"] = 24, ["*-male-smilodon-populator"] = 28,
                    ["*-female-homotherium-serum"] = 20, ["*-male-homotherium-serum"] = 24
                }),
            ["pantherinae-neofelis"] = new(
                new(StringComparer.OrdinalIgnoreCase) { ["*-diardi-*"] = 7, ["*-nebulosa-*"] = 6, ["*"] = 6 },
                new(StringComparer.OrdinalIgnoreCase) { ["*-diardi-*"] = 8, ["*-nebulosa-*"] = 8, ["*"] = 4 }),
            ["pantherinae-panthera"] = new(
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-female-atrox-*"] = 25, ["*-male-atrox-*"] = 34,
                    ["*-female-leo-*"] = 20, ["*-male-leo-*"] = 27,
                    ["*-female-onca-*"] = 15, ["*-male-onca-*"] = 18,
                    ["*-female-pardus-*"] = 11, ["*-male-pardus-*"] = 12,
                    ["*-female-spelaea-*"] = 22, ["*-male-spelaea-*"] = 29,
                    ["*-female-tigris-*"] = 20, ["*-male-tigris-*"] = 26,
                    ["*-female-uncia-*"] = 10, ["*-male-uncia-*"] = 10, ["*"] = 10
                },
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["*-female-atrox-*"] = 18, ["*-male-atrox-*"] = 22,
                    ["*-female-leo-*"] = 16, ["*-male-leo-*"] = 18,
                    ["*-female-onca-*"] = 12, ["*-male-onca-*"] = 14,
                    ["*-female-pardus-*"] = 10, ["*-male-pardus-*"] = 10,
                    ["*-female-spelaea-*"] = 16, ["*-male-spelaea-*"] = 20,
                    ["*-female-tigris-*"] = 16, ["*-male-tigris-*"] = 18,
                    ["*-female-uncia-*"] = 10, ["*-male-uncia-*"] = 10, ["*"] = 4
                })
        };

    private sealed class EnvironmentSnapshot
    {
        public long CapturedAtMs;
        public int CenterX;
        public int CenterY;
        public int CenterZ;
        public int Dimension;
        public float SearchRange;
        public bool NearWater;
        public bool NearLight;
        public bool NearHeat;
        public bool NearLargeTree;
        public bool NearCropField;
        public bool NearMechanicalDevice;
    }

    private sealed class CompanionIdleInvitation
    {
        public string Kind = "companion-rest";
        public long PartnerEntityId;
        public long ExpiresAtMs;
        public string Animation = "sit";
        public Vec3d Target = new();
        public int DurationMs = 10000;
        public long AbsoluteEndAtMs;
    }

    private sealed class CombatMemory
    {
        public long EntityId;
        public long ExpiresAtMs;
    }

    /// <summary>
    /// Keeps the juvenile's damage event available after a lethal hit removes
    /// the juvenile from loadedFoxes and clears its per-entity CombatMemory.
    /// The resolver still applies the normal owner, dimension, range, and live
    /// target checks before a guardian can use this snapshot.
    /// </summary>
    private sealed class JuvenileThreatMemory
    {
        public string OwnerUid = string.Empty;
        public int Dimension;
        public Vec3d Position = new();
        public long AttackerEntityId;
        public long ExpiresAtMs;
    }

    private static readonly string[] FoxPersonalities =
    {
        "timid", "bold", "curious", "affectionate", "independent", "playful",
        "restless", "homebody", "social", "solitary", "protective", "territorial",
        "greedy", "demanding", "stubborn", "skittish"
    };

    private static readonly string[] RandomFoxMoods =
    {
        "calm", "content", "curious", "restless", "playful", "sleepy", "alert", "anxious"
    };

    private static readonly string[] DeveloperMoodValues =
    {
        "", "calm", "content", "curious", "restless", "playful", "sleepy", "alert", "anxious",
        "resting", "rested", "social", "happy", "recovered", "refreshed", "relieved", "alarmed", "rallied"
    };

    private ICoreServerAPI? serverApi;
    private IServerNetworkChannel? serverChannel;
    private ICoreClientAPI? clientApi;
    private IClientNetworkChannel? clientChannel;
    private FeralKinshipLoggingProgressRenderer? loggingProgressRenderer;
    private FeralKinshipCompanionThoughtRenderer? companionThoughtRenderer;
    private bool clientLevelFinalized;
    private GuiDialogFeralKinshipFoxSocial? socialDialog;
    private GuiDialogFeralKinshipGuide? guideDialog;
    private GuiDialogFeralKinshipFoxDeveloper? developerDialog;
    private GuiDialogFeralKinshipFoxPack? packDialog;
    private GuiDialogFeralKinshipCompanionRecovery? recoveryDialog;
    private GuiDialogFeralKinshipFoxPerks? perkDialog;
    private GuiDialogFeralKinshipRename? renameDialog;
    private GuiDialogFeralKinshipNameSuggestion? nameSuggestionDialog;
    private readonly Queue<CompanionNameSuggestionPacket> pendingNameSuggestions = new();
    private GuiDialogFeralKinshipTrainingDummy? trainingDummyDialog;
    private GuiDialogFeralKinshipFoxBed? foxBedDialog;
    private GuiDialogFeralKinshipWorkCart? workCartDialog;
    private GuiDialogFeralKinshipWhistle? whistleDialog;
    private GuiDialogFeralKinshipIdleTest? idleTestDialog;
    private GuiDialogFeralKinshipStorageRouting? storageRoutingDialog;
    private FoxPackStatePacket? lastPackState;
    private RoomRegistry? roomRegistry;
    private WeatherSystemServer? weatherSystem;
    private SystemTemporalStability? temporalStabilitySystem;
    private FoxPackRepository? packRepository;
    private TagSetFast animalTag;
    private readonly Dictionary<long, Entity> loadedFoxes = new();
    private readonly Dictionary<string, bool> playerCampAreaStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> playerAreaKinds = new(StringComparer.Ordinal);
    private readonly HashSet<long> adultStateValidatedEntities = new();
    private readonly HashSet<long> ledgerHydratedEntities = new();
    private readonly HashSet<long> rebuildingEntityIds = new();
    private readonly HashSet<long> permanentlyDeletingEntityIds = new();
    private readonly Dictionary<long, int> lastPersistedHealthStates = new();
    private readonly Dictionary<long, EnvironmentSnapshot> environmentSnapshots = new();
    private readonly Dictionary<long, long> nextConditionCheckAtMs = new();
    private readonly Dictionary<long, long> nextEarlyWarningScanAtMs = new();
    private readonly Dictionary<long, long> nextBreedingPartnerScanAtMs = new();
    private readonly Dictionary<long, long> nextBirthRetryAtMsByEntity = new();
    private readonly Dictionary<long, long> lastProgressPacketAtMs = new();
    private readonly Dictionary<string, long> lastCargoSoundAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> workCartChunkLoadAttemptsAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> whistleCommandsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> idleTestCommandsByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<long, CompanionIdleInvitation> companionIdleInvitations = new();
    private readonly Dictionary<long, FoxIdlePlan> forcedIdlePlans = new();
    private readonly Dictionary<long, Vec3d> packCartCallTargets = new();
    private readonly Dictionary<long, long> droppedItemReservations = new();
    private readonly Dictionary<string, long> mowingWorksiteReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> naturalCleanupReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> charcoalTargetReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> naturalCleanupFailureUntilMs = new(StringComparer.Ordinal);
    private readonly Dictionary<long, long> storageRoutingDiagnosticAtMs = new();
    private readonly Dictionary<string, long> dutyDiagnosticAtMs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SnowTargetReservation> snowTargetReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SnowTargetReservation> snowApproachReservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> socialViewByOwner = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> perkViewByOwner = new(StringComparer.Ordinal);
    private readonly HashSet<string> packViewers = new(StringComparer.Ordinal);
    private readonly Dictionary<long, CombatMemory> companionAttackers = new();
    private readonly Dictionary<long, JuvenileThreatMemory> juvenileThreats = new();
    private readonly Dictionary<string, CombatMemory> ownerAttackers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CombatMemory> ownerAttackTargets = new(StringComparer.Ordinal);
    private readonly Dictionary<long, long> companionAggressiveTargets = new();
    private readonly Dictionary<string, bool> brambleReadyPendingOwners = new(StringComparer.Ordinal);
    private long expeditionTickListenerId;
    private long namePromptTickListenerId;
    private long nextCargoDispatchAtMs;
    private bool companionTutorialConfigLoaded;
    private bool companionTutorialSeen;
    private bool companionPackGuideSeen;
    private bool companionPackCartGuideSeen;
    private bool companionTalentGuideSeen;
    private string clientWhistleCommand = string.Empty;
    private string clientIdleTestCommand = string.Empty;
    private Harmony? physicsSafetyHarmony;

    private sealed class CompanionTutorialConfig
    {
        public int WelcomeVersion;
        public int PackGuideVersion;
        public int PackCartGuideVersion;
        public int TalentGuideVersion;
    }

    private sealed class SnowTargetReservation
    {
        internal required long EntityId { get; init; }
        internal long ExpiresAtMs { get; set; }
    }

    private static readonly string[] HuntLootCodes =
    {
        "game:bushmeat-raw",
        "game:redmeat-raw",
        "game:hide-raw-small",
        "game:hide-raw-medium",
        "game:fat",
        "game:bone"
    };

    private static readonly string[] GreatHuntLootCodes =
    {
        "game:redmeat-raw",
        "game:fat",
        "game:hide-raw-medium",
        "game:hide-raw-large",
        "game:bone"
    };

    private static readonly string[] ApexHuntLootCodes =
    {
        "game:redmeat-raw",
        "game:fat",
        "game:hide-raw-large",
        "game:hide-raw-huge",
        "game:bone"
    };

    private static readonly string[] ScavengeLootCodes =
    {
        "game:gear-rusty",
        "game:stick",
        "game:flint",
        "game:drygrass"
    };

    private static readonly string[] ForageLootCodes =
    {
        "game:fruit-blueberry",
        "game:fruit-redcurrant",
        "game:fruit-whitecurrant",
        "game:fruit-blackcurrant",
        "game:fruit-cranberry",
        "game:seeds-spelt",
        "game:seeds-rye",
        "game:seeds-flax",
        "game:seeds-rice",
        "game:vegetable-carrot",
        "game:vegetable-onion",
        "game:vegetable-turnip",
        "game:vegetable-parsnip",
        "game:vegetable-cabbage",
        "game:drygrass"
    };

    private static readonly string[] DistantForageLootCodes =
    {
        "game:resin",
        "game:honeycomb",
        "game:seeds-sunflower",
        "game:seeds-bellpepper",
        "game:seeds-amaranth",
        "game:seeds-peanut",
        "game:seeds-licorice",
        "game:seeds-pumpkin",
        "game:seeds-pineapple",
        "game:vegetable-pumpkin",
        "game:vegetable-bellpepper",
        "game:vegetable-cassava",
        "game:vegetable-olive"
    };

    // Exact item variants inspected from Art of Growing 1.2.2 and its
    // Breeding Addon 1.2.2.  Item lookup makes these soft dependencies: the
    // entries contribute only when the released provider registered them.
    private static readonly string[] ArtOfGrowingForageLootCodes =
    {
        "artofgrowing:vegetable-scallions",
        "artofgrowing:grainbundle-spelt-wet",
        "artofgrowing:grainbundle-rice-wet",
        "artofgrowing:grainbundle-flax-wet",
        "artofgrowing:grainbundle-rye-wet",
        "artofgrowing:grainbundle-amaranth-wet"
    };

    private static readonly string[] ArtOfGrowingBreedingLootCodes =
    {
        "artofgrowing:seeds-wild-carrot",
        "artofgrowing:seeds-small-carrot",
        "artofgrowing:seeds-wild-onion",
        "artofgrowing:seeds-small-onion",
        "artofgrowing:seeds-wild-turnip",
        "artofgrowing:seeds-small-turnip",
        "artofgrowing:seeds-wild-parsnip",
        "artofgrowing:seeds-small-parsnip",
        "artofgrowing:seeds-wild-cabbage",
        "artofgrowing:seeds-small-cabbage",
        "artofgrowing:vegetable-wild-carrot",
        "artofgrowing:vegetable-wild-onion",
        "artofgrowing:vegetable-wild-turnip",
        "artofgrowing:vegetable-wild-parsnip",
        "artofgrowing:vegetable-wild-cabbage",
        "artofgrowing:grainbundle-spelt-wild-wet",
        "artofgrowing:grainbundle-spelt-small-wet",
        "artofgrowing:grainbundle-rice-wild-wet",
        "artofgrowing:grainbundle-rice-small-wet",
        "artofgrowing:grainbundle-rye-wild-wet",
        "artofgrowing:grainbundle-rye-small-wet",
        "artofgrowing:grainbundle-flax-wild-wet",
        "artofgrowing:grainbundle-flax-small-wet",
        "artofgrowing:grainbundle-amaranth-wild-wet",
        "artofgrowing:grainbundle-amaranth-small-wet",
        "artofgrowing:grainbundle-peanut-wild",
        "artofgrowing:grainbundle-peanut-small",
        "artofgrowing:grainbundle-soybean-wild",
        "artofgrowing:grainbundle-soybean-small",
        "artofgrowing:grainbundle-sunflower-wild",
        "artofgrowing:grainbundle-sunflower-small"
    };

    private static readonly string[] HuntGoalMeatCodes =
    {
        "game:bushmeat-raw",
        "game:redmeat-raw"
    };

    // Butchering 1.14.x registers these as real, ground-storable corpse
    // ItemStacks.  Keeping the variants explicit avoids manufacturing an
    // entity or a fake processed drop, and PickAvailableLootItem filters the
    // whole provider out when Butchering is absent.
    private static readonly string[] SmallHuntCorpseCodes =
    {
        "butchering:deadchicken-male-1-dead",
        "butchering:deadchicken-female-1-dead",
        "butchering:deadhare-male-arctic-1-dead",
        "butchering:deadhare-female-european-1-dead"
    };

    private static readonly string[] LargeHuntCorpseCodes =
    {
        "butchering:deaddeer-male-adult-1-dead",
        "butchering:deaddeer-male-adultlarge-1-dead",
        "butchering:deadgoat-male-adult-1-dead",
        "butchering:deadwolf-male-eurasian-1-dead"
    };

    private static readonly string[] ForageGoalBerryCodes =
    {
        "game:fruit-blueberry",
        "game:fruit-redcurrant",
        "game:fruit-whitecurrant",
        "game:fruit-blackcurrant",
        "game:fruit-cranberry"
    };

    private static readonly string[] ForageGoalVegetableCodes =
    {
        "game:vegetable-carrot",
        "game:vegetable-onion",
        "game:vegetable-turnip",
        "game:vegetable-parsnip",
        "game:vegetable-cabbage"
    };

    private static readonly string[] ForageGoalGrainCodes =
    {
        "game:grain-spelt",
        "game:grain-rye",
        "game:grain-rice",
        "game:grain-flax"
    };

    private static readonly string[] ForageRareSeedCodes =
    {
        "game:seeds-sunflower",
        "game:seeds-bellpepper",
        "game:seeds-amaranth",
        "game:seeds-peanut",
        "game:seeds-licorice",
        "game:seeds-pumpkin",
        "game:seeds-pineapple"
    };

    private static readonly string[] ForageRareVegetableCodes =
    {
        "game:vegetable-pumpkin",
        "game:vegetable-bellpepper",
        "game:vegetable-cassava",
        "game:vegetable-olive"
    };

    private static readonly string[] ForageBerryCuttingCodes =
    {
        "game:fruitingbushcutting-beautyberry-free",
        "game:fruitingbushcutting-blackberry-free",
        "game:fruitingbushcutting-blueberry-free",
        "game:fruitingbushcutting-cloudberry-free",
        "game:fruitingbushcutting-cranberry-free",
        "game:fruitingbushcutting-blackcurrant-free",
        "game:fruitingbushcutting-raspberry-free",
        "game:fruitingbushcutting-redcurrant-free",
        "game:fruitingbushcutting-whitecurrant-free",
        "game:fruitingbushcutting-strawberry-free"
    };

    private static readonly string[] ScavengeVesselCodes =
    {
        "game:lootvessel-forage",
        "game:lootvessel-farming",
        "game:lootvessel-seed",
        "game:lootvessel-food",
        "game:lootvessel-ore",
        "game:lootvessel-tool",
        "game:lootvessel-arcticsupplies"
    };

    private static readonly int[] ScavengeVesselWeights =
    {
        35,
        25,
        20,
        12,
        6,
        1,
        1
    };

    private static readonly int[] RuinVesselWeights =
    {
        3,
        7,
        10,
        15,
        35,
        20,
        10
    };

    private static readonly string[] RuinDebrisRandomizerCodes =
    {
        "game:stackrandomizer-resource",
        "game:stackrandomizer-kitchen",
        "game:stackrandomizer-materials-mining",
        "game:stackrandomizer-ruinedweapon",
        "game:stackrandomizer-clutter-workshop-basic",
        "game:stackrandomizer-clutter-scribe",
        "game:stackrandomizer-clutter-lab",
        "game:stackrandomizer-clutter-science"
    };

    private static readonly int[] RuinDebrisRandomizerWeights =
    {
        30,
        25,
        20,
        10,
        7,
        5,
        2,
        1
    };

    private static readonly string[] AncientLoreRandomizerCodes =
    {
        "game:stackrandomizer-lore-villager",
        "game:stackrandomizer-lore-tobias",
        "game:stackrandomizer-lore-research",
        "game:stackrandomizer-lore-diaries",
        "game:stackrandomizer-lore-jonas"
    };

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    // Tamables:FOTSA creates its tamed entity definitions during
    // AssetsFinalize.  Run after its default-order system so the optional
    // compatibility overlay can see those definitions without making FOTSA a
    // required dependency of Companions.
    public override double ExecuteOrder() => 0.2;

    public override void StartPre(ICoreAPI api)
    {
        // Item JSON is resolved before Start(). Register this before asset
        // loading so the optional PetAI whistle patch can instantiate it.
        api.RegisterItemClass(
            "FeralKinshipCompanionWhistle",
            typeof(ItemFeralKinshipCompanionWhistle));
        api.RegisterItemClass(
            CompanionIdleTestItemClass,
            typeof(ItemFeralKinshipCompanionIdleTester));
        api.RegisterItemClass(
            CompanionPackLedgerItemClass,
            typeof(ItemFeralKinshipPackLedger));
        api.RegisterItemClass(
            CompanionRecoveryLedgerItemClass,
            typeof(ItemFeralKinshipCompanionRecoveryLedger));
        api.RegisterItemClass(
            CompanionDeveloperLedgerItemClass,
            typeof(ItemFeralKinshipDeveloperLedger));
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(SocialBehaviorCode, typeof(EntityBehaviorFeralKinshipFoxSocial));
        api.RegisterEntityBehaviorClass(DamageGuardBehaviorCode, typeof(EntityBehaviorFeralKinshipDamageGuard));
        api.RegisterEntity("FeralKinshipTrainingDummy", typeof(EntityFeralKinshipTrainingDummy));
        api.RegisterItemClass("FeralKinshipTrainingDummy", typeof(ItemFeralKinshipTrainingDummy));
        api.RegisterEntity("FeralKinshipAttackDummy", typeof(EntityFeralKinshipAttackDummy));
        api.RegisterItemClass("FeralKinshipAttackDummy", typeof(ItemFeralKinshipAttackDummy));
        api.RegisterEntity("FeralKinshipWeatheredHideDummy", typeof(EntityFeralKinshipWeatheredHideDummy));
        api.RegisterItemClass("FeralKinshipWeatheredHideDummy", typeof(ItemFeralKinshipWeatheredHideDummy));
        api.RegisterBlockClass("FeralKinshipPackCairn", typeof(BlockFeralKinshipPackCairn));
        api.RegisterBlockEntityClass("FeralKinshipPackCart", typeof(BlockEntityFeralKinshipPackCart));
        api.RegisterBlockClass("FeralKinshipWorkCart", typeof(BlockFeralKinshipWorkCart));
        api.RegisterBlockClass("FeralKinshipFoxBed", typeof(BlockFeralKinshipFoxBed));
        api.RegisterBlockClass("FeralKinshipFoxFlap", typeof(BlockFeralKinshipFoxFlap));
        api.RegisterBlockClass("FeralKinshipAmenity", typeof(BlockFeralKinshipAmenity));
        api.RegisterBlockClass("FeralKinshipDiningBoard", typeof(BlockFeralKinshipDiningBoard));
        api.RegisterBlockEntityClass("FeralKinshipDiningBoard", typeof(BlockEntityFeralKinshipDiningBoard));
        api.RegisterBlockClass("FeralKinshipPackStorage", typeof(BlockFeralKinshipPackStorage));
        api.RegisterBlockClass("FeralKinshipPackStorageWicker", typeof(BlockFeralKinshipPackStorageWicker));
        api.RegisterBlockClass("FeralKinshipPackStorageTrunk", typeof(BlockFeralKinshipPackStorageTrunk));
        api.RegisterBlockEntityClass("FeralKinshipPackStorage", typeof(BlockEntityFeralKinshipPackStorage));
        api.RegisterBlockEntityClass("FeralKinshipPackStorageWicker", typeof(BlockEntityFeralKinshipPackStorageWicker));
        api.RegisterBlockEntityClass("FeralKinshipPackStorageTrunk", typeof(BlockEntityFeralKinshipPackStorageTrunk));

        // Message identifiers are assigned in registration order. Both sides
        // therefore register the complete contract here before side-specific
        // handlers are attached.
        api.Network.RegisterChannel(NetworkChannelName)
            .RegisterMessageType<FoxSocialRequestPacket>()
            .RegisterMessageType<FoxSocialStatePacket>()
            .RegisterMessageType<FoxPerkStatePacket>()
            .RegisterMessageType<FoxPackStatePacket>()
            .RegisterMessageType<FoxPackOpenFromCairnPacket>()
            .RegisterMessageType<FoxBedAssignmentRequestPacket>()
            .RegisterMessageType<FoxBedAssignmentStatePacket>()
            .RegisterMessageType<FoxWorkCartAssignmentRequestPacket>()
            .RegisterMessageType<FoxWorkCartAssignmentStatePacket>()
            .RegisterMessageType<TrainingDummyActionPacket>()
            .RegisterMessageType<TrainingDummyStatePacket>()
            .RegisterMessageType<CompanionSoundPacket>()
            .RegisterMessageType<CompanionSpatialSoundPacket>()
            .RegisterMessageType<CompanionThoughtPacket>()
            .RegisterMessageType<CompanionDeveloperOpenPacket>()
            .RegisterMessageType<CompanionWhistlePacket>()
            .RegisterMessageType<CompanionIdleTestPacket>()
            .RegisterMessageType<CompanionNameSuggestionPacket>()
            .RegisterMessageType<FoxStorageRoutingRequestPacket>()
            .RegisterMessageType<FoxStorageRoutingStatePacket>()
            .RegisterMessageType<BrambleRequestPacket>()
            .RegisterMessageType<BrambleStatePacket>()
            .RegisterMessageType<CompanionRecoveryRequestPacket>()
            .RegisterMessageType<CompanionRecoveryStatePacket>()
            .RegisterMessageType<CompanionBackpackRequestPacket>()
            .RegisterMessageType<CompanionBackpackStatePacket>()
            .RegisterMessageType<DeveloperCartInspectionRequestPacket>()
            .RegisterMessageType<DeveloperCartInspectionStatePacket>();
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        api.EntityTagRegistry.TryCreateTagSetAndLogIssues(out animalTag, "animal");
        ApplyCompanionOverlays(api);
    }

    private static void ApplyCompanionOverlays(ICoreAPI api)
    {
        int corePatched = 0;
        int externalPetAiPatched = 0;
        foreach (EntityProperties properties in api.World.EntityTypes)
        {
            if (!CompanionSpeciesCatalog.TryGetByTameEntityCode(
                    properties.Code,
                    out CompanionSpeciesProfile profile))
            {
                continue;
            }

            if (properties.Client != null)
            {
                properties.Client.RendererName = "FeralKinshipCompanion";
                properties.Client.BehaviorsAsJsonObj = AppendEntityBehavior(
                    properties.Client.BehaviorsAsJsonObj,
                    SocialBehaviorCode
                );
            }

            if (properties.Server != null)
            {
                properties.Server.BehaviorsAsJsonObj = AppendEntityBehavior(
                    properties.Server.BehaviorsAsJsonObj,
                    DamageGuardBehaviorCode
                );
                properties.Server.BehaviorsAsJsonObj = AppendEntityBehavior(
                    properties.Server.BehaviorsAsJsonObj,
                    SocialBehaviorCode
                );
            }

            if (profile.UsesExternalPetAiTaskSet)
            {
                externalPetAiPatched++;
            }
            else
            {
                corePatched++;
            }
        }

        if (corePatched > 0)
        {
            api.Logger.Notification(
                "[FeralKinshipCompanions] Verified Companion support on {0} core/vanilla tame entity definitions.",
                corePatched
            );
        }

        if (externalPetAiPatched > 0)
        {
            api.Logger.Notification(
                "[FeralKinshipCompanions] Added Companion support to {0} external Tamables tamed entity definitions.",
                externalPetAiPatched
            );
        }
    }

    private static JsonObject[] AppendEntityBehavior(JsonObject[]? existing, string code)
    {
        existing ??= Array.Empty<JsonObject>();
        if (existing.Any(behavior =>
                string.Equals(behavior["code"].AsString(), code, StringComparison.OrdinalIgnoreCase)))
        {
            return existing;
        }

        JsonObject[] result = new JsonObject[existing.Length + 1];
        Array.Copy(existing, result, existing.Length);
        result[^1] = JsonObject.FromJson($$"""{ "code": "{{code}}" }""");
        return result;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        LoadBrambleDialogue(api);
        LoadCompanionFoodConfig();
        dialogueService = new CompanionDialogueService(SendDialoguePacket);
        if (!StfuModeEnabled)
        {
            dialogueService.Load(api);
        }
        dialogueService.Emitted = InterruptConversationsNear;
        if (!StfuModeEnabled)
        {
            LoadDialogueRuntime(api);
        }
        LoadCompanionNameConfig();
        RegisterDeveloperSpawnCommands(api);
        foreach (string error in ExpeditionEventRegistry.Validate())
        {
            api.Logger.Error("[FeralKinshipCompanions] Expedition event registry error: {0}", error);
        }
        physicsSafetyHarmony = new Harmony("feralkinshipcompanions.physics-safety");
        physicsSafetyHarmony.PatchAll(typeof(FeralKinshipPhysicsSafetyPatch).Assembly);
        FeralKinshipMortalWoundRecoveryPatch.Install(physicsSafetyHarmony, api.Logger);
        AiTaskRegistry.Register<AiTaskFeralKinshipMoodIdle>("feralkinshipmoodidle");
        AiTaskRegistry.Register<AiTaskFeralKinshipSourceIdleSound>("feralkinshipsourceidle");
        AiTaskRegistry.Register<AiTaskFeralKinshipFollowIdle>("feralkinshipfollowidle");
        AiTaskRegistry.Register<AiTaskFeralKinshipBackpackDelivery>("feralkinshipbackpackdelivery");
        AiTaskRegistry.Register<AiTaskFeralKinshipPetMeleeAttack>("feralkinshippetmeleeattack");
        AiTaskRegistry.Register<AiTaskFeralKinshipReturnToDen>("feralkinshipreturntoden");
        AiTaskRegistry.Register<AiTaskFeralKinshipRestAtDen>("feralkinshiprestatden");
        AiTaskRegistry.Register<AiTaskFeralKinshipExpeditionDepart>("feralkinshipexpeditiondepart");
        AiTaskRegistry.Register<AiTaskFeralKinshipPackIdle>("feralkinshippackidle");
        AiTaskRegistry.Register<AiTaskFeralKinshipFetchDroppedItem>("feralkinshipfetchdroppeditem");
        AiTaskRegistry.Register<AiTaskFeralKinshipLogging>("feralkinshiplogging");
        AiTaskRegistry.Register<AiTaskFeralKinshipMowGrass>("feralkinshipmowgrass");
        AiTaskRegistry.Register<AiTaskFeralKinshipBlueberryMode>("feralkinshipblueberrymode");
        AiTaskRegistry.Register<AiTaskFeralKinshipClearSnow>("feralkinshipclearsnow");
        AiTaskRegistry.Register<AiTaskFeralKinshipFollowMaster>("feralkinshipfollowmaster");
        AiTaskRegistry.Register<AiTaskFeralKinshipBrambleHome>("feralkinshipbramblehome");
        AiTaskRegistry.Register<AiTaskFeralKinshipChildFamilyCheck>("feralkinshipchildfamilycheck");
        AiTaskRegistry.Register<AiTaskFeralKinshipGetOutOfWater>("feralkinshipgetoutofwater");
        AiTaskRegistry.Register<AiTaskFeralKinshipCommandReturnHome>("feralkinshipcommandreturnhome");
        AiTaskRegistry.Register<AiTaskFeralKinshipCommandRestApproach>("feralkinshipcommandrestapproach");
        AiTaskRegistry.Register<AiTaskFeralKinshipCommandRest>("feralkinshipcommandrest");
        AiTaskRegistry.Register<AiTaskFeralKinshipCombatApproach>("feralkinshipcombatapproach");
        AiTaskRegistry.Register<AiTaskFeralKinshipSeekFood>("feralkinshipseekfood");
        AiTaskRegistry.Register<AiTaskFeralKinshipStarvingRest>("feralkinshipstarvingrest");
        AiTaskRegistry.Register<AiTaskFeralKinshipCatUseInventory>("feralkinshipcatuseinventory");
        roomRegistry = api.ModLoader.GetModSystem<RoomRegistry>();
        weatherSystem = api.ModLoader.GetModSystem<WeatherSystemServer>();
        temporalStabilitySystem = api.ModLoader.GetModSystem<SystemTemporalStability>();
        packRepository = new FoxPackRepository(api);
        brambleRepository = new BrambleRepository(api);
        RegisterStorageRoutingCommands(api);
        RegisterDialogueCommands(api);
        serverChannel = api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<FoxSocialRequestPacket>(OnFoxSocialRequest)
            .SetMessageHandler<FoxBedAssignmentRequestPacket>(OnFoxBedAssignmentRequest)
            .SetMessageHandler<FoxWorkCartAssignmentRequestPacket>(OnFoxWorkCartAssignmentRequest)
            .SetMessageHandler<TrainingDummyActionPacket>(OnTrainingDummyAction)
            .SetMessageHandler<CompanionWhistlePacket>(OnCompanionWhistlePacket)
            .SetMessageHandler<CompanionIdleTestPacket>(OnCompanionIdleTestPacket)
            .SetMessageHandler<FoxStorageRoutingRequestPacket>(OnFoxStorageRoutingRequest)
            .SetMessageHandler<BrambleRequestPacket>(OnBrambleRequest)
            .SetMessageHandler<CompanionRecoveryRequestPacket>(OnCompanionRecoveryRequest)
            .SetMessageHandler<CompanionBackpackRequestPacket>(OnCompanionBackpackRequest)
            .SetMessageHandler<DeveloperCartInspectionRequestPacket>(OnDeveloperCartInspectionRequest);

        api.Event.OnPlayerInteractEntity += OnPlayerInteractEntity;
        api.Event.DidPlaceBlock += OnPlayerDidPlaceBlock;
        api.Event.RegisterEventBusListener(OnAmbientExplosionEvent, 0.5, "onexplosion");
        api.Event.PlayerDisconnect += OnPlayerDisconnect;
        // PlayerNowPlaying can fire while the server-side client is still in
        // character creation.  Bramble opens a client UI, so wait for the
        // post-character-selection PlayerReady event before materializing the
        // helper or presenting onboarding.  Keep the same delayed path for
        // respawns, which are already past character creation.
        api.Event.PlayerNowPlaying += OnPlayerReady;
        api.Event.PlayerReady += OnPlayerReady;
        api.Event.PlayerRespawn += OnPlayerRespawn;
        api.Event.PlayerDeath += OnPlayerDeath;
        api.Event.SaveGameLoaded += OnSaveGameLoaded;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnCompanionContentReady);
        api.Event.GameWorldSave += OnGameWorldSave;
        api.Event.OnEntityDeath += OnProgressionEntityDeath;
        api.Event.OnEntityDeath += OnPredatorRequestTargetDeath;
        api.Event.OnEntityDespawn += OnProgressionEntityDespawn;
        expeditionTickListenerId = api.Event.RegisterGameTickListener(OnExpeditionTick, 250);
        progressionCombatCleanupListenerId = api.Event.RegisterGameTickListener(
            PurgeInactiveProgressionCombatEncounters,
            60000);
    }

    private void RegisterStorageRoutingCommands(ICoreServerAPI api)
    {
        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.Create("packroute")
            .WithDescription("Configure what a nearby pack storage destination accepts.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.chat)
            .WithArgs(parsers.OptionalWord("category-or-action"), parsers.OptionalWord("category"))
            .HandleWith(OnPackRouteCommand);
    }

    private TextCommandResult OnPackRouteCommand(TextCommandCallingArgs args)
    {
        if (serverApi == null || packRepository?.Loaded != true || args.Caller.Player is not IServerPlayer player)
        {
            return TextCommandResult.Error("This command can only be used by a player in a loaded world.");
        }

        BlockSelection? selection = player.CurrentBlockSelection;
        if (selection?.Position == null)
        {
            return TextCommandResult.Error("Look directly at a supported storage container first.");
        }

        BlockPos pos = NormalizeStorageRoutingPosition(selection.Position);

        Block? block = serverApi.World.BlockAccessor.GetBlock(pos);
        BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
        if (!FoxStorageRouting.IsSupportedStorageTarget(block, blockEntity))
        {
            return TextCommandResult.Error(
                "That is not a usable pack destination. The block must expose a writable item inventory.");
        }

        if (!serverApi.World.Claims.TryAccess(player, pos, EnumBlockAccessFlags.Use))
        {
            return TextCommandResult.Error("You do not have permission to configure that container.");
        }

        FoxPackAmenityRecord? record = packRepository.GetAmenity(pos);
        if (record != null && !string.Equals(record.OwnerUid, player.PlayerUID, StringComparison.Ordinal))
        {
            return TextCommandResult.Error("That destination belongs to another player's pack.");
        }

        string first = args.Parsers.Count > 0 && !args.Parsers[0].IsMissing
            ? ((string?)args[0] ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;
        string second = args.Parsers.Count > 1 && !args.Parsers[1].IsMissing
            ? ((string?)args[1] ?? string.Empty).Trim().ToLowerInvariant()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(first) || first is "status" or "show")
        {
            string current = record?.StorageRoutingEnabled == true
                ? FoxStorageRouting.GetDisplayName(record.StorageRoutingMask)
                : "Disabled";
            return TextCommandResult.Success($"This container accepts: {current}.");
        }

        if (first is "off" or "disable"
            || (first == "remove" && string.IsNullOrWhiteSpace(second)))
        {
            if (record != null && string.Equals(record.Kind, "dining", StringComparison.Ordinal)
                && string.Equals(record.OwnerUid, player.PlayerUID, StringComparison.Ordinal))
            {
                record.StorageRoutingEnabled = false;
                record.StorageRoutingMask = 0;
                packRepository.Save();
                return TextCommandResult.Success("Custom rules removed. This Dining Board is no longer a pack destination.");
            }

            string refusal = string.Empty;
            if (record == null || !packRepository.RemoveStorageTarget(player.PlayerUID, pos, out refusal))
            {
                return TextCommandResult.Error(refusal.Length == 0 ? "That destination is not configured." : refusal);
            }

            packRepository.Save();
            return TextCommandResult.Success("This container is no longer a pack destination.");
        }

        bool additive = first is "add" or "+";
        bool subtractive = first is "remove" or "subtract" or "-";
        string categoryValue = additive || subtractive ? second : first;
        if (!FoxStorageRouting.TryParse(categoryValue, out FoxStorageRouting.Category category)
            || category == FoxStorageRouting.Category.None)
        {
            return TextCommandResult.Error(
                $"Unknown storage category. Available categories: {FoxStorageRouting.AvailableCategories}.");
        }

        int currentMask = record?.StorageRoutingEnabled == true
            ? record.StorageRoutingMask
            : (int)FoxStorageRouting.Category.None;
        int nextMask = additive
            ? currentMask | (int)category
            : subtractive
                ? currentMask & ~(int)category
                : (int)category;
        if (nextMask == 0)
        {
            return TextCommandResult.Error("A destination needs at least one category. Use /packroute off to disable it.");
        }

        FoxPackAmenityRecord configured = packRepository.RegisterStorageTarget(player.PlayerUID, pos, nextMask);
        packRepository.Save();
        string action = additive ? "now also accepts" : subtractive ? "no longer accepts" : "now accepts";
        string display = FoxStorageRouting.GetDisplayName(configured.StorageRoutingMask);
        return TextCommandResult.Success($"This container {action} {display}.");
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        clientApi = api;
        LoadBrambleDialogue(api);
        clientLevelFinalized = false;
        FeralKinshipPetAiNamePromptPatch.TryApply(api);
        api.Event.LevelFinalize += OnClientLevelFinalize;
        namePromptTickListenerId = api.Event.RegisterGameTickListener(OnNamePromptTick, 250);
        loggingProgressRenderer = new FeralKinshipLoggingProgressRenderer(api);
        api.Event.RegisterRenderer(
            loggingProgressRenderer,
            EnumRenderStage.Ortho,
            "feralkinshipcompanions.logging-progress");
        companionThoughtRenderer = new FeralKinshipCompanionThoughtRenderer(api);
        api.Event.RegisterRenderer(
            companionThoughtRenderer,
            EnumRenderStage.Ortho,
            "feralkinshipcompanions.thoughts");
        api.RegisterEntityRendererClass("FeralKinshipCompanion", typeof(FeralKinshipFoxRenderer));
        api.RegisterEntityRendererClass("FeralKinshipFox", typeof(FeralKinshipFoxRenderer));
        clientChannel = api.Network.GetChannel(NetworkChannelName)
            .SetMessageHandler<FoxSocialStatePacket>(OnFoxSocialState);
        clientChannel.SetMessageHandler<FoxPerkStatePacket>(OnFoxPerkState);
        clientChannel.SetMessageHandler<FoxPackStatePacket>(OnFoxPackState);
        clientChannel.SetMessageHandler<FoxPackOpenFromCairnPacket>(OnFoxPackOpenFromCairn);
        clientChannel.SetMessageHandler<DeveloperCartInspectionStatePacket>(OnDeveloperCartInspectionState);
        clientChannel.SetMessageHandler<FoxBedAssignmentStatePacket>(OnFoxBedAssignmentState);
        clientChannel.SetMessageHandler<FoxWorkCartAssignmentStatePacket>(OnFoxWorkCartAssignmentState);
        clientChannel.SetMessageHandler<TrainingDummyStatePacket>(OnTrainingDummyState);
        clientChannel.SetMessageHandler<CompanionSoundPacket>(OnCompanionSound);
        clientChannel.SetMessageHandler<CompanionSpatialSoundPacket>(OnCompanionSpatialSound);
        clientChannel.SetMessageHandler<CompanionThoughtPacket>(OnCompanionThought);
        clientChannel.SetMessageHandler<CompanionDeveloperOpenPacket>(OnCompanionDeveloperOpen);
        clientChannel.SetMessageHandler<CompanionNameSuggestionPacket>(OnCompanionNameSuggestion);
        clientChannel.SetMessageHandler<FoxStorageRoutingStatePacket>(OnFoxStorageRoutingState);
        clientChannel.SetMessageHandler<BrambleStatePacket>(OnBrambleState);
        clientChannel.SetMessageHandler<CompanionRecoveryStatePacket>(OnCompanionRecoveryState);
        clientChannel.SetMessageHandler<CompanionBackpackStatePacket>(OnCompanionBackpackState);
    }

    public override void Dispose()
    {
        if (serverApi != null) CompanionNavigation.ClearDoorTransits(serverApi.World);
        brambleRepository?.Save();
        dialogueService = null;
        FeralKinshipPetAiNamePromptPatch.Remove();
        physicsSafetyHarmony?.UnpatchAll("feralkinshipcompanions.physics-safety");
        physicsSafetyHarmony = null;

        if (clientApi != null)
        {
            clientApi.Event.LevelFinalize -= OnClientLevelFinalize;
            if (loggingProgressRenderer != null)
            {
                clientApi.Event.UnregisterRenderer(
                    loggingProgressRenderer,
                    EnumRenderStage.Ortho);
                loggingProgressRenderer.Dispose();
                loggingProgressRenderer = null;
            }
            if (companionThoughtRenderer != null)
            {
                clientApi.Event.UnregisterRenderer(
                    companionThoughtRenderer,
                    EnumRenderStage.Ortho);
                companionThoughtRenderer.Dispose();
                companionThoughtRenderer = null;
            }
            if (namePromptTickListenerId != 0)
            {
                clientApi.Event.UnregisterGameTickListener(namePromptTickListenerId);
                namePromptTickListenerId = 0;
            }
        }

        if (serverApi != null)
        {
            serverApi.Event.OnPlayerInteractEntity -= OnPlayerInteractEntity;
            serverApi.Event.DidPlaceBlock -= OnPlayerDidPlaceBlock;
            serverApi.Event.UnregisterEventBusListener(OnAmbientExplosionEvent);
            serverApi.Event.PlayerDisconnect -= OnPlayerDisconnect;
            serverApi.Event.PlayerNowPlaying -= OnPlayerReady;
            serverApi.Event.PlayerReady -= OnPlayerReady;
            serverApi.Event.PlayerRespawn -= OnPlayerRespawn;
            serverApi.Event.PlayerDeath -= OnPlayerDeath;
            serverApi.Event.SaveGameLoaded -= OnSaveGameLoaded;
            serverApi.Event.GameWorldSave -= OnGameWorldSave;
            serverApi.Event.OnEntityDeath -= OnProgressionEntityDeath;
            serverApi.Event.OnEntityDeath -= OnPredatorRequestTargetDeath;
            serverApi.Event.OnEntityDespawn -= OnProgressionEntityDespawn;
            if (expeditionTickListenerId != 0)
            {
                serverApi.Event.UnregisterGameTickListener(expeditionTickListenerId);
                expeditionTickListenerId = 0;
            }
            if (progressionCombatCleanupListenerId != 0)
            {
                serverApi.Event.UnregisterGameTickListener(progressionCombatCleanupListenerId);
                progressionCombatCleanupListenerId = 0;
            }
        }
        progressionCombatEncounters.Clear();
        brambleReadyPendingOwners.Clear();
        pendingRecoveryChecks.Clear();
        pendingAdminCompanionTeleports.Clear();

        developerCartInspectionDialog?.TryClose();
        socialDialog?.TryClose();
        guideDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        recoveryDialog?.TryClose();
        perkDialog?.TryClose();
        renameDialog?.TryClose();
        nameSuggestionDialog?.TryClose();
        trainingDummyDialog?.TryClose();
        foxBedDialog?.TryClose();
        workCartDialog?.TryClose();
        idleTestDialog?.TryClose();
        storageRoutingDialog?.TryClose();
        serverApi = null;
        serverChannel = null;
        clientApi = null;
        clientChannel = null;
        clientLevelFinalized = false;
        socialDialog = null;
        guideDialog = null;
        developerDialog = null;
        packDialog = null;
        recoveryDialog = null;
        perkDialog = null;
        renameDialog = null;
        nameSuggestionDialog = null;
        pendingNameSuggestions.Clear();
        trainingDummyDialog = null;
        foxBedDialog = null;
        workCartDialog = null;
        idleTestDialog = null;
        storageRoutingDialog = null;
        lastPackState = null;
        roomRegistry = null;
        weatherSystem = null;
        temporalStabilitySystem = null;
        packRepository = null;
        brambleRepository = null;
        loadedFoxes.Clear();
        undergroundRecoveryDetectedAtMs.Clear();
        undergroundRecoveryNextAttemptAtMs.Clear();
        nextUndergroundRecoveryScanAtMs = 0;
        loadedBrambles.Clear();
        brambleRecommendationOverrides.Clear();
        lastBrambleState = null;
        playerCampAreaStates.Clear();
        playerAreaKinds.Clear();
        ledgerHydratedEntities.Clear();
        rebuildingEntityIds.Clear();
        permanentlyDeletingEntityIds.Clear();
        environmentSnapshots.Clear();
        sharedEnvironmentSnapshots.Clear();
        pendingPackStateOwners.Clear();
        companionIdleInvitations.Clear();
        forcedIdlePlans.Clear();
        packCartCallTargets.Clear();
        idleTestCommandsByOwner.Clear();
        nextConditionCheckAtMs.Clear();
        nextEarlyWarningScanAtMs.Clear();
        nextBreedingPartnerScanAtMs.Clear();
        nextBirthRetryAtMsByEntity.Clear();
        lastProgressPacketAtMs.Clear();
        lastCargoSoundAtMs.Clear();
        workCartChunkLoadAttemptsAtMs.Clear();
        ResetWorkCartLoggingRuntimeState();
        lastFoodUiLevelByEntity.Clear();
        socialViewByOwner.Clear();
        perkViewByOwner.Clear();
        backpackViewByOwner.Clear();
        backpackPreflights.Clear();
        packViewers.Clear();
        companionAttackers.Clear();
        juvenileThreats.Clear();
        ownerAttackers.Clear();
        ownerAttackTargets.Clear();
        ClearAmbientLifeState();
    }

    private void OnClientLevelFinalize()
    {
        clientLevelFinalized = true;
        if (clientApi != null)
        {
            FeralKinshipPetAiNamePromptPatch.TryApply(clientApi);
        }
    }

    private void OnCompanionSound(CompanionSoundPacket packet)
    {
        if (clientApi == null || !clientLevelFinalized)
        {
            return;
        }

        (AssetLocation sound, float volume)? resolved = packet.Cue switch
        {
            CompanionSoundCue.Rejected => (new AssetLocation("game:sounds/effect/woodswitch"), 0.40f),
            CompanionSoundCue.TalentPurchased => (new AssetLocation("game:sounds/tutorialstepsuccess"), 0.52f),
            CompanionSoundCue.TalentReset => (new AssetLocation("game:sounds/held/bookclose2"), 0.45f),
            CompanionSoundCue.RecruitmentSuccess => (new AssetLocation("game:sounds/effect/receptionbell"), 0.55f),
            CompanionSoundCue.RecruitmentFailure => (new AssetLocation("game:sounds/effect/woodswitch"), 0.42f),
            CompanionSoundCue.MortalWarning => (new AssetLocation("feralkinshipcompanions:sounds/alerts/mortal-warning"), 0.78f),
            CompanionSoundCue.RecoveryStarted => (new AssetLocation("game:sounds/effect/latch"), 0.38f),
            CompanionSoundCue.RecoveryComplete => (new AssetLocation("feralkinshipcompanions:sounds/recovery/complete"), 0.64f),
            CompanionSoundCue.CargoComplete => (new AssetLocation("game:sounds/block/chestclose"), 0.42f),
            _ => null
        };
        if (resolved is { } cue)
        {
            clientApi.Gui.PlaySound(cue.sound, false, cue.volume);
        }
    }

    private void OnCompanionSpatialSound(CompanionSpatialSoundPacket packet)
    {
        if (clientApi == null || !clientLevelFinalized
            || string.IsNullOrWhiteSpace(packet.Sound)
            || clientApi.World.Player.Entity.Pos.Dimension != packet.Dimension)
        {
            return;
        }

        clientApi.World.PlaySoundAt(
            new AssetLocation(packet.Sound),
            packet.X,
            packet.Y,
            packet.Z,
            null,
            packet.Pitch,
            packet.Range,
            packet.Volume);
    }

    private void OnCompanionThought(CompanionThoughtPacket packet)
    {
        if (clientApi == null || !clientLevelFinalized || companionThoughtRenderer == null)
        {
            return;
        }

        companionThoughtRenderer.QueueThought(packet);
    }

    private void OnCompanionDeveloperOpen(CompanionDeveloperOpenPacket packet)
    {
        TryOpenFoxDeveloperGui(packet.TargetEntityId);
    }

    internal bool IsOwnedCompanion(Entity? entity, string ownerUid)
    {
        return entity != null
            && IsTamedFox(entity)
            && string.Equals(GetCompanionOwnerUid(entity), ownerUid, StringComparison.Ordinal);
    }

    internal bool HasClientWhistleCommand => CompanionWhistleCommand.IsValid(clientWhistleCommand);

    internal bool HasNearbyWhistleTarget(EntityPlayer player)
    {
        return clientApi != null
            && clientApi.World.GetEntitiesAround(
                player.Pos.XYZ,
                15,
                5,
                entity => IsOwnedCompanion(entity, player.PlayerUID)
                    || (PetAiCompatibilityBridge.IsOwnedPet(entity, player.PlayerUID)
                        && PetAiCompatibilityBridge.HasReceiveCommand(entity)
                        && PetAiCompatibilityBridge.HasClosestCommandMapping(clientWhistleCommand))).Length > 0;
    }

    internal void TryOpenWhistleGui()
    {
        if (clientApi == null) return;
        socialDialog?.TryClose();
        perkDialog?.TryClose();
        packDialog?.TryClose();
        whistleDialog ??= new GuiDialogFeralKinshipWhistle(clientApi, this);
        whistleDialog.TryOpen();
    }

    internal void CloseWhistleGui()
    {
        whistleDialog?.TryClose();
    }

    internal string GetClientWhistleCommand() => clientWhistleCommand;

    internal void SelectWhistleCommand(string command)
    {
        if (!CompanionWhistleCommand.IsValid(command)) return;
        clientWhistleCommand = CompanionWhistleCommand.Normalize(command);
        FoxGuiTheme.PlayChoice(clientApi!);
        clientChannel?.SendPacket(new CompanionWhistlePacket
        {
            Action = CompanionWhistlePacket.SetCommand,
            Command = clientWhistleCommand
        });
    }

    internal void ClearWhistleCommand()
    {
        clientWhistleCommand = string.Empty;
        FoxGuiTheme.PlayChoice(clientApi!);
        clientChannel?.SendPacket(new CompanionWhistlePacket
        {
            Action = CompanionWhistlePacket.ClearCommand
        });
    }

    internal void TryOpenIdleTestGui()
    {
        if (clientApi == null) return;
        socialDialog?.TryClose();
        perkDialog?.TryClose();
        packDialog?.TryClose();
        whistleDialog?.TryClose();
        idleTestDialog ??= new GuiDialogFeralKinshipIdleTest(clientApi, this);
        idleTestDialog.TryOpen();
    }

    internal void CloseIdleTestGui()
    {
        idleTestDialog?.TryClose();
    }

    internal string GetClientIdleTestCommand() => clientIdleTestCommand;

    internal void SelectIdleTestCommand(string command)
    {
        if (!CompanionIdleTestCommand.IsValid(command)) return;
        clientIdleTestCommand = CompanionIdleTestCommand.Normalize(command);
        FoxGuiTheme.PlayChoice(clientApi!);
        clientChannel?.SendPacket(new CompanionIdleTestPacket
        {
            Action = CompanionIdleTestPacket.SetCommand,
            Command = clientIdleTestCommand
        });
    }

    internal void ClearIdleTestCommand()
    {
        clientIdleTestCommand = string.Empty;
        FoxGuiTheme.PlayChoice(clientApi!);
        clientChannel?.SendPacket(new CompanionIdleTestPacket
        {
            Action = CompanionIdleTestPacket.ClearCommand
        });
    }

    internal bool TryApplyWhistleCommand(IServerPlayer owner)
    {
        if (serverApi == null) return false;
        string command = GetWhistleCommand(owner);
        if (!CompanionWhistleCommand.IsValid(command)) return false;

        if (CompanionWhistleCommand.IsDropHeldItems(command))
        {
            return TryApplyWhistleDropHeldItems(owner);
        }

        if (CompanionWhistleCommand.IsDuty(command))
        {
            return TryApplyDutyWhistle(owner, command);
        }

        if (CompanionWhistleCommand.IsTargetAttack(command))
        {
            return TryApplyWhistleTargetAttack(owner);
        }

        Entity[] nearbyPets = serverApi.World.GetEntitiesAround(
            owner.Entity.Pos.XYZ,
            15,
            5,
            entity => IsOwnedCompanion(entity, owner.PlayerUID)
                || (PetAiCompatibilityBridge.IsOwnedPet(entity, owner.PlayerUID)
                    && PetAiCompatibilityBridge.HasReceiveCommand(entity)));
        if (nearbyPets.Length == 0) return false;

        int applied = 0;
        foreach (Entity pet in nearbyPets)
        {
            if (IsOwnedCompanion(pet, owner.PlayerUID))
            {
                ApplyWhistleCommand(pet, owner, command);
                applied++;
            }
            else if (PetAiCompatibilityBridge.TryApplyClosestCommand(pet, owner.Entity, command))
            {
                applied++;
            }
        }

        // Risk tolerance has no direct PetAI equivalent. If there were only
        // ordinary PetAI pets nearby, let PetAI's original whistle handle it.
        if (applied == 0) return false;

        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Whistle: {CompanionWhistleCommand.DisplayName(command)} set for {applied} nearby pet{(applied == 1 ? string.Empty : "s")}.",
            EnumChatType.Notification
        );
        serverApi.World.PlaySoundAt(
            CompanionWhistleSound,
            owner.Entity,
            null,
            1f,
            24f,
            0.78f
        );
        return true;
    }

    private bool TryApplyWhistleDropHeldItems(IServerPlayer owner)
    {
        if (serverApi == null) return false;

        Entity[] nearbyCompanions = serverApi.World.GetEntitiesAround(
            owner.Entity.Pos.XYZ,
            15,
            5,
            entity => IsOwnedCompanion(entity, owner.PlayerUID));
        if (nearbyCompanions.Length == 0) return false;

        foreach (Entity companion in nearbyCompanions)
        {
            ApplyWhistleCommand(companion, owner, CompanionWhistleCommand.UtilityCategory + ":" + CompanionWhistleCommand.DropHeldItemsCommand);
            serverApi.Event.RegisterCallback(_ =>
            {
                if (IsDialogueCompanionPresent(companion) && !IsFoxIncapacitated(companion))
                    EmitCommandAcknowledgement(companion, owner, "command.stand_still");
            }, 4000);
        }

        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Whistle: nearby Companions dropped held items and will stand still for 10 seconds ({nearbyCompanions.Length} companion{(nearbyCompanions.Length == 1 ? string.Empty : "s")}).",
            EnumChatType.Notification
        );
        serverApi.World.PlaySoundAt(
            CompanionWhistleSound,
            owner.Entity,
            null,
            1f,
            24f,
            0.78f
        );
        return true;
    }

    private bool TryApplyDutyWhistle(IServerPlayer owner, string command)
    {
        if (serverApi == null)
        {
            return false;
        }

        string duty = CompanionWhistleCommand.Value(command);
        int dutyMask;
        if (duty == "none")
        {
            dutyMask = 0;
        }
        else if (!CompanionWhistleCommand.TryGetDutyMask(command, out dutyMask))
        {
            return false;
        }

        bool groundCleanup = (dutyMask & (CompanionDuty.GroundDroppedItemsBit
            | CompanionDuty.GroundCattailsBit
            | CompanionDuty.GroundFlintBit
            | CompanionDuty.GroundSticksBit
            | CompanionDuty.GroundBouldersBit
            | CompanionDuty.GroundRocksBit)) != 0;
        bool mowLawn = CompanionDuty.IsSet(dutyMask, CompanionDuty.MowLawnBit);
        bool finishedProducts = (dutyMask & (CompanionDuty.FinishedCropsBit
            | CompanionDuty.FinishedBerriesBit
            | CompanionDuty.FinishedMushroomsBit)) != 0;
        bool flowerRemoval = CompanionDuty.IsSet(dutyMask, CompanionDuty.FlowerRemovalBit);
        bool snowShoveling = CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowShovelingBit)
            || CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowballCollectionBit);
        bool snowballCollection = CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowballCollectionBit);
        bool charcoalShoveling = CompanionDuty.IsSet(dutyMask, CompanionDuty.CharcoalShovelingBit);
        bool storageSorting = CompanionDuty.IsSet(dutyMask, CompanionDuty.StorageSortingBit);

        Entity[] nearbyCompanions = serverApi.World.GetEntitiesAround(
            owner.Entity.Pos.XYZ,
            15,
            5,
            entity => IsOwnedCompanion(entity, owner.PlayerUID)
                && !IsCompanionJuvenile(entity));
        int applied = 0;
        foreach (Entity companion in nearbyCompanions)
        {
            ITreeAttribute status = GetDomesticationStatus(companion, true)!;
            status.SetBool(GroundCleanupEnabledKey, groundCleanup);
            status.SetBool(GroundDroppedItemsEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundDroppedItemsBit));
            status.SetBool(GroundCattailsEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundCattailsBit));
            status.SetBool(GroundFlintEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundFlintBit));
            status.SetBool(GroundSticksEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundSticksBit));
            status.SetBool(GroundBouldersEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundBouldersBit));
            status.SetBool(GroundRocksEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundRocksBit));
            status.SetBool(MowLawnEnabledKey, mowLawn);
            status.SetBool(FinishedProductsEnabledKey, finishedProducts);
            status.SetBool(FinishedCropsEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedCropsBit));
            status.SetBool(FinishedBerriesEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedBerriesBit));
            status.SetBool(FinishedMushroomsEnabledKey, CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedMushroomsBit));
            status.SetBool(FlowerRemovalEnabledKey, flowerRemoval);
            status.SetBool(SnowShovelingEnabledKey, snowShoveling);
            status.SetBool(CharcoalShovelingEnabledKey, charcoalShoveling);
            status.SetBool(SnowballCollectionEnabledKey, snowballCollection);
            status.SetBool(GeneralStorageSortingEnabledKey, storageSorting);
            MarkSocialStateDirty(companion);
            RegisterFoxInPack(companion);
            string dutyEvent = dutyMask == 0 ? "command.clear_duties"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundDroppedItemsBit) ? "command.duty.dropped_items.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundCattailsBit) ? "command.duty.cattails.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundFlintBit) ? "command.duty.flint.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundSticksBit) ? "command.duty.sticks.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundBouldersBit) ? "command.duty.boulders.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.GroundRocksBit) ? "command.duty.rocks.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedCropsBit) ? "command.duty.crops.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedBerriesBit) ? "command.duty.berries.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.FinishedMushroomsBit) ? "command.duty.mushrooms.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.FlowerRemovalBit) ? "command.duty.flower_removal.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowballCollectionBit) ? "command.duty.snowball_collection.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.SnowShovelingBit) ? "command.duty.snow_shoveling.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.CharcoalShovelingBit) ? "command.duty.snow_shoveling.enabled"
            : CompanionDuty.IsSet(dutyMask, CompanionDuty.MowLawnBit) ? "command.duty.mowing.enabled"
                : CompanionDuty.IsSet(dutyMask, CompanionDuty.StorageSortingBit) ? "command.duty.general_storage_sorting.enabled"
                : string.Empty;
            EmitCommandAcknowledgement(companion, owner, dutyEvent);
            SendStateToOwner(companion, string.Empty);
            applied++;
        }

        if (applied == 0)
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Whistle: no owned adult Companions are close enough to receive the duty order.",
                EnumChatType.Notification
            );
            return true;
        }

        packRepository?.Save();
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Whistle: {CompanionWhistleCommand.DisplayName(command)} set for {applied} Companion{(applied == 1 ? string.Empty : "s")}.",
            EnumChatType.Notification
        );
        serverApi.World.PlaySoundAt(
            CompanionWhistleSound,
            owner.Entity,
            null,
            1f,
            24f,
            0.78f
        );
        return true;
    }

    private bool TryApplyWhistleTargetAttack(IServerPlayer owner)
    {
        Entity? target = owner.CurrentEntitySelection?.Entity;
        if (target == null
            || target is EntityPlayer
            || !target.Alive
            || target.State != EnumEntityState.Active)
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Whistle: look directly at a living enemy before blowing the attack command.",
                EnumChatType.Notification
            );
            return true;
        }

        // A selected owned Companion (or another owned PetAI animal) is a
        // valid selection, but never a valid attack target. Report that case
        // directly instead of falling through to the unrelated "no nearby
        // Companions" message.
        if (IsOwnedCompanion(target, owner.PlayerUID)
            || PetAiCompatibilityBridge.IsOwnedPet(target, owner.PlayerUID)
            || IsTamedFox(target))
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Whistle: that is an owned animal, not a valid enemy target.",
                EnumChatType.Notification
            );
            return true;
        }

        Entity[] nearbyCompanions = serverApi!.World.GetEntitiesAround(
            owner.Entity.Pos.XYZ,
            15,
            5,
            entity => IsOwnedCompanion(entity, owner.PlayerUID));
        int applied = 0;
        foreach (Entity companion in nearbyCompanions)
        {
            if (BeginTargetedAttack(companion, target))
            {
                EmitCommandAcknowledgement(companion, owner, "command.attack_target");
                applied++;
            }
        }

        if (applied == 0)
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Whistle: no owned Companions are close enough to receive the attack order.",
                EnumChatType.Notification
            );
            return true;
        }

        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Whistle: attack {GetEntityDisplayName(target)} with {applied} Companion{(applied == 1 ? string.Empty : "s")}.",
            EnumChatType.Notification
        );
        serverApi.World.PlaySoundAt(
            CompanionWhistleSound,
            owner.Entity,
            null,
            1f,
            24f,
            0.78f
        );
        return true;
    }

    private static string GetEntityDisplayName(Entity entity)
    {
        string name = entity.GetName();
        return string.IsNullOrWhiteSpace(name) ? "the target" : name;
    }

    private string GetWhistleCommand(IServerPlayer owner)
    {
        if (whistleCommandsByOwner.TryGetValue(owner.PlayerUID, out string? command)
            && CompanionWhistleCommand.IsValid(command))
        {
            return CompanionWhistleCommand.Normalize(command);
        }

        string saved = owner.Entity.WatchedAttributes.GetString(WhistleCommandKey, string.Empty);
        return CompanionWhistleCommand.IsValid(saved)
            ? CompanionWhistleCommand.Normalize(saved)
            : string.Empty;
    }

    private void OnCompanionWhistlePacket(IServerPlayer fromPlayer, CompanionWhistlePacket packet)
    {
        if (packet.Action == CompanionWhistlePacket.ClearCommand)
        {
            whistleCommandsByOwner.Remove(fromPlayer.PlayerUID);
            fromPlayer.Entity.WatchedAttributes.RemoveAttribute(WhistleCommandKey);
            fromPlayer.Entity.WatchedAttributes.MarkPathDirty(WhistleCommandKey);
            fromPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Whistle command cleared; PetAI commands are active again.",
                EnumChatType.Notification
            );
            return;
        }

        if (packet.Action != CompanionWhistlePacket.SetCommand
            || !CompanionWhistleCommand.IsValid(packet.Command))
        {
            return;
        }

        string command = CompanionWhistleCommand.Normalize(packet.Command);
        whistleCommandsByOwner[fromPlayer.PlayerUID] = command;
        fromPlayer.Entity.WatchedAttributes.SetString(WhistleCommandKey, command);
        fromPlayer.Entity.WatchedAttributes.MarkPathDirty(WhistleCommandKey);
        fromPlayer.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Whistle command selected: {CompanionWhistleCommand.DisplayName(command)}.",
            EnumChatType.Notification
        );
    }

    private string GetIdleTestCommand(IServerPlayer owner)
    {
        if (idleTestCommandsByOwner.TryGetValue(owner.PlayerUID, out string? command)
            && CompanionIdleTestCommand.IsValid(command))
        {
            return CompanionIdleTestCommand.Normalize(command);
        }

        string saved = owner.Entity.WatchedAttributes.GetString(IdleTestCommandKey, string.Empty);
        return CompanionIdleTestCommand.IsValid(saved)
            ? CompanionIdleTestCommand.Normalize(saved)
            : string.Empty;
    }

    private void OnCompanionIdleTestPacket(IServerPlayer fromPlayer, CompanionIdleTestPacket packet)
    {
        if (packet.Action == CompanionIdleTestPacket.ClearCommand)
        {
            idleTestCommandsByOwner.Remove(fromPlayer.PlayerUID);
            fromPlayer.Entity.WatchedAttributes.RemoveAttribute(IdleTestCommandKey);
            fromPlayer.Entity.WatchedAttributes.MarkPathDirty(IdleTestCommandKey);
            fromPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Companion idle test behavior cleared.",
                EnumChatType.Notification
            );
            return;
        }

        if (packet.Action != CompanionIdleTestPacket.SetCommand
            || !CompanionIdleTestCommand.IsValid(packet.Command)) return;

        string command = CompanionIdleTestCommand.Normalize(packet.Command);
        idleTestCommandsByOwner[fromPlayer.PlayerUID] = command;
        fromPlayer.Entity.WatchedAttributes.SetString(IdleTestCommandKey, command);
        fromPlayer.Entity.WatchedAttributes.MarkPathDirty(IdleTestCommandKey);
        fromPlayer.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Companion idle test behavior selected: {CompanionIdleTestCommand.DisplayName(command)}.",
            EnumChatType.Notification
        );
    }

    internal bool TryApplyIdleTest(IServerPlayer owner, Entity? selectedTarget)
    {
        if (serverApi == null) return false;

        string command = GetIdleTestCommand(owner);
        if (!CompanionIdleTestCommand.IsValid(command))
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Select an idle test behavior with Shift-right-click first.",
                EnumChatType.Notification
            );
            return true;
        }

        Entity? target = selectedTarget ?? owner.CurrentEntitySelection?.Entity;
        if (target == null || !IsOwnedCompanion(target, owner.PlayerUID)
            || !target.Alive || target.State != EnumEntityState.Active)
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Look at one of your living companions to attempt the selected idle behavior.",
                EnumChatType.Notification
            );
            return true;
        }

        if (target.Pos.Dimension != owner.Entity.Pos.Dimension
            || target.Pos.SquareDistanceTo(owner.Entity.Pos) > 20 * 20)
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "The selected companion is too far away to receive the idle test.",
                EnumChatType.Notification
            );
            return true;
        }

        if (GetCompanionActivityMode(target) != CompanionActivityMode.AtEase
            || IsFoxAwayFromWorld(target)
            || IsFoxIncapacitated(target)
            || HasDirectedCompanionTask(target))
        {
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "The companion must be healthy, present, and At Ease for an idle test.",
                EnumChatType.Notification
            );
            return true;
        }

        command = CompanionIdleTestCommand.Normalize(command);
        if (command is CompanionIdleTestCommand.PairedRest or CompanionIdleTestCommand.PairedSleep)
        {
            Entity? partner = loadedFoxes.Values
                .Where(other => other != target && IsOwnedCompanion(other, owner.PlayerUID)
                    && other.Alive && other.State == EnumEntityState.Active
                    && other.Pos.Dimension == target.Pos.Dimension
                    && other.Pos.SquareDistanceTo(target.Pos) <= 18 * 18
                    && GetCompanionActivityMode(other) == CompanionActivityMode.AtEase
                    && !IsFoxAwayFromWorld(other) && !IsFoxIncapacitated(other)
                    && !HasDirectedCompanionTask(other))
                .OrderBy(other => other.Pos.SquareDistanceTo(target.Pos))
                .FirstOrDefault();
            if (partner == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Paired rest needs another nearby companion who is At Ease.",
                    EnumChatType.Notification
                );
                return true;
            }

            StopAmbientCompanionTask(target);
            StopAmbientCompanionTask(partner);
            companionIdleInvitations.Remove(target.EntityId);
            companionIdleInvitations.Remove(partner.EntityId);
            forcedIdlePlans.Remove(target.EntityId);
            forcedIdlePlans.Remove(partner.EntityId);

            long now = target.World.ElapsedMilliseconds;
            int restDuration = 10000 + serverApi.World.Rand.Next(7000);
            int departureDelay = 1500 + serverApi.World.Rand.Next(2500);
            long sharedEnd = now + restDuration;
            string animation = command == CompanionIdleTestCommand.PairedSleep
                ? "sleep"
                : GetMood(target) == "sleepy" ? "sleep" : "sit";
            BuildCompanionRestTargets(target, partner, out Vec3d targetSpot, out Vec3d partnerSpot);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "companion-rest",
                PartnerEntityId = partner.EntityId,
                Target = targetSpot,
                Animation = animation,
                DurationMs = restDuration,
                AbsoluteEndAtMs = sharedEnd
            };
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "companion-rest",
                PartnerEntityId = target.EntityId,
                ExpiresAtMs = sharedEnd + departureDelay + 10000,
                Target = partnerSpot,
                Animation = animation,
                DurationMs = restDuration + departureDelay,
                AbsoluteEndAtMs = sharedEnd + departureDelay
            };
        }
        else if (command == CompanionIdleTestCommand.PackmateGreeting)
        {
            Entity? partner = loadedFoxes.Values
                .Where(other => other != target && IsOwnedCompanion(other, owner.PlayerUID)
                    && other.Alive && other.State == EnumEntityState.Active
                    && other.Pos.Dimension == target.Pos.Dimension
                    && other.Pos.SquareDistanceTo(target.Pos) <= 18 * 18
                    && GetCompanionActivityMode(other) == CompanionActivityMode.AtEase
                    && !IsFoxAwayFromWorld(other) && !IsFoxIncapacitated(other)
                    && !HasDirectedCompanionTask(other))
                .OrderBy(other => other.Pos.SquareDistanceTo(target.Pos))
                .FirstOrDefault();
            if (partner == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Packmate greeting needs another nearby companion who is At Ease.",
                    EnumChatType.Notification
                );
                return true;
            }

            StopAmbientCompanionTask(target);
            StopAmbientCompanionTask(partner);
            companionIdleInvitations.Remove(target.EntityId);
            companionIdleInvitations.Remove(partner.EntityId);
            forcedIdlePlans.Remove(target.EntityId);
            forcedIdlePlans.Remove(partner.EntityId);

            int greetingDuration = 5000 + serverApi.World.Rand.Next(4500);
            long greetingEnd = target.World.ElapsedMilliseconds + greetingDuration;
            int departureDelay = 900 + serverApi.World.Rand.Next(1800);
            BuildCompanionRestTargets(target, partner, out Vec3d targetSpot, out Vec3d partnerSpot);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "packmate-greeting",
                PartnerEntityId = partner.EntityId,
                Target = targetSpot,
                Animation = "idle",
                DurationMs = greetingDuration,
                AbsoluteEndAtMs = greetingEnd,
                TargetDistance = 0.45f
            };
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "packmate-greeting",
                PartnerEntityId = target.EntityId,
                ExpiresAtMs = greetingEnd + departureDelay + 7000,
                Target = partnerSpot,
                Animation = "idle",
                DurationMs = greetingDuration + departureDelay,
                AbsoluteEndAtMs = greetingEnd + departureDelay
            };
        }
        else if (command == CompanionIdleTestCommand.PackmatePlay)
        {
            Entity? partner = loadedFoxes.Values
                .Where(other => other != target && IsOwnedCompanion(other, owner.PlayerUID)
                    && other.Alive && other.State == EnumEntityState.Active
                    && other.Pos.Dimension == target.Pos.Dimension
                    && other.Pos.SquareDistanceTo(target.Pos) <= 18 * 18
                    && GetCompanionActivityMode(other) == CompanionActivityMode.AtEase
                    && !IsFoxAwayFromWorld(other) && !IsFoxIncapacitated(other)
                    && !HasDirectedCompanionTask(other))
                .OrderBy(other => other.Pos.SquareDistanceTo(target.Pos))
                .FirstOrDefault();
            if (partner == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Packmate play needs another nearby companion who is At Ease.",
                    EnumChatType.Notification
                );
                return true;
            }

            StopAmbientCompanionTask(target);
            StopAmbientCompanionTask(partner);
            companionIdleInvitations.Remove(target.EntityId);
            companionIdleInvitations.Remove(partner.EntityId);
            forcedIdlePlans.Remove(target.EntityId);
            forcedIdlePlans.Remove(partner.EntityId);

            int playDuration = 10000 + serverApi.World.Rand.Next(6000);
            long playEnd = target.World.ElapsedMilliseconds + playDuration;
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "chase",
                PartnerEntityId = partner.EntityId,
                Target = partner.Pos.XYZ.Clone(),
                Animation = "Run",
                DurationMs = playDuration,
                AbsoluteEndAtMs = playEnd,
                MoveSpeed = 0.035f,
                TargetDistance = 0.8f
            };
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "play-runner",
                PartnerEntityId = target.EntityId,
                ExpiresAtMs = playEnd + 8000,
                Animation = "Run",
                Target = BuildPlayEscapeTarget(partner, target),
                DurationMs = playDuration,
                AbsoluteEndAtMs = playEnd
            };
        }
        else if (command == CompanionIdleTestCommand.PackSpaceVisit)
        {
            if (!TryGetFoxAmenities(target, out IReadOnlyList<FoxPackAmenityRecord> amenities))
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Visit pack space needs a nearby communal pack space.",
                    EnumChatType.Notification
                );
                return true;
            }

            FoxPackAmenityRecord? chosen = amenities
                .Where(a => a.Dimension == target.Pos.Dimension
                    && target.Pos.SquareDistanceTo(new Vec3d(a.X + 0.5, a.Y + 0.2, a.Z + 0.5)) <= 48 * 48)
                .OrderBy(a => a.Kind == "storage" ? 0 : 1)
                .ThenBy(a => target.Pos.SquareDistanceTo(new Vec3d(a.X + 0.5, a.Y + 0.2, a.Z + 0.5)))
                .FirstOrDefault();
            if (chosen == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Visit pack space needs a communal pack space within 48 blocks.",
                    EnumChatType.Notification
                );
                return true;
            }

            string animation = chosen.Kind is "lounge" or "lookout" ? "sit" : "Sniff";
            BuildPackSpaceVisitTargets(chosen, target, out Vec3d firstSpaceTarget, out Vec3d secondSpaceTarget);
            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "pack-space-visit",
                Target = firstSpaceTarget,
                Animation = animation,
                DurationMs = 12000,
                MoveSpeed = 0.018f,
                TargetDistance = 0.7f,
                Waypoints = new List<Vec3d> { firstSpaceTarget, secondSpaceTarget },
                LookAtTarget = new Vec3d(chosen.X + 0.5, chosen.Y + 0.2, chosen.Z + 0.5)
            };
        }
        else if (command is CompanionIdleTestCommand.SeeOff
            or CompanionIdleTestCommand.ScentInvestigation
            or CompanionIdleTestCommand.CampLookout)
        {
            FoxIdlePlan? ambientPlan = command switch
            {
                CompanionIdleTestCommand.SeeOff => TryCreateSeeOffPlan(target, out FoxIdlePlan? seeOffPlan, forced: true)
                    ? seeOffPlan
                    : null,
                CompanionIdleTestCommand.ScentInvestigation => TryCreateScentInvestigationPlan(target, out FoxIdlePlan? scentPlan, forced: true)
                    ? scentPlan
                    : null,
                _ => TryCreateCampLookoutPlan(target, out FoxIdlePlan? lookoutPlan, forced: true)
                    ? lookoutPlan
                    : null
            };
            if (ambientPlan == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    $"{CompanionIdleTestCommand.DisplayName(command)} could not find a safe target right now.",
                    EnumChatType.Notification
                );
                return true;
            }

            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = ambientPlan;
        }
        else if (CompanionIdleTestCommand.IsDirectAnimation(command))
        {
            string animation = command switch
            {
                CompanionIdleTestCommand.BasicSit => "sit",
                CompanionIdleTestCommand.BasicSleep => "sleep",
                CompanionIdleTestCommand.BasicSniff => "sniff",
                _ => "idle"
            };
            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "direct-animation",
                Immediate = true,
                Animation = animation,
                DurationMs = 10000
            };
        }
        else if (command == CompanionIdleTestCommand.BasicLookAround)
        {
            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "look-around",
                Immediate = true,
                Animation = "idle",
                DurationMs = 7000
            };
        }
        else if (command == CompanionIdleTestCommand.BasicWander)
        {
            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "short-wander",
                Target = BuildIdleWanderTarget(target),
                Animation = "idle",
                DurationMs = 9000,
                MoveSpeed = 0.018f,
                TargetDistance = 0.7f
            };
        }
        else if (command == CompanionIdleTestCommand.PackCartSit)
        {
            if (!TryGetPackCartIdleTarget(target, out Vec3d? cartTarget) || cartTarget == null)
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Sit on pack cart needs an active Pack Cart in the same dimension.",
                    EnumChatType.Notification
                );
                return true;
            }

            StopAmbientCompanionTask(target);
            if (!TryReservePackCartSit(target, cartTarget, out string cartReservationKey))
            {
                owner.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "That Pack Cart already has a companion sitting on it.",
                    EnumChatType.Notification
                );
                return true;
            }

            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "pack-cart-sit",
                OwnerEntityId = owner.Entity.EntityId,
                Target = cartTarget,
                Animation = "sit",
                DurationMs = 12000,
                MoveSpeed = 0.018f,
                TargetDistance = 0.7f,
                ReservationKey = cartReservationKey
            };
        }
        else
        {
            StopAmbientCompanionTask(target);
            companionIdleInvitations.Remove(target.EntityId);
            forcedIdlePlans[target.EntityId] = new FoxIdlePlan
            {
                Kind = "shadow-follow",
                OwnerEntityId = owner.Entity.EntityId,
                Target = owner.Entity.Pos.XYZ.Clone(),
                Animation = "idle",
                DurationMs = 15000,
                MoveSpeed = 0.018f,
                TargetDistance = 3.25f
            };
        }

        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            $"Idle test attempted: {CompanionIdleTestCommand.DisplayName(command)}.",
            EnumChatType.Notification
        );
        return true;
    }

    private bool HasDirectedCompanionTask(Entity entity)
    {
        AiTaskManager? manager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        return manager?.ActiveTasksBySlot.Any(task => task != null && IsDirectedCompanionTask(task)) == true;
    }

    private static void StopAmbientCompanionTask(Entity entity)
    {
        AiTaskManager? manager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        if (manager == null) return;
        foreach (IAiTask? task in manager.ActiveTasksBySlot.ToArray())
        {
            if (task != null && IsAmbientCompanionTask(task)) manager.StopTask(task.Slot);
        }
    }

    internal bool IsCompanionOwnerInsidePackCamp(Entity companion, Entity owner)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || owner is not EntityPlayer || !owner.Alive
            || companion.Pos.Dimension != owner.Pos.Dimension)
        {
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        BlockPos? marker = GetActiveCairnPosition(ownerUid);
        if (marker == null || marker.dimension != owner.Pos.Dimension) return false;

        double centerX = marker.X + 0.5;
        double centerZ = marker.Z + 0.5;
        double ownerDx = owner.Pos.X - centerX;
        double ownerDz = owner.Pos.Z - centerZ;
        double companionDx = companion.Pos.X - centerX;
        double companionDz = companion.Pos.Z - centerZ;
        double radius = GetCompanionCampRadius(companion);
        double radiusSquared = radius * radius;
        return ownerDx * ownerDx + ownerDz * ownerDz <= radiusSquared
            && companionDx * companionDx + companionDz * companionDz <= radiusSquared;
    }

    private void ApplyWhistleCommand(Entity companion, IServerPlayer owner, string command)
    {
        string category = CompanionWhistleCommand.Category(command);
        string value = CompanionWhistleCommand.Value(command);
        switch (category)
        {
            case CompanionWhistleCommand.UtilityCategory when value == CompanionWhistleCommand.DropHeldItemsCommand:
                ApplyWhistleDropHeldItems(companion);
                EmitCommandAcknowledgement(companion, owner, "command.drop_held_items");
                break;
            case CompanionWhistleCommand.ActivityCategory:
                SetCompanionActivity(companion, owner, value, showFeedback: false);
                EmitCommandAcknowledgement(companion, owner, value switch
                {
                    CompanionActivityMode.Follow => "command.follow",
                    CompanionActivityMode.Rest => "command.rest",
                    CompanionActivityMode.ReturnHome => "command.return_home",
                    _ => "command.at_ease"
                });
                break;
            case CompanionWhistleCommand.CombatCategory:
                SetCompanionCombatStyle(companion, owner, value, showFeedback: false);
                EmitCommandAcknowledgement(companion, owner, "command.combat." + CompanionCombatStyle.Normalize(value));
                break;
            case CompanionWhistleCommand.RiskCategory:
                SetCompanionRiskTolerance(companion, owner, value, showFeedback: false);
                EmitCommandAcknowledgement(companion, owner, "command.risk." + CompanionRiskTolerance.Normalize(value));
                break;
        }
    }

    private void ApplyWhistleDropHeldItems(Entity companion)
    {
        DropFoxStorageCargo(companion);

        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        status.SetLong(WhistleHoldEndsUtcMsKey, UtcNowMs() + WhistleDropHoldMs);
        MarkSocialStateDirty(companion);

        if (companion is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
            agent.Pos.Motion.Set(0, 0, 0);
        }
    }

    private void SendOwnerSound(IServerPlayer owner, string cue)
    {
        serverChannel?.SendPacket(new CompanionSoundPacket { Cue = cue }, owner);
    }

    internal void SendOwnerSpatialSound(
        IPlayer owner,
        AssetLocation? sound,
        BlockPos position,
        float pitch,
        float range = 24f,
        float volume = 1f)
    {
        if (serverChannel == null || owner is not IServerPlayer serverPlayer || sound == null) return;

        serverChannel.SendPacket(new CompanionSpatialSoundPacket
        {
            Sound = sound.ToString(),
            X = position.X + 0.5,
            Y = position.InternalY + 0.5,
            Z = position.Z + 0.5,
            Dimension = position.dimension,
            Pitch = pitch,
            Range = range,
            Volume = volume
        }, serverPlayer);
    }

    private static void PlaySoundAtEntity(Entity entity, string sound, float volume, float range = 24f, float pitch = 1f)
    {
        entity.World.PlaySoundAt(new AssetLocation(sound), entity, null, pitch, range, volume);
    }

    private void PlayActivityCommandSound(IServerPlayer owner, string mode)
    {
        string sound = mode switch
        {
            CompanionActivityMode.Follow => "feralkinshipcompanions:sounds/commands/follow",
            CompanionActivityMode.AtEase => "feralkinshipcompanions:sounds/commands/at-ease",
            CompanionActivityMode.Rest => "feralkinshipcompanions:sounds/commands/rest",
            CompanionActivityMode.ReturnHome => "feralkinshipcompanions:sounds/commands/return-home",
            _ => string.Empty
        };
        if (!string.IsNullOrWhiteSpace(sound))
        {
            PlaySoundAtEntity(owner.Entity, sound, 0.58f, 24f);
        }
    }

    private static void PlayControlSetSound(IServerPlayer owner)
    {
        PlaySoundAtEntity(owner.Entity, "game:sounds/effect/woodswitch", 0.28f, 12f, 1.08f);
    }

    private void PlayCartSound(string ownerUid, string sound, float volume, float range = 28f)
    {
        if (serverApi == null)
        {
            return;
        }
        BlockPos? cart = GetActiveCairnPosition(ownerUid);
        if (cart == null)
        {
            return;
        }
        serverApi.World.PlaySoundAt(new AssetLocation(sound), cart, 0.45, null, false, range, volume);
    }

    private void PlayExpeditionDepartureSounds(string ownerUid)
    {
        if (serverApi == null)
        {
            return;
        }
        PlayCartSound(ownerUid, "game:sounds/block/creak/woodcreak_long1", 0.62f, 30f);
        serverApi.Event.RegisterCallback(_ => PlayCartSound(ownerUid, "game:sounds/block/cloth", 0.34f, 22f), 180);
        serverApi.Event.RegisterCallback(_ => PlayCartSound(ownerUid, "game:sounds/block/planks", 0.40f, 24f), 430);
    }

    private void PlayExpeditionReturnSounds(string ownerUid)
    {
        if (serverApi == null)
        {
            return;
        }
        PlayCartSound(ownerUid, "game:sounds/effect/receptionbell", 0.60f, 34f);
        serverApi.Event.RegisterCallback(_ => PlayCartSound(ownerUid, "game:sounds/effect/receptionbell", 0.52f, 34f), 280);
        serverApi.Event.RegisterCallback(_ => PlayCartSound(ownerUid, "game:sounds/block/creak/woodcreak_long2", 0.55f, 30f), 520);
        serverApi.Event.RegisterCallback(_ => PlayCartSound(ownerUid, "game:sounds/block/planks", 0.34f, 24f), 820);
    }

    private void PlayCargoSoundRateLimited(string ownerUid, string phase, BlockPos position, string sound, float volume)
    {
        if (serverApi == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }
        string key = ownerUid + ":" + phase;
        long now = serverApi.World.ElapsedMilliseconds;
        if (lastCargoSoundAtMs.TryGetValue(key, out long last) && now - last < CargoSoundCooldownMs)
        {
            return;
        }
        lastCargoSoundAtMs[key] = now;
        serverApi.World.PlaySoundAt(new AssetLocation(sound), position, 0.45, null, false, 18f, volume);
    }

    private void OnSaveGameLoaded()
    {
        if (serverApi == null)
        {
            return;
        }

        companionContentReady = false;
        ledgerHydratedEntities.Clear();
        nextEarlyWarningScanAtMs.Clear();
        nextBreedingPartnerScanAtMs.Clear();
        nextBirthRetryAtMsByEntity.Clear();
        ResetWorkCartLoggingRuntimeState();
        packRepository?.Load();
        brambleRepository?.Load();
        if (packRepository?.MigratedExpeditionData == true
            || packRepository?.MigratedRetiredRoutes == true)
        {
            packRepository.Save();
            if (packRepository.MigratedRetiredRoutes)
                serverApi.Logger.Notification("[FeralKinshipCompanions] Retired ruin routes and refunded their purchased pack points.");
            if (packRepository.MigratedExpeditionData)
                serverApi.Logger.Notification("[FeralKinshipCompanions] Migrated expedition records to stable IDs and preserved all valid active parties and reports.");
        }
        if (!BrambleEnabled)
        {
            RemoveDisabledBrambleEntities();
            RefreshLoadedFoxPackRecords();
            ArmWorkCartLoggingStartupRecovery();
            return;
        }
        foreach (Entity bramble in serverApi.World.LoadedEntities.Values.Where(IsGuideFox).ToArray())
        {
            RegisterLoadedBramble(bramble);
        }
        RefreshLoadedFoxPackRecords();
        ArmWorkCartLoggingStartupRecovery();

        if (packRepository?.MigratedLegacyData == true)
        {
            serverApi.Logger.Notification(
                "[FeralKinshipCompanions] Migrated companion pack records to the authoritative v2 ledger."
            );
        }
    }

    private void OnGameWorldSave()
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        RefreshLoadedFoxPackRecords();
        packRepository.Save();
        brambleRepository?.Save();
    }

    private void RemoveDisabledBrambleEntities()
    {
        if (serverApi == null || brambleRepository?.Loaded != true)
        {
            return;
        }

        foreach (Entity bramble in serverApi.World.LoadedEntities.Values
            .Where(IsGuideFox)
            .ToArray())
        {
            loadedBrambles.Remove(bramble.EntityId);
            if (bramble.Alive)
            {
                bramble.Die(EnumDespawnReason.Removed);
            }
        }

        foreach (BramblePlayerRecord record in brambleRepository.All)
        {
            record.CurrentEntityId = 0;
        }

        brambleRepository.Save();
    }

    private void OnPlayerDisconnect(IServerPlayer player)
    {
        RebaseOwnerCompanionFoodClocks(player.PlayerUID);
        brambleReadyPendingOwners.Remove(player.PlayerUID);
        idleTestCommandsByOwner.Remove(player.PlayerUID);
        if (socialViewByOwner.Remove(player.PlayerUID, out long viewedEntityId))
        {
            EndMenuAttention(viewedEntityId);
        }
        if (perkViewByOwner.Remove(player.PlayerUID, out long perkEntityId))
        {
            EndMenuAttention(perkEntityId);
        }
        if (backpackViewByOwner.Remove(player.PlayerUID, out long backpackEntityId))
        {
            EndMenuAttention(backpackEntityId);
        }
        foreach (long entityId in backpackPreflights
                     .Where(entry => string.Equals(entry.Value.OwnerUid, player.PlayerUID, StringComparison.Ordinal))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            backpackPreflights.Remove(entityId);
        }
        packViewers.Remove(player.PlayerUID);
        ownerAttackers.Remove(player.PlayerUID);
        ownerAttackTargets.Remove(player.PlayerUID);
        playerCampAreaStates.Remove(player.PlayerUID);
        ReleaseAmbientLifeForOwner(player.PlayerUID);
    }

    private void OnPlayerDeath(IServerPlayer player, DamageSource damageSource)
    {
        if (serverApi == null || brambleRepository?.Loaded != true)
        {
            return;
        }

        BramblePlayerRecord record = brambleRepository.GetOrCreate(player.PlayerUID);
        List<Entity> ownedBrambles = new();

        if (record.CurrentEntityId > 0
            && serverApi.World.GetEntityById(record.CurrentEntityId) is Entity current
            && IsGuideFox(current)
            && string.Equals(GetCompanionOwnerUid(current), player.PlayerUID, StringComparison.Ordinal))
        {
            ownedBrambles.Add(current);
        }

        foreach (Entity loaded in loadedBrambles.Values
            .Where(entity => entity.Alive
                && IsGuideFox(entity)
                && string.Equals(GetCompanionOwnerUid(entity), player.PlayerUID, StringComparison.Ordinal))
            .ToArray())
        {
            if (!ownedBrambles.Any(entity => entity.EntityId == loaded.EntityId))
            {
                ownedBrambles.Add(loaded);
            }
        }

        foreach (Entity bramble in ownedBrambles)
        {
            ObserveBramblePosition(record, bramble);
        }

        // Invalidate the old instance even if it is currently outside the
        // loaded area. When that chunk eventually loads, its old token will
        // fail RegisterLoadedBramble and the stale entity will be removed.
        record.CurrentEntityId = 0;
        record.EntityToken = Guid.NewGuid().ToString("N");
        foreach (Entity bramble in ownedBrambles)
        {
            loadedBrambles.Remove(bramble.EntityId);
            if (bramble.Alive)
            {
                bramble.Die(EnumDespawnReason.Removed);
            }
        }

        brambleRepository.Save();
    }

    private void OnPlayerReady(IServerPlayer player)
    {
        HandlePlayerReady(player, forceBrambleNearPlayer: false);
    }

    private void OnPlayerRespawn(IServerPlayer player)
    {
        HandlePlayerReady(player, forceBrambleNearPlayer: true);
    }

    private void HandlePlayerReady(IServerPlayer player, bool forceBrambleNearPlayer)
    {
        RebaseOwnerCompanionFoodClocks(player.PlayerUID);
        if (player.Entity == null)
        {
            return;
        }

        if (player.Entity.GetBehavior<EntityBehaviorFeralKinshipOwnerCombatTracker>() == null)
        {
            player.Entity.AddBehavior(new EntityBehaviorFeralKinshipOwnerCombatTracker(player.Entity, this));
        }

        IReadOnlyList<FoxExpeditionSummaryPacket> pendingReports = packRepository?.GetPendingExpeditionReports(player.PlayerUID)
            ?? Array.Empty<FoxExpeditionSummaryPacket>();
        if (pendingReports.Count > 0)
        {
            SendExpeditionReturnNotification(player, pendingReports.Count);
            foreach (FoxExpeditionSummaryPacket report in pendingReports)
            {
                report.NotificationPending = false;
            }
            packRepository?.Save();
        }

        if (!BrambleEnabled)
        {
            return;
        }

        // PlayerReady is the server event that proves the client selected its
        // character and class. PlayerNowPlaying is also used as a fallback for
        // server/client paths that do not emit PlayerReady reliably. Neither
        // path is allowed to open Bramble's UI until a delayed check sees the
        // connection in the actual Playing state.
        ScheduleBrambleAfterPlayerReady(player, forceBrambleNearPlayer);
    }

    private void ScheduleBrambleAfterPlayerReady(
        IServerPlayer player,
        bool forceBrambleNearPlayer,
        int attempt = 0)
    {
        if (serverApi == null || !BrambleEnabled)
        {
            return;
        }

        if (brambleReadyPendingOwners.TryGetValue(player.PlayerUID, out bool alreadyForced))
        {
            if (forceBrambleNearPlayer && !alreadyForced)
            {
                brambleReadyPendingOwners[player.PlayerUID] = true;
            }
            return;
        }
        brambleReadyPendingOwners[player.PlayerUID] = forceBrambleNearPlayer;

        int delayMs = attempt == 0 ? BrambleReadyDelayMs : BrambleReadyRetryDelayMs;
        serverApi.Event.RegisterCallback(_ =>
        {
            bool forceNearPlayer = brambleReadyPendingOwners.TryGetValue(player.PlayerUID, out bool forced)
                && forced;
            brambleReadyPendingOwners.Remove(player.PlayerUID);
            if (serverApi?.World.PlayerByUid(player.PlayerUID) is not IServerPlayer online
                || online.ConnectionState != EnumClientState.Playing
                || online.Entity == null
                || !online.Entity.Alive)
            {
                if (attempt < BrambleReadyMaxAttempts
                    && serverApi?.World.PlayerByUid(player.PlayerUID) is IServerPlayer stillOnline)
                {
                    ScheduleBrambleAfterPlayerReady(stillOnline, forceNearPlayer, attempt + 1);
                }
                return;
            }

            EnsureBrambleForPlayer(online, forceNearPlayer);
            BramblePlayerRecord state = brambleRepository?.GetOrCreate(online.PlayerUID) ?? new BramblePlayerRecord();
            if (!BrambleOnboardingState.IsResolved(state.OnboardingState))
            {
                SendBrambleState(online, showOnboarding: true);
            }
        }, delayMs);
    }

    internal bool TryOpenFoxSocialGui(Entity entity, string initialTab = "overview")
    {
        if (clientApi?.World.Player.Entity == null || !entity.Alive || !IsTamedFox(entity))
        {
            return false;
        }

        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (!string.Equals(ownerId, clientApi.World.Player.PlayerUID, StringComparison.Ordinal))
        {
            return false;
        }

        long targetEntityId = entity.EntityId;
        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        socialDialog = new GuiDialogFeralKinshipFoxSocial(clientApi, this, targetEntityId, initialTab);
        socialDialog.TryOpen();
        clientChannel?.SendPacket(new FoxSocialRequestPacket
        {
            Action = FoxSocialRequestAction.Open,
            TargetEntityId = targetEntityId
        });
        return true;
    }

    internal void CloseFoxSocialGui()
    {
        socialDialog?.TryClose();
    }

    internal bool TryOpenFoxRenameGui(
        long entityId,
        string currentName,
        bool fromNameSuggestion = false)
    {
        if (clientApi?.World.Player.Entity == null)
        {
            return false;
        }

        if (renameDialog?.IsOpened() == true
            || (!fromNameSuggestion && nameSuggestionDialog?.IsOpened() == true))
        {
            return false;
        }

        if (clientApi.World.GetEntityById(entityId) is Entity target && IsGuideFox(target))
        {
            return false;
        }

        // The social window may remain open while the animal moves just
        // outside the short interaction range.  Let the rename dialog open;
        // the server still performs the authoritative ownership/range check
        // when the name is submitted.  The old client-side lookup made the
        // button appear dead in that perfectly normal situation.
        FeralKinshipPetAiNamePromptPatch.CloseOpenPetAiProfile(entityId);
        socialDialog?.TryClose();
        if (fromNameSuggestion)
        {
            nameSuggestionDialog?.TryClose();
        }
        renameDialog?.TryClose();
        renameDialog = new GuiDialogFeralKinshipRename(clientApi, this, entityId, currentName);
        renameDialog.TryOpen();
        return true;
    }

    internal bool TryOpenCompanionNameSuggestionGui(
        long entityId,
        string suggestedName,
        bool isNewborn = false)
    {
        if (clientApi?.World.Player.Entity == null || string.IsNullOrWhiteSpace(suggestedName))
        {
            return false;
        }

        if (nameSuggestionDialog?.IsOpened() == true || renameDialog?.IsOpened() == true)
        {
            return false;
        }

        if (clientApi.World.GetEntityById(entityId) is Entity target && IsGuideFox(target))
        {
            return false;
        }

        FeralKinshipPetAiNamePromptPatch.CloseOpenPetAiProfile(entityId);
        socialDialog?.TryClose();
        nameSuggestionDialog = new GuiDialogFeralKinshipNameSuggestion(
            clientApi,
            this,
            entityId,
            suggestedName,
            isNewborn
        );
        nameSuggestionDialog.TryOpen();
        return true;
    }

    internal int GetOwnedCompanionCountClient()
    {
        if (clientApi?.World.Player == null)
        {
            return 0;
        }

        string ownerUid = clientApi.World.Player.PlayerUID;
        return clientApi.World.LoadedEntities.Values.Count(entity =>
            entity.Alive
            && IsTamedFox(entity)
            && string.Equals(GetCompanionOwnerUid(entity), ownerUid, StringComparison.Ordinal));
    }

    internal bool TryOpenFoxSocialGui(long entityId)
    {
        Entity? entity = clientApi?.World.GetEntityById(entityId);
        return entity != null && TryOpenFoxSocialGui(entity);
    }

    internal bool TryOpenFoxPerksGui(Entity entity)
    {
        if (clientApi?.World.Player.Entity == null || !entity.Alive || !IsTamedFox(entity)
            || IsCompanionJuvenile(entity))
        {
            return false;
        }

        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (!string.Equals(ownerId, clientApi.World.Player.PlayerUID, StringComparison.Ordinal))
        {
            return false;
        }

        long targetEntityId = entity.EntityId;
        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        perkDialog = new GuiDialogFeralKinshipFoxPerks(clientApi, this, targetEntityId);
        perkDialog.TryOpen();
        clientChannel?.SendPacket(new FoxSocialRequestPacket
        {
            Action = FoxSocialRequestAction.OpenPerks,
            TargetEntityId = targetEntityId
        });
        return true;
    }

    internal bool TryOpenFoxPerksGui(long entityId)
    {
        Entity? entity = clientApi?.World.GetEntityById(entityId);
        return entity != null && TryOpenFoxPerksGui(entity);
    }

    internal bool TryOpenFoxDeveloperGui(Entity entity)
    {
        if (clientApi?.World.Player.Entity == null
            || !CanUseDeveloperToolsClient()
            || !entity.Alive
            || !IsTamedFox(entity))
        {
            return false;
        }

        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (!string.Equals(ownerId, clientApi.World.Player.PlayerUID, StringComparison.Ordinal))
        {
            return false;
        }

        long targetEntityId = entity.EntityId;
        string targetFoxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        developerDialog = new GuiDialogFeralKinshipFoxDeveloper(clientApi, this, targetEntityId, targetFoxId);
        developerDialog.TryOpen();
        clientChannel?.SendPacket(new FoxSocialRequestPacket
        {
            Action = FoxSocialRequestAction.Open,
            TargetEntityId = targetEntityId
        });
        return true;
    }

    /// <summary>Opens the developer panel without requiring a target entity.</summary>
    internal bool TryOpenFoxDeveloperGui()
    {
        if (clientApi?.World.Player.Entity == null || !CanUseDeveloperToolsClient())
        {
            return false;
        }

        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        developerDialog = new GuiDialogFeralKinshipFoxDeveloper(clientApi, this, 0);
        developerDialog.TryOpen();
        SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.Open);
        return true;
    }

    internal bool TrySelectFoxDeveloperTarget(long entityId)
    {
        Entity? entity = clientApi?.World.GetEntityById(entityId);
        return entity != null && TryOpenFoxDeveloperGui(entity);
    }

    internal bool TrySelectFoxDeveloperRecord(string foxId, long entityId)
    {
        if (string.IsNullOrWhiteSpace(foxId))
        {
            return false;
        }

        Entity? entity = clientApi?.World.GetEntityById(entityId);
        if (entity != null
            && clientApi != null
            && IsOwnedCompanion(entity, clientApi.World.Player.PlayerUID))
        {
            return TryOpenFoxDeveloperGui(entity);
        }

        if (clientApi?.World.Player.Entity == null || !CanUseDeveloperToolsClient())
        {
            return false;
        }

        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        developerDialog = new GuiDialogFeralKinshipFoxDeveloper(clientApi, this, entityId, foxId);
        developerDialog.TryOpen();
        SendCompanionRecoveryAction(CompanionRecoveryRequestPacket.OpenDeveloper, foxId);
        return true;
    }

    internal bool TryOpenFoxDeveloperGui(long entityId)
    {
        Entity? entity = clientApi?.World.GetEntityById(entityId);
        return entity != null && TryOpenFoxDeveloperGui(entity);
    }

    internal void SendFoxSocialAction(
        long entityId,
        int action,
        string requestedJob = "",
        IEnumerable<string>? selectedFoxIds = null,
        string selectedMissingFoxId = "",
        bool prepareExpedition = false,
        long expeditionId = 0,
        string developerFoxId = "",
        long scavengeSiteId = 0,
        string scavengeFocus = "",
        string scoutDuration = "")
    {
        clientChannel?.SendPacket(new FoxSocialRequestPacket
        {
            Action = action,
            TargetEntityId = entityId,
            RequestedJob = requestedJob,
            SelectedMissingFoxId = selectedMissingFoxId,
            PrepareExpedition = prepareExpedition,
            ExpeditionId = expeditionId,
            DeveloperFoxId = developerFoxId ?? string.Empty,
            ScavengeSiteId = scavengeSiteId,
            ScavengeFocus = scavengeFocus ?? string.Empty,
            ScoutDuration = scoutDuration ?? string.Empty,
            SelectedFoxIds = selectedFoxIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList()
                ?? new List<string>()
        });
    }

    internal void RequestCompanionNameSuggestion(long entityId)
    {
        SendFoxSocialAction(entityId, FoxSocialRequestAction.RequestNameSuggestion);
    }

    internal bool CanUseDeveloperToolsClient()
    {
        IClientPlayer? player = clientApi?.World.Player;
        return player != null
            && (player.WorldData.CurrentGameMode == EnumGameMode.Creative
                || player.HasPrivilege(Privilege.controlserver)
                || player.HasPrivilege(Privilege.root));
    }

    internal bool ShouldShowCompanionTutorial()
    {
        EnsureCompanionTutorialConfigLoaded();
        return !companionTutorialSeen;
    }

    internal void MarkCompanionTutorialSeen()
    {
        if (companionTutorialSeen)
        {
            return;
        }

        companionTutorialConfigLoaded = true;
        companionTutorialSeen = true;
        StoreCompanionTutorialConfig();
    }

    internal bool ShouldShowCompanionPackGuide()
    {
        EnsureCompanionTutorialConfigLoaded();
        return !companionPackGuideSeen;
    }

    internal void MarkCompanionPackGuideSeen()
    {
        if (companionPackGuideSeen)
        {
            return;
        }

        companionPackGuideSeen = true;
        StoreCompanionTutorialConfig();
    }

    internal bool ShouldShowCompanionPackCartGuide()
    {
        EnsureCompanionTutorialConfigLoaded();
        return !companionPackCartGuideSeen;
    }

    internal void MarkCompanionPackCartGuideSeen()
    {
        if (companionPackCartGuideSeen)
        {
            return;
        }

        companionPackCartGuideSeen = true;
        StoreCompanionTutorialConfig();
    }

    internal bool ShouldShowCompanionTalentGuide()
    {
        EnsureCompanionTutorialConfigLoaded();
        return !companionTalentGuideSeen;
    }

    internal void MarkCompanionTalentGuideSeen()
    {
        if (companionTalentGuideSeen)
        {
            return;
        }

        companionTalentGuideSeen = true;
        StoreCompanionTutorialConfig();
    }

    private void StoreCompanionTutorialConfig()
    {
        if (clientApi == null)
        {
            return;
        }

        ConfigDefaults.StorePreservingUnknown(
            clientApi,
            CompanionTutorialConfigFileName,
            new CompanionTutorialConfig
            {
                WelcomeVersion = companionTutorialSeen ? CompanionTutorialVersion : 0,
                PackGuideVersion = companionPackGuideSeen ? CompanionPackGuideVersion : 0,
                PackCartGuideVersion = companionPackCartGuideSeen ? CompanionPackCartGuideVersion : 0,
                TalentGuideVersion = companionTalentGuideSeen ? CompanionTalentGuideVersion : 0
            });
    }

    private void EnsureCompanionTutorialConfigLoaded()
    {
        if (companionTutorialConfigLoaded || clientApi == null)
        {
            return;
        }

        companionTutorialConfigLoaded = true;
        try
        {
            CompanionTutorialConfig saved = ConfigDefaults.LoadAndUpdate(
                clientApi,
                CompanionTutorialConfigFileName,
                () => new CompanionTutorialConfig());
            companionTutorialSeen = saved?.WelcomeVersion >= CompanionTutorialVersion;
            companionPackGuideSeen = saved?.PackGuideVersion >= CompanionPackGuideVersion;
            companionPackCartGuideSeen = saved?.PackCartGuideVersion >= CompanionPackCartGuideVersion;
            companionTalentGuideSeen = saved?.TalentGuideVersion >= CompanionTalentGuideVersion;
        }
        catch (Exception e)
        {
            companionTutorialSeen = false;
            clientApi.Logger.Warning(
                "[FeralKinshipCompanions] Could not load the local tutorial state; the welcome guide may appear again. {0}",
                e.Message
            );
        }
    }

    internal bool TryOpenTrainingDummyGui(TrainingDummyStatePacket packet)
    {
        if (clientApi == null)
        {
            return false;
        }

        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        trainingDummyDialog?.TryClose();
        trainingDummyDialog = new GuiDialogFeralKinshipTrainingDummy(
            clientApi,
            this,
            packet.TargetEntityId
        );
        trainingDummyDialog.TryOpen();
        trainingDummyDialog.ApplyState(packet);
        return true;
    }

    internal void SendTrainingDummyAction(long entityId, int action)
    {
        clientChannel?.SendPacket(new TrainingDummyActionPacket
        {
            Action = action,
            TargetEntityId = entityId
        });
    }

    internal bool TryOpenStorageRoutingGui(BlockPos selectedPos)
    {
        if (clientApi == null || selectedPos == null)
        {
            return false;
        }

        socialDialog?.TryClose();
        packDialog?.TryClose();
        workCartDialog?.TryClose();
        storageRoutingDialog?.TryClose();

        FoxStorageRoutingStatePacket waiting = new()
        {
            X = selectedPos.X,
            Y = selectedPos.Y,
            Z = selectedPos.Z,
            Dimension = selectedPos.dimension,
            Message = "Reading this storage container..."
        };
        storageRoutingDialog = new GuiDialogFeralKinshipStorageRouting(clientApi, this, waiting);
        storageRoutingDialog.TryOpen();
        clientChannel?.SendPacket(new FoxStorageRoutingRequestPacket
        {
            Action = FoxStorageRoutingRequestPacket.Open,
            X = selectedPos.X,
            Y = selectedPos.Y,
            Z = selectedPos.Z,
            Dimension = selectedPos.dimension
        });
        return true;
    }

    internal void SendStorageRoutingAction(
        FoxStorageRoutingStatePacket state,
        int action,
        int categoryMask = 0,
        string itemCode = "")
    {
        clientChannel?.SendPacket(new FoxStorageRoutingRequestPacket
        {
            Action = action,
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Dimension = state.Dimension,
            CategoryMask = categoryMask,
            ItemCode = itemCode ?? string.Empty
        });
    }

    internal void SendFoxBedAssignment(FoxBedAssignmentStatePacket state, string foxId, bool bramble = false)
    {
        clientChannel?.SendPacket(new FoxBedAssignmentRequestPacket
        {
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Dimension = state.Dimension,
            FoxId = foxId ?? string.Empty,
            Bramble = bramble
        });
    }

    internal void SendFoxWorkCartAssignment(FoxWorkCartAssignmentStatePacket state, string foxId, bool assign, bool assignWithinRadius = false)
    {
        clientChannel?.SendPacket(new FoxWorkCartAssignmentRequestPacket
        {
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Dimension = state.Dimension,
            FoxId = foxId ?? string.Empty,
            Assign = assign,
            AssignWithinRadius = assignWithinRadius
        });
    }

    internal void SendFoxWorkCartLogging(FoxWorkCartAssignmentStatePacket state, bool enabled)
    {
        clientChannel?.SendPacket(new FoxWorkCartAssignmentRequestPacket
        {
            X = state.X,
            Y = state.Y,
            Z = state.Z,
            Dimension = state.Dimension,
            SetLogging = true,
            LoggingEnabled = enabled
        });
    }

    internal bool TryOpenFoxPackGui(long sourceEntityId)
    {
        if (clientApi == null)
        {
            return false;
        }

        socialDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        packDialog = new GuiDialogFeralKinshipFoxPack(clientApi, this, sourceEntityId);
        packDialog.TryOpen();
        clientChannel?.SendPacket(new FoxSocialRequestPacket
        {
            Action = FoxSocialRequestAction.OpenPack,
            TargetEntityId = sourceEntityId
        });
        return true;
    }

    internal bool TryOpenCompanionRecoveryGui()
    {
        if (clientApi == null || !CanUseDeveloperToolsClient())
        {
            return false;
        }

        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        storageRoutingDialog?.TryClose();
        recoveryDialog?.TryClose();
        recoveryDialog = new GuiDialogFeralKinshipCompanionRecovery(clientApi, this);
        recoveryDialog.TryOpen();
        clientChannel?.SendPacket(new CompanionRecoveryRequestPacket
        {
            Action = CompanionRecoveryRequestPacket.Open
        });
        return true;
    }

    internal void SendCompanionRecoveryAction(int action, string foxId = "")
    {
        clientChannel?.SendPacket(new CompanionRecoveryRequestPacket
        {
            Action = action,
            FoxId = foxId ?? string.Empty
        });
    }

    private void OpenFoxPackGuiFromCairn()
    {
        if (clientApi == null)
        {
            return;
        }

        socialDialog?.TryClose();
        developerDialog?.TryClose();
        packDialog?.TryClose();
        perkDialog?.TryClose();
        packDialog = new GuiDialogFeralKinshipFoxPack(clientApi, this, 0);
        packDialog.TryOpen();
        if (lastPackState != null)
        {
            packDialog.ApplyState(lastPackState);
        }
    }

    internal static bool IsTamedFox(Entity entity)
    {
        return !IsGuideFox(entity)
            && TryGetCompanionSpecies(entity, out _)
            && string.Equals(
                GetDomesticationStatus(entity)?.GetString("domesticationLevel"),
                "DOMESTICATED",
                StringComparison.OrdinalIgnoreCase
            );
    }

    internal static string GetCompanionOwnerUid(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetString("owner", string.Empty) ?? string.Empty;
    }

    internal static string GetCompanionActivityMode(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string stored = status?.GetString(ActivityModeKey, string.Empty) ?? string.Empty;
        if (CompanionActivityMode.IsValid(stored)) return CompanionActivityMode.Normalize(stored);

        // One-time compatibility with animals saved under the old PetAI menu.
        string legacy = entity.WatchedAttributes.GetString("activeCommand", string.Empty);
        return string.Equals(legacy, "followmaster", StringComparison.OrdinalIgnoreCase)
            ? CompanionActivityMode.Follow
            : CompanionActivityMode.AtEase;
    }

    internal static bool IsWhistleHoldActive(Entity entity)
    {
        return (GetDomesticationStatus(entity)?.GetLong(WhistleHoldEndsUtcMsKey, 0) ?? 0) > UtcNowMs();
    }

    internal static bool IsGroundCleanupEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(GroundCleanupEnabledKey, false) == true;
    }

    private static bool IsDutyOptionEnabled(Entity entity, string key)
    {
        // The option keys were added after the parent duties. Missing keys
        // therefore retain the old all-inclusive behavior for existing saves.
        return GetDomesticationStatus(entity)?.GetBool(key, true) != false;
    }

    internal static bool IsGroundDroppedItemsEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundDroppedItemsEnabledKey);
    internal static bool IsGroundCattailsEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundCattailsEnabledKey);
    internal static bool IsGroundFlintEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundFlintEnabledKey);
    internal static bool IsGroundSticksEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundSticksEnabledKey);
    internal static bool IsGroundBouldersEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundBouldersEnabledKey);
    internal static bool IsGroundRocksEnabled(Entity entity) => IsDutyOptionEnabled(entity, GroundRocksEnabledKey);

    internal int GetGroundCleanupSearchRange(Entity entity)
    {
        return (int)(IsFoxAssignedToWorkCart(entity)
            ? GetWorkCartRadius(entity)
            : GetCompanionCampRadius(entity));
    }

    internal static bool IsMowLawnEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(MowLawnEnabledKey, false) == true;
    }

    internal static bool IsFinishedProductsEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(FinishedProductsEnabledKey, false) == true;
    }

    internal static bool IsFinishedCropsEnabled(Entity entity) => IsDutyOptionEnabled(entity, FinishedCropsEnabledKey);
    internal static bool IsFinishedBerriesEnabled(Entity entity) => IsDutyOptionEnabled(entity, FinishedBerriesEnabledKey);
    internal static bool IsFinishedMushroomsEnabled(Entity entity) => IsDutyOptionEnabled(entity, FinishedMushroomsEnabledKey);

    internal static bool IsFlowerRemovalEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(FlowerRemovalEnabledKey, false) == true;
    }

    internal static bool IsSnowShovelingEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(SnowShovelingEnabledKey, false) == true;
    }

    internal static bool IsCharcoalShovelingEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(CharcoalShovelingEnabledKey, false) == true;
    }

    internal static bool IsSnowballCollectionEnabled(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(SnowballCollectionEnabledKey, false) == true;
    }

    internal static bool IsGeneralStorageSortingEnabled(Entity entity)
    {
        // Missing means enabled so worlds from before this opt-out retain the
        // routing behavior they already had.
        return GetDomesticationStatus(entity)?.GetBool(GeneralStorageSortingEnabledKey, true) != false;
    }

    internal static int GetPermanentRangeRank(Entity entity)
    {
        return Math.Max(0, GetDomesticationStatus(entity)?.GetInt(PackRangeTalentRankKey, 0) ?? 0);
    }

    internal static float GetCompanionCampRadius(Entity entity)
    {
        return BaseCompanionCampRadius * (1f + GetPermanentRangeRank(entity) * PermanentRangeIncreasePerRank);
    }

    internal static float GetWorkCartRadius(Entity entity)
    {
        return BaseWorkCartRadius * (1f + GetPermanentRangeRank(entity) * PermanentRangeIncreasePerRank);
    }

    private float GetOwnerCampRadius(string ownerUid)
    {
        int rank = packRepository?.GetPackTalentRank(ownerUid, "far-reaching-pack") ?? 0;
        return BaseCompanionCampRadius * (1f + rank * PermanentRangeIncreasePerRank);
    }

    private float GetOwnerWorkCartRadius(string ownerUid)
    {
        int rank = packRepository?.GetPackTalentRank(ownerUid, "far-reaching-pack") ?? 0;
        return BaseWorkCartRadius * (1f + rank * PermanentRangeIncreasePerRank);
    }

    internal static string GetCompanionFollowDistance(Entity entity)
    {
        return CompanionFollowDistance.Normalize(
            GetDomesticationStatus(entity)?.GetString(FollowDistanceKey, CompanionFollowDistance.Normal)
        );
    }

    internal static string GetCompanionCombatStyle(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string stored = status?.GetString(CombatStyleKey, string.Empty) ?? string.Empty;
        if (CompanionCombatStyle.IsValid(stored)) return CompanionCombatStyle.Normalize(stored);

        return entity.WatchedAttributes.GetString("aggressionLevel", string.Empty).ToUpperInvariant() switch
        {
            "PASSIVE" => CompanionCombatStyle.Passive,
            "PROTECTIVE" => CompanionCombatStyle.Protect,
            "AGGRESSIVE" => CompanionCombatStyle.Aggressive,
            _ => CompanionCombatStyle.Defensive
        };
    }

    internal static string GetCompanionRiskTolerance(Entity entity)
    {
        return CompanionRiskTolerance.Normalize(
            GetDomesticationStatus(entity)?.GetString(RiskToleranceKey, CompanionRiskTolerance.Steady)
        );
    }

    internal static long GetCompanionActivityElapsedMs(Entity entity)
    {
        long started = GetDomesticationStatus(entity)?.GetLong(ActivityStartedUtcMsKey, 0) ?? 0;
        return started <= 0 ? 0 : Math.Max(0, UtcNowMs() - started);
    }

    internal static bool CanRunCompanionCommandTask(Entity entity)
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return (IsTamedFox(entity) || IsGuideFox(entity))
            && entity.Alive
            && entity.State == EnumEntityState.Active
            && !IsFoxAwayFromWorld(entity)
            && !IsFoxIncapacitated(entity)
            && system?.IsCompanionFoodRestricted(entity) != true;
    }

    internal static bool CanRunCompanionCombatTask(Entity entity)
    {
        string combatStyle = GetCompanionCombatStyle(entity);
        bool targetedAttack = GetDomesticationStatus(entity)?
            .GetBool(TargetedAttackActiveKey, false) == true;
        bool automaticRetreat = GetDomesticationStatus(entity)?
            .GetBool(AutomaticRetreatActiveKey, false) == true;
        return CanRunCompanionCommandTask(entity)
            && (targetedAttack
                || (GetCompanionActivityMode(entity) != CompanionActivityMode.ReturnHome
                    && (!automaticRetreat || combatStyle == CompanionCombatStyle.Flee)
                    && combatStyle != CompanionCombatStyle.Passive));
    }

    internal static bool TryGetCompanionSpecies(Entity entity, out CompanionSpeciesProfile profile)
    {
        if (CompanionSpeciesCatalog.TryGetByTameEntityCode(entity.Code, out profile))
        {
            return true;
        }

        if (IsCompanionJuvenile(entity)
            && CompanionBreedingCatalog.TryGetForJuvenile(entity, out CompanionBreedingDefinition definition)
            && CompanionBreedingCatalog.TryGetSpeciesProfile(definition.SpeciesId, out profile))
        {
            return true;
        }

        profile = null!;
        return false;
    }

    private static string GetCompanionSpeciesDisplayName(FoxPackRecordV2 record)
    {
        return CompanionSpeciesCatalog.TryGetById(record.SpeciesId, out CompanionSpeciesProfile profile)
            ? profile.DisplayName
            : "Companion";
    }

    private static CompanionSpeciesProfile GetCompanionSpecies(Entity entity)
    {
        return TryGetCompanionSpecies(entity, out CompanionSpeciesProfile profile)
            ? profile
            : CompanionSpeciesCatalog.Fox;
    }

    internal static string ResolveCompanionAnimation(Entity entity, string requestedCode)
    {
        CompanionSpeciesProfile profile = GetCompanionSpecies(entity);
        if (profile.UsesExternalPetAiTaskSet)
        {
            return ResolveTamablesFotsaAnimation(entity, requestedCode, profile);
        }

        return requestedCode.ToLowerInvariant() switch
        {
            "walk" => profile.ResolveAnimationOrFallback(profile.WalkAnimationCode),
            "run" => profile.ResolveAnimationOrFallback(profile.RunAnimationCode),
            "sit" => profile.ResolveAnimationOrFallback(profile.SitAnimationCode),
            "sleep" => profile.ResolveAnimationOrFallback(profile.SleepAnimationCode),
            "lie" => profile.ResolveAnimationOrFallback(profile.LieAnimationCode),
            "attack" => profile.ResolveAnimationOrFallback(profile.AttackAnimationCode),
            _ => profile.ResolveAnimationOrFallback(requestedCode)
        };
    }

    private static string ResolveTamablesFotsaAnimation(
        Entity entity,
        string requestedCode,
        CompanionSpeciesProfile profile)
    {
        string[] candidates = requestedCode.ToLowerInvariant() switch
        {
            "walk" => new[] { "walk", "wander", "seekentity", "gallop", "run" },
            "run" => new[] { "run", "gallop", "walk" },
            "sit" => new[] { "sit" },
            "sleep" => new[] { "sleep", "lie" },
            "lie" => new[] { "lie", "sleep" },
            "attack" => new[] { "attack", "meleeattack" },
            _ => new[] { requestedCode }
        };

        if (entity.Properties.Client?.Animations != null)
        {
            foreach (string candidate in candidates)
            {
                AnimationMetaData? match = entity.Properties.Client.Animations.FirstOrDefault(animation =>
                    string.Equals(animation.Code, candidate, StringComparison.OrdinalIgnoreCase));
                if (match != null && !string.IsNullOrWhiteSpace(match.Code))
                {
                    return match.Code;
                }
            }
        }

        return requestedCode.ToLowerInvariant() switch
        {
            "walk" => profile.ResolveAnimationOrFallback(profile.WalkAnimationCode),
            "run" => profile.ResolveAnimationOrFallback(profile.RunAnimationCode),
            "sit" => profile.ResolveAnimationOrFallback(profile.SitAnimationCode),
            "sleep" => profile.ResolveAnimationOrFallback(profile.SleepAnimationCode),
            "lie" => profile.ResolveAnimationOrFallback(profile.LieAnimationCode),
            "attack" => profile.ResolveAnimationOrFallback(profile.AttackAnimationCode),
            _ => profile.ResolveAnimationOrFallback(requestedCode)
        };
    }

    internal static float GetCompanionArrivalRadius(Entity entity)
    {
        return GetCompanionSpecies(entity).SuggestedArrivalRadius;
    }

    private static bool IsFoxBedCode(AssetLocation code)
    {
        return string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.Ordinal)
            && code.Path.StartsWith(FoxBedBlockPathPrefix, StringComparison.Ordinal);
    }

    private static bool IsPackMarkerCode(AssetLocation code)
    {
        return string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.Ordinal)
            && (string.Equals(code.Path, PackCairnBlockPath, StringComparison.Ordinal)
                || code.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal));
    }

    private static bool IsWorkCartCode(AssetLocation code)
    {
        return string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.Ordinal)
            && code.Path.StartsWith(WorkCartBlockPathPrefix, StringComparison.Ordinal);
    }

    private static string GetWorkCartKind(AssetLocation code)
    {
        if (code.Path.StartsWith("workcart-logging-", StringComparison.Ordinal)) return "logging";
        if (code.Path.StartsWith("workcart-mining-", StringComparison.Ordinal)) return "mining";
        if (code.Path.StartsWith("workcart-quarrying-", StringComparison.Ordinal)) return "quarrying";
        return "generic";
    }

    internal void OnPackMarkerRemoved(BlockPos pos, bool isBed)
    {
        if (packRepository?.Loaded != true)
        {
            return;
        }

        if (isBed && packRepository.GetBed(pos) is FoxBedRecord bed
            && !string.IsNullOrWhiteSpace(bed.BrambleOwnerUid))
        {
            ClearBrambleBedAssignment(bed.BrambleOwnerUid);
        }

        bool changed = isBed ? packRepository.RemoveBed(pos) : packRepository.RemoveCairn(pos);
        if (changed)
        {
            packRepository.Save();
        }
    }

    internal void OnWorkCartRemoved(BlockPos pos)
    {
        ClearWorkCartLoggingQueue(pos);
        if (packRepository?.Loaded == true && packRepository.RemoveWorkCart(pos))
        {
            packRepository.Save();
        }
    }

    internal void OnPackAmenityRemoved(BlockPos pos)
    {
        if (packRepository?.Loaded == true && packRepository.RemoveAmenity(pos)) packRepository.Save();
    }

    internal bool OnPackAmenityInteracted(IPlayer byPlayer, BlockPos pos, bool announce = true)
    {
        if (byPlayer is not IServerPlayer player || packRepository?.Loaded != true) return false;
        FoxPackAmenityRecord? amenity = packRepository.GetAmenity(pos);
        if (amenity == null || !string.Equals(amenity.OwnerUid, player.PlayerUID, StringComparison.Ordinal))
        {
            player.SendMessage(GlobalConstants.GeneralChatGroup, "This communal pack space belongs to the player who placed it.", EnumChatType.Notification);
            return false;
        }
        if (announce)
        {
            string label = amenity.Kind switch { "dining" => "Dining board", "lounge" => "Resting rug", "play" => "Play post", "lookout" => "Lookout perch", _ => "Pack space" };
            player.SendMessage(GlobalConstants.GeneralChatGroup, $"{label}: every companion in this pack may use it when their mood and personality suit it.", EnumChatType.Notification);
        }
        return true;
    }

    internal bool HasAnyAmbientDutyEnabled(Entity fox)
    {
        return IsGroundCleanupEnabled(fox)
            || IsFinishedProductsEnabled(fox)
            || IsFlowerRemovalEnabled(fox)
            || IsSnowShovelingEnabled(fox)
            || IsCharcoalShovelingEnabled(fox)
            || IsWorkCartLoggingCleanupEnabled(fox)
            || IsGeneralStorageSortingEnabled(fox);
    }

    /// <summary>
    /// Selects one ambient duty using a single, explicit priority order.
    /// Individual task instances no longer compete by comparing fractional
    /// priorities; this method is the only place where ordinary duty order is
    /// decided.
    /// </summary>
    internal bool TryCreateFoxPriorityStoragePlan(Entity fox, out FoxStoragePlan? plan)
    {
        plan = null;

        // Cargo already in the fox's carry slot always wins. This also lets a
        // duty that was interrupted finish delivery before selecting another
        // source.
        if (HasFoxStorageCargo(fox)
            && TryCreateFoxStoragePlan(fox, false, false, false, false, false, false, false, out plan)
            && plan != null) return true;

        if (IsCharcoalShovelingEnabled(fox)
            && TryCreateFoxStoragePlan(fox, false, false, false, false, false, false, true, out plan)
            && plan != null) return true;

        // Ground debris is deliberately first among ambient duties. Loose
        // flint and stone are natural-cleanup source blocks and are selected
        // by this branch before mowing is ever considered.
        if ((IsGroundCleanupEnabled(fox) || IsWorkCartLoggingCleanupEnabled(fox))
            && TryCreateFoxStoragePlan(fox, false, false, false, false, false, false, false, out plan)
            && plan != null) return true;

        if (IsFinishedProductsEnabled(fox)
            && TryCreateFoxStoragePlan(fox, false, true, false, false, false, false, false, out plan)
            && plan != null) return true;

        if (IsFlowerRemovalEnabled(fox)
            && TryCreateFoxStoragePlan(fox, false, false, true, false, false, false, false, out plan)
            && plan != null) return true;

        if (IsSnowShovelingEnabled(fox)
            && TryCreateFoxStoragePlan(
                fox,
                false,
                false,
                false,
                true,
                IsSnowballCollectionEnabled(fox),
                false,
                false,
                out plan)
            && plan != null) return true;

        // General source-to-destination sorting is last so it cannot starve
        // local cleanup. It still has its own internal source/category rules.
        if (IsGeneralStorageSortingEnabled(fox)
            && TryCreateFoxStoragePlan(fox, false, false, false, false, false, true, false, out plan)
            && plan != null) return true;

        return false;
    }

    internal bool TryCreateFoxStoragePlan(
        Entity fox,
        bool commandedCourier,
        bool gatherFinishedProducts,
        bool removeFlowers,
        bool shovelSnow,
        bool collectSnowballs,
        bool routeStorage,
        bool shovelCharcoal,
        out FoxStoragePlan? plan)
    {
        plan = null;
        if (serverApi == null || !IsTamedFox(fox) || IsFoxAwayFromWorld(fox)
            || IsFoxIncapacitated(fox)) return false;

        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        bool pendingCartPickup = fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false);
        bool commandedCarry = fox.WatchedAttributes.GetBool(FoxStorageCommandedCarryKey, false);
        if (commandedCourier)
        {
            if (!pendingCartPickup && !(carried != null && carried.StackSize > 0 && commandedCarry)) return false;
        }
        else
        {
            if (pendingCartPickup || commandedCarry || ShouldFoxSeekDenShelter(fox)) return false;
        }

        // A routed item already in the fox's mouth must always finish its
        // delivery before this shared task considers another General source.
        // Previously the RouteStorage instance bypassed the ordinary cargo
        // branch, repeatedly selected a source, and then failed pickup because
        // HasFoxStorageCargo was already true.
        if (carried != null && carried.StackSize > 0)
        {
            IReadOnlyList<BlockPos> failedTargets = GetFoxStorageFailedTargets(fox);
            if (!TryGetFoxStorage(
                    fox,
                    out BlockPos? carriedStoragePos,
                    carried,
                    requireFullStack: false,
                    excludedStoragePositions: failedTargets)
                || carriedStoragePos == null)
            {
                if (!TryReturnFoxStorageCargoToSource(fox)) DropFoxStorageCargo(fox);
                return false;
            }

            plan = new FoxStoragePlan
            {
                StoragePos = carriedStoragePos,
                StorageTarget = GetFoxStorageApproachTarget(fox, carriedStoragePos),
                AlreadyCarrying = true,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        // General storage routing is a source-to-destination job, not an
        // ordinary "find somewhere to put what I am carrying" job.  Inspect
        // the General source before doing the ordinary storage lookup: that
        // pre-flight check can reject the transfer this task is meant to make,
        // particularly when a destination is represented by a multiblock.
        if (routeStorage && !commandedCourier)
        {
            if (!IsGeneralStorageSortingEnabled(fox)) return false;
            if (GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase) return false;

            string routingMood = GetMood(fox);
            if (routingMood is "sleepy" or "resting" or "anxious" or "alarmed") return false;

            return TryCreateFoxStorageRoutingPlan(fox, out plan) && plan != null;
        }

        // Natural cleanup must identify the source item before resolving its
        // destination. A generic storage preflight here would reject a fox
        // that has a Flint/Stone destination but no General destination,
        // which made stored flint routable while ground flint was invisible.
        bool naturalCleanupOnly = !commandedCourier
            && !gatherFinishedProducts
            && !removeFlowers
            && !shovelSnow
            && !shovelCharcoal;
        BlockPos? storagePos = null;
        if (!naturalCleanupOnly
            && (!TryGetFoxStorage(fox, out storagePos) || storagePos == null))
        {
            if (carried != null && carried.StackSize > 0) DropFoxStorageCargo(fox);
            return false;
        }
        if (pendingCartPickup)
        {
            if (!TryGetFoxPackCartPickupTarget(fox, out Vec3d? cartTarget) || cartTarget == null)
            {
                return false;
            }
            plan = new FoxStoragePlan
            {
                StoragePos = storagePos!,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos!),
                ItemTarget = cartTarget,
                CollectFromPackCart = true,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        if (!commandedCourier)
        {
            bool workCartLoggingCleanup = !gatherFinishedProducts
                && !removeFlowers
                && !shovelSnow
                && !shovelCharcoal
                && IsWorkCartLoggingCleanupEnabled(fox);
            if ((!gatherFinishedProducts
                    && !removeFlowers
                    && !shovelSnow
                    && !shovelCharcoal
                    && !IsGroundCleanupEnabled(fox)
                    && !workCartLoggingCleanup)
                || (gatherFinishedProducts && !IsFinishedProductsEnabled(fox))
                || (removeFlowers && !IsFlowerRemovalEnabled(fox))
                || (shovelSnow && !IsSnowShovelingEnabled(fox))
                || (shovelCharcoal && !IsCharcoalShovelingEnabled(fox))
                || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase) return false;

            string mood = GetMood(fox);
            if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        }

        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        float searchRange = GetGroundCleanupSearchRange(fox);
        if (shovelCharcoal)
        {
            if (!TryFindCharcoalTarget(
                    fox,
                    cleanupCenter,
                    searchRange,
                    out BlockPos? charcoalTarget,
                    out Vec3d? charcoalApproach)
                || charcoalTarget == null
                || charcoalApproach == null) return false;

            if (!TryGetCharcoalItemPreview(charcoalTarget, out ItemStack? charcoalItem)
                || charcoalItem == null
                || !TryGetFoxStorage(fox, out storagePos, charcoalItem)
                || storagePos == null) return false;
            if (!TryReserveCharcoalTarget(fox, charcoalTarget)) return false;

            plan = new FoxStoragePlan
            {
                StoragePos = storagePos,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos),
                CharcoalTarget = charcoalTarget,
                ItemTarget = charcoalApproach,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        if (gatherFinishedProducts)
        {
            if (!TryFindFinishedProductTarget(
                    fox,
                    cleanupCenter,
                    searchRange,
                    out BlockPos? finishedProductTarget,
                    out Vec3d? finishedProductApproach)
                || finishedProductTarget == null
                || finishedProductApproach == null) return false;

            plan = new FoxStoragePlan
            {
                StoragePos = storagePos!,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos!),
                FinishedProductTarget = finishedProductTarget,
                ItemTarget = finishedProductApproach,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        if (removeFlowers)
        {
            if (!TryFindFlowerTarget(
                    fox,
                    cleanupCenter,
                    searchRange,
                    out BlockPos? flowerTarget,
                    out Vec3d? flowerApproach)
                || flowerTarget == null
                || flowerApproach == null) return false;

            plan = new FoxStoragePlan
            {
                StoragePos = storagePos!,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos!),
                FlowerTarget = flowerTarget,
                ItemTarget = flowerApproach,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        if (shovelSnow)
        {
            if (!TryFindSnowTarget(
                    fox,
                    cleanupCenter,
                    searchRange,
                    out BlockPos? snowTarget,
                    out Vec3d? snowApproach)
                || snowTarget == null
                || snowApproach == null) return false;

            plan = new FoxStoragePlan
            {
                StoragePos = storagePos!,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos!),
                SnowTarget = snowTarget,
                ItemTarget = snowApproach,
                CollectSnowballs = collectSnowballs,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        Vec3d cleanupOrigin = FeralKinshipNaturalCleanup.GetTarget(cleanupCenter);
        EntityItem? item = null;
        bool workCartOwnsDebris = IsWorkCartLoggingCleanupEnabled(fox);
        if (IsGroundDroppedItemsEnabled(fox) || workCartOwnsDebris)
        {
            bool assignedWorkCart = IsFoxAssignedToWorkCart(fox);
            item = assignedWorkCart
                ? TryClaimQueuedWorkCartDroppedItem(fox)
                : FindPrioritizedWorkCartLoggingDebris(fox);
            // Cellars and upper floors belong to the same camp cleanup area.
            if (!assignedWorkCart) item ??= serverApi.World.GetEntitiesAround(cleanupOrigin, searchRange, searchRange,
                    candidate => candidate is EntityItem dropped && dropped.Alive && dropped.Itemstack != null
                        && dropped.Pos.Dimension == fox.Pos.Dimension
                        && dropped.Itemstack.StackSize > 0
                        && !CompanionPickupPolicy.IsCorpse(dropped.Itemstack.Collectible?.GetType())
                        && serverApi.World.ElapsedMilliseconds - dropped.itemSpawnedMilliseconds >= 2500
                        && (!droppedItemReservations.TryGetValue(dropped.EntityId, out long reservedBy)
                            || reservedBy == fox.EntityId))
                .OfType<EntityItem>()
                .OrderBy(candidate => candidate.Pos.SquareDistanceTo(fox.Pos))
                .FirstOrDefault();
        }
        if (item == null)
        {
            if (!TryFindNaturalCleanupTarget(
                    fox,
                    cleanupCenter,
                    searchRange,
                    out BlockPos? naturalTarget,
                    out Vec3d? naturalApproachTarget)
                || naturalTarget == null
                || naturalApproachTarget == null) return false;

            // The storage destination was selected before the cleanup target
            // was known. Resolve a representative drop first so a Food-only
            // Dining Board (or any other typed destination) cannot become the
            // destination for flint, cattails, rocks, or other debris.
            if (!TryGetNaturalCleanupItemPreview(naturalTarget, out ItemStack? naturalItem))
            {
                LogDutyDiagnostic(fox, "natural-plan", $"target={naturalTarget} stage=drop-preview-failed");
                return false;
            }
            if (!TryGetFoxStorage(fox, out storagePos, naturalItem) || storagePos == null)
            {
                LogDutyDiagnostic(
                    fox,
                    "natural-plan",
                    $"target={naturalTarget} block={serverApi.World.BlockAccessor.GetBlock(naturalTarget).Code} "
                    + $"item={naturalItem?.Collectible?.Code} stage=typed-storage-destination-failed");
                return false;
            }
            if (!TryReserveNaturalCleanupTarget(fox, naturalTarget)) return false;

            plan = new FoxStoragePlan
            {
                StoragePos = storagePos!,
                StorageTarget = GetFoxStorageApproachTarget(fox, storagePos!),
                NaturalBlockTarget = naturalTarget,
                ItemTarget = naturalApproachTarget,
                MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
            };
            return true;
        }

        // A dropped item is already available, so select the destination
        // against its actual category before reserving it. This prevents the
        // initial no-cargo storage lookup from routing non-food items into a
        // Food-only Dining Board.
        LogDutyDiagnostic(fox, "ground-item-selected",
            $"itemEntity={item.EntityId} itemPos={item.Pos.XYZ} cleanupOrigin={cleanupOrigin} horizontalRange={searchRange} verticalRange={searchRange}");
        ItemStack? pickupPreview = GetDroppedItemPickupPreview(fox, item);
        if (pickupPreview == null
            || !TryGetFoxStorage(fox, out storagePos, pickupPreview)
            || storagePos == null)
        {
            return false;
        }

        droppedItemReservations[item.EntityId] = fox.EntityId;
        plan = new FoxStoragePlan
        {
            StoragePos = storagePos,
            StorageTarget = GetFoxStorageApproachTarget(fox, storagePos),
            ItemEntityId = item.EntityId,
            ItemTarget = item.Pos.XYZ.Clone(),
            MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
        };
        return true;
    }

    private bool TryCreateFoxStorageRoutingPlan(Entity fox, out FoxStoragePlan? plan)
    {
        plan = null;
        if (serverApi == null || packRepository?.Loaded != true || IsCompanionJuvenile(fox)) return false;

        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerUid)) return false;

        if (!TryGetStorageRoutingScope(
                ownerUid,
                fox.Pos.Dimension,
                fox,
                out BlockPos? storageCenter,
                out double storageRadius,
                out _)
            || storageCenter == null) return false;

        List<FoxPackAmenityRecord> sources = packRepository.GetAmenitiesForOwner(ownerUid)
            .Where(record => (record.Kind == "storage" || record.Kind == "storage-target" || record.Kind == "dining")
                && IsStorageRoutingEnabled(record)
                && record.Dimension == fox.Pos.Dimension
                && IsWithinStorageRoutingScope(record, storageCenter, storageRadius))
            .OrderBy(record => fox.Pos.SquareDistanceTo(new Vec3d(
                record.X + 0.5,
                record.Y + record.Dimension * BlockPos.DimensionBoundary + 0.2,
                record.Z + 0.5)))
            .ToList();

        int supportedSourceCount = 0;
        int populatedSourceCount = 0;
        int classifiedItemCount = 0;
        int correctlyPlacedItemCount = 0;
        int misplacedItemCount = 0;
        int noDestinationCount = 0;

        foreach (FoxPackAmenityRecord sourceRecord in sources)
        {
            BlockPos sourcePos = new(sourceRecord.X, sourceRecord.Y, sourceRecord.Z, sourceRecord.Dimension);
            Block? sourceBlock = serverApi.World.BlockAccessor.GetBlock(sourcePos);
            BlockEntity? sourceEntity = serverApi.World.BlockAccessor.GetBlockEntity(sourcePos);
            if (!FoxStorageRouting.IsSupportedStorageTarget(sourceBlock, sourceEntity)
                || sourceEntity is not IBlockEntityContainer sourceContainer)
            {
                continue;
            }

            supportedSourceCount++;

            for (int slotIndex = 0; slotIndex < sourceContainer.Inventory.Count; slotIndex++)
            {
                ItemSlot? sourceSlot = sourceContainer.Inventory[slotIndex];
                if (sourceSlot == null || sourceSlot.Empty || sourceSlot.Itemstack == null) continue;

                populatedSourceCount++;

                ItemStack candidate = sourceSlot.Itemstack.Clone();
                if (!candidate.ResolveBlockOrItem(serverApi.World) || candidate.Collectible == null)
                {
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"source={sourcePos} slot={slotIndex} skipped unresolved item");
                    continue;
                }
                if (candidate.StackSize <= 0)
                {
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"source={sourcePos} slot={slotIndex} item={candidate.Collectible.Code} skipped empty stack");
                    continue;
                }

                if (CompanionPickupPolicy.IsCorpse(candidate.Collectible.GetType())) continue;

                FoxStorageRouting.Category candidateCategory =
                    FoxStorageRouting.Classify(candidate, serverApi.World);
                int sourceMask = GetEffectiveStorageRoutingMask(sourceRecord);
                if (sourceMask == (int)FoxStorageRouting.Category.None)
                {
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"source={sourcePos} slot={slotIndex} item={candidate.Collectible.Code} skipped source-routing=disabled");
                    continue;
                }
                if (!FoxStorageRouting.IsGeneral(sourceRecord, sourceMask)
                    && StorageRoutingAccepts(sourceRecord, candidate))
                {
                    correctlyPlacedItemCount++;
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"source={sourcePos} slot={slotIndex} item={candidate.Collectible.Code} "
                        + $"category={candidateCategory} skipped already-in-category={FoxStorageRouting.GetDisplayName(sourceMask)}");
                    continue;
                }
                classifiedItemCount++;
                misplacedItemCount++;

                if (!TryGetPackStorage(
                        ownerUid,
                        fox.Pos.Dimension,
                        fox.Pos.XYZ,
                        out BlockPos? destination,
                        candidate,
                        requireSpecific: true,
                        excludedStoragePos: sourcePos,
                        requireFullStack: false,
                        fox: fox)
                    || destination == null)
                {
                    noDestinationCount++;
                    continue;
                }

                if (!TryGetFoxStorageApproachTarget(fox, sourcePos, out Vec3d? sourceApproach)
                    || sourceApproach == null
                    || !TryGetFoxStorageApproachTarget(fox, destination, out Vec3d? destinationApproach)
                    || destinationApproach == null)
                {
                    noDestinationCount++;
                    continue;
                }

                plan = new FoxStoragePlan
                {
                    SourceStoragePos = sourcePos,
                    SourceSlotIndex = slotIndex,
                    StoragePos = destination,
                    ItemTarget = sourceApproach,
                    StorageTarget = destinationApproach,
                    RouteStorage = true,
                    MoveSpeed = GetFoxPerkRank(fox, "messenger") > 0 ? 0.030f : 0.022f
                };
                LogStorageRoutingDiagnostic(
                    fox,
                    $"route found source={sourcePos} slot={slotIndex} item={candidate.Collectible.Code} category={candidateCategory} destination={destination}");
                return true;
            }
        }

        LogStorageRoutingDiagnostic(
            fox,
            $"no route sources={sources.Count} supported={supportedSourceCount} populated={populatedSourceCount} "
            + $"classified={classifiedItemCount} misplaced={misplacedItemCount} correctlyPlaced={correctlyPlacedItemCount} "
            + $"noDestination={noDestinationCount}");
        return false;
    }

    private void LogStorageRoutingDiagnostic(Entity fox, string message)
        => LogStorageRoutingDiagnostic(fox, message, force: false);

    private void LogStorageRoutingDiagnostic(Entity fox, string message, bool force)
    {
        if (serverApi == null || !DiagnosticLoggingEnabled) return;
        long now = serverApi.World.ElapsedMilliseconds;
        if (!force
            && storageRoutingDiagnosticAtMs.TryGetValue(fox.EntityId, out long nextAtMs)
            && now < nextAtMs) return;
        storageRoutingDiagnosticAtMs[fox.EntityId] = now + 5000;
        serverApi.Logger.Debug("[FeralKinshipCompanions] Storage routing fox {0}: {1}", fox.EntityId, message);
    }

    private void LogDutyDiagnostic(Entity fox, string stage, string message)
    {
        if (serverApi == null || !DiagnosticLoggingEnabled) return;
        string key = $"{fox.EntityId}:{stage}";
        long now = serverApi.World.ElapsedMilliseconds;
        if (dutyDiagnosticAtMs.TryGetValue(key, out long nextAtMs) && now < nextAtMs) return;
        dutyDiagnosticAtMs[key] = now + 3000;
        serverApi.Logger.Debug(
            "[FeralKinshipCompanions] Duty diagnostic fox {0} {1}: {2}",
            fox.EntityId,
            stage,
            message);
    }

    internal void LogDutyNavigationFailure(Entity fox, FoxStoragePlan? plan, string reason)
    {
        if (plan == null || serverApi == null) return;
        if (reason.StartsWith("storage-leg", StringComparison.Ordinal))
        {
            BlockPos storageTarget = plan.StoragePos;
            Block? storageBlock = serverApi.World.BlockAccessor.GetBlock(storageTarget);
            ItemStack? cargo = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
            LogDutyDiagnostic(
                fox,
                "navigation",
                $"target={storageTarget} block={storageBlock?.Code} approach={plan.StorageTarget} "
                + $"fox={fox.Pos.X:0.00},{fox.Pos.Y:0.00},{fox.Pos.Z:0.00} "
                + $"cargo={cargo?.Collectible?.Code}x{cargo?.StackSize ?? 0} reason={reason}");
            return;
        }
        if (plan.NaturalBlockTarget == null) return;
        BlockPos target = plan.NaturalBlockTarget;
        Block? block = serverApi.World.BlockAccessor.GetBlock(target);
        LogDutyDiagnostic(
            fox,
            "navigation",
            $"target={target} block={block?.Code} approach={plan.ItemTarget} reason={reason}");
    }

    internal bool TryRetargetFoxStorageCargo(
        Entity fox,
        FoxStoragePlan plan,
        IReadOnlyCollection<BlockPos> excludedStoragePositions)
    {
        if (serverApi == null || !HasFoxStorageCargo(fox)) return false;
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        if (carried == null
            || !TryGetPackStorage(
                ownerUid,
                fox.Pos.Dimension,
                fox.Pos.XYZ,
                out BlockPos? destination,
                carried,
                requireFullStack: false,
                excludedStoragePositions: excludedStoragePositions,
                fox: fox)
            || destination == null
            || excludedStoragePositions.Any(excluded => excluded.Equals(destination)))
        {
            return false;
        }

        plan.StoragePos = destination;
        plan.StorageTarget = GetFoxStorageApproachTarget(fox, destination);
        LogStorageRoutingDiagnostic(
            fox,
            $"storage path failed; retrying cargo={carried.Collectible?.Code}x{carried.StackSize} at {destination}",
            force: true);
        return true;
    }

    internal bool TryGetFoxStorageReturnSourcePosition(Entity fox, out BlockPos? sourcePos)
    {
        sourcePos = null;
        if (serverApi == null || !fox.WatchedAttributes.GetBool(FoxStorageReturnSourceValidKey, false))
        {
            return false;
        }

        int x = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceXKey, 0);
        int y = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceYKey, 0);
        int z = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceZKey, 0);
        int dimension = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceDimensionKey, fox.Pos.Dimension);
        BlockPos candidate = new(x, y, z, dimension);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        FoxPackAmenityRecord? record = packRepository?.GetAmenity(candidate);
        BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(candidate);
        if (dimension != fox.Pos.Dimension
            || record == null
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !IsStorageRoutingEnabled(record)
            || blockEntity is not IBlockEntityContainer
            || !FoxStorageRouting.IsSupportedStorageTarget(
                serverApi.World.BlockAccessor.GetBlock(candidate),
                blockEntity))
        {
            return false;
        }

        sourcePos = candidate;
        return true;
    }

    internal void ResolveFoxStorageCargoAfterNavigationFailure(Entity fox)
    {
        if (!HasFoxStorageCargo(fox)) return;
        ItemStack? cargo = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (TryReturnFoxStorageCargoToSource(fox))
        {
            LogStorageRoutingDiagnostic(
                fox,
                $"storage path recovery returned {cargo?.StackSize ?? 0}x {cargo?.Collectible?.Code} to its source",
                force: true);
            return;
        }

        int droppedCount = cargo?.StackSize ?? 0;
        string? code = cargo?.Collectible?.Code?.ToString();
        DropFoxStorageCargo(fox);
        LogStorageRoutingDiagnostic(
            fox,
            HasFoxStorageCargo(fox)
                ? $"storage path recovery could not drop {droppedCount}x {code}; retaining cargo for retry"
                : $"storage path recovery dropped {droppedCount}x {code} beside the fox",
            force: true);
    }

    internal bool TryTakeStorageRoutingItem(
        Entity fox,
        BlockPos sourcePos,
        int sourceSlotIndex,
        BlockPos destinationPos)
    {
        if (serverApi == null || HasFoxStorageCargo(fox) || sourceSlotIndex < 0) return false;

        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        FoxPackAmenityRecord? sourceRecord = packRepository?.GetAmenity(sourcePos);
        if (string.IsNullOrWhiteSpace(ownerUid)
            || sourceRecord == null
            || !string.Equals(sourceRecord.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !IsStorageRoutingEnabled(sourceRecord)) return false;

        BlockEntity? sourceEntity = serverApi.World.BlockAccessor.GetBlockEntity(sourcePos);
        if (sourceEntity is not IBlockEntityContainer sourceContainer
            || sourceSlotIndex >= sourceContainer.Inventory.Count) return false;

        ItemSlot? sourceSlot = sourceContainer.Inventory[sourceSlotIndex];
        if (sourceSlot == null || sourceSlot.Empty || sourceSlot.Itemstack == null) return false;

        ItemStack candidate = sourceSlot.Itemstack.Clone();
        if (!candidate.ResolveBlockOrItem(serverApi.World) || candidate.Collectible == null
            || candidate.StackSize <= 0
            || CompanionPickupPolicy.IsCorpse(candidate.Collectible.GetType())) return false;

        int sourceMask = GetEffectiveStorageRoutingMask(sourceRecord);
        if (!FoxStorageRouting.IsGeneral(sourceRecord, sourceMask)
            && StorageRoutingAccepts(sourceRecord, candidate)) return false;

        // Keep the destination chosen by the priority selector authoritative
        // for this transfer. Re-running selection from the fox's new position
        // could silently choose a different tie-break winner after it reached
        // the source, while the plan still navigated to the original box.
        FoxPackAmenityRecord? destinationRecord = packRepository?.GetAmenity(destinationPos);
        Block? destinationBlock = serverApi.World.BlockAccessor.GetBlock(destinationPos);
        BlockEntity? destinationEntity = serverApi.World.BlockAccessor.GetBlockEntity(destinationPos);
        if (destinationPos.Equals(sourcePos)
            || destinationPos.dimension != fox.Pos.Dimension
            || destinationRecord == null
            || !string.Equals(destinationRecord.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !IsStorageRoutingEnabled(destinationRecord)) return false;

        int destinationMask = GetEffectiveStorageRoutingMask(destinationRecord);
        if (FoxStorageRouting.IsGeneral(destinationRecord, destinationMask)
            || !StorageRoutingAccepts(destinationRecord, candidate)
            || !FoxStorageRouting.IsSupportedStorageTarget(destinationBlock, destinationEntity)
            || destinationEntity is not IBlockEntityContainer destinationContainer
            || !CanInventoryAcceptOne(destinationContainer.Inventory, candidate)) return false;

        int amount = 1;
        if (GetFoxPerkRank(fox, "mouthful") > 0 || GetFoxPackTalentRank(fox, "mouthful") > 0)
        {
            amount = Math.Min(candidate.StackSize, candidate.Collectible.MaxStackSize);
            ItemStack proposed = candidate.GetEmptyClone();
            proposed.StackSize = amount;
            if (amount > 1 && !CanInventoryFullyAccept(destinationContainer.Inventory, proposed)) amount = 1;
        }

        ItemStack? taken = sourceSlot.TakeOut(amount);
        if (taken == null || taken.StackSize <= 0) return false;

        sourceSlot.MarkDirty();
        if (sourceEntity is BlockEntity sourceBlockEntity) sourceBlockEntity.MarkDirty();
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, taken);
        SetFoxStorageReturnSource(fox, sourcePos);
        fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
        PlayCargoSoundRateLimited(ownerUid, "pickup", sourcePos, "game:sounds/player/collect1", 0.26f);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    private bool TryFindNaturalCleanupTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        target = null;
        approachTarget = null;
        if (serverApi == null) return false;
        if (IsFoxAssignedToWorkCart(fox))
        {
            return TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Natural, out target, out approachTarget);
        }
        double nearestDistance = double.MaxValue;
        int bestPriority = FeralKinshipNaturalCleanup.IneligiblePriority;
        int flintSeen = 0;
        int stoneSeen = 0;
        int optionRejected = 0;
        int reserved = 0;
        int backoff = 0;
        int noApproach = 0;
        int approachFound = 0;
        for (int dx = -((int)searchRange); dx <= (int)searchRange; dx++)
        {
            for (int dz = -((int)searchRange); dz <= (int)searchRange; dz++)
            {
                if (dx * dx + dz * dz > searchRange * searchRange) continue;

                BlockPos surfaceProbe = new(center.X + dx, center.Y, center.Z + dz, fox.Pos.Dimension);
                // The terrain map is the original world-generation height and
                // does not follow later building, digging, or landscaping.
                // The rain map is updated live, so search around both surfaces:
                // loose stones/flint/sticks are rain-permeable while boulders
                // can be the current topmost non-permeable block.
                int terrainHeight = serverApi.World.BlockAccessor.GetTerrainMapheightAt(surfaceProbe);
                int rainHeight = serverApi.World.BlockAccessor.GetRainMapHeightAt(surfaceProbe);
                int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
                int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
                for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
                {
                    BlockPos candidate = new(center.X + dx, y, center.Z + dz, fox.Pos.Dimension);
                    Block? candidateBlock = serverApi.World.BlockAccessor.GetBlock(candidate);
                    int priority = FeralKinshipNaturalCleanup.GetPriority(candidateBlock);
                    if (priority == FeralKinshipNaturalCleanup.IneligiblePriority) continue;
                    if (priority == 1) flintSeen++;
                    if (priority == 4) stoneSeen++;
                    if (!IsNaturalCleanupOptionEnabled(fox, priority))
                    {
                        if (priority is 1 or 4) optionRejected++;
                        continue;
                    }
                    if (IsNaturalCleanupTargetBackedOff(candidate))
                    {
                        if (priority is 1 or 4) backoff++;
                        continue;
                    }
                    if (!IsNaturalCleanupTargetAvailable(fox, candidate))
                    {
                        if (priority is 1 or 4) reserved++;
                        continue;
                    }
                    if (!TryGetNaturalCleanupApproachTarget(fox, candidate, out Vec3d? candidateApproach)
                        || candidateApproach == null)
                    {
                        if (priority is 1 or 4) noApproach++;
                        continue;
                    }

                    if (priority is 1 or 4) approachFound++;

                    double distance = fox.Pos.SquareDistanceTo(candidateApproach);
                    if (priority > bestPriority
                        || (priority == bestPriority && distance >= nearestDistance)) continue;
                    bestPriority = priority;
                    nearestDistance = distance;
                    target = candidate;
                    approachTarget = candidateApproach;
                }
            }
        }

        string selectedCode = target == null
            ? "none"
            : serverApi.World.BlockAccessor.GetBlock(target).Code?.ToString() ?? "unknown";
        if (flintSeen > 0 || stoneSeen > 0 || target == null)
        {
            LogDutyDiagnostic(
                fox,
                "natural-scan",
                $"center={center} flint={flintSeen} stone={stoneSeen} optionRejected={optionRejected} "
                + $"reserved={reserved} backoff={backoff} noApproach={noApproach} approachFound={approachFound} "
                + $"selected={target?.ToString() ?? "none"} selectedCode={selectedCode}");
        }

        return target != null;
    }

    internal bool TryGetMowingApproachTarget(Entity fox, BlockPos grass, out Vec3d? approachTarget)
    {
        return TryGetNaturalCleanupApproachTarget(fox, grass, out approachTarget);
    }

    private static bool IsNaturalCleanupOptionEnabled(Entity fox, int priority)
    {
        return priority switch
        {
            0 => IsGroundCattailsEnabled(fox),
            1 => IsGroundFlintEnabled(fox),
            2 => IsGroundSticksEnabled(fox),
            3 => IsGroundBouldersEnabled(fox),
            4 => IsGroundRocksEnabled(fox),
            _ => false
        };
    }

    private bool TryGetNaturalCleanupApproachTarget(
        Entity fox,
        BlockPos debris,
        out Vec3d? target,
        System.Func<Vec3d, bool>? approachAllowed = null)
    {
        target = null;
        if (serverApi == null) return false;

        // Natural debris can have a very small collision box even when it looks
        // like a flat item on the ground. Prefer standing beside every debris
        // block, and keep the debris position itself separate from the place
        // the companion needs to stand. The center is only a last-resort
        // fallback for cramped terrain where no side position is usable.
        (int X, int Z)[] offsets =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1),
            (-1, -1), (-1, 1), (1, -1), (1, 1),
            (-2, 0), (2, 0), (0, -2), (0, 2),
            (0, 0)
        };
        int[] verticalOffsets = { 0, 1, -1 };
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        foreach ((int x, int z) in offsets
                     .OrderBy(offset => offset.X == 0 && offset.Z == 0 ? 1 : 0)
                     .ThenBy(offset => fox.Pos.SquareDistanceTo(
                         new Vec3d(
                             debris.X + offset.X + 0.5,
                             debris.Y + debris.dimension * BlockPos.DimensionBoundary,
                             debris.Z + offset.Z + 0.5))))
        {
            foreach (int dy in verticalOffsets)
            {
                BlockPos feet = new(debris.X + x, debris.Y + dy, debris.Z + z, debris.dimension);
                if (!blocks.GetBlock(feet.DownCopy()).SideSolid[BlockFacing.UP.Index]) continue;

                Block feetFluid = blocks.GetBlock(feet, BlockLayersAccess.Fluid);
                Block headFluid = blocks.GetBlock(feet.UpCopy(), BlockLayersAccess.Fluid);
                Block aboveHeadFluid = blocks.GetBlock(feet.UpCopy(2), BlockLayersAccess.Fluid);
                bool naturalCleanup = FeralKinshipNaturalCleanup.IsEligible(blocks.GetBlock(debris));
                if (naturalCleanup
                    ? IsUnsafeNaturalCleanupLiquid(feetFluid)
                        || IsUnsafeNaturalCleanupLiquid(headFluid)
                        || IsUnsafeNaturalCleanupLiquid(aboveHeadFluid)
                        || aboveHeadFluid.IsLiquid()
                    : feetFluid.IsLiquid() || headFluid.IsLiquid()) continue;

                Vec3d candidate = new(
                    feet.X + 0.5,
                    feet.Y + feet.dimension * BlockPos.DimensionBoundary,
                    feet.Z + 0.5
                );
                if (serverApi.World.CollisionTester.IsColliding(
                        blocks,
                        fox.Properties.SpawnCollisionBox,
                        candidate,
                        false)
                    || approachAllowed?.Invoke(candidate) == false) continue;

                target = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool IsUnsafeNaturalCleanupLiquid(Block block)
    {
        return block.IsLiquid() && string.Equals(block.LiquidCode, "lava", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryFindFinishedProductTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        target = null;
        approachTarget = null;
        if (serverApi == null) return false;
        if (IsFoxAssignedToWorkCart(fox))
        {
            return TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Harvest, out target, out approachTarget);
        }

        double nearestDistance = double.MaxValue;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        for (int dx = -((int)searchRange); dx <= (int)searchRange; dx++)
        {
            for (int dz = -((int)searchRange); dz <= (int)searchRange; dz++)
            {
                if (dx * dx + dz * dz > searchRange * searchRange) continue;

                BlockPos surfaceProbe = new(center.X + dx, center.Y, center.Z + dz, fox.Pos.Dimension);
                int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
                int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
                int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
                int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
                for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
                {
                    BlockPos candidate = new(center.X + dx, y, center.Z + dz, fox.Pos.Dimension);
                    Block? product = blocks.GetBlock(candidate);
                    if (!IsFinishedProductBlock(product, candidate)
                        || !IsFinishedProductOptionEnabled(fox, product)) continue;
                    if (!TryGetNaturalCleanupApproachTarget(fox, candidate, out Vec3d? candidateApproach)
                        || candidateApproach == null) continue;

                    double distance = fox.Pos.SquareDistanceTo(candidateApproach);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    target = candidate;
                    approachTarget = candidateApproach;
                }
            }
        }

        return target != null;
    }

    private static bool IsFinishedProductOptionEnabled(Entity fox, Block? block)
    {
        return block switch
        {
            BlockCrop => IsFinishedCropsEnabled(fox),
            BlockMushroom => IsFinishedMushroomsEnabled(fox),
            null => false,
            _ => IsFinishedBerriesEnabled(fox)
        };
    }

    private bool TryFindFlowerTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        if (IsFoxAssignedToWorkCart(fox))
        {
            return TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Flowers, out target, out approachTarget);
        }
        return TryFindCampBlockTarget(
            fox,
            center,
            searchRange,
            IsFlowerRemovalBlock,
            out target,
            out approachTarget);
    }

    private bool TryFindCharcoalTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        out BlockPos? target,
        out Vec3d? approachTarget,
        bool reserveTarget = true)
    {
        if (IsFoxAssignedToWorkCart(fox))
        {
            return TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Charcoal, out target, out approachTarget);
        }

        target = null;
        approachTarget = null;
        if (serverApi == null) return false;

        double nearestDistance = double.MaxValue;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        for (int dx = -((int)searchRange); dx <= (int)searchRange; dx++)
        for (int dz = -((int)searchRange); dz <= (int)searchRange; dz++)
        {
            if (dx * dx + dz * dz > searchRange * searchRange) continue;

            BlockPos surfaceProbe = new(center.X + dx, center.Y, center.Z + dz, fox.Pos.Dimension);
            int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
            int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
            int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
            int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
            for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
            {
                BlockPos candidate = new(center.X + dx, y, center.Z + dz, fox.Pos.Dimension);
                if (!TryResolveCharcoalTopTarget(candidate, out BlockPos topCandidate)
                    || !IsCharcoalTargetAvailable(fox, topCandidate)
                    || !TryGetNaturalCleanupApproachTarget(fox, topCandidate, out Vec3d? candidateApproach)
                    || candidateApproach == null) continue;

                double distance = fox.Pos.SquareDistanceTo(candidateApproach);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                target = topCandidate;
                approachTarget = candidateApproach;
            }
        }

        return target != null && (!reserveTarget || TryReserveCharcoalTarget(fox, target));
    }

    private bool TryFindSnowTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        target = null;
        approachTarget = null;
        if (serverApi == null) return false;
        if (IsFoxAssignedToWorkCart(fox))
        {
            return TryClaimQueuedWorkCartBlock(fox, WorkCartChoreKind.Snow, out target, out approachTarget);
        }
        IPlayer? owner = GetCompanionBlockAccessPlayer(fox);
        if (owner == null) return false;

        int bestLogisticsPriority = int.MaxValue;
        double nearestDistance = double.MaxValue;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        for (int dx = -((int)searchRange); dx <= (int)searchRange; dx++)
        for (int dz = -((int)searchRange); dz <= (int)searchRange; dz++)
        {
            if (dx * dx + dz * dz > searchRange * searchRange) continue;

            BlockPos surfaceProbe = new(center.X + dx, center.Y, center.Z + dz, fox.Pos.Dimension);
            int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
            int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
            int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
            int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
            for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
            {
                BlockPos candidate = new(center.X + dx, y, center.Z + dz, fox.Pos.Dimension);
                if (!IsSnowShovelableBlock(blocks.GetBlock(candidate))
                    || !IsSnowTargetAvailable(fox, candidate)
                    || !TryGetNaturalCleanupApproachTarget(
                        fox,
                        candidate,
                        out Vec3d? candidateApproach,
                        approach => IsSnowApproachAvailable(fox, approach))
                    || candidateApproach == null) continue;

                int logisticsPriority = GetSnowLogisticsPriority(candidate);
                double distance = fox.Pos.SquareDistanceTo(candidateApproach);
                if (logisticsPriority > bestLogisticsPriority
                    || (logisticsPriority == bestLogisticsPriority && distance >= nearestDistance)
                    || !CanPlayerModifyBlock(owner, candidate)) continue;
                bestLogisticsPriority = logisticsPriority;
                nearestDistance = distance;
                target = candidate;
                approachTarget = candidateApproach;
            }
        }

        return target != null;
    }

    private bool TryFindCampBlockTarget(
        Entity fox,
        BlockPos center,
        float searchRange,
        System.Func<Block?, bool> predicate,
        out BlockPos? target,
        out Vec3d? approachTarget)
    {
        target = null;
        approachTarget = null;
        if (serverApi == null) return false;

        double nearestDistance = double.MaxValue;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        for (int dx = -((int)searchRange); dx <= (int)searchRange; dx++)
        {
            for (int dz = -((int)searchRange); dz <= (int)searchRange; dz++)
            {
                if (dx * dx + dz * dz > searchRange * searchRange) continue;

                BlockPos surfaceProbe = new(center.X + dx, center.Y, center.Z + dz, fox.Pos.Dimension);
                int terrainHeight = blocks.GetTerrainMapheightAt(surfaceProbe);
                int rainHeight = blocks.GetRainMapHeightAt(surfaceProbe);
                int minSurfaceHeight = Math.Min(terrainHeight, rainHeight) - 8;
                int maxSurfaceHeight = Math.Max(terrainHeight, rainHeight) + 8;
                for (int y = minSurfaceHeight; y <= maxSurfaceHeight; y++)
                {
                    BlockPos candidate = new(center.X + dx, y, center.Z + dz, fox.Pos.Dimension);
                    if (!predicate(blocks.GetBlock(candidate))) continue;
                    if (!TryGetNaturalCleanupApproachTarget(fox, candidate, out Vec3d? candidateApproach)
                        || candidateApproach == null) continue;

                    double distance = fox.Pos.SquareDistanceTo(candidateApproach);
                    if (distance >= nearestDistance) continue;
                    nearestDistance = distance;
                    target = candidate;
                    approachTarget = candidateApproach;
                }
            }
        }

        return target != null;
    }

    private static bool IsFlowerRemovalBlock(Block? block)
    {
        string? path = block?.Code?.Path;
        return string.Equals(block?.Code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            && path?.StartsWith("flower-", StringComparison.OrdinalIgnoreCase) == true
            && path.EndsWith("-free", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsCharcoalPileBlock(Block? block)
    {
        return block is BlockCharcoalPile
            || (string.Equals(block?.Code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
                && block?.Code?.Path?.StartsWith("charcoalpile-", StringComparison.OrdinalIgnoreCase) == true);
    }

    private bool TryResolveCharcoalTopTarget(BlockPos origin, out BlockPos topTarget)
    {
        topTarget = origin.Copy();
        if (serverApi == null) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        if (!IsCharcoalPileBlock(blocks.GetBlock(topTarget))) return false;

        BlockPos probe = topTarget.UpCopy();
        while (probe.Y < blocks.MapSizeY && IsCharcoalPileBlock(blocks.GetBlock(probe)))
        {
            topTarget = probe.Copy();
            probe.Up();
        }

        return true;
    }

    private static bool IsSnowShovelableBlock(Block? block)
    {
        // Glass slabs carry their snow as a cover variant on the glass block
        // itself, rather than as a separate snowlayer block above it.  Keep
        // this material-based so colored, quartz, pane, and modded glass
        // variants use the same path without a block-code allowlist.
        if (IsSnowCoveredGlassBlock(block)) return true;

        string? path = block?.Code?.Path;
        if (!string.Equals(block?.Code?.Domain, "game", StringComparison.OrdinalIgnoreCase)
            || path == null) return false;

        if (path.StartsWith("snowlayer-", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(path[10..], out int layer) && layer is >= 1 and <= 7;
        }

        // Snow-covered tall grass is a cover variant of the plant itself,
        // rather than a separate snowlayer block. It has real snow collision
        // and can create the same extra ledge height, so cleanup and emergency
        // escape must treat it as snow while preserving the underlying grass.
        return block is BlockPlant
            && path.StartsWith("tallgrass-", StringComparison.OrdinalIgnoreCase)
            && block.snowLevel > 0
            && block.notSnowCovered != null
            && block.Variant["cover"]?.StartsWith("snow", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSnowCoveredGlassBlock(Block? block)
    {
        return block?.BlockMaterial == EnumBlockMaterial.Glass
            && block.snowLevel > 0
            && block.notSnowCovered != null;
    }

    internal static bool IsSnowLayer(Block? block)
    {
        return IsSnowShovelableBlock(block);
    }

    internal bool TryFindSnowRescueTarget(Entity fox, out BlockPos? target, out bool emergency)
    {
        target = null;
        emergency = CompanionNavigation.TryGetEmergencySnowGoal(
                fox,
                out Vec3d? routeGoal,
                out int emergencyFailures)
            && routeGoal != null;
        if (serverApi == null
            || fox is not EntityAgent agent
            || (!emergency && !IsSnowShovelingEnabled(fox))) return false;

        BlockPos origin = fox.Pos.AsBlockPos;
        int bestPriority = int.MaxValue;
        double bestScore = double.MaxValue;
        double forwardX = emergency ? routeGoal!.X - fox.Pos.X : Math.Sin(fox.Pos.Yaw);
        double forwardZ = emergency ? routeGoal!.Z - fox.Pos.Z : Math.Cos(fox.Pos.Yaw);
        double forwardLength = Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
        if (forwardLength > 0.001d)
        {
            forwardX /= forwardLength;
            forwardZ /= forwardLength;
        }
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                for (int dy = -1; dy <= 3; dy++)
                {
                    BlockPos candidate = new(origin.X + dx, origin.Y + dy, origin.Z + dz, fox.Pos.Dimension);
                    if (!IsSnowShovelableBlock(serverApi.World.BlockAccessor.GetBlock(candidate))) continue;
                    if (!IsSnowTargetAvailable(fox, candidate)) continue;
                    if (!CanCompanionModifyBlock(fox, candidate)) continue;

                    double horizontalSq = dx * dx + dz * dz;
                    if (horizontalSq > 2.5d * 2.5d) continue;

                    Vec3d candidateCenter = new(
                        candidate.X + 0.5,
                        candidate.Y + candidate.dimension * BlockPos.DimensionBoundary + 0.5,
                        candidate.Z + 0.5);
                    double distance = fox.Pos.SquareDistanceTo(candidateCenter);
                    if (distance > 3.2d * 3.2d
                        || !CompanionNavigation.CanReachSnowLayer(agent, candidate, 3.2d)) continue;

                    double projection = dx * forwardX + dz * forwardZ;
                    double lateralSq = Math.Max(0d, horizontalSq - projection * projection);
                    bool routeRelevant = projection >= -0.15d && lateralSq <= 1.15d * 1.15d;
                    int logisticsPriority = GetSnowLogisticsPriority(candidate);
                    if (emergency && !routeRelevant)
                    {
                        // First try the failed-route corridor. Repeated failed
                        // replans progressively widen the rescue search: path-
                        // critical nearby snow first, then any reachable layer
                        // if the companion is still snowed in.
                        if (emergencyFailures < 3
                            || (emergencyFailures < 4 && logisticsPriority >= 5)) continue;
                    }

                    int priority = logisticsPriority + (emergency && !routeRelevant ? 10 : 0);

                    // Snow on the upper side of a one-block ledge is the
                    // usual cause of the apparent 1.5-block obstruction.
                    // The same logistics tiers used by the camp-wide duty
                    // apply here, with distance and forward direction used
                    // only to choose inside a tier.
                    double score = distance
                        - (dy >= 1 ? 2.5d : 0d)
                        - (projection > 0 ? Math.Min(projection, 2d) * 0.5d : 0d);
                    if (priority > bestPriority
                        || (priority == bestPriority && score >= bestScore)) continue;

                    bestPriority = priority;
                    bestScore = score;
                    target = candidate;
                }
            }
        }

        return target != null;
    }

    private bool CanCompanionModifyBlock(Entity fox, BlockPos position)
    {
        IPlayer? owner = GetCompanionBlockAccessPlayer(fox);
        return owner != null && CanPlayerModifyBlock(owner, position);
    }

    private IPlayer? GetCompanionBlockAccessPlayer(Entity fox)
    {
        if (serverApi == null) return null;
        string ownerUid = GetCompanionOwnerUid(fox);
        return string.IsNullOrWhiteSpace(ownerUid)
            ? null
            : serverApi.World.PlayerByUid(ownerUid);
    }

    private bool CanPlayerModifyBlock(IPlayer owner, BlockPos position)
    {
        return serverApi != null
            && serverApi.World.Claims.TestAccess(owner, position, EnumBlockAccessFlags.BuildOrBreak)
                == EnumWorldAccessResponse.Granted;
    }

    private bool IsSnowBesideDoor(BlockPos snowPosition)
    {
        if (serverApi == null) return false;
        for (int dx = -2; dx <= 2; dx++)
        for (int dz = -2; dz <= 2; dz++)
        for (int dy = -1; dy <= 2; dy++)
        {
            if (dx * dx + dz * dz > 4) continue;
            BlockPos nearby = snowPosition.AddCopy(dx, dy, dz);
            Block block = serverApi.World.BlockAccessor.GetBlock(nearby);
            if (block is BlockBaseDoor
                || block.GetBehavior<BlockBehaviorDoor>() != null
                || BlockBehaviorDoor.getDoorAt(serverApi.World, nearby) != null)
            {
                return true;
            }
        }
        return false;
    }

    private int GetSnowLogisticsPriority(BlockPos snowPosition)
    {
        // Keep the category order explicit so a nearby decorative patch never
        // wins over snow that affects a route farther across the same camp.
        // Distance is used only to choose among targets in the same category.
        if (IsSnowBesideCompanionFlap(snowPosition)) return 0;
        if (IsSnowBesideDoor(snowPosition)) return 1;
        if (IsSnowAtLedge(snowPosition)) return 2;
        if (IsSnowOnGlass(snowPosition)) return 3;
        if (IsSnowBesideCompanionLandmark(snowPosition)) return 4;
        if (IsSnowInNarrowPassage(snowPosition)) return 5;
        return 6;
    }

    private bool IsSnowOnGlass(BlockPos snowPosition)
    {
        if (serverApi == null) return false;
        Block? snowBlock = serverApi.World.BlockAccessor.GetBlock(snowPosition);
        if (IsSnowCoveredGlassBlock(snowBlock)) return true;

        BlockPos supportPosition = snowPosition.DownCopy();
        Block supportBlock = serverApi.World.BlockAccessor.GetBlock(supportPosition);
        return supportBlock.GetBlockMaterial(serverApi.World.BlockAccessor, supportPosition)
            == EnumBlockMaterial.Glass;
    }

    private bool IsSnowBesideCompanionFlap(BlockPos snowPosition)
    {
        return IsSnowNearBlock(
            snowPosition,
            static (block, _) => block is BlockFeralKinshipFoxFlap,
            horizontalRadius: 2,
            minimumVerticalOffset: -1,
            maximumVerticalOffset: 2);
    }

    private bool IsSnowBesideCompanionLandmark(BlockPos snowPosition)
    {
        return IsSnowNearBlock(snowPosition, static (block, _) =>
        {
            AssetLocation? code = block.Code;
            if (code == null
                || !string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = code.Path;
            return path.StartsWith(FoxBedBlockPathPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(PackCartBlockPathPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(WorkCartBlockPathPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(PackAmenityBlockPathPrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(PackStorageBlockPathPrefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, PackCairnBlockPath, StringComparison.OrdinalIgnoreCase);
        });
    }

    private bool IsSnowNearBlock(
        BlockPos snowPosition,
        System.Func<Block, BlockPos, bool> predicate,
        int horizontalRadius = 1,
        int minimumVerticalOffset = 0,
        int maximumVerticalOffset = 2)
    {
        if (serverApi == null) return false;
        for (int dx = -horizontalRadius; dx <= horizontalRadius; dx++)
        for (int dz = -horizontalRadius; dz <= horizontalRadius; dz++)
        for (int dy = minimumVerticalOffset; dy <= maximumVerticalOffset; dy++)
        {
            if (dx * dx + dz * dz > horizontalRadius * horizontalRadius) continue;
            BlockPos nearby = snowPosition.AddCopy(dx, dy, dz);
            if (predicate(serverApi.World.BlockAccessor.GetBlock(nearby), nearby)) return true;
        }
        return false;
    }

    private bool IsSnowAtLedge(BlockPos snowPosition)
    {
        if (serverApi == null) return false;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        foreach (BlockFacing facing in BlockFacing.HORIZONTALS)
        {
            BlockPos beside = snowPosition.AddCopy(facing);
            BlockPos aboveBeside = beside.UpCopy();
            Block sideBlock = blocks.GetBlock(beside);
            bool solidBeside = sideBlock.GetCollisionBoxes(blocks, beside)?.Length > 0
                && !IsSnowShovelableBlock(sideBlock)
                && blocks.GetBlock(aboveBeside).GetCollisionBoxes(blocks, aboveBeside)?.Length is null or 0;
            BlockPos belowBeside = beside.DownCopy();
            bool openBeside = sideBlock.GetCollisionBoxes(blocks, beside)?.Length is null or 0;
            bool openOneBelow = blocks.GetBlock(belowBeside)
                .GetCollisionBoxes(blocks, belowBeside)?.Length is null or 0;
            BlockPos twoBelowBeside = belowBeside.DownCopy();
            bool supportedTwoBelow = blocks.GetBlock(twoBelowBeside)
                .GetCollisionBoxes(blocks, twoBelowBeside)?.Length > 0;
            if (solidBeside || (openBeside && openOneBelow && supportedTwoBelow)) return true;
        }
        return false;
    }

    private bool IsSnowInNarrowPassage(BlockPos snowPosition)
    {
        if (serverApi == null) return false;
        IBlockAccessor blocks = serverApi.World.BlockAccessor;

        bool westBlocked = HasCollision(blocks, snowPosition.AddCopy(-1, 0, 0));
        bool eastBlocked = HasCollision(blocks, snowPosition.AddCopy(1, 0, 0));
        bool northBlocked = HasCollision(blocks, snowPosition.AddCopy(0, 0, -1));
        bool southBlocked = HasCollision(blocks, snowPosition.AddCopy(0, 0, 1));
        return (westBlocked && eastBlocked && !northBlocked && !southBlocked)
            || (northBlocked && southBlocked && !westBlocked && !eastBlocked);
    }

    private static bool HasCollision(IBlockAccessor blocks, BlockPos position)
    {
        return blocks.GetBlock(position).GetCollisionBoxes(blocks, position)?.Length > 0;
    }

    private bool IsFinishedProductBlock(Block? block, BlockPos position)
    {
        if (block == null || IsWitheredPlantBlock(block)) return false;

        if (block is BlockCrop crop)
        {
            return crop.CropProps != null
                && crop.CurrentCropStage >= crop.CropProps.GrowthStages;
        }

        if (block is BlockMushroom)
        {
            return !string.Equals(block.Variant["state"], "harvested", StringComparison.OrdinalIgnoreCase);
        }

        return block.GetBEBehavior<BEBehaviorFruitingBush>(position)?.BState.Growthstate
            == EnumFruitingBushGrowthState.Ripe;
    }

    private static bool IsWitheredPlantBlock(Block block)
    {
        if (block is BlockDeadCrop) return true;

        // Variant values are folded into Vintage Story block codes. Checking
        // the path therefore covers vanilla pumpkin vines as well as modded
        // BlockCrop variants whose final numeric stage still looks mature.
        string? path = block.Code?.Path;
        return path?.Contains("wither", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(path, "deadcrop", StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryTakeFinishedProduct(Entity fox, BlockPos target)
    {
        if (serverApi == null || HasFoxStorageCargo(fox)) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block? block = blocks.GetBlock(target);
        if (!IsFinishedProductBlock(block, target) || block == null) return false;

        ItemStack[]? drops;
        Action finishHarvest;
        if (block is BlockCrop)
        {
            float cropMultiplier = 1f
                + GetFoxPackTalentRank(fox, "bountiful-crops") * 0.20f;
            drops = block.GetDrops(serverApi.World, target, null, cropMultiplier);
            BlockEntityFarmland? farmland = blocks.GetBlockEntity(target.DownCopy()) as BlockEntityFarmland;
            finishHarvest = () =>
            {
                blocks.SetBlock(0, target);
                farmland?.OnCropBlockBroken();
            };
        }
        else if (block is BlockMushroom)
        {
            float mushroomMultiplier = 1f
                + GetFoxPackTalentRank(fox, "mushroom-lore") * 0.20f;
            drops = block.GetDrops(serverApi.World, target, null, mushroomMultiplier);
            Block? harvested = serverApi.World.GetBlock(block.CodeWithVariant("state", "harvested"));
            if (harvested == null) return false;
            finishHarvest = () => blocks.SetBlock(harvested.Id, target);
        }
        else
        {
            BEBehaviorFruitingBush? berryBush = block.GetBEBehavior<BEBehaviorFruitingBush>(target);
            if (berryBush?.BState.Growthstate != EnumFruitingBushGrowthState.Ripe) return false;
            drops = berryBush.GetRipeDrops(null);
            finishHarvest = () =>
            {
                berryBush.BState.Growthstate = EnumFruitingBushGrowthState.Mature;
                berryBush.BState.TransitionHoursLeft = berryBush.GetHoursForNextStage();
                blocks.GetBlockEntity(target)?.MarkDirty(true);
            };
        }

        List<ItemStack> validDrops = drops?
            .Where(drop => drop != null && drop.StackSize > 0)
            .Select(drop => drop.Clone())
            .ToList() ?? new List<ItemStack>();
        if (validDrops.Count == 0) return false;
        // Keep the source intact if another mod adds a corpse to this harvest.
        if (validDrops.Any(drop => CompanionPickupPolicy.IsCorpse(drop.Collectible?.GetType()))) return false;

        if (block is not BlockCrop
            && block is not BlockMushroom
            && GetFoxPackTalentRank(fox, "berry-keeper") > 0)
        {
            ApplyFractionalYieldBonus(fox, validDrops, 0.20f);
        }

        finishHarvest();
        ItemStack collected = validDrops[0];
        Vec3d dropPosition = new(
            target.X + 0.5,
            target.Y + target.dimension * BlockPos.DimensionBoundary + 0.2,
            target.Z + 0.5);
        foreach (ItemStack extraDrop in validDrops.Skip(1))
        {
            // The fox has one carried stack at a time. Preserve secondary
            // harvest outputs in the world rather than silently deleting them;
            // Ground Cleanup can collect them on the next pass.
            serverApi.World.SpawnItemEntity(extraDrop, dropPosition);
        }

        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, collected);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        PlayCargoSoundRateLimited(ownerUid, "pickup", target, "game:sounds/player/collect1", 0.26f);
        AwardDutyExperience(fox, 2);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    private static void ApplyFractionalYieldBonus(Entity fox, IEnumerable<ItemStack> drops, float bonus)
    {
        // Fruiting-bush drops do not accept Vintage Story's drop multiplier.
        // Random rounding preserves a true 20% long-run bonus without turning
        // every one-berry harvest into a guaranteed extra item.
        foreach (ItemStack drop in drops)
        {
            float exactExtra = Math.Max(0, drop.StackSize) * Math.Max(0f, bonus);
            int extra = (int)Math.Floor(exactExtra);
            if (fox.World.Rand.NextDouble() < exactExtra - extra)
            {
                extra++;
            }
            drop.StackSize += extra;
        }
    }

    internal bool TryTakeFlower(Entity fox, BlockPos target)
    {
        if (serverApi == null || HasFoxStorageCargo(fox)) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block? block = blocks.GetBlock(target);
        if (!IsFlowerRemovalBlock(block) || block == null) return false;

        List<ItemStack> validDrops = block.GetDrops(serverApi.World, target, null, 1f)?
            .Where(drop => drop != null && drop.StackSize > 0)
            .Select(drop => drop.Clone())
            .ToList() ?? new List<ItemStack>();
        ItemStack collected = validDrops.FirstOrDefault() ?? new ItemStack(block, 1);
        blocks.SetBlock(0, target);

        Vec3d dropPosition = new(
            target.X + 0.5,
            target.Y + target.dimension * BlockPos.DimensionBoundary + 0.2,
            target.Z + 0.5);
        foreach (ItemStack extraDrop in validDrops.Skip(1))
        {
            serverApi.World.SpawnItemEntity(extraDrop, dropPosition);
        }

        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, collected);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        PlayCargoSoundRateLimited(ownerUid, "pickup", target, "game:sounds/player/collect1", 0.26f);
        AwardDutyExperience(fox, 2);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    internal bool TryTakeSnow(
        Entity fox,
        BlockPos target,
        bool collectSnowballs,
        bool allowWhileCarrying = false)
    {
        if (serverApi == null
            || (HasFoxStorageCargo(fox) && (!allowWhileCarrying || collectSnowballs))
            || !CanCompanionModifyBlock(fox, target)) return false;

        Block? block = serverApi.World.BlockAccessor.GetBlock(target);
        if (!IsSnowShovelableBlock(block) || block == null) return false;

        float currentSnowLevel = block.GetSnowLevel(target);
        float remainingSnowLevel = Math.Max(0f, currentSnowLevel - 1f);
        Block? replacement = block.GetSnowCoveredVariant(target, remainingSnowLevel);
        if (replacement == null) return false;
        block.PerformSnowLevelUpdate(
            serverApi.World.BlockAccessor,
            target,
            replacement,
            remainingSnowLevel);
        SyncSnowAccumulationAfterManualRemoval(target, remainingSnowLevel);
        RecordCleanupDutyUnit(fox);

        if (collectSnowballs)
        {
            Item? snowball = serverApi.World.GetItem(new AssetLocation("game:snowball-snow"));
            if (snowball != null)
            {
                ClearFoxStorageReturnSource(fox);
                ClearFoxStorageFailedTargets(fox);
                fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, new ItemStack(snowball, 2));
                fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
            }
        }

        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        PlayCargoSoundRateLimited(ownerUid, "pickup", target, "game:sounds/player/collect1", 0.22f);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    private void SyncSnowAccumulationAfterManualRemoval(BlockPos target, float remainingSnowLevel)
    {
        if (serverApi == null || target.dimension != Dimensions.NormalWorld) return;

        int chunkX = (int)Math.Floor(target.X / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(target.Z / (double)GlobalConstants.ChunkSize);
        IServerMapChunk? mapChunk = serverApi.WorldManager.GetMapChunk(chunkX, chunkZ);
        if (mapChunk == null) return;

        int index = new Vec2iStruct(target.X, target.Z).ToInChunkIndex;
        mapChunk.SnowAccum[index] = Math.Max(0f, remainingSnowLevel);
        mapChunk.MarkDirty();
    }

    private bool IsSnowTargetAvailable(Entity fox, BlockPos target)
    {
        return IsSnowReservationAvailable(fox, snowTargetReservations, SnowTargetKey(target));
    }

    private bool IsSnowApproachAvailable(Entity fox, Vec3d approach)
    {
        return IsSnowReservationAvailable(fox, snowApproachReservations, SnowApproachKey(approach));
    }

    private static bool IsSnowReservationAvailable(
        Entity fox,
        Dictionary<string, SnowTargetReservation> reservations,
        string key)
    {
        if (!reservations.TryGetValue(key, out SnowTargetReservation? reservation)) return true;
        if (reservation.ExpiresAtMs <= fox.World.ElapsedMilliseconds)
        {
            reservations.Remove(key);
            return true;
        }
        return reservation.EntityId == fox.EntityId;
    }

    internal bool TryReserveSnowTarget(Entity fox, BlockPos target, Vec3d? approach = null)
    {
        if (!IsSnowTargetAvailable(fox, target)
            || (approach != null && !IsSnowApproachAvailable(fox, approach))) return false;

        long expiresAtMs = fox.World.ElapsedMilliseconds + SnowTargetReservationLifetimeMs;
        snowTargetReservations[SnowTargetKey(target)] = new SnowTargetReservation
        {
            EntityId = fox.EntityId,
            ExpiresAtMs = expiresAtMs
        };
        if (approach != null)
        {
            snowApproachReservations[SnowApproachKey(approach)] = new SnowTargetReservation
            {
                EntityId = fox.EntityId,
                ExpiresAtMs = expiresAtMs
            };
        }
        return true;
    }

    internal bool RefreshSnowTargetReservation(Entity fox, BlockPos target, Vec3d? approach = null)
    {
        string key = SnowTargetKey(target);
        if (!snowTargetReservations.TryGetValue(key, out SnowTargetReservation? reservation)
            || reservation.EntityId != fox.EntityId) return false;
        SnowTargetReservation? approachReservation = null;
        if (approach != null
            && (!snowApproachReservations.TryGetValue(
                    SnowApproachKey(approach),
                    out approachReservation)
                || approachReservation.EntityId != fox.EntityId)) return false;

        long expiresAtMs = fox.World.ElapsedMilliseconds + SnowTargetReservationLifetimeMs;
        reservation.ExpiresAtMs = expiresAtMs;
        if (approachReservation != null) approachReservation.ExpiresAtMs = expiresAtMs;
        return true;
    }

    internal void ReleaseSnowTargetReservation(Entity fox, BlockPos? target, Vec3d? approach = null)
    {
        if (target != null)
        {
            string key = SnowTargetKey(target);
            if (snowTargetReservations.TryGetValue(key, out SnowTargetReservation? reservation)
                && reservation.EntityId == fox.EntityId)
            {
                snowTargetReservations.Remove(key);
            }
        }
        if (approach != null)
        {
            string key = SnowApproachKey(approach);
            if (snowApproachReservations.TryGetValue(key, out SnowTargetReservation? reservation)
                && reservation.EntityId == fox.EntityId)
            {
                snowApproachReservations.Remove(key);
            }
        }
    }

    private static string SnowTargetKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private static string SnowApproachKey(Vec3d position)
    {
        BlockPos cell = position.AsBlockPos;
        return SnowTargetKey(cell);
    }

    internal bool TryReserveMowingSlot(Entity fox, BlockPos dutyCenter, out string reservationKey)
    {
        reservationKey = MowingWorksiteKey(dutyCenter);
        if (mowingWorksiteReservations.TryGetValue(reservationKey, out long holderId)
            && holderId != fox.EntityId)
        {
            Entity? holder = serverApi?.World.GetEntityById(holderId);
            if (holder?.Alive == true && !IsFoxAwayFromWorld(holder)) return false;
            mowingWorksiteReservations.Remove(reservationKey);
        }

        mowingWorksiteReservations[reservationKey] = fox.EntityId;
        return true;
    }

    internal void ReleaseMowingSlot(Entity fox, string? reservationKey)
    {
        if (!string.IsNullOrWhiteSpace(reservationKey)
            && mowingWorksiteReservations.TryGetValue(reservationKey, out long holderId)
            && holderId == fox.EntityId)
        {
            mowingWorksiteReservations.Remove(reservationKey);
        }
    }

    private bool IsNaturalCleanupTargetAvailable(Entity fox, BlockPos target)
    {
        string key = NaturalCleanupKey(target);
        if (!naturalCleanupReservations.TryGetValue(key, out long holderId)) return true;
        if (holderId == fox.EntityId) return true;

        Entity? holder = serverApi?.World.GetEntityById(holderId);
        if (holder?.Alive == true && !IsFoxAwayFromWorld(holder)) return false;
        naturalCleanupReservations.Remove(key);
        return true;
    }

    private bool IsNaturalCleanupTargetBackedOff(BlockPos target)
    {
        string key = NaturalCleanupKey(target);
        if (!naturalCleanupFailureUntilMs.TryGetValue(key, out long untilMs)) return false;
        long now = serverApi?.World.ElapsedMilliseconds ?? 0;
        if (now < untilMs) return true;
        naturalCleanupFailureUntilMs.Remove(key);
        return false;
    }

    internal void MarkNaturalCleanupTargetFailed(Entity fox, BlockPos? target, string reason)
    {
        if (target == null || serverApi == null) return;
        BlockPos failedTarget = target;
        naturalCleanupFailureUntilMs[NaturalCleanupKey(failedTarget)] =
            serverApi.World.ElapsedMilliseconds + 30000;
        Block? block = serverApi.World.BlockAccessor.GetBlock(failedTarget);
        LogDutyDiagnostic(
            fox,
            "target-backoff",
            $"target={failedTarget} block={block?.Code} durationMs=30000 reason={reason}");
    }

    private bool TryReserveNaturalCleanupTarget(Entity fox, BlockPos target)
    {
        if (!IsNaturalCleanupTargetAvailable(fox, target)) return false;
        naturalCleanupReservations[NaturalCleanupKey(target)] = fox.EntityId;
        return true;
    }

    internal void ReleaseNaturalCleanupTarget(Entity fox, BlockPos? target)
    {
        if (target == null) return;
        string key = NaturalCleanupKey(target);
        if (naturalCleanupReservations.TryGetValue(key, out long holderId)
            && holderId == fox.EntityId)
        {
            naturalCleanupReservations.Remove(key);
        }
    }

    private bool IsCharcoalTargetAvailable(Entity fox, BlockPos target)
    {
        string key = CharcoalTargetKey(target);
        if (!charcoalTargetReservations.TryGetValue(key, out long holderId)) return true;
        if (holderId == fox.EntityId) return true;

        Entity? holder = serverApi?.World.GetEntityById(holderId);
        if (holder?.Alive == true && !IsFoxAwayFromWorld(holder)) return false;
        charcoalTargetReservations.Remove(key);
        return true;
    }

    private bool TryReserveCharcoalTarget(Entity fox, BlockPos target)
    {
        if (!IsCharcoalTargetAvailable(fox, target)) return false;
        charcoalTargetReservations[CharcoalTargetKey(target)] = fox.EntityId;
        return true;
    }

    internal void ReleaseCharcoalTarget(Entity fox, BlockPos? target)
    {
        if (target == null) return;
        string key = CharcoalTargetKey(target);
        if (charcoalTargetReservations.TryGetValue(key, out long holderId)
            && holderId == fox.EntityId)
        {
            charcoalTargetReservations.Remove(key);
        }
    }

    private static string MowingWorksiteKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private static string NaturalCleanupKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private static string CharcoalTargetKey(BlockPos position) =>
        $"{position.dimension}:{position.X}:{position.Y}:{position.Z}";

    private bool TryGetGroundCleanupCenter(Entity fox, out BlockPos? center)
    {
        center = null;
        if (serverApi == null || packRepository?.Loaded != true) return false;

        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? workRecord)
            && workRecord?.HasHome == true
            && string.Equals(workRecord.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
        {
            BlockPos workCart = new(workRecord.HomeX, workRecord.HomeY, workRecord.HomeZ, workRecord.HomeDimension);
            if (workCart.dimension != fox.Pos.Dimension)
            {
                return false;
            }

            double workDx = fox.Pos.X - (workCart.X + 0.5);
            double workDz = fox.Pos.Z - (workCart.Z + 0.5);
            float workRadius = GetWorkCartRadius(fox);
            if (workDx * workDx + workDz * workDz <= workRadius * workRadius)
            {
                center = workCart;
                return true;
            }
            return false;
        }

        string ownerUid = GetCompanionOwnerUid(fox);
        BlockPos? marker = GetActiveCairnPosition(ownerUid);
        if (marker == null || marker.dimension != fox.Pos.Dimension) return false;

        double dx = fox.Pos.X - (marker.X + 0.5);
        double dz = fox.Pos.Z - (marker.Z + 0.5);
        float campRadius = GetCompanionCampRadius(fox);
        if (dx * dx + dz * dz > campRadius * campRadius) return false;

        center = marker;
        return true;
    }

    internal bool TryGetCompanionDutyCenter(Entity fox, out BlockPos? center, out float radius)
    {
        radius = 0f;
        if (!TryGetGroundCleanupCenter(fox, out center) || center == null)
        {
            return false;
        }

        radius = GetGroundCleanupSearchRange(fox);
        return true;
    }

    internal bool HasAvailableGroundCleanupTarget(Entity fox)
    {
        bool workCartOwnsDebris = IsWorkCartLoggingCleanupEnabled(fox);
        if (serverApi == null || (!IsGroundCleanupEnabled(fox) && !workCartOwnsDebris)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox)) return false;

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null) return false;

        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        float searchRange = GetGroundCleanupSearchRange(fox);
        if (IsFoxAssignedToWorkCart(fox))
        {
            return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Natural);
        }
        Vec3d cleanupOrigin = FeralKinshipNaturalCleanup.GetTarget(cleanupCenter);
        EntityItem? item = (IsGroundDroppedItemsEnabled(fox) || workCartOwnsDebris)
            ? FindPrioritizedWorkCartLoggingDebris(fox)
                ?? serverApi.World.GetEntitiesAround(cleanupOrigin, searchRange, searchRange,
                    candidate => candidate is EntityItem dropped && dropped.Alive && dropped.Itemstack != null
                        && dropped.Pos.Dimension == fox.Pos.Dimension
                        && dropped.Itemstack.StackSize > 0
                        && !CompanionPickupPolicy.IsCorpse(dropped.Itemstack.Collectible?.GetType())
                        && serverApi.World.ElapsedMilliseconds - dropped.itemSpawnedMilliseconds >= 2500
                        && (!droppedItemReservations.TryGetValue(dropped.EntityId, out long reservedBy)
                            || reservedBy == fox.EntityId))
                .OfType<EntityItem>()
                .FirstOrDefault()
            : null;
        return item != null || TryFindNaturalCleanupTarget(fox, cleanupCenter, searchRange, out _, out _);
    }

    internal bool HasAvailableFinishedProductTarget(Entity fox)
    {
        if (serverApi == null || !IsFinishedProductsEnabled(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox)) return false;

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null) return false;
        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        if (IsFoxAssignedToWorkCart(fox)) return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Harvest);
        return TryFindFinishedProductTarget(
            fox,
            cleanupCenter,
            GetGroundCleanupSearchRange(fox),
            out _,
            out _);
    }

    internal bool HasAvailableFlowerRemovalTarget(Entity fox)
    {
        if (serverApi == null || !IsFlowerRemovalEnabled(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox)) return false;

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null) return false;
        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        if (IsFoxAssignedToWorkCart(fox)) return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Flowers);
        return TryFindFlowerTarget(fox, cleanupCenter, GetGroundCleanupSearchRange(fox), out _, out _);
    }

    internal bool HasAvailableCharcoalShovelTarget(Entity fox)
    {
        if (serverApi == null || !IsCharcoalShovelingEnabled(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox)) return false;

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null) return false;
        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        if (IsFoxAssignedToWorkCart(fox)) return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Charcoal);
        return TryFindCharcoalTarget(fox, cleanupCenter, GetGroundCleanupSearchRange(fox), out _, out _, reserveTarget: false);
    }

    internal bool HasAvailableSnowShovelTarget(Entity fox)
    {
        if (serverApi == null || !IsSnowShovelingEnabled(fox)
            || GetCompanionActivityMode(fox) != CompanionActivityMode.AtEase
            || ShouldFoxSeekDenShelter(fox)) return false;

        string mood = GetMood(fox);
        if (mood is "sleepy" or "resting" or "anxious" or "alarmed") return false;
        if (!TryGetFoxStorage(fox, out BlockPos? storagePos) || storagePos == null) return false;
        if (!TryGetGroundCleanupCenter(fox, out BlockPos? cleanupCenter) || cleanupCenter == null) return false;

        if (IsFoxAssignedToWorkCart(fox)) return HasQueuedWorkCartChore(fox, WorkCartChoreKind.Snow);
        return TryFindSnowTarget(fox, cleanupCenter, GetGroundCleanupSearchRange(fox), out _, out _);
    }

    /// <summary>
    /// Mowing is the lowest-priority ordinary camp chore. Its task used to
    /// select grass independently, so it could begin before another enabled
    /// chore had a chance to claim its target. Keep the arbitration here so
    /// both normal camps and Work Carts use the same yield point. Storage
    /// planning is intentionally not part of this check; an active storage
    /// job is handled by the mowing task's existing guard.
    /// </summary>
    internal bool ShouldDeferMowingForHigherPriorityDuty(Entity fox)
    {
        if (IsFoxAssignedToWorkCart(fox)) return ShouldDeferWorkCartMowing(fox);

        return HasAvailableGroundCleanupTarget(fox)
            || HasAvailableCharcoalShovelTarget(fox)
            || HasAvailableFinishedProductTarget(fox)
            || HasAvailableFlowerRemovalTarget(fox)
            || HasAvailableSnowShovelTarget(fox);
    }

    internal bool TryGetNaturalCleanupItemPreview(BlockPos target, out ItemStack? collected)
    {
        collected = null;
        if (serverApi == null) return false;

        Block? block = serverApi.World.BlockAccessor.GetBlock(target);
        if (!FeralKinshipNaturalCleanup.IsEligible(block)) return false;

        if (FeralKinshipNaturalCleanup.IsCattail(block))
        {
            Item? cattailTops = serverApi.World.GetItem(new AssetLocation("game:cattailtops"));
            if (cattailTops == null) return false;
            collected = new ItemStack(cattailTops);
            return true;
        }

        // Match the actual pickup path, including the vanilla drop roll and
        // the loose-stone fallback used when a block has no player-context
        // drop result. This is only a preview; it does not change the world.
        ItemStack[]? drops = block.GetDrops(serverApi.World, target, null, 1f);
        collected = drops?.FirstOrDefault(drop => drop != null && drop.StackSize > 0)?.Clone();
        if (collected == null && block.Code?.Path != null
            && (block.Code.Path.StartsWith("loosestones-", StringComparison.OrdinalIgnoreCase)
                || block.Code.Path.StartsWith("looseboulders-", StringComparison.OrdinalIgnoreCase)
                || block.Code.Path.StartsWith("looseflints-", StringComparison.OrdinalIgnoreCase)))
        {
            if (block.Code.Path.StartsWith("looseflints-", StringComparison.OrdinalIgnoreCase))
            {
                Item? flint = serverApi.World.GetItem(new AssetLocation("game:flint"));
                if (flint != null) collected = new ItemStack(flint);
            }
            else
            {
                string? rock = block.Variant["rock"];
                Block? stone = !string.IsNullOrWhiteSpace(rock)
                    ? serverApi.World.GetBlock(new AssetLocation($"game:stone-{rock}"))
                    : null;
                if (stone != null)
                {
                    int quantity = block.Code.Path.StartsWith("looseboulders-", StringComparison.OrdinalIgnoreCase)
                        ? 5
                        : 1;
                    collected = new ItemStack(stone, quantity);
                }
            }
        }

        return collected != null;
    }

    private bool TryGetCharcoalItemPreview(BlockPos target, out ItemStack? collected)
    {
        collected = null;
        if (serverApi == null) return false;

        if (!TryResolveCharcoalTopTarget(target, out BlockPos topTarget)) return false;
        Block? block = serverApi.World.BlockAccessor.GetBlock(topTarget);
        if (!IsCharcoalPileBlock(block)) return false;
        collected = block.GetDrops(serverApi.World, topTarget, null, 1f)?
            .FirstOrDefault(drop => drop != null && drop.StackSize > 0)?.Clone();
        if (collected != null) collected.StackSize = 1;
        return collected != null;
    }

    internal bool TryTakeCharcoal(Entity fox, BlockPos target)
    {
        if (serverApi == null || HasFoxStorageCargo(fox)) return false;
        if (!TryResolveCharcoalTopTarget(target, out BlockPos topTarget)
            || !CanCompanionModifyBlock(fox, topTarget)) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        Block? block = blocks.GetBlock(topTarget);
        if (!IsCharcoalPileBlock(block) || block == null) return false;

        List<ItemStack> validDrops = block.GetDrops(serverApi.World, topTarget, null, 1f)?
            .Where(drop => drop != null && drop.StackSize > 0)
            .Select(drop => drop.Clone())
            .ToList() ?? new List<ItemStack>();
        if (validDrops.Count == 0) return false;

        // Match the vanilla charcoal-pile interaction: the highest contiguous
        // pile block is the one that breaks, and one layer is removed.
        Block? previousLayer = (block as BlockLayeredSlowDig)?.GetPrevLayer(serverApi.World);
        IPlayer? owner = GetCompanionBlockAccessPlayer(fox);
        // Use the block's own break definition, including its charcoal sound
        // variants and pitch range, just like vanilla block breaking. The
        // pickup cue below is intentionally omitted: charcoal breaking is
        // already the pickup action, and the generic collect sound was being
        // pitch-shifted into an unrelated deep-sounding effect.
        var breakSound = block.Sounds.GetBreakSound(owner);
        if (breakSound != null)
        {
            serverApi.World.PlaySoundAt(breakSound, topTarget, -0.5, null, 1f);
        }
        block.SpawnBlockBrokenParticles(topTarget, owner);
        blocks.SetBlock(previousLayer?.Id ?? 0, topTarget);
        blocks.TriggerNeighbourBlockUpdate(topTarget);

        // Charcoal is a one-piece mouth carry. Do not leak secondary drops to
        // the ground, and do not preserve a larger stack from a modded drop.
        ItemStack carried = validDrops[0];
        carried.StackSize = 1;

        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, carried);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        AwardDutyExperience(fox, 2);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    internal bool TryTakeNaturalStorageItem(Entity fox, BlockPos target)
    {
        if (serverApi == null || HasFoxStorageCargo(fox)) return false;

        Block? block = serverApi.World.BlockAccessor.GetBlock(target);
        if (!FeralKinshipNaturalCleanup.IsEligible(block)) return false;
        int cleanupPriority = FeralKinshipNaturalCleanup.GetPriority(block);

        ItemStack? collected = null;
        if (FeralKinshipNaturalCleanup.IsCattail(block))
        {
            Item? cattailTops = serverApi.World.GetItem(new AssetLocation("game:cattailtops"));
            Block? harvested = serverApi.World.GetBlock(block.CodeWithVariant("state", "harvested"));
            if (cattailTops == null || harvested == null) return false;

            collected = new ItemStack(cattailTops);
            serverApi.World.BlockAccessor.SetBlock(harvested.Id, target);
        }
        else
        {
            // Preserve the vanilla block drop roll. Loose flints are defined
            // as avg 1.3, var 0, which means one flint most of the time and
            // two flints on the fractional roll; do not normalize this to 1.
            // The same GetDrops path is used by the player's pickup behavior.
            ItemStack[]? drops = block.GetDrops(serverApi.World, target, null, 1f);
            collected = drops?.FirstOrDefault(drop => drop != null && drop.StackSize > 0)?.Clone();
            if (collected == null && block.Code?.Path != null
                && (block.Code.Path.StartsWith("loosestones-", StringComparison.OrdinalIgnoreCase)
                    || block.Code.Path.StartsWith("looseboulders-", StringComparison.OrdinalIgnoreCase)
                    || block.Code.Path.StartsWith("looseflints-", StringComparison.OrdinalIgnoreCase)))
            {
                // These blocks normally resolve their stone drops through the
                // vanilla block-drop table. Keep the AI cleanup path useful if
                // that table returns no stack without a player context.
                if (block.Code.Path.StartsWith("looseflints-", StringComparison.OrdinalIgnoreCase))
                {
                    Item? flint = serverApi.World.GetItem(new AssetLocation("game:flint"));
                    if (flint != null) collected = new ItemStack(flint);
                }
                else
                {
                    string? rock = block.Variant["rock"];
                    Block? stone = !string.IsNullOrWhiteSpace(rock)
                        ? serverApi.World.GetBlock(new AssetLocation($"game:stone-{rock}"))
                        : null;
                    if (stone != null)
                    {
                        int quantity = block.Code.Path.StartsWith("looseboulders-", StringComparison.OrdinalIgnoreCase)
                            ? 5
                            : 1;
                        collected = new ItemStack(stone, quantity);
                    }
                }
            }
            if (collected == null || CompanionPickupPolicy.IsCorpse(collected.Collectible?.GetType())) return false;

            serverApi.World.BlockAccessor.SetBlock(0, target);
        }

        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, collected);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        PlayCargoSoundRateLimited(ownerUid, "pickup", target, "game:sounds/player/collect1", 0.26f);
        AwardDutyExperience(fox, cleanupPriority is 1 or 3 or 4 ? 4 : 2);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    internal bool TryTakeReservedStorageItem(Entity fox, long itemEntityId, BlockPos storagePos)
    {
        if (serverApi == null || itemEntityId <= 0
            || !droppedItemReservations.TryGetValue(itemEntityId, out long foxId)
            || foxId != fox.EntityId
            || fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey) != null) return false;

        EntityItem? item = serverApi.World.GetEntityById(itemEntityId) as EntityItem;
        if (item?.Alive != true || item.Itemstack == null
            || item.Pos.Dimension != fox.Pos.Dimension
            || item.Pos.SquareDistanceTo(fox.Pos) > 2.2 * 2.2) return false;

        // The destination may have changed while the fox was walking to the
        // dropped item. Re-run the same whole-pickup capacity check immediately
        // before taking anything so a partial destination cannot leave the fox
        // carrying a remainder it was never able to deposit.
        ItemStack? pickupPreview = GetDroppedItemPickupPreview(fox, item);
        if (pickupPreview == null || !CanFoxStorageAcceptStack(storagePos, pickupPreview)) return false;

        bool canGatherNearby = GetFoxPerkRank(fox, "mouthful") > 0
            || GetFoxPackTalentRank(fox, "mouthful") > 0;
        ItemStack? taken = canGatherNearby
            ? TakeNearbyCompatibleItems(fox, item)
            : item.Slot.TakeOut(1);
        if (taken == null || taken.StackSize <= 0) return false;
        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, taken);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        if (item.Slot.Empty)
        {
            item.Itemstack = null;
            item.Alive = false;
        }
        else
        {
            item.Itemstack = item.Slot.Itemstack;
        }
        droppedItemReservations.Remove(itemEntityId);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        PlayCargoSoundRateLimited(ownerUid, "pickup", fox.Pos.AsBlockPos, "game:sounds/player/collect1", 0.26f);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    private ItemStack? TakeNearbyCompatibleItems(Entity fox, EntityItem primary)
    {
        if (serverApi == null || primary.Itemstack == null) return null;

        ItemStack reference = primary.Itemstack.Clone();
        reference.ResolveBlockOrItem(serverApi.World);
        if (reference.Collectible == null) return null;

        int remaining = reference.Collectible.MaxStackSize;
        ItemStack? taken = primary.Slot.TakeOut(Math.Min(primary.Slot.StackSize, remaining));
        if (taken == null || taken.StackSize <= 0) return null;
        remaining -= taken.StackSize;
        UpdateDroppedItemEntity(primary);

        if (remaining <= 0) return taken;

        EntityItem[] nearby = serverApi.World.GetEntitiesAround(
                primary.Pos.XYZ,
                2.5f,
                2.5f,
                candidate => candidate is EntityItem item
                    && item.EntityId != primary.EntityId
                    && item.Alive
                    && item.Pos.Dimension == primary.Pos.Dimension
                    && item.Pos.SquareDistanceTo(primary.Pos) <= 2.5f * 2.5f
                    && item.Itemstack != null
                    && item.Itemstack.StackSize > 0
                    && (!droppedItemReservations.TryGetValue(item.EntityId, out long reservedBy)
                        || reservedBy == fox.EntityId))
            .OfType<EntityItem>()
            .OrderBy(item => item.Pos.SquareDistanceTo(primary.Pos))
            .ToArray();

        foreach (EntityItem candidate in nearby)
        {
            if (remaining <= 0 || candidate.Itemstack == null) break;
            ItemStack candidateStack = candidate.Itemstack.Clone();
            candidateStack.ResolveBlockOrItem(serverApi.World);
            if (!reference.Equals(
                    serverApi.World,
                    candidateStack,
                    GlobalConstants.IgnoredStackAttributes)) continue;

            int amount = Math.Min(remaining, candidate.Slot.StackSize);
            ItemStack? extra = candidate.Slot.TakeOut(amount);
            if (extra == null || extra.StackSize <= 0) continue;

            taken.StackSize += extra.StackSize;
            remaining -= extra.StackSize;
            UpdateDroppedItemEntity(candidate);
            droppedItemReservations.Remove(candidate.EntityId);
        }

        return taken;
    }

    private static void UpdateDroppedItemEntity(EntityItem item)
    {
        if (item.Slot.Empty)
        {
            item.Itemstack = null;
            item.Alive = false;
        }
        else
        {
            item.Itemstack = item.Slot.Itemstack;
        }
    }

    internal bool TryTakePackLootAtCart(Entity fox, BlockPos storagePos)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || !fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false)
            || HasFoxStorageCargo(fox)) return false;
        if (serverApi.World.BlockAccessor.GetBlockEntity(storagePos) is not BlockEntityGenericTypedContainer container)
        {
            return false;
        }

        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        if (!TryGetFoxPackCartPickupTarget(fox, out Vec3d? cartTarget) || cartTarget == null
            || fox.Pos.SquareDistanceTo(cartTarget) > 1.25 * 1.25)
        {
            return false;
        }

        List<byte[]> remaining = new();
        ItemStack? load = null;
        foreach (byte[] bytes in packRepository.GetLootBytes(ownerUid))
        {
            ItemStack stack = new(bytes);
            if (load == null && stack.ResolveBlockOrItem(serverApi.World) && stack.Collectible != null
                && stack.StackSize > 0
                && !CompanionPickupPolicy.IsCorpse(stack.Collectible.GetType()))
            {
                int quantity = GetFoxPerkRank(fox, "mouthful") > 0
                    || GetFoxPackTalentRank(fox, "mouthful") > 0
                    ? stack.StackSize
                    : 1;
                ItemStack proposedLoad = stack.GetEmptyClone();
                proposedLoad.StackSize = quantity;
                if (!CanInventoryFullyAccept(container.Inventory, proposedLoad))
                {
                    remaining.Add(bytes);
                    continue;
                }
                load = proposedLoad;
                stack.StackSize -= quantity;
                if (stack.StackSize > 0) remaining.Add(stack.ToBytes());
            }
            else
            {
                remaining.Add(bytes);
            }
        }

        fox.WatchedAttributes.SetBool(FoxStorageCartPickupKey, false);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
        if (load == null)
        {
            if (packRepository.IsCargoUnloadingActive(ownerUid))
            {
                packRepository.SetCargoUnloadingActive(ownerUid, false);
                ClearPendingCartPickupJobs(ownerUid);
                packRepository.Save();
                if (serverApi.World.PlayerByUid(ownerUid) is IServerPlayer owner)
                {
                    owner.SendMessage(GlobalConstants.GeneralChatGroup,
                        "Continuous cart unloading stopped because no remaining cache stack fits in the Pack Collection Box.",
                        EnumChatType.Notification);
                    if (packViewers.Contains(ownerUid))
                    {
                        SendPackState(owner, "Unloading stopped: make more room in the Pack Collection Box, then start it again.");
                    }
                }
            }
            return false;
        }

        ClearFoxStorageReturnSource(fox);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, load);
        fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, true);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
        packRepository.ReplaceLoot(ownerUid, remaining);
        packRepository.Save();
        PlayCargoSoundRateLimited(ownerUid, "pickup", fox.Pos.AsBlockPos, "game:sounds/player/collect1", 0.28f);
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return true;
    }

    internal bool TryDepositFoxStorageCargo(
        Entity fox,
        BlockPos storagePos,
        IReadOnlyCollection<BlockPos> previouslyAttempted,
        out bool allDeposited,
        out bool incompatible,
        out BlockPos? nextStoragePos)
    {
        allDeposited = false;
        incompatible = false;
        nextStoragePos = null;
        if (serverApi == null) return false;
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0)
        {
            allDeposited = true;
            return true;
        }
        if (carried.Collectible == null && !carried.ResolveBlockOrItem(serverApi.World))
        {
            // WatchedAttributes serializes item stacks and may return them
            // without their runtime Collectible reference. Survival inventory
            // slots dereference that reference while evaluating suitability.
            // Resolve it before asking the collection box for a destination.
            DropFoxStorageCargo(fox);
            return false;
        }

        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        List<BlockPos> attemptedDestinations = previouslyAttempted.ToList();
        BlockPos? selectedStoragePos = storagePos.dimension == fox.Pos.Dimension
            ? NormalizeStorageRoutingPosition(storagePos)
            : null;
        bool depositedAny = false;
        bool encounteredNoProgress = false;

        // Plans may outlive a storage-rule or capacity change, and the pickup
        // plan may have selected a generic nearby container before the item
        // was known. Process only the bin the fox physically reached. If it
        // is full or accepts only part of the cargo, return the next live
        // destination so the AI can walk there before attempting insertion.
        bool targetProcessed = false;
        while (HasFoxStorageCargo(fox) && !targetProcessed)
        {
            carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
            if (carried == null || carried.StackSize <= 0) break;

            if (selectedStoragePos == null || attemptedDestinations.Any(pos => pos.Equals(selectedStoragePos)))
            {
                if (!TryGetPackStorage(
                        ownerUid,
                        fox.Pos.Dimension,
                        fox.Pos.XYZ,
                        out selectedStoragePos,
                        carried,
                        requireFullStack: false,
                        excludedStoragePositions: attemptedDestinations,
                        fox: fox)
                    || selectedStoragePos == null)
                {
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"no other accepting destination for {carried.Collectible?.Code}x{carried.StackSize}; tried={attemptedDestinations.Count}");
                    break;
                }
            }

            BlockPos plannedStoragePos = selectedStoragePos;
            BlockPos targetPos = NormalizeStorageRoutingPosition(plannedStoragePos);
            selectedStoragePos = null;
            if (attemptedDestinations.Any(pos => pos.Equals(targetPos))) continue;
            if (!attemptedDestinations.Any(pos => pos.Equals(plannedStoragePos)))
            {
                attemptedDestinations.Add(plannedStoragePos);
            }
            attemptedDestinations.Add(targetPos);
            targetProcessed = true;
            if (targetPos.dimension != fox.Pos.Dimension) continue;

            FoxPackAmenityRecord? routingRecord = packRepository?.GetAmenity(targetPos);
            if (routingRecord == null
                || !string.Equals(routingRecord.OwnerUid, ownerUid, StringComparison.Ordinal)
                || !IsStorageRoutingEnabled(routingRecord)
                || !StorageRoutingAccepts(routingRecord, carried))
            {
                LogStorageRoutingDiagnostic(fox, $"deposit target {targetPos} no longer accepts {carried.Collectible?.Code}");
                continue;
            }

            Block? block = serverApi.World.BlockAccessor.GetBlock(targetPos);
            BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(targetPos);
            if (!FoxStorageRouting.IsSupportedStorageTarget(block, blockEntity)
                || blockEntity is not IBlockEntityContainer container)
            {
                LogStorageRoutingDiagnostic(fox, $"deposit target {targetPos} has no usable capacity for {carried.Collectible?.Code}");
                continue;
            }

            IInventory? inventory;
            try
            {
                inventory = container.Inventory;
            }
            catch (Exception exception)
            {
                LogStorageRoutingDiagnostic(fox, $"deposit target {targetPos} inventory query failed: {exception.Message}");
                continue;
            }
            if (inventory == null)
            {
                LogStorageRoutingDiagnostic(fox, $"deposit target {targetPos} has no usable capacity for {carried.Collectible?.Code}");
                continue;
            }

            int bulkCapacitySlots = inventory.Count(slot => FoxStorageBulkInsertion.CanTransfer(slot, carried));
            if (!CanInventoryAcceptOne(inventory, carried))
            {
                LogStorageRoutingDiagnostic(
                    fox,
                    $"deposit target {targetPos} block={block?.Code} rejects {carried.Collectible?.Code}; "
                    + $"bulk-capacity-slots={bulkCapacitySlots}/{inventory.Count}");
                continue;
            }

            LogStorageRoutingDiagnostic(
                fox,
                $"deposit attempt target={targetPos} block={block?.Code} item={carried.Collectible?.Code} "
                + $"stack={carried.StackSize} bulk-capacity-slots={bulkCapacitySlots}/{inventory.Count}");

            int beforeDeposit = carried.StackSize;
            int moved = TransferCargoIntoInventory(fox, carried, inventory, blockEntity, out ItemStack? remaining);
            if (remaining == null)
            {
                fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
                ClearFoxStorageFailedTargets(fox);
                fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
                fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
                fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
                ClearFoxStorageReturnSource(fox);
                allDeposited = true;
                depositedAny = true;
            }
            else
            {
                fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, remaining);
                fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
                depositedAny |= moved > 0;
                encounteredNoProgress |= moved <= 0;
            }

            if (moved > 0)
            {
                LogStorageRoutingDiagnostic(
                    fox,
                    $"deposit at {targetPos} moved {moved}/{beforeDeposit}; remaining={remaining?.StackSize ?? 0}",
                    force: true);
                PlayCargoSoundRateLimited(ownerUid, "deposit", targetPos, "game:sounds/block/chestclose", 0.30f);
            }
            else
            {
                LogStorageRoutingDiagnostic(
                    fox,
                    $"deposit at {targetPos} made no progress; cargo={beforeDeposit}x{carried.Collectible?.Code}",
                    force: true);
            }
        }

        if (HasFoxStorageCargo(fox))
        {
            carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
            if (TryGetPackStorage(
                    ownerUid,
                    fox.Pos.Dimension,
                    fox.Pos.XYZ,
                    out BlockPos? alternateStoragePos,
                    carried,
                    requireFullStack: false,
                    excludedStoragePositions: attemptedDestinations,
                    fox: fox)
                && alternateStoragePos != null
                && !attemptedDestinations.Any(pos => pos.Equals(alternateStoragePos)))
            {
                nextStoragePos = alternateStoragePos;
                LogStorageRoutingDiagnostic(
                    fox,
                    $"next accepting destination for {carried?.Collectible?.Code}x{carried?.StackSize} is {nextStoragePos}; fox must route there",
                    force: true);
            }
            else
            {
                nextStoragePos = null;
                // Items moved out of General get one last chance to go back
                // where they came from. Otherwise leave no invisible cargo.
                bool returnedToSource = TryReturnFoxStorageCargoToSource(fox);
                if (!returnedToSource)
                {
                    carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
                    LogStorageRoutingDiagnostic(
                        fox,
                        $"no untried accepting destination; dropping {carried?.StackSize ?? 0}x {carried?.Collectible?.Code}",
                        force: true);
                    DropFoxStorageCargo(fox);
                }
                encounteredNoProgress |= !returnedToSource;
            }
        }

        incompatible = encounteredNoProgress;
        SendStateToOwner(fox, string.Empty);
        SendPackStateToOwner(fox);
        return depositedAny;
    }

    private int TransferCargoIntoInventory(
        Entity fox,
        ItemStack cargo,
        IInventory inventory,
        BlockEntity? containerEntity,
        out ItemStack? remaining)
    {
        DummySlot source = new(cargo);
        int before = source.StackSize;
        List<ItemSlot> skippedSlots = new();
        try
        {
            while (!source.Empty)
            {
                ItemSlot? target;
                try
                {
                    target = inventory.GetBestSuitedSlot(source, null, skippedSlots)?.slot;
                    if (target == null)
                    {
                        target = inventory.FirstOrDefault(slot => !skippedSlots.Contains(slot)
                            && FoxStorageBulkInsertion.CanTransfer(slot, source.Itemstack!));
                    }
                }
                catch (Exception exception)
                {
                    LogStorageRoutingDiagnostic(fox, $"inventory slot selection failed: {exception.Message}");
                    break;
                }

                if (target == null) break;
                int sourceSizeBefore = source.StackSize;
                bool useBulkTransfer = FoxStorageBulkInsertion.CanTransfer(target, source.Itemstack!);
                try
                {
                    int moved = useBulkTransfer
                        ? FoxStorageBulkInsertion.Transfer(source, serverApi!.World, target, source.StackSize)
                        : source.TryPutInto(serverApi!.World, target, source.StackSize);
                    if (moved <= 0 || (!source.Empty && source.StackSize >= sourceSizeBefore))
                    {
                        skippedSlots.Add(target);
                    }
                }
                catch (Exception exception)
                {
                    skippedSlots.Add(target);
                    LogStorageRoutingDiagnostic(fox, $"inventory slot transfer failed: {exception.Message}");
                }
            }
        }
        finally
        {
            try
            {
                containerEntity?.MarkDirty();
            }
            catch (Exception exception)
            {
                LogStorageRoutingDiagnostic(fox, $"inventory mark-dirty failed: {exception.Message}");
            }
            remaining = source.Empty ? null : source.Itemstack;
        }

        return Math.Max(0, before - (remaining?.StackSize ?? 0));
    }

    private bool TryReturnFoxStorageCargoToSource(Entity fox)
    {
        if (serverApi == null
            || !fox.WatchedAttributes.GetBool(FoxStorageReturnSourceValidKey, false)
            || !HasFoxStorageCargo(fox)) return false;

        int x = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceXKey, 0);
        int y = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceYKey, 0);
        int z = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceZKey, 0);
        int dimension = fox.WatchedAttributes.GetInt(FoxStorageReturnSourceDimensionKey, fox.Pos.Dimension);
        BlockPos sourcePos = new(x, y, z, dimension);
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        FoxPackAmenityRecord? sourceRecord = packRepository?.GetAmenity(sourcePos);
        BlockEntity? sourceBlockEntity = serverApi.World.BlockAccessor.GetBlockEntity(sourcePos);
        if (dimension != fox.Pos.Dimension
            || sourceRecord == null
            || !string.Equals(sourceRecord.OwnerUid, ownerUid, StringComparison.Ordinal)
            || sourceBlockEntity is not IBlockEntityContainer sourceContainer
            || !FoxStorageRouting.IsSupportedStorageTarget(
                serverApi.World.BlockAccessor.GetBlock(sourcePos),
                sourceBlockEntity))
        {
            return false;
        }

        IInventory? sourceInventory;
        try
        {
            sourceInventory = sourceContainer.Inventory;
        }
        catch
        {
            return false;
        }
        if (sourceInventory == null) return false;

        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0) return false;
        int moved = TransferCargoIntoInventory(
            fox,
            carried,
            sourceInventory,
            sourceBlockEntity,
            out ItemStack? remaining);
        if (remaining != null)
        {
            if (moved > 0)
            {
                fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, remaining);
                fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
            }
            LogStorageRoutingDiagnostic(fox, $"could not fully return cargo to source {sourcePos}; moved={moved}, remaining={remaining.StackSize}");
            return false;
        }

        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
        ClearFoxStorageFailedTargets(fox);
        ClearFoxStorageReturnSource(fox);
        fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
        PlayCargoSoundRateLimited(ownerUid, "deposit", sourcePos, "game:sounds/block/chestclose", 0.30f);
        LogStorageRoutingDiagnostic(fox, $"returned {moved} item(s) to original storage {sourcePos}");
        return true;
    }

    private static void SetFoxStorageReturnSource(Entity fox, BlockPos sourcePos)
    {
        fox.WatchedAttributes.SetInt(FoxStorageReturnSourceXKey, sourcePos.X);
        fox.WatchedAttributes.SetInt(FoxStorageReturnSourceYKey, sourcePos.Y);
        fox.WatchedAttributes.SetInt(FoxStorageReturnSourceZKey, sourcePos.Z);
        fox.WatchedAttributes.SetInt(FoxStorageReturnSourceDimensionKey, sourcePos.dimension);
        fox.WatchedAttributes.SetBool(FoxStorageReturnSourceValidKey, true);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceXKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceYKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceZKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceDimensionKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceValidKey);
    }

    private static void ClearFoxStorageReturnSource(Entity fox)
    {
        fox.WatchedAttributes.SetBool(FoxStorageReturnSourceValidKey, false);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageReturnSourceValidKey);
    }

    internal static IReadOnlyList<BlockPos> GetFoxStorageFailedTargets(Entity fox)
    {
        List<BlockPos> targets = new();
        foreach (string encoded in fox.WatchedAttributes.GetStringArray(
                     FoxStorageFailedTargetsKey,
                     Array.Empty<string>()))
        {
            string[] parts = encoded.Split(':');
            if (parts.Length != 4
                || !int.TryParse(parts[0], out int dimension)
                || !int.TryParse(parts[1], out int x)
                || !int.TryParse(parts[2], out int y)
                || !int.TryParse(parts[3], out int z)) continue;
            targets.Add(new BlockPos(x, y, z, dimension));
        }
        return targets;
    }

    internal static void RememberFoxStorageFailedTarget(Entity fox, BlockPos target)
    {
        List<BlockPos> targets = GetFoxStorageFailedTargets(fox).ToList();
        if (targets.Any(existing => existing.Equals(target))) return;
        targets.Add(new BlockPos(target.X, target.Y, target.Z, target.dimension));
        fox.WatchedAttributes.SetStringArray(
            FoxStorageFailedTargetsKey,
            targets.TakeLast(16)
                .Select(pos => $"{pos.dimension}:{pos.X}:{pos.Y}:{pos.Z}")
                .ToArray());
        fox.WatchedAttributes.MarkPathDirty(FoxStorageFailedTargetsKey);
    }

    private static void ClearFoxStorageFailedTargets(Entity fox)
    {
        fox.WatchedAttributes.SetStringArray(FoxStorageFailedTargetsKey, Array.Empty<string>());
        fox.WatchedAttributes.MarkPathDirty(FoxStorageFailedTargetsKey);
    }

    internal void ReleaseDroppedItemReservation(Entity fox, long itemEntityId)
    {
        if (itemEntityId > 0 && droppedItemReservations.TryGetValue(itemEntityId, out long foxId)
            && foxId == fox.EntityId) droppedItemReservations.Remove(itemEntityId);
    }

    private bool TryGetFoxStorage(
        Entity fox,
        out BlockPos? storagePos,
        ItemStack? carried = null,
        bool requireFullStack = true,
        IReadOnlyCollection<BlockPos>? excludedStoragePositions = null)
    {
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        carried ??= fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        return TryGetPackStorage(
            ownerUid,
            fox.Pos.Dimension,
            fox.Pos.XYZ,
            out storagePos,
            carried,
            requireFullStack: requireFullStack,
            excludedStoragePositions: excludedStoragePositions,
            fox: fox);
    }

    private bool CanFoxStorageAcceptStack(BlockPos storagePos, ItemStack stack)
    {
        if (serverApi == null
            || serverApi.World.BlockAccessor.GetBlockEntity(storagePos) is not IBlockEntityContainer container
            || !FoxStorageRouting.IsSupportedStorageTarget(
                serverApi.World.BlockAccessor.GetBlock(storagePos),
                serverApi.World.BlockAccessor.GetBlockEntity(storagePos))
            || !CanInventoryFullyAccept(container.Inventory, stack))
        {
            return false;
        }

        FoxPackAmenityRecord? record = packRepository?.GetAmenity(storagePos);
        return record != null
            && IsStorageRoutingEnabled(record)
            && StorageRoutingAccepts(record, stack);
    }

    private ItemStack? GetDroppedItemPickupPreview(Entity fox, EntityItem primary)
    {
        if (serverApi == null || primary.Itemstack == null) return null;

        ItemStack reference = primary.Itemstack.Clone();
        if (!reference.ResolveBlockOrItem(serverApi.World) || reference.Collectible == null
            || CompanionPickupPolicy.IsCorpse(reference.Collectible.GetType()))
        {
            return null;
        }

        bool carriesWholeStack = GetFoxPerkRank(fox, "mouthful") > 0
            || GetFoxPackTalentRank(fox, "mouthful") > 0;
        int amount = carriesWholeStack
            ? Math.Min(primary.Itemstack.StackSize, reference.Collectible.MaxStackSize)
            : Math.Min(primary.Itemstack.StackSize, 1);

        if (carriesWholeStack)
        {
            int remaining = Math.Max(0, reference.Collectible.MaxStackSize - amount);
            if (remaining > 0)
            {
                EntityItem[] nearby = serverApi.World.GetEntitiesAround(
                        primary.Pos.XYZ,
                        2.5f,
                        2.5f,
                        candidate => candidate is EntityItem item
                            && item.EntityId != primary.EntityId
                            && item.Alive
                            && item.Pos.Dimension == primary.Pos.Dimension
                            && item.Pos.SquareDistanceTo(primary.Pos) <= 2.5f * 2.5f
                            && item.Itemstack != null
                            && item.Itemstack.StackSize > 0
                            && (!droppedItemReservations.TryGetValue(item.EntityId, out long reservedBy)
                                || reservedBy == fox.EntityId))
                    .OfType<EntityItem>()
                    .OrderBy(item => item.Pos.SquareDistanceTo(primary.Pos))
                    .ToArray();

                foreach (EntityItem candidate in nearby)
                {
                    if (remaining <= 0 || candidate.Itemstack == null) break;

                    ItemStack candidateStack = candidate.Itemstack.Clone();
                    if (!candidateStack.ResolveBlockOrItem(serverApi.World)
                        || !reference.Equals(
                            serverApi.World,
                            candidateStack,
                            GlobalConstants.IgnoredStackAttributes)) continue;

                    int added = Math.Min(remaining, candidate.Itemstack.StackSize);
                    amount += added;
                    remaining -= added;
                }
            }
        }

        if (amount <= 0) return null;
        ItemStack preview = reference.GetEmptyClone();
        preview.StackSize = amount;
        return preview;
    }

    private bool TryGetFoxPackCartPickupTarget(Entity fox, out Vec3d? target)
    {
        target = null;
        if (serverApi == null || packRepository?.Loaded != true) return false;
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerUid) || !packRepository.HasLoot(ownerUid)
            || !packRepository.IsCargoUnloadingActive(ownerUid))
        {
            fox.WatchedAttributes.SetBool(FoxStorageCartPickupKey, false);
            fox.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
            return false;
        }

        BlockPos? cart = GetActiveCairnPosition(ownerUid);
        if (cart == null || cart.dimension != fox.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) == null) return false;
        Block block = serverApi.World.BlockAccessor.GetBlock(cart);
        BlockPos portal = GetPackMarkerPortalBlock(cart, block);
        target = new Vec3d(
            portal.X + 0.5,
            portal.Y + portal.dimension * BlockPos.DimensionBoundary,
            portal.Z + 0.5
        );
        return true;
    }

    internal bool TryGetFoxStorageApproachTarget(Entity fox, BlockPos storagePos, out Vec3d? target)
    {
        target = null;
        if (serverApi == null)
        {
            return false;
        }

        (int X, int Z)[] offsets = {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (-1, 1), (1, -1), (1, 1),
            (-2, 0), (2, 0), (0, -2), (0, 2), (-2, -1), (-2, 1), (2, -1), (2, 1),
            (-1, -2), (1, -2), (-1, 2), (1, 2)
        };
        int[] verticalOffsets = { 0, -1, -2 };
        foreach ((int x, int z) in offsets
                     .OrderBy(offset => Math.Abs(offset.X) + Math.Abs(offset.Z))
                     .ThenBy(offset => fox.Pos.SquareDistanceTo(
                         new Vec3d(storagePos.X + offset.X + 0.5,
                             storagePos.Y + storagePos.dimension * BlockPos.DimensionBoundary,
                             storagePos.Z + offset.Z + 0.5))))
        {
            foreach (int verticalOffset in verticalOffsets)
            {
                BlockPos candidate = storagePos.AddCopy(x, verticalOffset, z);
                BlockPos below = candidate.DownCopy();
                Vec3d candidateTarget = new(
                    candidate.X + 0.5,
                    candidate.Y + candidate.dimension * BlockPos.DimensionBoundary,
                    candidate.Z + 0.5
                );
                if (IsValidFoxStorageApproach(fox, candidate, below, candidateTarget))
                {
                    target = candidateTarget;
                    return true;
                }
            }
        }

        return false;
    }

    internal Vec3d GetFoxStorageApproachTarget(Entity fox, BlockPos storagePos)
    {
        if (TryGetFoxStorageApproachTarget(fox, storagePos, out Vec3d? target) && target != null)
        {
            return target;
        }

        return new Vec3d(
            storagePos.X + 0.5,
            storagePos.Y + storagePos.dimension * BlockPos.DimensionBoundary,
            storagePos.Z + 0.5
        );
    }

    private bool IsValidFoxStorageApproach(Entity fox, BlockPos candidate, BlockPos below, Vec3d target)
    {
        if (serverApi == null || candidate.dimension != fox.Pos.Dimension) return false;

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        if (blocks.GetChunkAtBlockPos(candidate) == null
            || blocks.IsNotTraversable(candidate)
            || blocks.IsNotTraversable(candidate.UpCopy())
            || !blocks.GetBlock(below).SideSolid[BlockFacing.UP.Index]
            || blocks.GetBlock(candidate, BlockLayersAccess.Fluid).IsLiquid()
            || blocks.GetBlock(candidate.UpCopy(), BlockLayersAccess.Fluid).IsLiquid())
        {
            return false;
        }

        return !serverApi.World.CollisionTester.IsColliding(
            blocks,
            fox.CollisionBox,
            target,
            false);
    }

    private bool TryGetPackStorage(
        string ownerUid,
        int dimension,
        Vec3d origin,
        out BlockPos? storagePos,
        ItemStack? carried = null,
        bool requireSpecific = false,
        BlockPos? excludedStoragePos = null,
        bool requireFullStack = true,
        IReadOnlyCollection<BlockPos>? excludedStoragePositions = null,
        Entity? fox = null)
    {
        storagePos = null;
        if (packRepository?.Loaded != true || serverApi == null || string.IsNullOrEmpty(ownerUid)) return false;
        if (!TryGetStorageRoutingScope(
                ownerUid,
                dimension,
                fox,
                out BlockPos? storageCenter,
                out double storageRadius,
                out string storageScope)
            || storageCenter == null) return false;
        FoxStorageRouting.Category carriedCategory = carried == null
            ? FoxStorageRouting.Category.None
            : FoxStorageRouting.Classify(carried, serverApi.World);
        List<(BlockPos Position, int Priority, string Tier, bool ContainsItem, double Fullness, double Distance)> candidates = new();
        int outsidePackRange = 0;
        int withoutCapacity = 0;
        int withoutApproach = 0;

        foreach (FoxPackAmenityRecord record in packRepository.GetAmenitiesForOwner(ownerUid)
                     .Where(record => (record.Kind == "storage" || record.Kind == "storage-target" || record.Kind == "dining")
                         && IsStorageRoutingEnabled(record)
                         && record.Dimension == dimension))
        {
            BlockPos candidate = new(record.X, record.Y, record.Z, record.Dimension);
            if ((excludedStoragePos != null && candidate.Equals(excludedStoragePos))
                || (excludedStoragePositions != null
                    && excludedStoragePositions.Any(excluded => candidate.Equals(excluded)))) continue;
            if (!IsWithinStorageRoutingScope(record, storageCenter, storageRadius))
            {
                outsidePackRange++;
                continue;
            }
            int routingMask = GetEffectiveStorageRoutingMask(record);
            if (requireSpecific && FoxStorageRouting.IsGeneral(record, routingMask))
            {
                continue;
            }
            Block? block = serverApi.World.BlockAccessor.GetBlock(candidate);
            BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(candidate);
            if (!FoxStorageRouting.IsSupportedStorageTarget(block, blockEntity)
                || blockEntity is not IBlockEntityContainer container
                || (carried != null && !StorageRoutingAccepts(record, carried)))
            {
                continue;
            }

            IInventory? inventory;
            try
            {
                inventory = container.Inventory;
            }
            catch
            {
                continue;
            }
            if (inventory == null) continue;

            bool hasCapacity;
            try
            {
                hasCapacity = carried == null
                    ? HasOpenInventorySlot(inventory)
                    : requireFullStack
                        ? CanInventoryFullyAccept(inventory, carried)
                        : CanInventoryAcceptOne(inventory, carried);
            }
            catch
            {
                continue;
            }
            if (!hasCapacity)
            {
                withoutCapacity++;
                continue;
            }

            if (fox != null && !TryGetFoxStorageApproachTarget(fox, candidate, out _))
            {
                withoutApproach++;
                continue;
            }

            int priority = 0;
            string tier = "nearest";
            bool containsItem = false;
            double fullness = GetInventoryFillRatio(inventory);
            if (carried != null)
            {
                if (record.StorageAdvancedMode)
                {
                    priority = 0;
                    tier = "advanced-exact";
                }
                else if (FoxStorageRouting.IsGeneral(record, routingMask))
                {
                    containsItem = InventoryContainsItem(inventory, carried);
                    if (containsItem)
                    {
                        priority = 3;
                        tier = "general-contains-item";
                    }
                    else if (fullness > 0d)
                    {
                        priority = 4;
                        tier = "general-fullest";
                    }
                    else
                    {
                        priority = 5;
                        tier = "general-empty";
                    }
                }
                else if (routingMask == (int)carriedCategory)
                {
                    priority = 1;
                    tier = "specific-category";
                }
                else
                {
                    priority = 2;
                    tier = "specific-multi-category";
                }
            }

            double distance = origin.SquareDistanceTo(new Vec3d(
                record.X + 0.5,
                record.Y + record.Dimension * BlockPos.DimensionBoundary + 0.2,
                record.Z + 0.5));
            candidates.Add((candidate, priority, tier, containsItem, fullness, distance));
        }

        if (candidates.Count == 0)
        {
            if (fox != null && carried != null)
            {
                LogDutyDiagnostic(
                    fox,
                    "storage-selection",
                    $"destination selection scope={storageScope} item={carried.Collectible?.Code} found none; "
                    + $"outside-pack-range={outsidePackRange} no-capacity={withoutCapacity} no-approach={withoutApproach}");
            }
            return false;
        }

        (BlockPos Position, int Priority, string Tier, bool ContainsItem, double Fullness, double Distance) selected = carried == null
            ? candidates.OrderBy(candidate => candidate.Distance).First()
            : candidates
                .OrderBy(candidate => candidate.Priority)
                .ThenByDescending(candidate => candidate.Fullness)
                .ThenBy(candidate => candidate.Distance)
                .First();
        storagePos = selected.Position;
        if (fox != null && carried != null)
        {
            LogDutyDiagnostic(
                fox,
                "storage-selection",
                $"destination selection scope={storageScope} item={carried.Collectible?.Code} selected={selected.Position} "
                + $"tier={selected.Tier} contains-item={selected.ContainsItem} fullness={selected.Fullness:0.000} "
                + $"distance={Math.Sqrt(selected.Distance):0.0} candidates={candidates.Count} "
                + $"outside-pack-range={outsidePackRange} no-capacity={withoutCapacity} no-approach={withoutApproach}");
        }
        return true;
    }

    private bool TryGetStorageRoutingScope(
        string ownerUid,
        int dimension,
        Entity? fox,
        out BlockPos? center,
        out double radius,
        out string scope,
        bool useDeliveryOverride = true)
    {
        center = null;
        radius = 0d;
        scope = "none";
        if (packRepository?.Loaded != true || string.IsNullOrWhiteSpace(ownerUid)) return false;

        bool commandedPackCartDelivery = useDeliveryOverride
            && (fox?.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false) == true
                || fox?.WatchedAttributes.GetBool(FoxStorageCommandedCarryKey, false) == true);
        if (fox != null && !commandedPackCartDelivery)
        {
            string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(foxId)
                && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                && record?.HasHome == true
                && string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
            {
                BlockPos workCart = new(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
                if (workCart.dimension != dimension) return false;
                center = workCart;
                radius = GetWorkCartRadius(fox);
                scope = $"work-cart@{workCart}";
                return true;
            }
        }

        BlockPos? packMarker = GetActiveCairnPosition(ownerUid);
        if (packMarker == null || packMarker.dimension != dimension) return false;
        center = packMarker;
        radius = fox == null ? GetOwnerCampRadius(ownerUid) : GetCompanionCampRadius(fox);
        scope = $"main-pack@{packMarker}";
        return true;
    }

    private static bool IsWithinStorageRoutingScope(
        FoxPackAmenityRecord record,
        BlockPos center,
        double radius)
    {
        return CompanionCampBounds.Contains(record.X, record.Z, record.Dimension,
            center.X, center.Z, center.dimension, radius);
    }

    private static bool InventoryContainsItem(IInventory inventory, ItemStack stack)
    {
        AssetLocation? itemCode = stack.Collectible?.Code;
        if (itemCode == null) return false;

        foreach (ItemSlot slot in inventory)
        {
            if (!slot.Empty && slot.Itemstack?.Collectible?.Code?.Equals(itemCode) == true) return true;
        }
        return false;
    }

    private static bool IsStorageRoutingEnabled(FoxPackAmenityRecord record)
    {
        // Pack Collection Boxes are inherently the unconfigured General box.
        // Older saved records may not contain the routing fields yet, so keep
        // them usable even before the repository migration has rewritten the
        // record in memory.
        return record.StorageRoutingEnabled
            || string.Equals(record.Kind, "storage", StringComparison.Ordinal);
    }

    private static int GetEffectiveStorageRoutingMask(FoxPackAmenityRecord record)
    {
        if (record.StorageRoutingMask != 0) return record.StorageRoutingMask;
        return string.Equals(record.Kind, "storage", StringComparison.Ordinal)
            ? (int)FoxStorageRouting.Category.General
            : (int)FoxStorageRouting.Category.None;
    }

    private bool StorageRoutingAccepts(FoxPackAmenityRecord record, ItemStack stack)
    {
        return serverApi?.World != null
            && FoxStorageRouting.Accepts(
                record,
                GetEffectiveStorageRoutingMask(record),
                stack,
                serverApi.World);
    }

    internal static bool HasFoxStorageCargo(Entity fox)
    {
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        return carried != null && carried.StackSize > 0;
    }

    internal static ItemStack? GetFoxStorageCargoForRender(Entity fox)
    {
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0) return null;
        if (carried.Collectible == null) carried.ResolveBlockOrItem(fox.World);
        return carried.Collectible == null ? null : carried;
    }

    internal static bool HasFoxStorageJob(Entity fox)
    {
        return HasFoxStorageCargo(fox) || fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false);
    }

    internal static bool HasFoxCommandedStorageJob(Entity fox)
    {
        return fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false)
            || (HasFoxStorageCargo(fox)
                && fox.WatchedAttributes.GetBool(FoxStorageCommandedCarryKey, false));
    }

    private static bool HasFoxPendingCartPickup(Entity fox)
    {
        return fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false);
    }

    private string GetFoxStorageCargoDisplay(Entity fox)
    {
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0) return string.Empty;
        if (carried.Collectible == null && serverApi != null) carried.ResolveBlockOrItem(serverApi.World);
        string name = carried.Collectible == null ? "Unknown item" : carried.GetName();
        return $"{carried.StackSize}x {name}";
    }

    private void RetrieveFoxStorageCargo(Entity fox, IServerPlayer owner)
    {
        if (serverApi == null) return;
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0)
        {
            SendState(fox, owner, "This companion is not holding a collection item.");
            return;
        }

        string itemName = carried.GetName();
        bool wasCommandedCourier = fox.WatchedAttributes.GetBool(FoxStorageCommandedCarryKey, false);
        int before = carried.StackSize;
        owner.InventoryManager.TryGiveItemstack(carried, false);
        int received = Math.Max(0, before - carried.StackSize);
        int dropped = Math.Max(0, carried.StackSize);
        if (dropped > 0)
        {
            serverApi.World.SpawnItemEntity(carried, owner.Entity.Pos.XYZ.AddCopy(0, 0.5, 0));
        }

        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
        ClearFoxStorageFailedTargets(fox);
        ClearFoxStorageReturnSource(fox);
        fox.WatchedAttributes.SetBool(FoxStorageCartPickupKey, false);
        fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
        string message = $"Retrieved {before}x {itemName} from this companion.";
        if (wasCommandedCourier && packRepository?.IsCargoUnloadingActive(owner.PlayerUID) == true)
        {
            packRepository.SetCargoUnloadingActive(owner.PlayerUID, false);
            ClearPendingCartPickupJobs(owner.PlayerUID);
            packRepository.Save();
            message += " Continuous cart unloading was stopped so this companion will not be assigned another load.";
        }
        if (received == 0 && dropped > 0) message += " Your inventory was full, so it was placed at your feet.";
        else if (dropped > 0) message += $" {dropped} {(dropped == 1 ? "item was" : "items were")} placed at your feet because your inventory filled up.";
        SendState(fox, owner, message);
        SendPackStateToOwner(fox);
    }

    private void DropFoxStorageCargo(Entity fox)
    {
        if (serverApi == null) return;
        if (fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false))
        {
            fox.WatchedAttributes.SetBool(FoxStorageCartPickupKey, false);
            fox.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
        }
        if (fox.WatchedAttributes.GetBool(FoxStorageCommandedCarryKey, false))
        {
            fox.WatchedAttributes.SetBool(FoxStorageCommandedCarryKey, false);
            fox.WatchedAttributes.MarkPathDirty(FoxStorageCommandedCarryKey);
        }
        ItemStack? carried = fox.WatchedAttributes.GetItemstack(FoxStorageCarryKey);
        if (carried == null || carried.StackSize <= 0)
        {
            ClearFoxStorageReturnSource(fox);
            return;
        }
        try
        {
            if (serverApi.World.SpawnItemEntity(carried, fox.Pos.XYZ.AddCopy(0, 0.25, 0)) == null)
            {
                LogStorageRoutingDiagnostic(fox, $"could not spawn dropped cargo {carried.Collectible?.Code}; retaining it for another delivery attempt");
                return;
            }
        }
        catch (Exception exception)
        {
            LogStorageRoutingDiagnostic(fox, $"could not drop carried cargo: {exception.Message}; retaining it for another delivery attempt");
            return;
        }
        fox.WatchedAttributes.SetItemstack(FoxStorageCarryKey, null);
        ClearFoxStorageFailedTargets(fox);
        fox.WatchedAttributes.MarkPathDirty(FoxStorageCarryKey);
        ClearFoxStorageReturnSource(fox);
    }

    internal bool TryGetFoxAmenities(Entity fox, out IReadOnlyList<FoxPackAmenityRecord> amenities)
    {
        string ownerUid = GetDomesticationStatus(fox)?.GetString("owner", string.Empty) ?? string.Empty;
        amenities = packRepository?.Loaded == true && !string.IsNullOrEmpty(ownerUid)
            ? packRepository.GetAmenitiesForOwner(ownerUid)
            : Array.Empty<FoxPackAmenityRecord>();
        return amenities.Count > 0;
    }

    internal bool TryCreateFoxIdlePlan(Entity fox, out FoxIdlePlan? plan)
    {
        plan = null;
        bool hasForcedPlan = forcedIdlePlans.ContainsKey(fox.EntityId);
        bool hasInvitation = companionIdleInvitations.ContainsKey(fox.EntityId);
        if (serverApi == null || !IsTamedFox(fox) || IsFoxAwayFromWorld(fox)
            || IsFoxIncapacitated(fox)
            || (!hasForcedPlan && !hasInvitation && ShouldFoxSeekDenShelter(fox))) return false;

        ITreeAttribute? status = GetDomesticationStatus(fox);
        string ownerUid = status?.GetString("owner", string.Empty) ?? string.Empty;
        string personality = status?.GetString(PersonalityKey, string.Empty) ?? string.Empty;
        string mood = GetMood(fox);
        if (string.IsNullOrEmpty(ownerUid)) return false;

        if (forcedIdlePlans.Remove(fox.EntityId, out FoxIdlePlan? forcedPlan))
        {
            if (forcedPlan.RespectShelterWhenForced && ShouldFoxSeekDenShelter(fox)
                || forcedPlan.Kind == "menu-attention" && !IsMenuAttentionActive(fox, out _)
                || forcedPlan.Kind == "interest-watch"
                    && !IsAmbientInterestStillValid(fox, forcedPlan.ReservationKey))
            {
                ReleaseAmbientIdleReservation(fox, forcedPlan);
                return false;
            }

            if (!forcedPlan.RespectShelterWhenForced)
            {
                forcedPlan.IgnoreShelter = true;
            }
            plan = forcedPlan;
            return plan != null;
        }

        if (companionIdleInvitations.TryGetValue(fox.EntityId, out CompanionIdleInvitation? invitation))
        {
            if (invitation.ExpiresAtMs < fox.World.ElapsedMilliseconds)
            {
                companionIdleInvitations.Remove(fox.EntityId);
            }
            else
            {
                Entity? invitingPartner = fox.World.GetEntityById(invitation.PartnerEntityId);
                if (invitingPartner != null && invitingPartner.Alive
                    && invitingPartner.Pos.Dimension == fox.Pos.Dimension
                    && !IsFoxAwayFromWorld(invitingPartner)
                    && !IsFoxIncapacitated(invitingPartner))
                {
                    companionIdleInvitations.Remove(fox.EntityId);
                    plan = new FoxIdlePlan { Kind = invitation.Kind, Target = invitation.Target.Clone(),
                        PartnerEntityId = invitingPartner.EntityId, Animation = invitation.Animation,
                        Immediate = false, DurationMs = invitation.DurationMs,
                        AbsoluteEndAtMs = invitation.AbsoluteEndAtMs,
                        MoveSpeed = invitation.Kind == "play-runner" ? 0.038f : 0.018f,
                        TargetDistance = invitation.Kind == "play-runner" ? 0.8f : 0.45f,
                        IgnoreShelter = invitation.Kind != "packmate-greeting" };
                    return true;
                }
            }
        }

        if (TryCreatePerimeterWalkPlan(fox, out FoxIdlePlan? perimeterPlan) && perimeterPlan != null)
        {
            plan = perimeterPlan;
            return true;
        }

        Entity? partner = loadedFoxes.Values
            .Where(other => other != fox && other.Alive && IsOwner(other, ownerUid)
                && !IsFoxAwayFromWorld(other) && !IsFoxIncapacitated(other)
                && other.Pos.Dimension == fox.Pos.Dimension
                && IsAvailableForPackPlay(other))
            .OrderBy(other => other.Pos.SquareDistanceTo(fox.Pos))
            .FirstOrDefault(other => other.Pos.SquareDistanceTo(fox.Pos) <= 18 * 18);

        double socialChance = personality switch
        {
            "social" or "affectionate" => 0.72, "playful" => 0.62,
            "solitary" or "independent" => 0.08, _ => 0.30
        };
        if (partner != null && (mood == "playful" || personality is "playful" or "restless")
            && !companionIdleInvitations.ContainsKey(partner.EntityId)
            && serverApi.World.Rand.NextDouble() < socialChance)
        {
            int chaseDuration = 8500 + serverApi.World.Rand.Next(6500);
            long chaseEnd = fox.World.ElapsedMilliseconds + chaseDuration;
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "play-runner",
                PartnerEntityId = fox.EntityId,
                ExpiresAtMs = chaseEnd + 8000,
                Animation = "Run",
                Target = BuildPlayEscapeTarget(partner, fox),
                DurationMs = chaseDuration,
                AbsoluteEndAtMs = chaseEnd
            };
            plan = new FoxIdlePlan { Kind = "chase", Target = partner.Pos.XYZ.Clone(), PartnerEntityId = partner.EntityId,
                Animation = "Run", DurationMs = chaseDuration, AbsoluteEndAtMs = chaseEnd,
                MoveSpeed = 0.035f, TargetDistance = 0.8f };
            return true;
        }
        if (partner != null && partner.Pos.SquareDistanceTo(fox.Pos) <= 3.5 * 3.5
            && (mood is "sleepy" or "resting" or "content" || personality is "social" or "affectionate")
            && serverApi.World.Rand.NextDouble() < socialChance)
        {
            string sharedAnimation = mood == "sleepy" ? "sleep" : "sit";
            BuildCompanionRestTargets(fox, partner, out Vec3d foxTarget, out Vec3d partnerTarget);
            int restDuration = 9000 + serverApi.World.Rand.Next(9000);
            long sharedEnd = fox.World.ElapsedMilliseconds + restDuration;
            int departureDelay = 1500 + serverApi.World.Rand.Next(2500);
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "companion-rest",
                PartnerEntityId = fox.EntityId,
                ExpiresAtMs = sharedEnd + departureDelay + 10000,
                Animation = sharedAnimation,
                Target = partnerTarget,
                DurationMs = restDuration + departureDelay,
                AbsoluteEndAtMs = sharedEnd + departureDelay
            };
            plan = new FoxIdlePlan { Kind = "companion-rest", Target = foxTarget, PartnerEntityId = partner.EntityId,
                Animation = sharedAnimation, Immediate = false, DurationMs = restDuration,
                AbsoluteEndAtMs = sharedEnd, TargetDistance = 0.45f };
            return true;
        }

        if (partner != null && partner.Pos.SquareDistanceTo(fox.Pos) <= 18 * 18
            && (mood is "content" or "curious" or "playful" or "restless"
                || personality is "social" or "affectionate" or "curious")
            && !companionIdleInvitations.ContainsKey(partner.EntityId)
            && serverApi.World.Rand.NextDouble() < socialChance * 0.24)
        {
            BuildCompanionRestTargets(fox, partner, out Vec3d foxTarget, out Vec3d partnerTarget);
            int greetingDuration = 5000 + serverApi.World.Rand.Next(4500);
            long greetingEnd = fox.World.ElapsedMilliseconds + greetingDuration;
            int departureDelay = 900 + serverApi.World.Rand.Next(1800);
            companionIdleInvitations[partner.EntityId] = new CompanionIdleInvitation
            {
                Kind = "packmate-greeting",
                PartnerEntityId = fox.EntityId,
                ExpiresAtMs = greetingEnd + departureDelay + 7000,
                Animation = "idle",
                Target = partnerTarget,
                DurationMs = greetingDuration + departureDelay,
                AbsoluteEndAtMs = greetingEnd + departureDelay
            };
            plan = new FoxIdlePlan
            {
                Kind = "packmate-greeting",
                Target = foxTarget,
                PartnerEntityId = partner.EntityId,
                Animation = "idle",
                DurationMs = greetingDuration,
                AbsoluteEndAtMs = greetingEnd,
                TargetDistance = 0.45f
            };
            return true;
        }

        Entity? owner = fox.World.PlayerByUid(ownerUid)?.Entity;

        if (TryCreateSeeOffPlan(fox, out FoxIdlePlan? seeOffPlan) && seeOffPlan != null)
        {
            plan = seeOffPlan;
            return true;
        }

        if (TryCreateCampLookoutPlan(fox, out FoxIdlePlan? lookoutPlan) && lookoutPlan != null)
        {
            plan = lookoutPlan;
            return true;
        }

        if (TryCreateScentInvestigationPlan(fox, out FoxIdlePlan? scentPlan) && scentPlan != null)
        {
            plan = scentPlan;
            return true;
        }

        if (owner != null && IsCompanionOwnerInsidePackCamp(fox, owner)
            && (mood is "content" or "curious" or "restless" or "playful"
                || personality is "social" or "affectionate" or "restless" or "curious")
            && serverApi.World.Rand.NextDouble() < 0.22)
        {
            plan = new FoxIdlePlan
            {
                Kind = "shadow-follow",
                OwnerEntityId = owner.EntityId,
                Target = owner.Pos.XYZ.Clone(),
                Animation = "idle",
                DurationMs = 10000 + serverApi.World.Rand.Next(12000),
                MoveSpeed = 0.018f,
                TargetDistance = 3.25f
            };
            return true;
        }

        if (owner != null && IsCompanionOwnerInsidePackCamp(fox, owner)
            && (mood is "content" or "curious" or "sleepy" or "resting"
                || personality is "social" or "affectionate" or "curious")
            && serverApi.World.Rand.NextDouble() < 0.12
            && TryGetPackCartIdleTarget(fox, out Vec3d? cartTarget)
            && cartTarget != null
            && TryReservePackCartSit(fox, cartTarget, out string cartReservationKey))
        {
            plan = new FoxIdlePlan
            {
                Kind = "pack-cart-sit",
                OwnerEntityId = owner.EntityId,
                Target = cartTarget,
                Animation = "sit",
                DurationMs = 9000 + serverApi.World.Rand.Next(9000),
                MoveSpeed = 0.018f,
                TargetDistance = 0.7f,
                ReservationKey = cartReservationKey
            };
            return true;
        }

        if (!TryGetFoxAmenities(fox, out IReadOnlyList<FoxPackAmenityRecord> amenities)) return false;
        string[] preferred = personality switch
        {
            "greedy" or "demanding" => new[] { "lounge" },
            "playful" or "restless" or "curious" => new[] { "play", "lookout" },
            "protective" or "territorial" or "bold" => new[] { "lookout", "play" },
            "homebody" or "affectionate" or "social" => new[] { "lounge" },
            "solitary" or "independent" => new[] { "lookout", "lounge" },
            _ => new[] { "lounge", "play", "lookout" }
        };
        bool allowAmbientStorageVisit = serverApi.World.Rand.NextDouble() < 0.06;
        FoxPackAmenityRecord? chosen = amenities
            .Where(a => a.Dimension == fox.Pos.Dimension
                && ((a.Kind == "storage" && allowAmbientStorageVisit) || preferred.Contains(a.Kind))
                && fox.Pos.SquareDistanceTo(new Vec3d(a.X + 0.5, a.Y + 0.2, a.Z + 0.5)) <= 48 * 48)
            .OrderBy(a => a.Kind == "storage" ? -1 : Array.IndexOf(preferred, a.Kind))
            .ThenBy(a => fox.Pos.SquareDistanceTo(new Vec3d(a.X + 0.5, a.Y + 0.2, a.Z + 0.5)))
            .FirstOrDefault();
        if (chosen == null) return false;

        string animation = chosen.Kind switch { "lounge" => mood == "sleepy" ? "sleep" : "sit", "lookout" => "sit", _ => "Sniff" };
        BuildPackSpaceVisitTargets(chosen, fox, out Vec3d firstSpaceTarget, out Vec3d secondSpaceTarget);
        plan = new FoxIdlePlan
        {
            Kind = "pack-space-visit",
            Target = firstSpaceTarget,
            Animation = animation,
            DurationMs = 9000 + serverApi.World.Rand.Next(14000),
            MoveSpeed = 0.018f,
            TargetDistance = 0.7f,
            Waypoints = new List<Vec3d> { firstSpaceTarget, secondSpaceTarget },
            LookAtTarget = new Vec3d(chosen.X + 0.5, chosen.Y + 0.2, chosen.Z + 0.5)
        };
        return true;
    }

    internal bool HasPendingFoxIdleInvitation(Entity fox)
    {
        return companionIdleInvitations.TryGetValue(fox.EntityId, out CompanionIdleInvitation? invitation)
            && invitation.ExpiresAtMs >= fox.World.ElapsedMilliseconds;
    }

    internal bool HasForcedFoxIdlePlan(Entity fox)
    {
        return forcedIdlePlans.ContainsKey(fox.EntityId);
    }

    private static bool IsAvailableForPackPlay(Entity fox)
    {
        AiTaskManager? manager = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        return manager == null || !manager.ActiveTasksBySlot.Any(task => task != null && task.Priority >= 1.361f);
    }

    private const float DirectedCompanionTaskPriority = 2.8f;

    /// <summary>
    /// Returns true while a persistent Kinship command or another high-priority
    /// task owns movement. At Ease permits ambient animal life; Follow permits
    /// only its dedicated bounded idle task inside the owner's follow ring.
    /// </summary>
    internal static bool ShouldPauseAmbientCompanionAi(EntityAgent entity, IAiTask? currentTask = null)
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        bool followIdle = GetCompanionActivityMode(entity) == CompanionActivityMode.Follow
            && currentTask is AiTaskFeralKinshipFollowIdle;
        if (GetCompanionActivityMode(entity) != CompanionActivityMode.AtEase && !followIdle
            || system?.IsCompanionFoodRestricted(entity) == true)
        {
            return true;
        }

        AiTaskManager? manager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        if (manager == null) return false;

        return manager.ActiveTasksBySlot.Any(task =>
            task != null
            && !ReferenceEquals(task, currentTask)
            && (task.Priority >= DirectedCompanionTaskPriority || IsDirectedCompanionTask(task))
        );
    }

    internal static bool IsExpeditionDeparturePending(Entity entity)
    {
        FeralKinshipCompanionSystem? system = entity.Api.ModLoader.GetModSystem<FeralKinshipCompanionSystem>();
        return system?.HasPendingExpeditionDeparture(entity) == true;
    }

    /// <summary>
    /// Stops ambient tasks already in flight. The task-manager callback below
    /// prevents new starts, while this covers an ambient path that was already
    /// running when a command was issued or combat began in another slot.
    /// </summary>
    internal static void StopAmbientCompanionAi(EntityAgent entity)
    {
        AiTaskManager? manager = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        if (manager == null) return;

        foreach (IAiTask? task in manager.ActiveTasksBySlot.ToArray())
        {
            // The policy is task-specific: Follow allows its dedicated idle,
            // while still suppressing ordinary wandering and pack activities.
            if (task != null && IsAmbientCompanionTask(task)
                && ShouldPauseAmbientCompanionAi(entity, task))
            {
                manager.StopTask(task.Slot);
            }
        }
    }

    /// <summary>
    /// Breaks hostile AI tasks that currently have this Companion selected.
    /// Return Home is meant to be a disengage order; changing the Companion's
    /// own combat style is not enough if a nearby hostile still has an active
    /// melee or seek task pointed at it.
    /// </summary>
    private void DisengageHostileTargets(Entity companion)
    {
        if (serverApi == null || companion is not EntityAgent)
        {
            return;
        }

        Entity[] nearby = serverApi.World.GetEntitiesAround(companion.Pos.XYZ, 64f, 24f);
        foreach (Entity nearbyEntity in nearby)
        {
            if (nearbyEntity is not EntityAgent hostile || hostile == companion)
            {
                continue;
            }

            AiTaskManager? manager = hostile.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
            if (manager == null)
            {
                continue;
            }

            foreach (IAiTask? task in manager.ActiveTasksBySlot.ToArray())
            {
                if (task is AiTaskBaseTargetable targetable
                    && targetable.AggressiveTargeting
                    && targetable.TargetEntity?.EntityId == companion.EntityId)
                {
                    manager.StopTask(task.Slot);
                }
            }
        }

        companionAttackers.Remove(companion.EntityId);
    }

    private static bool IsDirectedCompanionTask(IAiTask task)
    {
        string? id = task.Id?.ToLowerInvariant();

        if (id == "feralkinshipfetchdroppeditem")
        {
            return task is AiTaskFeralKinshipFetchDroppedItem { CommandedCourier: true };
        }

        return id is
            "feralkinshipfollowmaster" or "feralkinshipcommandreturnhome" or
            "feralkinshipcommandrestapproach" or "feralkinshipcommandrest" or
            "feralkinshipcombatapproach" or "feralkinshippetmeleeattack" or
            "feralkinshipgetoutofwater" or "getoutofwater" or
            "feralkinshipexpeditiondepart"
            || task.Priority >= DirectedCompanionTaskPriority;
    }

    internal static bool IsAmbientCompanionTask(IAiTask task)
    {
        return task is AiTaskIdle
            or AiTaskWander
            or AiTaskLookAround
            or AiTaskFeralKinshipFollowIdle
            or AiTaskFeralKinshipMoodIdle
            or AiTaskFeralKinshipPackIdle
            or AiTaskFeralKinshipReturnToDen
            or AiTaskFeralKinshipRestAtDen
            or AiTaskFeralKinshipBlueberryMode
            or AiTaskFeralKinshipMowGrass
            or AiTaskFeralKinshipLogging
            || task is AiTaskFeralKinshipFetchDroppedItem fetch && !fetch.CommandedCourier;
    }

    private Vec3d BuildPlayEscapeTarget(Entity runner, Entity chaser)
    {
        double dx = runner.Pos.X - chaser.Pos.X;
        double dz = runner.Pos.Z - chaser.Pos.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 0.15)
        {
            double angle = serverApi!.World.Rand.NextDouble() * Math.PI * 2;
            dx = Math.Cos(angle);
            dz = Math.Sin(angle);
            length = 1;
        }

        double jitter = (serverApi!.World.Rand.NextDouble() - 0.5) * 0.9;
        double cos = Math.Cos(jitter);
        double sin = Math.Sin(jitter);
        double nx = dx / length;
        double nz = dz / length;
        double rx = nx * cos - nz * sin;
        double rz = nx * sin + nz * cos;
        double distance = 10 + serverApi.World.Rand.NextDouble() * 7;
        double targetX = runner.Pos.X + rx * distance;
        double targetZ = runner.Pos.Z + rz * distance;
        if (TryFindSafeAmbientGround(runner, targetX, targetZ, runner.Pos.AsBlockPos.Y, out Vec3d? safeTarget)
            && safeTarget != null)
        {
            return safeTarget;
        }

        return new Vec3d(targetX, runner.Pos.Y, targetZ);
    }

    private Vec3d BuildIdleWanderTarget(Entity fox)
    {
        string ownerUid = GetCompanionOwnerUid(fox);
        BlockPos? marker = GetActiveCairnPosition(ownerUid);
        double originX = marker != null && marker.dimension == fox.Pos.Dimension
            ? marker.X + 0.5
            : fox.Pos.X;
        double originZ = marker != null && marker.dimension == fox.Pos.Dimension
            ? marker.Z + 0.5
            : fox.Pos.Z;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = serverApi!.World.Rand.NextDouble() * Math.PI * 2;
            double distance = 3d + serverApi.World.Rand.NextDouble() * 3d;
            double targetX = originX + Math.Cos(angle) * distance;
            double targetZ = originZ + Math.Sin(angle) * distance;
            if (TryFindSafeAmbientGround(fox, targetX, targetZ, marker?.Y ?? fox.Pos.AsBlockPos.Y, out Vec3d? safeTarget)
                && safeTarget != null)
            {
                return safeTarget;
            }
        }

        return fox.Pos.XYZ.Clone();
    }

    internal bool TryGetNextPlayEscapeTarget(Entity runner, long chaserEntityId, out Vec3d? target)
    {
        target = null;
        Entity? chaser = runner.World.GetEntityById(chaserEntityId);
        if (chaser?.Alive != true || chaser.Pos.Dimension != runner.Pos.Dimension) return false;
        target = BuildPlayEscapeTarget(runner, chaser);
        return true;
    }

    private static void BuildCompanionRestTargets(Entity first, Entity second, out Vec3d firstTarget, out Vec3d secondTarget)
    {
        double dx = second.Pos.X - first.Pos.X;
        double dz = second.Pos.Z - first.Pos.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 0.15)
        {
            // Entity ids give an enduring direction without requiring another
            // random value on the client or after a save/load boundary.
            double angle = ((first.EntityId ^ second.EntityId) % 628) / 100d;
            dx = Math.Cos(angle);
            dz = Math.Sin(angle);
            length = 1;
        }

        double nx = dx / length;
        double nz = dz / length;
        double midpointX = (first.Pos.X + second.Pos.X) * 0.5;
        double midpointZ = (first.Pos.Z + second.Pos.Z) * 0.5;
        CompanionSpeciesProfile firstSpecies = GetCompanionSpecies(first);
        CompanionSpeciesProfile secondSpecies = GetCompanionSpecies(second);
        double centerSpacing = Math.Max(
            1.44,
            firstSpecies.PersonalSpaceRadius + secondSpecies.PersonalSpaceRadius
        );
        double halfSpacing = centerSpacing * 0.5;
        firstTarget = new Vec3d(midpointX - nx * halfSpacing, first.Pos.Y, midpointZ - nz * halfSpacing);
        secondTarget = new Vec3d(midpointX + nx * halfSpacing, second.Pos.Y, midpointZ + nz * halfSpacing);
    }

    private static void BuildPackSpaceVisitTargets(
        FoxPackAmenityRecord amenity,
        Entity fox,
        out Vec3d firstTarget,
        out Vec3d secondTarget)
    {
        double centerX = amenity.X + 0.5;
        double centerZ = amenity.Z + 0.5;
        double offset = 1.35;
        double dx = fox.Pos.X - centerX;
        double dz = fox.Pos.Z - centerZ;
        double y = amenity.Y + 0.22;

        if (Math.Abs(dx) >= Math.Abs(dz))
        {
            double sign = dx >= 0 ? 1 : -1;
            firstTarget = new Vec3d(centerX + sign * offset, y, centerZ);
            secondTarget = new Vec3d(centerX - sign * offset, y, centerZ);
        }
        else
        {
            double sign = dz >= 0 ? 1 : -1;
            firstTarget = new Vec3d(centerX, y, centerZ + sign * offset);
            secondTarget = new Vec3d(centerX, y, centerZ - sign * offset);
        }
    }

    internal void OnPackCairnInteracted(IPlayer byPlayer, BlockPos pos)
    {
        if (byPlayer is not IServerPlayer serverPlayer || packRepository?.Loaded != true || serverApi == null)
        {
            return;
        }

        // The client requests a separate read-only snapshot while holding the
        // developer ledger. Do not let the block's parallel interaction open
        // a normal Pack session or perform ownership repairs, even on own carts.
        if (CanUseDeveloperTools(serverPlayer)
            && serverPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible
                is ItemFeralKinshipDeveloperLedger)
        {
            return;
        }

        PackCartOwnershipResolution ownership = ResolvePackCartOwnership(pos, applyRepairs: true);
        if (!ownership.HasOwner)
        {
            serverPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "This Pack Cart has no ownership tag. Break and place it again to establish ownership.",
                EnumChatType.Notification
            );
            return;
        }

        if (!string.Equals(ownership.OwnerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
        {
            serverPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Only the player who placed this Pack Cart can open its pack menu.",
                EnumChatType.Notification
            );
            return;
        }

        socialViewByOwner.Remove(serverPlayer.PlayerUID);
        packViewers.Add(serverPlayer.PlayerUID);
        MarkBramblePackCartExplored(serverPlayer.PlayerUID);
        MarkBramblePackExplored(serverPlayer.PlayerUID);
        serverChannel?.SendPacket(new FoxPackOpenFromCairnPacket { Open = true }, serverPlayer);
        SendPackState(serverPlayer);
    }

    private BlockEntityFeralKinshipPackCart? EnsurePackCartBlockEntity(BlockPos pos)
    {
        if (serverApi == null || serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
        {
            return null;
        }

        Block block = serverApi.World.BlockAccessor.GetBlock(pos);
        if (block.Code == null || !IsPackMarkerCode(block.Code))
        {
            return null;
        }

        BlockEntity? existing = serverApi.World.BlockAccessor.GetBlockEntity(pos);
        if (existing is BlockEntityFeralKinshipPackCart packCart)
        {
            return packCart;
        }

        if (existing != null || string.IsNullOrWhiteSpace(block.EntityClass))
        {
            return null;
        }

        // Existing worlds can contain Pack Carts placed before the ownership
        // block entity existed. Materialize it when their chunk is loaded.
        serverApi.World.BlockAccessor.SpawnBlockEntity(block.EntityClass, pos);
        BlockEntityFeralKinshipPackCart? spawned =
            serverApi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityFeralKinshipPackCart;
        spawned?.MarkDirty(true);
        return spawned;
    }

    private PackCartOwnershipResolution ResolvePackCartOwnership(BlockPos pos, bool applyRepairs)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return PackCartOwnershipPolicy.Resolve(null, null);
        }

        BlockEntityFeralKinshipPackCart? blockEntity = EnsurePackCartBlockEntity(pos);
        if (blockEntity == null)
        {
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Pack Cart at {0} has no usable ownership block entity; ownership was not inferred.",
                pos);
            return PackCartOwnershipPolicy.Resolve(null, null);
        }

        FoxPackCairnRecord? repositoryRecord = packRepository.GetCairn(pos);
        PackCartOwnershipResolution ownership = PackCartOwnershipPolicy.Resolve(
            blockEntity.OwnerUid,
            repositoryRecord?.OwnerUid);

        if (!applyRepairs || !ownership.HasOwner)
        {
            return ownership;
        }

        bool wroteBlockTag = ownership.WriteBlockTag && blockEntity.SetOwnerUid(ownership.OwnerUid);
        bool wroteRepository = false;
        if (ownership.WriteRepository)
        {
            packRepository.RegisterCairn(ownership.OwnerUid, pos);
            wroteRepository = true;
        }

        if (wroteBlockTag || wroteRepository)
        {
            packRepository.Save();
            serverApi.Logger.Notification(
                "[FeralKinshipCompanions] Reconciled Pack Cart ownership at {0}: owner {1}, authority {2}, wrote block tag={3}, repository={4}.",
                pos,
                ownership.OwnerUid,
                ownership.Authority,
                wroteBlockTag,
                wroteRepository);
        }

        return ownership;
    }

    private bool RegisterPlacedPackCartOwner(IServerPlayer byPlayer, BlockPos pos)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return false;
        }

        BlockEntityFeralKinshipPackCart? blockEntity = EnsurePackCartBlockEntity(pos);
        if (blockEntity == null)
        {
            // Never leave an old positional owner behind when the explicit tag
            // could not be written for a newly placed cart.
            if (packRepository.RemoveCairn(pos))
            {
                packRepository.Save();
            }
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not write Pack Cart ownership for placer {0} at {1}; no repository fallback was assigned.",
                byPlayer.PlayerUID,
                pos);
            byPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Pack Cart ownership could not be recorded. Break and place it again before using it.",
                EnumChatType.Notification);
            return false;
        }

        blockEntity.SetOwnerUid(byPlayer.PlayerUID);
        packRepository.RegisterCairn(byPlayer.PlayerUID, pos);
        packRepository.Save();
        serverApi.Logger.Notification(
            "[FeralKinshipCompanions] Tagged Pack Cart at {0} for placing player {1}.",
            pos,
            byPlayer.PlayerUID);
        return true;
    }

    internal bool TryGetAssignedFoxDen(Entity entity, out Vec3d? den)
    {
        den = null;
        if (packRepository?.Loaded != true || !IsTamedFox(entity))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !record.HasHome
            || entity.Pos.Dimension != record.HomeDimension)
        {
            return false;
        }

        den = new Vec3d(
            record.HomeX + 0.5d,
            record.HomeY + record.HomeDimension * BlockPos.DimensionBoundary + 0.35d,
            record.HomeZ + 0.5d
        );
        return true;
    }

    /// <summary>
    /// Resolves the explicit command destination in stable priority order:
    /// assigned bed, newest pack cart/cairn, then the online owner.
    /// </summary>
    internal bool TryResolveCompanionCommandHome(Entity entity, out Vec3d? target)
    {
        if (packCartCallTargets.TryGetValue(entity.EntityId, out Vec3d? cartCallTarget))
        {
            if (TryGetPackCartIdleTarget(entity, out Vec3d? activeCartTarget)
                && activeCartTarget != null
                && activeCartTarget.SquareDistanceTo(cartCallTarget) <= 36d)
            {
                target = cartCallTarget;
                return true;
            }
            packCartCallTargets.Remove(entity.EntityId);
        }
        if (TryResolveCompanionPlacedHome(entity, out target)) return true;

        target = null;
        if (serverApi == null || !IsTamedFox(entity)) return false;
        string ownerUid = GetCompanionOwnerUid(entity);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (owner?.Alive == true)
        {
            target = owner.Pos.XYZ;
            return true;
        }
        return false;
    }

    private bool TryResolveCompanionPlacedHome(Entity entity, out Vec3d? target)
    {
        target = null;
        if (serverApi == null || packRepository?.Loaded != true || !IsTamedFox(entity)) return false;

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            && record?.HasHome == true)
        {
            BlockPos homePos = new(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
            IWorldChunk? homeChunk = serverApi.World.BlockAccessor.GetChunkAtBlockPos(homePos);
            bool isWorkCart = string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase);
            bool homeIsUsable = homeChunk == null
                || (isWorkCart
                    ? IsWorkCartCode(serverApi.World.BlockAccessor.GetBlock(homePos).Code)
                    : serverApi.World.BlockAccessor.GetBlock(homePos).Code?.Path.StartsWith(
                        FoxBedBlockPathPrefix,
                        StringComparison.OrdinalIgnoreCase
                    ) == true);
            if (homeIsUsable)
            {
                Vec3d fallback = new(
                    record.HomeX + 0.5,
                    record.HomeY + record.HomeDimension * BlockPos.DimensionBoundary + 0.35,
                    record.HomeZ + 0.5
                );
                // The bed block itself is not a reliable A* destination: it
                // can be occupied by the bed collision/selection shape.  If
                // the home chunk is loaded, route to a safe standing position
                // beside it and keep the precise coordinate only as the
                // unloaded-home fallback used by the teleport path.
                target = homeChunk != null
                    ? FindSafeEntityPosition(
                        homePos,
                        entity.Properties,
                        entity,
                        2,
                        2
                    ) ?? fallback
                    : fallback;
                return true;
            }
        }

        string ownerUid = GetCompanionOwnerUid(entity);
        BlockPos? marker = GetActiveCairnPosition(ownerUid);
        if (marker != null)
        {
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(marker) != null)
            {
                Block markerBlock = serverApi.World.BlockAccessor.GetBlock(marker);
                BlockPos portal = GetPackMarkerPortalBlock(marker, markerBlock);
                Vec3d fallback = new(
                    portal.X + 0.5,
                    portal.Y + portal.dimension * BlockPos.DimensionBoundary,
                    portal.Z + 0.5
                );
                target = FindSafeEntityPosition(
                    portal,
                    entity.Properties,
                    entity,
                    2,
                    2
                ) ?? fallback;
            }
            else
            {
                target = new Vec3d(
                    marker.X + 0.5,
                    marker.Y + marker.dimension * BlockPos.DimensionBoundary + 0.2,
                    marker.Z + 0.5
                );
            }
            return true;
        }
        return false;
    }

    internal bool TryTeleportCompanionToCommandHome(Entity entity, Vec3d target)
    {
        if (serverApi == null) return false;
        BlockPos origin = target.AsBlockPos;
        if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(origin) == null)
        {
            int chunkX = (int)Math.Floor(origin.X / (double)GlobalConstants.ChunkSize);
            int chunkZ = (int)Math.Floor(origin.Z / (double)GlobalConstants.ChunkSize);
            if (origin.dimension == 0)
            {
                serverApi.WorldManager.LoadChunkColumnPriority(
                    chunkX,
                    chunkZ,
                    new ChunkLoadOptions { KeepLoaded = false }
                );
            }
            else
            {
                serverApi.WorldManager.LoadChunkColumnForDimension(
                    chunkX,
                    chunkZ,
                    origin.dimension
                );
            }
            return false;
        }

        Vec3d? safe = FindSafeEntityPosition(origin, entity.Properties, entity, 6, 4);
        if (safe == null) return false;
        entity.TeleportTo(safe);
        entity.PositionBeforeFalling.Set(safe.X, safe.Y, safe.Z);
        return true;
    }

    internal void MarkCompanionRestArrival(Entity entity)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (status.GetLong(ActivityArrivedUtcMsKey, 0) > 0) return;
        status.SetLong(ActivityArrivedUtcMsKey, UtcNowMs());
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
    }

    internal bool IsCompanionRestComplete(Entity entity)
    {
        long arrived = GetDomesticationStatus(entity)?.GetLong(ActivityArrivedUtcMsKey, 0) ?? 0;
        if (arrived <= 0 || UtcNowMs() - arrived < MinimumCommandRestMs) return false;
        GetHealth(entity, out float current, out float maximum);
        return maximum <= 0f || current >= maximum - 0.01f;
    }

    internal static bool HasCompanionRestArrival(Entity entity)
    {
        return (GetDomesticationStatus(entity)?.GetLong(ActivityArrivedUtcMsKey, 0) ?? 0) > 0;
    }

    internal void CompleteCompanionActivity(Entity entity, string completedMode)
    {
        if (GetCompanionActivityMode(entity) != completedMode) return;
        bool calledToCart = completedMode == CompanionActivityMode.ReturnHome
            && packCartCallTargets.Remove(entity.EntityId);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        bool automaticRetreatActive = status.GetBool(AutomaticRetreatActiveKey, false);
        bool automaticRetreatHome = completedMode == CompanionActivityMode.ReturnHome
            && automaticRetreatActive;
        if (completedMode == CompanionActivityMode.Rest && automaticRetreatActive)
        {
            status.SetString(MoodKey, "rested");
            ClearAutomaticRetreat(entity);
        }
        else
        {
            SetCompanionActivityState(
                entity,
                automaticRetreatHome ? CompanionActivityMode.Rest : CompanionActivityMode.AtEase
            );
            if (completedMode == CompanionActivityMode.Rest)
            {
                status.SetString(MoodKey, "rested");
                MarkSocialStateDirty(entity);
                string ownerUid = status.GetString("owner", string.Empty);
                if (serverApi?.World.PlayerByUid(ownerUid) is IServerPlayer owner)
                {
                    SendOwnerSound(owner, CompanionSoundCue.RecoveryComplete);
                }
            }
        }
        RegisterFoxInPack(entity);
        packRepository?.Save();
        SendStateToOwner(
            entity,
            automaticRetreatHome
                ? "Home reached. This companion is resting until recovered."
                : completedMode == CompanionActivityMode.Rest
                ? "Rest complete. This companion is now At Ease."
                : calledToCart ? "Pack Cart reached. This companion is now At Ease."
                : "Home reached. This companion is now At Ease."
        );
    }

    private void RequestCompanionHomeTravel(Entity entity)
    {
        if (!IsTamedFox(entity) || IsFoxAwayFromWorld(entity))
        {
            return;
        }

        packCartCallTargets.Remove(entity.EntityId);
        CancelTargetedAttack(entity);
        DisengageHostileTargets(entity);
        SetCompanionActivityState(entity, CompanionActivityMode.ReturnHome);
        RegisterFoxInPack(entity);
    }

    private static void SetCompanionActivityState(Entity entity, string mode)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        mode = CompanionActivityMode.Normalize(mode);
        status.SetString(ActivityModeKey, mode);
        status.SetLong(ActivityStartedUtcMsKey, UtcNowMs());
        status.SetLong(ActivityArrivedUtcMsKey, 0);
        if (mode == CompanionActivityMode.Rest)
        {
            status.SetString(MoodKey, "resting");
        }
        else if (status.GetString(MoodKey, string.Empty) == "resting")
        {
            status.SetString(MoodKey, "calm");
        }
        MarkSocialStateDirty(entity);
        if (entity is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
        }
    }

    internal void RecordCompanionAttacker(Entity companion, Entity? attacker)
    {
        if (attacker == null || !IsValidCompanionFleeThreat(companion, attacker)) return;
        long expiresAtMs = UtcNowMs() + CombatMemoryMs;
        companionAttackers[companion.EntityId] = new CombatMemory
        {
            EntityId = attacker.EntityId,
            ExpiresAtMs = expiresAtMs
        };

        if (IsCompanionJuvenile(companion))
        {
            string ownerUid = GetCompanionOwnerUid(companion);
            if (!string.IsNullOrWhiteSpace(ownerUid))
            {
                juvenileThreats[companion.EntityId] = new JuvenileThreatMemory
                {
                    OwnerUid = ownerUid,
                    Dimension = companion.Pos.Dimension,
                    Position = new Vec3d(companion.Pos.X, companion.Pos.Y, companion.Pos.Z),
                    AttackerEntityId = attacker.EntityId,
                    ExpiresAtMs = expiresAtMs
                };
            }
        }

    }

    internal void TryTriggerAutomaticRetreat(Entity companion, Entity? attacker, float incomingDamage)
    {
        if (serverApi == null
            || incomingDamage <= 0f
            || (attacker != null && IsTamedFox(attacker)))
        {
            return;
        }

        string tolerance = GetCompanionRiskTolerance(companion);
        float threshold = CompanionRiskTolerance.RetreatHealthFraction(tolerance);
        if (threshold <= 0f) return;

        GetHealth(companion, out float currentHealth, out float maximumHealth);
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        bool retreatAlreadyReported = status.GetBool(AutomaticRetreatActiveKey, false);
        if (GetCompanionActivityMode(companion) == CompanionActivityMode.ReturnHome
            && !retreatAlreadyReported)
        {
            // A player-issued Return Home command is not an automatic retreat.
            return;
        }
        float projectedHealth = currentHealth - incomingDamage;
        if (maximumHealth <= 0f
            || projectedHealth <= 0f
            || projectedHealth > maximumHealth * threshold
            || retreatAlreadyReported)
        {
            return;
        }

        if (status.GetBool(TargetedAttackActiveKey, false))
        {
            RestoreTargetedAttack(companion);
        }

        bool retreatHome = TryResolveCompanionPlacedHome(companion, out _);
        string previousCombatStyle = GetCompanionCombatStyle(companion);
        if (!retreatHome && previousCombatStyle == CompanionCombatStyle.Flee)
        {
            // A player-selected Flee mode already owns the response. Do not
            // attach an automatic state that the health monitor must later clear.
            return;
        }

        string message;
        // Set this before changing activity/combat style.  Task predicates can
        // run immediately after StopTasks(), so they must see the retreat
        // guard during the same tick rather than one tick later.
        status.SetString(AutomaticRetreatPreviousCombatStyleKey, previousCombatStyle);
        status.SetString(AutomaticRetreatPreviousActivityKey, GetCompanionActivityMode(companion));
        status.SetBool(AutomaticRetreatActiveKey, true);
        if (retreatHome)
        {
            DisengageHostileTargets(companion);
            status.SetString(CombatStyleKey, CompanionCombatStyle.Passive);
            SetCompanionActivityState(companion, CompanionActivityMode.ReturnHome);
            message = $"{GetFoxDisplayName(companion)} is hurt and retreating home.";
        }
        else
        {
            status.SetString(CombatStyleKey, CompanionCombatStyle.Flee);
            MarkSocialStateDirty(companion);
            if (companion is EntityAgent agent)
            {
                agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                agent.Controls.StopAllMovement();
            }
            message = $"{GetFoxDisplayName(companion)} is hurt and fleeing!";
        }

        MarkSocialStateDirty(companion);
        RegisterFoxInPack(companion);
        packRepository?.Save();
        string ownerUid = GetCompanionOwnerUid(companion);
        if (serverApi.World.PlayerByUid(ownerUid) is IServerPlayer owner)
        {
            EmitDialogueEvent(companion, owner, "combat.retreat.full", string.Empty, CompanionDialoguePriority.High);
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                message,
                EnumChatType.Notification
            );
            SendState(companion, owner, message);
        }
    }

    private void UpdateAutomaticRetreatRecovery(Entity companion, ITreeAttribute status)
    {
        if (!status.GetBool(AutomaticRetreatActiveKey, false)) return;

        float threshold = CompanionRiskTolerance.RetreatHealthFraction(GetCompanionRiskTolerance(companion));
        GetHealth(companion, out float currentHealth, out float maximumHealth);
        float recoveryThreshold = maximumHealth * Math.Min(1f, threshold + AutomaticRetreatRecoveryHysteresis);
        if (maximumHealth > 0f && threshold > 0f && currentHealth <= recoveryThreshold) return;

        if (!ClearAutomaticRetreat(companion)) return;
        RegisterFoxInPack(companion);
        packRepository?.Save();
        SendStateToOwner(companion, $"{GetFoxDisplayName(companion)} has recovered and resumed its previous commands.");
    }

    private bool ClearAutomaticRetreat(Entity companion, bool restorePreviousActivity = true)
    {
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (status?.GetBool(AutomaticRetreatActiveKey, false) != true) return false;

        string currentStyle = GetCompanionCombatStyle(companion);
        string previousStyle = status.GetString(AutomaticRetreatPreviousCombatStyleKey, string.Empty);
        string previousActivity = status.GetString(AutomaticRetreatPreviousActivityKey, string.Empty);
        status.SetBool(AutomaticRetreatActiveKey, false);
        status.SetString(AutomaticRetreatPreviousCombatStyleKey, string.Empty);
        status.SetString(AutomaticRetreatPreviousActivityKey, string.Empty);

        if (CompanionCombatStyle.IsValid(previousStyle))
        {
            status.SetString(CombatStyleKey, CompanionCombatStyle.Normalize(previousStyle));
        }
        else if (currentStyle is CompanionCombatStyle.Flee or CompanionCombatStyle.Passive)
        {
            // Older saves do not have the captured style. Defensive is the
            // conservative usable fallback for those already-active retreats.
            status.SetString(CombatStyleKey, CompanionCombatStyle.Defensive);
        }

        string currentActivity = GetCompanionActivityMode(companion);
        if (restorePreviousActivity
            && (currentActivity == CompanionActivityMode.ReturnHome
                || currentActivity == CompanionActivityMode.Rest))
        {
            string resumedActivity = CompanionActivityMode.IsValid(previousActivity)
                ? CompanionActivityMode.Normalize(previousActivity)
                : CompanionActivityMode.AtEase;
            if (resumedActivity == CompanionActivityMode.Rest)
            {
                resumedActivity = CompanionActivityMode.AtEase;
            }
            SetCompanionActivityState(companion, resumedActivity);
        }

        if (companion is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
            agent.Pos.Motion.Set(0, 0, 0);
        }
        MarkSocialStateDirty(companion);
        return true;
    }

    internal void RecordOwnerAttacker(Entity owner, Entity? attacker)
    {
        if (serverApi == null || attacker == null || owner is not EntityPlayer player) return;
        string ownerUid = player.PlayerUID;
        if (string.IsNullOrWhiteSpace(ownerUid) || !IsValidOwnerCombatTarget(attacker)) return;
        ownerAttackers[ownerUid] = new CombatMemory
        {
            EntityId = attacker.EntityId,
            ExpiresAtMs = UtcNowMs() + CombatMemoryMs
        };
    }

    internal void RecordOwnerAttackTarget(Entity owner, Entity? target)
    {
        if (serverApi == null || target == null || owner is not EntityPlayer player) return;
        string ownerUid = player.PlayerUID;
        if (string.IsNullOrWhiteSpace(ownerUid) || !IsValidOwnerCombatTarget(target)) return;
        ownerAttackTargets[ownerUid] = new CombatMemory
        {
            EntityId = target.EntityId,
            ExpiresAtMs = UtcNowMs() + AssistMemoryMs
        };
    }

    internal bool BeginTargetedAttack(Entity companion, Entity target)
    {
        if (serverApi == null
            || !IsTamedFox(companion)
            || !IsValidCompanionCombatTarget(companion, target))
        {
            return false;
        }

        ClearAutomaticRetreat(companion);
        ITreeAttribute status = GetDomesticationStatus(companion, true)!;
        if (!status.GetBool(TargetedAttackActiveKey, false))
        {
            status.SetString(TargetedAttackPreviousActivityKey, GetCompanionActivityMode(companion));
            status.SetString(TargetedAttackPreviousFollowDistanceKey, GetCompanionFollowDistance(companion));
            status.SetString(TargetedAttackPreviousCombatStyleKey, GetCompanionCombatStyle(companion));
            status.SetString(TargetedAttackPreviousRiskToleranceKey, GetCompanionRiskTolerance(companion));
        }

        status.SetBool(TargetedAttackActiveKey, true);
        status.SetLong(TargetedAttackTargetIdKey, target.EntityId);
        status.SetString(CombatStyleKey, CompanionCombatStyle.Aggressive);
        SetCompanionActivityState(companion, CompanionActivityMode.AtEase);
        MarkSocialStateDirty(companion);
        companionAggressiveTargets[companion.EntityId] = target.EntityId;
        if (companion is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
        }
        RegisterFoxInPack(companion);
        packRepository?.Save();
        return true;
    }

    private bool TryResolveTargetedAttack(Entity companion, out Entity? target)
    {
        target = null;
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (status?.GetBool(TargetedAttackActiveKey, false) != true)
        {
            return false;
        }

        long targetId = status.GetLong(TargetedAttackTargetIdKey, 0);
        Entity? resolved = targetId > 0 ? serverApi?.World.GetEntityById(targetId) : null;
        if (resolved == null || !IsValidCompanionCombatTarget(companion, resolved))
        {
            RestoreTargetedAttack(companion);
            return false;
        }

        Entity? owner = GetOwnerEntity(companion);
        if (owner?.Alive == true
            && owner.Pos.Dimension == companion.Pos.Dimension
            && companion.Pos.SquareDistanceTo(owner.Pos) > TargetedAttackOwnerDistance * TargetedAttackOwnerDistance)
        {
            // Do not let a commanded animal chase an entity into an unloaded
            // region. Bring it back to a known-safe owner position and end the
            // temporary order so it cannot immediately run away again.
            TryTeleportCompanionToOwner(companion, owner);
            RestoreTargetedAttack(companion);
            return false;
        }

        companionAggressiveTargets[companion.EntityId] = resolved.EntityId;
        target = resolved;
        return true;
    }

    private void MaintainTargetedAttackOrders(IReadOnlyList<Entity> loadedCompanions)
    {
        foreach (Entity companion in loadedCompanions)
        {
            if (GetDomesticationStatus(companion)?.GetBool(TargetedAttackActiveKey, false) != true)
            {
                continue;
            }

            TryResolveTargetedAttack(companion, out _);
        }
    }

    private void MaintainCampAreaNotifications()
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        foreach (IPlayer player in serverApi.World.AllOnlinePlayers)
        {
            if (player is IServerPlayer owner)
            {
                MaintainPlayerCampAreaNotification(owner);
            }
        }
    }

    private void MaintainPlayerCampAreaNotification(IServerPlayer owner)
    {
        if (!TryGetPlayerCampAreaState(owner, out bool insideCamp, out string areaKind))
        {
            playerCampAreaStates.Remove(owner.PlayerUID);
            playerAreaKinds.Remove(owner.PlayerUID);
            return;
        }

        if (!playerCampAreaStates.TryGetValue(owner.PlayerUID, out bool wasInsideCamp))
        {
            // Establish the baseline silently. Loading or reconnecting should
            // not look like the player crossed the boundary.
            playerCampAreaStates[owner.PlayerUID] = insideCamp;
            playerAreaKinds[owner.PlayerUID] = areaKind;
            return;
        }

        if (wasInsideCamp == insideCamp)
        {
            return;
        }

        string previousKind = playerAreaKinds.TryGetValue(owner.PlayerUID, out string? storedKind)
            ? storedKind
            : string.Empty;
        playerCampAreaStates[owner.PlayerUID] = insideCamp;
        playerAreaKinds[owner.PlayerUID] = areaKind;
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            insideCamp
                ? areaKind == "workcart" ? "Entering Work Cart area." : "Entering camp area."
                : wasInsideCamp && previousKind == "workcart" ? "Leaving Work Cart area." : "Leaving camp area.",
            EnumChatType.Notification
        );
    }

    private bool TryGetPlayerCampAreaState(IServerPlayer owner, out bool insideCamp, out string areaKind)
    {
        insideCamp = false;
        areaKind = string.Empty;
        if (serverApi == null || packRepository?.Loaded != true || owner.Entity == null || !owner.Entity.Alive)
        {
            return false;
        }

        BlockPos? marker = GetActiveCairnPosition(owner.PlayerUID);
        if (marker != null && marker.dimension == owner.Entity.Pos.Dimension)
        {
            double dx = owner.Entity.Pos.X - (marker.X + 0.5);
            double dz = owner.Entity.Pos.Z - (marker.Z + 0.5);
            float campRadius = GetOwnerCampRadius(owner.PlayerUID);
            if (dx * dx + dz * dz <= campRadius * campRadius)
            {
                insideCamp = true;
                areaKind = "camp";
                return true;
            }
        }

        foreach (FoxWorkCartRecord cart in packRepository.GetWorkCartsForOwner(owner.PlayerUID))
        {
            if (cart.Dimension != owner.Entity.Pos.Dimension) continue;
            double dx = owner.Entity.Pos.X - (cart.X + 0.5);
            double dz = owner.Entity.Pos.Z - (cart.Z + 0.5);
            float workRadius = GetOwnerWorkCartRadius(owner.PlayerUID);
            if (dx * dx + dz * dz <= workRadius * workRadius)
            {
                insideCamp = true;
                areaKind = "workcart";
                return true;
            }
        }

        if (marker == null && !packRepository.GetWorkCartsForOwner(owner.PlayerUID).Any())
        {
            return false;
        }

        areaKind = "outside";
        return true;
    }

    private void RestoreTargetedAttack(Entity companion)
    {
        ITreeAttribute? status = GetDomesticationStatus(companion);
        if (status?.GetBool(TargetedAttackActiveKey, false) != true)
        {
            companionAggressiveTargets.Remove(companion.EntityId);
            return;
        }

        string activity = CompanionActivityMode.Normalize(
            status.GetString(TargetedAttackPreviousActivityKey, CompanionActivityMode.AtEase)
        );
        string followDistance = CompanionFollowDistance.Normalize(
            status.GetString(TargetedAttackPreviousFollowDistanceKey, CompanionFollowDistance.Normal)
        );
        string combatStyle = CompanionCombatStyle.Normalize(
            status.GetString(TargetedAttackPreviousCombatStyleKey, CompanionCombatStyle.Defensive)
        );
        string riskTolerance = CompanionRiskTolerance.Normalize(
            status.GetString(TargetedAttackPreviousRiskToleranceKey, CompanionRiskTolerance.Steady)
        );

        SetCompanionActivityState(companion, activity);
        status.SetString(FollowDistanceKey, followDistance);
        status.SetString(CombatStyleKey, combatStyle);
        status.SetString(RiskToleranceKey, riskTolerance);
        status.SetBool(TargetedAttackActiveKey, false);
        status.SetLong(TargetedAttackTargetIdKey, 0);
        status.RemoveAttribute(TargetedAttackPreviousActivityKey);
        status.RemoveAttribute(TargetedAttackPreviousFollowDistanceKey);
        status.RemoveAttribute(TargetedAttackPreviousCombatStyleKey);
        status.RemoveAttribute(TargetedAttackPreviousRiskToleranceKey);
        companionAggressiveTargets.Remove(companion.EntityId);
        MarkSocialStateDirty(companion);
        RegisterFoxInPack(companion);
        packRepository?.Save();
    }

    private void TryTeleportCompanionToOwner(Entity companion, Entity owner)
    {
        if (serverApi == null || owner.Pos.Dimension != companion.Pos.Dimension)
        {
            return;
        }

        Vec3d? safe = FindSafeEntityPosition(owner.Pos.AsBlockPos, companion.Properties, companion, 6, 4);
        if (safe == null)
        {
            return;
        }

        companion.TeleportTo(safe);
        companion.PositionBeforeFalling.Set(safe.X, safe.Y, safe.Z);
    }

    internal bool TryResolveCompanionCombatTarget(Entity companion, out Entity? target)
    {
        target = null;
        if (serverApi == null || !CanRunCompanionCombatTask(companion)) return false;

        if (TryResolveTargetedAttack(companion, out target))
        {
            return true;
        }

        string style = GetCompanionCombatStyle(companion);
        string ownerUid = GetCompanionOwnerUid(companion);
        if (GetCompanionActivityMode(companion) == CompanionActivityMode.Rest)
        {
            CombatMemory? directThreat = GetLiveCombatMemory(companionAttackers, companion.EntityId);
            if (directThreat == null
                && CompanionProtectionPolicy.ShouldProtectNearbyOwnedCompanions(style)
                && TryResolveNearbyOwnedCompanionThreat(companion, ownerUid, out target))
            {
                return true;
            }
            if (directThreat == null
                && CompanionProtectionPolicy.ShouldAggressiveRespondToJuvenileThreats(style)
                && TryResolveNearbyJuvenileThreat(companion, ownerUid, out target))
            {
                return true;
            }
            if (directThreat == null) return false;
            Entity? attacker = serverApi.World.GetEntityById(directThreat.EntityId);
            if (attacker == null || !IsValidCompanionCombatTarget(companion, attacker)) return false;
            target = attacker;
            return true;
        }
        CombatMemory? memory = style switch
        {
            CompanionCombatStyle.Defensive => GetLiveCombatMemory(companionAttackers, companion.EntityId),
            CompanionCombatStyle.Protect => GetLiveCombatMemory(ownerAttackers, ownerUid),
            CompanionCombatStyle.Assist => GetLiveCombatMemory(ownerAttackTargets, ownerUid),
            _ => null
        };

        if (memory != null)
        {
            Entity? remembered = serverApi.World.GetEntityById(memory.EntityId);
            if (remembered != null && IsValidCompanionCombatTarget(companion, remembered))
            {
                target = remembered;
                return true;
            }
        }

        if (CompanionProtectionPolicy.ShouldProtectNearbyOwnedCompanions(style)
            && TryResolveNearbyOwnedCompanionThreat(companion, ownerUid, out target))
        {
            return true;
        }
        if (CompanionProtectionPolicy.ShouldAggressiveRespondToJuvenileThreats(style)
            && TryResolveNearbyJuvenileThreat(companion, ownerUid, out target))
        {
            return true;
        }

        if (style != CompanionCombatStyle.Aggressive)
        {
            companionAggressiveTargets.Remove(companion.EntityId);
            return false;
        }
        if (!TryGetAggressiveSearch(companion, out Vec3d? searchCenter, out float acquireRange, out float releaseRange)
            || searchCenter == null)
        {
            companionAggressiveTargets.Remove(companion.EntityId);
            return false;
        }

        if (companionAggressiveTargets.TryGetValue(companion.EntityId, out long retainedId))
        {
            Entity? retained = serverApi.World.GetEntityById(retainedId);
            if (retained != null
                && IsValidCompanionCombatTarget(companion, retained)
                && IsKnownHostile(retained)
                && retained.Pos.XYZ.SquareDistanceTo(searchCenter) <= releaseRange * releaseRange)
            {
                target = retained;
                return true;
            }
            companionAggressiveTargets.Remove(companion.EntityId);
        }

        target = serverApi.World.GetEntitiesAround(searchCenter, acquireRange, Math.Min(12f, acquireRange))
            .Where(candidate => IsValidCompanionCombatTarget(companion, candidate) && IsKnownHostile(candidate))
            .OrderBy(candidate => candidate.Pos.XYZ.SquareDistanceTo(searchCenter))
            .FirstOrDefault();
        if (target != null)
        {
            companionAggressiveTargets[companion.EntityId] = target.EntityId;
        }
        return target != null;
    }

    private bool TryResolveNearbyOwnedCompanionThreat(Entity protector, string ownerUid, out Entity? attacker)
    {
        attacker = null;
        if (serverApi == null || string.IsNullOrWhiteSpace(ownerUid)) return false;

        double nearestDistanceSquared = double.PositiveInfinity;
        foreach (Entity ownedCompanion in loadedFoxes.Values)
        {
            double distanceSquared = protector.Pos.XYZ.SquareDistanceTo(ownedCompanion.Pos.XYZ);
            if (!IsTamedFox(ownedCompanion)
                || !CompanionProtectionPolicy.IsWithinOwnedCompanionScope(
                    ownerUid,
                    protector.Pos.Dimension,
                    GetCompanionOwnerUid(ownedCompanion),
                    ownedCompanion.Pos.Dimension,
                    distanceSquared))
            {
                continue;
            }

            CombatMemory? memory = GetLiveCombatMemory(companionAttackers, ownedCompanion.EntityId);
            if (memory == null) continue;

            Entity? candidate = serverApi.World.GetEntityById(memory.EntityId);
            if (candidate == null || !IsValidCompanionCombatTarget(protector, candidate)) continue;
            if (distanceSquared >= nearestDistanceSquared) continue;

            nearestDistanceSquared = distanceSquared;
            attacker = candidate;
        }

        return attacker != null;
    }

    private bool TryResolveNearbyJuvenileThreat(Entity guardian, string ownerUid, out Entity? attacker)
    {
        attacker = null;
        if (serverApi == null || string.IsNullOrWhiteSpace(ownerUid)) return false;

        foreach (Entity juvenile in loadedFoxes.Values)
        {
            if (!IsCompanionJuvenile(juvenile)
                || !IsTamedFox(juvenile)
                || !CompanionProtectionPolicy.IsWithinOwnedCompanionScope(
                    ownerUid,
                    guardian.Pos.Dimension,
                    GetCompanionOwnerUid(juvenile),
                    juvenile.Pos.Dimension,
                    guardian.Pos.XYZ.SquareDistanceTo(juvenile.Pos.XYZ)))
            {
                continue;
            }

            CombatMemory? memory = GetLiveCombatMemory(companionAttackers, juvenile.EntityId);
            if (memory == null) continue;

            Entity? candidate = serverApi.World.GetEntityById(memory.EntityId);
            if (candidate == null || !IsValidCompanionCombatTarget(guardian, candidate)) continue;
            attacker = candidate;
            return true;
        }

        long now = UtcNowMs();
        foreach ((long juvenileId, JuvenileThreatMemory threat) in juvenileThreats.ToArray())
        {
            if (threat.ExpiresAtMs <= now)
            {
                juvenileThreats.Remove(juvenileId);
                continue;
            }

            Entity? candidate = serverApi.World.GetEntityById(threat.AttackerEntityId);
            if (candidate == null
                || !CompanionProtectionPolicy.IsUsableJuvenileThreatSnapshot(
                    ownerUid,
                    guardian.Pos.Dimension,
                    threat.OwnerUid,
                    threat.Dimension,
                    guardian.Pos.XYZ.SquareDistanceTo(threat.Position),
                    now,
                    threat.ExpiresAtMs,
                    candidate.Alive,
                    candidate.State == EnumEntityState.Active,
                    candidate is EntityAgent,
                    candidate is EntityPlayer,
                    IsTamedFox(candidate))
                || !IsValidCompanionCombatTarget(guardian, candidate)) continue;
            attacker = candidate;
            return true;
        }

        return false;
    }

    internal bool HasActiveCompanionCombatResponse(Entity companion)
    {
        if (!CanRunCompanionCombatTask(companion)) return false;
        if (GetCompanionCombatStyle(companion) == CompanionCombatStyle.Flee)
        {
            return TryResolveCompanionFleeDestination(companion, out Vec3d? destination)
                && destination != null;
        }

        return TryResolveCompanionCombatTarget(companion, out Entity? target)
            && target != null;
    }

    private bool TryGetAggressiveSearch(
        Entity companion,
        out Vec3d? searchCenter,
        out float acquireRange,
        out float releaseRange)
    {
        searchCenter = companion.Pos.XYZ;
        acquireRange = 20f;
        releaseRange = 24f;
        if (GetCompanionActivityMode(companion) != CompanionActivityMode.Follow)
        {
            return true;
        }

        string ownerUid = GetCompanionOwnerUid(companion);
        Entity? owner = serverApi?.World.PlayerByUid(ownerUid)?.Entity;
        if (owner?.Alive != true || owner.Pos.Dimension != companion.Pos.Dimension)
        {
            searchCenter = null;
            return false;
        }

        searchCenter = owner.Pos.XYZ;
        switch (GetCompanionFollowDistance(companion))
        {
            case CompanionFollowDistance.Close:
                acquireRange = 12f;
                releaseRange = 16f;
                break;
            case CompanionFollowDistance.Back:
                acquireRange = 28f;
                releaseRange = 32f;
                break;
            default:
                acquireRange = 20f;
                releaseRange = 24f;
                break;
        }
        return true;
    }

    internal bool TryResolveCompanionFleeDestination(Entity companion, out Vec3d? destination)
    {
        destination = null;
        if (serverApi == null || GetCompanionCombatStyle(companion) != CompanionCombatStyle.Flee) return false;

        Entity? threat = null;
        CombatMemory? memory = GetLiveCombatMemory(companionAttackers, companion.EntityId);
        if (memory != null)
        {
            Entity? remembered = serverApi.World.GetEntityById(memory.EntityId);
            if (remembered != null
                && IsValidCompanionFleeThreat(companion, remembered)
                && remembered.Pos.SquareDistanceTo(companion.Pos) <= 18d * 18d)
            {
                threat = remembered;
            }
        }
        threat ??= serverApi.World.GetEntitiesAround(companion.Pos.XYZ, 14f, 10f)
            .Where(candidate => IsValidCompanionCombatTarget(companion, candidate) && IsKnownHostile(candidate))
            .OrderBy(candidate => candidate.Pos.SquareDistanceTo(companion.Pos))
            .FirstOrDefault();
        if (threat == null) return false;

        string ownerUid = GetCompanionOwnerUid(companion);
        Entity? owner = serverApi.World.PlayerByUid(ownerUid)?.Entity;
        if (owner?.Alive == true
            && owner.Pos.Dimension == companion.Pos.Dimension
            && TryBuildFleeAnchorDestination(companion, threat, owner.Pos.XYZ, out destination))
        {
            return true;
        }

        if (TryResolveCompanionCommandHome(companion, out Vec3d? home)
            && home != null
            && home.AsBlockPos.dimension == companion.Pos.Dimension
            && TryBuildFleeAnchorDestination(companion, threat, home, out destination))
        {
            return true;
        }

        double dx = companion.Pos.X - threat.Pos.X;
        double dz = companion.Pos.Z - threat.Pos.Z;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < 0.01)
        {
            dx = companion.World.Rand.NextDouble() - 0.5;
            dz = companion.World.Rand.NextDouble() - 0.5;
            length = Math.Max(0.01, Math.Sqrt(dx * dx + dz * dz));
        }
        destination = new Vec3d(
            companion.Pos.X + dx / length * 10,
            companion.Pos.Y,
            companion.Pos.Z + dz / length * 10
        );
        return true;
    }

    private static bool TryBuildFleeAnchorDestination(
        Entity companion,
        Entity threat,
        Vec3d anchor,
        out Vec3d? destination)
    {
        destination = null;
        double currentThreatDistance = Math.Sqrt(companion.Pos.SquareDistanceTo(threat.Pos));
        double anchorThreatDistance = Math.Sqrt(anchor.SquareDistanceTo(threat.Pos.XYZ));
        if (anchorThreatDistance <= currentThreatDistance + 0.5d)
        {
            return false;
        }

        double travelX = anchor.X - companion.Pos.X;
        double travelY = anchor.Y - companion.Pos.Y;
        double travelZ = anchor.Z - companion.Pos.Z;
        double travelDistance = Math.Sqrt(travelX * travelX + travelY * travelY + travelZ * travelZ);
        if (travelDistance >= 4d)
        {
            double boundedTravel = Math.Min(16d, travelDistance);
            destination = new Vec3d(
                companion.Pos.X + travelX / travelDistance * boundedTravel,
                companion.Pos.Y + travelY / travelDistance * boundedTravel,
                companion.Pos.Z + travelZ / travelDistance * boundedTravel
            );
            return true;
        }

        // The safer anchor is already beside the companion. Running merely to
        // the owner's exact feet would complete immediately and make Flee look
        // identical to Passive, so continue through/past the anchor.
        double awayX = anchor.X - threat.Pos.X;
        double awayZ = anchor.Z - threat.Pos.Z;
        double awayLength = Math.Sqrt(awayX * awayX + awayZ * awayZ);
        if (awayLength < 0.01d)
        {
            return false;
        }

        double desiredThreatDistance = Math.Max(anchorThreatDistance + 6d, currentThreatDistance + 10d);
        destination = new Vec3d(
            threat.Pos.X + awayX / awayLength * desiredThreatDistance,
            anchor.Y,
            threat.Pos.Z + awayZ / awayLength * desiredThreatDistance
        );
        return true;
    }

    private CombatMemory? GetLiveCombatMemory(Dictionary<long, CombatMemory> memories, long key)
    {
        if (!memories.TryGetValue(key, out CombatMemory? memory)) return null;
        if (memory.ExpiresAtMs >= UtcNowMs()) return memory;
        memories.Remove(key);
        return null;
    }

    private CombatMemory? GetLiveCombatMemory(Dictionary<string, CombatMemory> memories, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !memories.TryGetValue(key, out CombatMemory? memory)) return null;
        if (memory.ExpiresAtMs >= UtcNowMs()) return memory;
        memories.Remove(key);
        return null;
    }

    private void PruneJuvenileThreats(long nowMs)
    {
        foreach ((long juvenileId, JuvenileThreatMemory threat) in juvenileThreats.ToArray())
        {
            if (threat.ExpiresAtMs <= nowMs)
            {
                juvenileThreats.Remove(juvenileId);
            }
        }
    }

    private static bool IsValidOwnerCombatTarget(Entity target)
    {
        return target.Alive
            && target.State == EnumEntityState.Active
            && target is EntityAgent
            && target is not EntityPlayer
            && !IsTamedFox(target);
    }

    internal static bool IsValidCompanionCombatTarget(Entity companion, Entity target)
    {
        return target != companion
            && target.Alive
            && target.State == EnumEntityState.Active
            && target is EntityAgent
            && target is not EntityPlayer
            && target.Pos.Dimension == companion.Pos.Dimension
            && !IsTamedFox(target);
    }

    private static bool IsValidCompanionFleeThreat(Entity companion, Entity target)
    {
        return target != companion
            && target.Alive
            && target.State == EnumEntityState.Active
            && target is EntityAgent
            && target.Pos.Dimension == companion.Pos.Dimension
            && !IsTamedFox(target);
    }

    private static bool IsKnownHostile(Entity entity)
    {
        string path = entity.Code?.Path ?? string.Empty;
        return path.StartsWith("trainingdummy", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("drifter", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shiver", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bowtorn", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("locust", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("wolf", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("hyena", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bear", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("cockatrice", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("direwolf", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("hellboar", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("scorpion", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("spider", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("golem", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("geodecrab", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shark", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bigshark", StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryGetFoxDayTerritoryCenter(Entity entity, out Vec3d? center)
    {
        center = null;
        if (serverApi == null || packRepository?.Loaded != true || !IsTamedFox(entity))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null)
        {
            return false;
        }

        if (string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase)
            && record.HasHome)
        {
            BlockPos workCart = new(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
            if (workCart.dimension == entity.Pos.Dimension
                && serverApi.World.BlockAccessor.GetChunkAtBlockPos(workCart) != null)
            {
                Block block = serverApi.World.BlockAccessor.GetBlock(workCart);
                if (IsWorkCartCode(block.Code))
                {
                    BlockPos portal = GetPackMarkerPortalBlock(workCart, block);
                    center = new Vec3d(portal.X + 0.5, portal.Y + portal.dimension * BlockPos.DimensionBoundary, portal.Z + 0.5);
                    return true;
                }
            }
        }

        BlockPos? marker = GetActiveCairnPosition(record.OwnerUid);
        if (marker != null
            && marker.dimension == entity.Pos.Dimension
            && serverApi.World.BlockAccessor.GetChunkAtBlockPos(marker) != null)
        {
            Block block = serverApi.World.BlockAccessor.GetBlock(marker);
            if (block.Code?.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal) == true)
            {
                BlockPos portal = GetPackMarkerPortalBlock(marker, block);
                center = new Vec3d(
                    portal.X + 0.5,
                    portal.Y + portal.dimension * BlockPos.DimensionBoundary,
                    portal.Z + 0.5
                );
                return true;
            }
        }

        return TryGetAssignedFoxDen(entity, out center);
    }

    internal bool ShouldFoxSeekDenShelter(Entity entity)
    {
        if (serverApi == null
            || !entity.Alive
            || IsFoxAwayFromWorld(entity)
            || IsFoxIncapacitated(entity))
        {
            return false;
        }

        if (temporalStabilitySystem?.StormData?.nowStormActive == true)
        {
            return true;
        }

        if (weatherSystem?.GetPrecipitation(entity.Pos.XYZ) > 0.1f)
        {
            return true;
        }

        double hour = entity.World.Calendar.HourOfDay;
        double morningThreshold = 6.5d - GetFoxPackTalentRank(entity, "earlier-morning") * 1d;
        double bedtimeThreshold = 19.5d + GetFoxPackTalentRank(entity, "later-bedtime") * 1d;
        return hour < morningThreshold
            || hour >= bedtimeThreshold
            || string.Equals(GetMood(entity), "sleepy", StringComparison.Ordinal);
    }

    internal bool IsTemporalStormActive()
    {
        return temporalStabilitySystem?.StormData?.nowStormActive == true;
    }

    internal void OnFoxBedInteracted(IPlayer byPlayer, BlockPos pos)
    {
        if (byPlayer is not IServerPlayer serverPlayer || packRepository?.Loaded != true)
        {
            return;
        }

        FoxBedRecord? bed = packRepository.GetBed(pos);
        if (bed == null)
        {
            Block? block = serverApi?.World.BlockAccessor.GetBlock(pos);
            if (block?.Code != null && IsFoxBedCode(block.Code))
            {
                bed = packRepository.RegisterBed(serverPlayer.PlayerUID, pos, block.Code.ToShortString());
                packRepository.Save();
            }
        }

        if (bed == null || !string.Equals(bed.OwnerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
        {
            serverPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Only the player who placed this companion bed can assign it.",
                EnumChatType.Notification
            );
            return;
        }

        SendFoxBedState(serverPlayer, bed, string.Empty);
    }

    internal void OnWorkCartInteracted(IPlayer byPlayer, BlockPos pos)
    {
        if (byPlayer is not IServerPlayer serverPlayer || packRepository?.Loaded != true)
        {
            return;
        }

        FoxWorkCartRecord? cart = packRepository.GetWorkCart(pos);
        if (cart == null)
        {
            Block? block = serverApi?.World.BlockAccessor.GetBlock(pos);
            if (block?.Code != null && IsWorkCartCode(block.Code))
            {
                cart = packRepository.RegisterWorkCart(
                    serverPlayer.PlayerUID,
                    pos,
                    GetWorkCartKind(block.Code));
                packRepository.Save();
            }
        }

        if (cart == null || !string.Equals(cart.OwnerUid, serverPlayer.PlayerUID, StringComparison.Ordinal))
        {
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                "Only the player who placed this Work Cart can assign it.", EnumChatType.Notification);
            return;
        }

        SendFoxWorkCartState(serverPlayer, cart, string.Empty);
    }

    internal static bool IsFoxMortallyWounded(Entity entity)
    {
        return entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0)
            == MortallyWoundedHealthState;
    }

    internal static bool IsFoxIncapacitated(Entity entity)
    {
        int state = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        return state is MortallyWoundedHealthState or RecoveringHealthState;
    }

    private void PersistHealthStateIfChanged(Entity entity)
    {
        int state = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        if (lastPersistedHealthStates.TryGetValue(entity.EntityId, out int previousState)
            && previousState == state)
        {
            return;
        }

        RegisterFoxInPack(entity);
        lastPersistedHealthStates[entity.EntityId] = state;
        packRepository?.Save();
    }

    internal static bool IsFoxAwayFromWorld(Entity entity)
    {
        return GetDomesticationStatus(entity)?.GetBool(ExpeditionAwayKey, false) == true;
    }

    internal Entity? FindLoadedCompanionByFoxId(string foxId)
    {
        if (string.IsNullOrWhiteSpace(foxId))
        {
            return null;
        }

        foreach (Entity candidate in loadedFoxes.Values)
        {
            if (!candidate.Alive)
            {
                continue;
            }

            string candidateId = GetDomesticationStatus(candidate)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
            if (string.Equals(candidateId, foxId, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void SetFoxAwayFromWorld(Entity entity, bool away)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (status.GetBool(ExpeditionAwayKey, false) != away)
        {
            status.SetBool(ExpeditionAwayKey, away);
            MarkSocialStateDirty(entity);
        }

        entity.GetBehavior<EntityBehaviorFeralKinshipFoxSocial>()?.RefreshExpeditionSuspension();
    }

    internal static string GetMoodIdleAnimation(Entity entity)
    {
        string mood = GetMood(entity);

        return mood switch
        {
            "sleepy" or "resting" or "rested" => "sleep",
            "calm" or "content" or "social" or "happy" or "recovered" or "refreshed" or "relieved" => "sit",
            "curious" or "restless" or "playful" or "alert" or "anxious" or "alarmed" or "rallied" => "sniff",
            _ => "idle"
        };
    }

    internal static string GetMood(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null)
        {
            return string.Empty;
        }

        string forcedMood = status.GetString(PackMoodOverrideKey, string.Empty);
        if (!string.IsNullOrEmpty(forcedMood)
            && GetRemainingSeconds(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideRemainingKey) > 0f)
        {
            return forcedMood;
        }

        return status.GetString(MoodKey, string.Empty);
    }

    internal static bool IsForcedMood(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        return status != null
            && ((!string.IsNullOrEmpty(status.GetString(PackMoodOverrideKey, string.Empty))
                    && GetRemainingSeconds(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideRemainingKey) > 0f)
                || (status.GetBool(DeveloperMoodForcedKey, false)
                    && !string.IsNullOrEmpty(status.GetString(MoodOverrideKey, string.Empty))
                    && GetRemainingSeconds(status, MoodOverrideEndsUtcMsKey, MoodOverrideRemainingKey) > 0f));
    }

    internal static int GetFoxDamagePerkRank(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        ITreeAttribute? perks = status?.GetTreeAttribute(PerkTreeKey);
        int rank = perks?.GetInt("damage-training", -1) ?? -1;
        if (rank < 0)
        {
            rank = status?.GetInt(DamagePerkRankKey, 0) ?? 0;
        }

        return Math.Clamp(rank, 0, MaximumDamagePerkRank);
    }

    internal static ITreeAttribute? GetFoxPerkTree(Entity entity, bool create = false)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity, create);
        if (status == null)
        {
            return null;
        }

        ITreeAttribute? perks = status.GetTreeAttribute(PerkTreeKey);
        if (perks == null && create)
        {
            perks = new TreeAttribute();
            status[PerkTreeKey] = perks;
        }

        if (perks != null && perks.GetInt("damage-training", -1) < 0)
        {
            int legacyRank = status.GetInt(DamagePerkRankKey, 0);
            if (legacyRank > 0)
            {
                perks.SetInt("damage-training", Math.Clamp(legacyRank, 0, MaximumDamagePerkRank));
            }
        }

        return perks;
    }

    internal static float GetFoxDamageMultiplier(Entity entity)
    {
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        float bonus = 0f;
        if (perks != null)
        {
            bonus += FoxPerkCatalog.GetRank(perks, "feral-damage") * DamageBonusPerRank;
            bonus += FoxPerkCatalog.GetRank(perks, "damage-training") * DamageBonusPerRank;
            bonus += FoxPerkCatalog.GetRank(perks, "predators-force") * DamageBonusPerRank;
            bonus += FoxPerkCatalog.GetRank(perks, "predator") * 0.20f;
        }

        bonus += GetFoxPackTalentRank(entity, "pack-strength") * DamageBonusPerRank;
        bonus += GetFoxMoodDamageBonus(entity);
        return 1f + bonus;
    }

    internal static float GetFoxDamageBonusPercent(Entity entity)
    {
        return (GetFoxDamageMultiplier(entity) - 1f) * 100f;
    }

    internal static float GetCompanionBaseMeleeDamage(Entity entity)
    {
        CompanionSpeciesProfile profile = GetCompanionSpecies(entity);
        return ReferenceEquals(profile, CompanionSpeciesCatalog.TamablesFotsa)
            && TryResolveFotsaStat(entity, false, out float fotsaDamage)
            ? fotsaDamage
            : profile.BaseMeleeDamage;
    }

    private static bool TryResolveFotsaStat(Entity entity, bool maxHealth, out float value)
    {
        value = 0f;
        if (!ReferenceEquals(GetCompanionSpecies(entity), CompanionSpeciesCatalog.TamablesFotsa)
            || !TryGetFotsaStatProfile(entity, out FotsaCombatStatProfile? profile, out string variantString)
            || profile == null)
        {
            return false;
        }

        FotsaCombatStatProfile resolvedProfile = profile;
        Dictionary<string, float> values = maxHealth ? resolvedProfile.MaxHealthByType : resolvedProfile.DamageByType;
        int bestSpecificity = -1;
        foreach ((string pattern, float candidate) in values)
        {
            if (!WildcardMatches(pattern, variantString))
            {
                continue;
            }

            int specificity = pattern.Count(character => character != '*');
            if (specificity > bestSpecificity)
            {
                bestSpecificity = specificity;
                value = candidate;
            }
        }

        return bestSpecificity >= 0 && value > 0f;
    }

    private static bool TryGetFotsaStatProfile(
        Entity entity,
        out FotsaCombatStatProfile? profile,
        out string variantString)
    {
        const string tamePrefix = "tame-";
        profile = null;
        variantString = string.Empty;
        if (!string.Equals(entity.Code.Domain, "tamablesfotsa", StringComparison.OrdinalIgnoreCase)
            || !entity.Code.Path.StartsWith(tamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        variantString = entity.Code.Path[tamePrefix.Length..];
        string[] variantCandidates =
        {
            variantString,
            StripFotsaSourceDomainPrefix(variantString)
        };
        string? sourceKey = variantCandidates
            .SelectMany(candidate => FotsaCombatStatsBySourceCode.Keys
                .Where(key => candidate.Equals(key, StringComparison.OrdinalIgnoreCase)
                    || candidate.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase))
                .Select(key => (Candidate: candidate, Key: key)))
            .OrderByDescending(match => match.Key.Length)
            .Select(match => match.Key)
            .FirstOrDefault();
        if (sourceKey == null)
        {
            return false;
        }

        variantString = variantCandidates
            .First(candidate => candidate.Equals(sourceKey, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(sourceKey + "-", StringComparison.OrdinalIgnoreCase));
        profile = FotsaCombatStatsBySourceCode[sourceKey];
        return true;
    }

    private static string StripFotsaSourceDomainPrefix(string encodedVariantString)
    {
        int separator = encodedVariantString.IndexOf('-');
        return separator > 0
            ? encodedVariantString[(separator + 1)..]
            : encodedVariantString;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        int patternIndex = 0;
        int valueIndex = 0;
        int starIndex = -1;
        int starMatchIndex = 0;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && char.ToLowerInvariant(pattern[patternIndex]) == char.ToLowerInvariant(value[valueIndex]))
            {
                patternIndex++;
                valueIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                starMatchIndex = valueIndex;
                continue;
            }

            if (starIndex < 0)
            {
                return false;
            }

            patternIndex = starIndex + 1;
            valueIndex = ++starMatchIndex;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    internal static float GetFoxAttackSpeedMultiplier(Entity entity)
    {
        float bonus = GetFoxPerkRank(entity, "attack-speed") * 0.05f
            + GetFoxPackTalentRank(entity, "quick-fangs") * PackTalentPercent;
        if (IsDeadlineActive(entity, BattleRhythmEndsUtcMsKey))
        {
            bonus += GetFoxPerkRank(entity, "battle-rhythm") * BattleRhythmBonusPerRank;
        }

        return 1f + bonus;
    }

    internal static float GetFoxMovementSpeedMultiplier(Entity entity)
    {
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        float bonus = perks == null
            ? 0f
            : FoxPerkCatalog.GetRank(perks, "more-speed") * 0.10f
                + FoxPerkCatalog.GetRank(perks, "even-more-speed") * 0.05f;

        bonus += GetFoxPackTalentRank(entity, "fleet-pack") * PackTalentPercent;

        bonus += GetFoxMoodMovementBonus(entity);
        if (IsDeadlineActive(entity, AdrenalineRushEndsUtcMsKey))
        {
            bonus += GetFoxPerkRank(entity, "adrenaline-rush") * AdrenalineRushMovementPerRank;
        }
        if (IsDeadlineActive(entity, PanicSprintEndsUtcMsKey))
        {
            bonus += GetFoxPerkRank(entity, "panic-sprint") * PanicSprintBonusPerRank;
        }

        BlockPos feet = entity.Pos.AsBlockPos;
        Block below = entity.World.BlockAccessor.GetBlock(feet.DownCopy());
        string ground = below.Code?.Path ?? string.Empty;
        if (GetFoxPerkRank(entity, "roadrunner") > 0
            && (below.WalkSpeedMultiplier > 1f
                || ground.Contains("road", StringComparison.OrdinalIgnoreCase)
                || ground.Contains("path", StringComparison.OrdinalIgnoreCase)))
        {
            bonus += 0.15f;
        }

        float multiplier = 1f + bonus;
        return IsCompanionPregnant(entity)
            ? multiplier * GetPregnancyMovementMultiplier(entity)
            : multiplier;
    }

    internal static float GetFoxMoodRegenMultiplier(Entity entity)
    {
        float bonus = GetFoxMoodRegenBonus(entity)
            + GetFoxPackTalentRank(entity, "shared-recovery") * PackTalentSharedRecoveryPercent;
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        if (perks != null)
        {
            int combatRecoveryRank = FoxPerkCatalog.GetRank(perks, "combat-recovery");
            if (combatRecoveryRank > 0 && HasBeenOutOfCombatLongEnough(entity))
            {
                bonus += combatRecoveryRank * CombatRecoveryBonusPerRank;
            }

            if (IsDeadlineActive(entity, LastDamageUtcMsKey, ThickBloodDurationSeconds))
            {
                bonus += FoxPerkCatalog.GetRank(perks, "thick-blood") * ThickBloodBonusPerRank;
            }

            if (IsDeadlineActive(entity, FreshMeatEndsUtcMsKey))
            {
                bonus += FoxPerkCatalog.GetRank(perks, "fresh-meat") * FreshMeatBonusPerRank;
            }
        }

        return 1f + bonus;
    }

    private static float GetFoxMoodRegenBonus(Entity entity)
    {
        return GetMood(entity) switch
        {
            "sleepy" => 0.25f,
            "resting" => 0.40f,
            "rested" => 0.25f,
            "refreshed" => 0.20f,
            "recovered" => 0.30f,
            "relieved" => 0.20f,
            "happy" => 0.10f,
            _ => 0f
        };
    }

    private static float GetFoxMoodMovementBonus(Entity entity)
    {
        return GetMood(entity) switch
        {
            "sleepy" => -0.10f,
            "resting" => -0.15f,
            "curious" => 0.05f,
            "playful" => 0.05f,
            "restless" => 0.10f,
            "anxious" => 0.05f,
            "alert" => 0.05f,
            "alarmed" => 0.10f,
            "rallied" => 0.05f,
            _ => 0f
        };
    }

    private static float GetFoxMoodDamageBonus(Entity entity)
    {
        return string.Equals(GetMood(entity), "rallied", StringComparison.OrdinalIgnoreCase)
            ? 0.05f
            : 0f;
    }

    internal static float GetFoxRequestCooldownSeconds(Entity entity)
    {
        return Math.Max(
            0f,
            PlayerRequestCooldownSeconds
                - GetFoxPerkRank(entity, "reduced-request-cooldown") * 30f
                - GetFoxPerkRank(entity, "true-companion") * 60f
                - GetFoxPackTalentRank(entity, "patient-pack") * PlayerRequestCooldownSeconds * PackTalentCooldownReduction
        );
    }

    internal static float GetFoxCancelCooldownSeconds(Entity entity)
    {
        return Math.Max(
            0f,
            PlayerCancelCooldownSeconds
                - GetFoxPerkRank(entity, "patient-fox") * 30f
                - GetFoxPerkRank(entity, "true-companion") * 30f
                - GetFoxPackTalentRank(entity, "cancellation-discipline") * PlayerCancelCooldownSeconds * PackTalentCooldownReduction
        );
    }

    internal static int GetFoxPerkRank(Entity entity, string perkId)
    {
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        return perks == null ? 0 : FoxPerkCatalog.GetRank(perks, perkId);
    }

    internal static int GetFoxPackTalentRank(Entity entity, string talentId)
    {
        if (string.Equals(talentId, "far-reaching-pack", StringComparison.Ordinal))
        {
            return GetPermanentRangeRank(entity);
        }
        string snapshot = GetDomesticationStatus(entity)?.GetString(PackTalentSnapshotKey, string.Empty)
            ?? string.Empty;
        return ContainsCommaSeparatedToken(snapshot, talentId) ? 1 : 0;
    }

    private static bool ContainsCommaSeparatedToken(string value, string token)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return false;

        int start = 0;
        while (start < value.Length)
        {
            int separator = value.IndexOf(',', start);
            int end = separator < 0 ? value.Length : separator;
            while (start < end && char.IsWhiteSpace(value[start])) start++;
            while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
            if (value.AsSpan(start, end - start).Equals(token.AsSpan(), StringComparison.Ordinal))
            {
                return true;
            }
            if (separator < 0) break;
            start = separator + 1;
        }
        return false;
    }

    internal static float GetFoxArmorBonus(Entity entity)
    {
        return GetFoxPerkRank(entity, "armor") * 0.5f
            + GetFoxPerkRank(entity, "hardened-frame") * 0.5f;
    }

    internal static float GetFoxDamageResistance(Entity entity)
    {
        return IsDeadlineActive(entity, AdrenalineRushEndsUtcMsKey)
            ? GetFoxPerkRank(entity, "adrenaline-rush") * AdrenalineRushResistancePerRank
            : 0f;
    }

    internal static float GetFoxDodgeChance(Entity entity)
    {
        float chance = GetFoxPerkRank(entity, "dodge") * DodgeChancePerRank;
        if (GetFoxPerkRank(entity, "ghoststep") > 0)
        {
            chance += 0.15f;
        }

        return Math.Min(0.75f, chance);
    }

    internal static float GetFoxNaturalDamageResistance(Entity entity)
    {
        return Math.Min(
            0.75f,
            GetFoxPerkRank(entity, "natural-resistance") * NaturalResistancePerRank
                + GetFoxPackTalentRank(entity, "weathered-pack") * PackTalentPercent
        );
    }

    internal static float GetDutyScanIntervalMultiplier(Entity entity, bool finishedProducts)
    {
        float multiplier = GetFoxPackTalentRank(entity, "dutiful-pack") > 0
            ? PackTalentDutyScanMultiplier
            : 1f;
        if (finishedProducts && GetFoxPackTalentRank(entity, "harvest-crew") > 0)
        {
            multiplier *= PackTalentHarvestCadenceMultiplier;
        }
        return multiplier;
    }

    internal static float GetDutySuccessCooldownMultiplier(Entity entity, bool finishedProducts)
    {
        float multiplier = GetFoxPackTalentRank(entity, "work-rhythm") > 0
            ? PackTalentDutyCooldownMultiplier
            : 1f;
        if (finishedProducts && GetFoxPackTalentRank(entity, "harvest-crew") > 0)
        {
            multiplier *= PackTalentHarvestCadenceMultiplier;
        }
        return multiplier;
    }

    internal static long GetDutyFailureRetryMs(Entity entity, long baseDelayMs)
    {
        return GetFoxPackTalentRank(entity, "reliable-routine") > 0
            ? Math.Max(250L, (long)Math.Round(baseDelayMs * PackTalentDutyRetryMultiplier))
            : baseDelayMs;
    }

    internal static float GetDutyMovementSpeed(Entity entity, float baseSpeed)
    {
        return baseSpeed * (1f + GetFoxPackTalentRank(entity, "working-paws") * PackTalentPercent);
    }

    internal static float GetCargoMovementSpeed(Entity entity, float baseSpeed)
    {
        return baseSpeed * (1f + GetFoxPackTalentRank(entity, "couriers-pace") * 0.20f);
    }

    private static float GetFoxRescueWindowHours(Entity entity)
    {
        return MortallyWoundedRescueWindowHours
            + GetFoxPerkRank(entity, "catlike-landing") * StabilizedRescueHoursPerRank;
    }

    private static bool HasBeenOutOfCombatLongEnough(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        long lastCombat = status?.GetLong(LastCombatUtcMsKey, 0) ?? 0;
        return lastCombat > 0 && UtcNowMs() - lastCombat >= SecondsToMilliseconds(CombatRecoveryGraceSeconds);
    }

    private static bool IsDeadlineActive(Entity entity, string key)
    {
        return IsDeadlineActive(entity, key, 0f);
    }

    private static bool IsDeadlineActive(Entity entity, string key, float fallbackDurationSeconds)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null)
        {
            return false;
        }

        long value = status.GetLong(key, 0);
        if (value <= 0)
        {
            return false;
        }

        long deadline = key == LastDamageUtcMsKey && fallbackDurationSeconds > 0f
            ? value + SecondsToMilliseconds(fallbackDurationSeconds)
            : value;
        return deadline > UtcNowMs();
    }

    private static float GetDeadlineRemainingSeconds(ITreeAttribute? status, string key)
    {
        long deadline = status?.GetLong(key, 0) ?? 0;
        return deadline <= 0
            ? 0f
            : Math.Max(0f, (deadline - UtcNowMs()) / 1000f);
    }

    private static float GetFoxMaxHealthBonus(Entity entity)
    {
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        float flatBonus = perks == null ? 0f : FoxPerkCatalog.GetRank(perks, "hardiness") * 5f
            + FoxPerkCatalog.GetRank(perks, "even-more-health") * 2f
            + FoxPerkCatalog.GetRank(perks, "iron-fox") * 10f;
        float baseHealth = entity.GetBehavior<EntityBehaviorHealth>()?.BaseMaxHealth ?? FoxBaseMaxHealth;
        float packHealthPercent = GetFoxPackTalentRank(entity, "hardy-pack") * PackTalentPercent;
        return flatBonus + (baseHealth + flatBonus) * packHealthPercent;
    }

    internal static bool IsPersistentRestMood(Entity entity)
    {
        return GetMood(entity) is "resting" or "rested";
    }

    private void OnFoxSocialRequest(IServerPlayer fromPlayer, FoxSocialRequestPacket packet)
    {
        // Saved/unloaded developer records can return before the live-entity
        // checks below. Authorize developer actions before either branch.
        if (serverApi == null
            || (IsDeveloperAction(packet.Action) && !CanUseDeveloperTools(fromPlayer)))
        {
            return;
        }

        if (packet.Action == FoxSocialRequestAction.CloseView)
        {
            if (socialViewByOwner.TryGetValue(fromPlayer.PlayerUID, out long viewedEntityId)
                && (viewedEntityId == packet.TargetEntityId
                    || IsMatchingDeveloperView(packet.DeveloperFoxId, viewedEntityId, fromPlayer.PlayerUID)))
            {
                socialViewByOwner.Remove(fromPlayer.PlayerUID);
                EndMenuAttention(viewedEntityId);
            }
            return;
        }

        if (packet.Action == FoxSocialRequestAction.ClosePack)
        {
            packViewers.Remove(fromPlayer.PlayerUID);
            return;
        }

        if (packet.Action == FoxSocialRequestAction.ClosePerks)
        {
            if (perkViewByOwner.Remove(fromPlayer.PlayerUID, out long viewedEntityId))
            {
                EndMenuAttention(viewedEntityId);
            }
            return;
        }

        if (packet.TargetEntityId == 0 && IsPackViewAction(packet.Action))
        {
            if (!packViewers.Contains(fromPlayer.PlayerUID)
                || GetActiveCairnPosition(fromPlayer.PlayerUID) == null
                || (IsDeveloperAction(packet.Action) && !CanUseDeveloperTools(fromPlayer)))
            {
                return;
            }

            HandlePackViewAction(fromPlayer, packet);
            return;
        }

        bool developerRecordRequest = IsDeveloperAction(packet.Action)
            && !string.IsNullOrWhiteSpace(packet.DeveloperFoxId);
        FoxPackRecordV2? developerRecord = null;
        Entity? entity;
        if (developerRecordRequest)
        {
            if (packRepository?.TryGetRecord(packet.DeveloperFoxId, out developerRecord) != true
                || developerRecord == null
                || developerRecord.Archived
                || !string.Equals(developerRecord.OwnerUid, fromPlayer.PlayerUID, StringComparison.Ordinal))
            {
                return;
            }

            entity = FindLoadedCompanionForRecovery(packet.DeveloperFoxId);
            if (entity == null)
            {
                if (packet.Action == FoxSocialRequestAction.DeleteAllCompanionsDeveloper)
                {
                    PermanentlyDeleteAllCompanionsDeveloper(fromPlayer);
                }
                else
                {
                    SendDeveloperRecordState(fromPlayer, developerRecord, "That Companion is not loaded on the server yet.");
                }

                return;
            }
        }
        else
        {
            entity = serverApi.World.GetEntityById(packet.TargetEntityId);
            if (entity == null
                || !IsTamedFox(entity)
                || !IsOwner(entity, fromPlayer.PlayerUID)
                || !IsSocialActionInRange(entity, fromPlayer))
            {
                return;
            }
        }

        if (!IsTamedFox(entity)
            || !IsOwner(entity, fromPlayer.PlayerUID)
            || (!developerRecordRequest && !IsSocialActionInRange(entity, fromPlayer)))
        {
            return;
        }

        // Expeditions are organized at the active Pack Cart. A fox can still
        // open the shared roster, cache, and archive, but stale or hand-crafted
        // packets cannot start or alter expedition progression through a fox.
        if (IsCartOnlyPackAction(packet.Action))
        {
            return;
        }

        // This is an owner-requested repair path. It must remain available
        // while the vanilla mortally-wounded behavior is blocking ordinary
        // social actions, so a stale injury marker can be cleared manually.
        if (packet.Action == FoxSocialRequestAction.RebuildFoxDeveloper)
        {
            RebuildCompanionDeveloper(entity, fromPlayer);
            return;
        }

        if (packet.Action == FoxSocialRequestAction.DeleteAllCompanionsDeveloper)
        {
            PermanentlyDeleteAllCompanionsDeveloper(fromPlayer);
            return;
        }

        // Client-side interaction already refuses to open these screens while
        // a fox is downed. Keep the same rule on the server so a stale or
        // hand-crafted packet cannot interact with an incapacitated fox.
        if (IsFoxIncapacitated(entity))
        {
            return;
        }

        if (IsBackpackDeliveryActive(entity)
            && packet.Action is not FoxSocialRequestAction.Open
                and not FoxSocialRequestAction.OpenPack)
        {
            SendState(entity, fromPlayer, "This companion is committed to its backpack delivery.");
            return;
        }

        TryRepairStaleAdultCompanionState(entity);

        if (IsCompanionJuvenile(entity)
            && packet.Action is FoxSocialRequestAction.GenerateDeveloper
                or FoxSocialRequestAction.OpenPerks
                or FoxSocialRequestAction.BuyPerk
                or FoxSocialRequestAction.ResetPerks
                or FoxSocialRequestAction.SetCombatStyle
                or FoxSocialRequestAction.SetRiskTolerance
                or FoxSocialRequestAction.ThreatSurvey
                or FoxSocialRequestAction.AddExperienceDeveloper
                or FoxSocialRequestAction.AdvanceLevelDeveloper)
        {
            SendState(entity, fromPlayer, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        if (IsPackViewAction(packet.Action))
        {
            HandlePackViewAction(fromPlayer, packet);
            return;
        }

        switch (packet.Action)
        {
            case FoxSocialRequestAction.Open:
                socialViewByOwner[fromPlayer.PlayerUID] = entity.EntityId;
                packViewers.Remove(fromPlayer.PlayerUID);
                BeginMenuAttention(entity, fromPlayer.Entity);
                EnsureFoxNumberIfNeeded(entity);
                EnsureFoxPersonality(entity);
                SendState(entity, fromPlayer, "");
                break;
            case FoxSocialRequestAction.Generate:
                GenerateRequest(entity, fromPlayer, string.Empty, developerOverride: false);
                break;
            case FoxSocialRequestAction.GenerateDeveloper:
                GenerateRequest(entity, fromPlayer, packet.RequestedJob, developerOverride: true);
                break;
            case FoxSocialRequestAction.TellBadJokeDeveloper:
                TellBadFoxJoke(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.Cancel:
                CancelRequest(entity, fromPlayer, developerOverride: false);
                break;
            case FoxSocialRequestAction.CancelDeveloper:
                CancelRequest(entity, fromPlayer, developerOverride: true);
                break;
            case FoxSocialRequestAction.SetMoodDeveloper:
                SetMoodDeveloper(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.ResetCooldownsDeveloper:
                ResetCooldownsDeveloper(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.AddFoxPointDeveloper:
                AdjustFoxPointsDeveloper(entity, fromPlayer, 1);
                break;
            case FoxSocialRequestAction.RemoveFoxPointDeveloper:
                AdjustFoxPointsDeveloper(entity, fromPlayer, -1);
                break;
            case FoxSocialRequestAction.AddPackPointDeveloper:
                AdjustPackPointsDeveloper(entity, fromPlayer, 1);
                break;
            case FoxSocialRequestAction.RemovePackPointDeveloper:
                AdjustPackPointsDeveloper(entity, fromPlayer, -1);
                break;
            case FoxSocialRequestAction.SetHealthOneDeveloper:
                SetHealthOneDeveloper(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.AddExperienceDeveloper:
                AddExperienceDeveloper(entity, fromPlayer, false);
                break;
            case FoxSocialRequestAction.AdvanceLevelDeveloper:
                AddExperienceDeveloper(entity, fromPlayer, true);
                break;
            case FoxSocialRequestAction.OpenPack:
                if (socialViewByOwner.Remove(fromPlayer.PlayerUID, out long perksSourceEntityId))
                {
                    EndMenuAttention(perksSourceEntityId);
                }
                packViewers.Add(fromPlayer.PlayerUID);
                MarkBramblePackExplored(fromPlayer.PlayerUID);
                EnsureFoxNumberIfNeeded(entity);
                EnsureFoxPersonality(entity);
                SendPackState(fromPlayer);
                break;
            case FoxSocialRequestAction.OpenPerks:
                if (IsCompanionJuvenile(entity))
                {
                    SendState(entity, fromPlayer, Lang.Get("feralkinshipcompanions:child-talents-banked"));
                    break;
                }
                if (socialViewByOwner.Remove(fromPlayer.PlayerUID, out long socialEntityId))
                {
                    EndMenuAttention(socialEntityId);
                }
                packViewers.Remove(fromPlayer.PlayerUID);
                perkViewByOwner[fromPlayer.PlayerUID] = entity.EntityId;
                MarkBrambleTalentsExplored(fromPlayer.PlayerUID);
                BeginMenuAttention(entity, fromPlayer.Entity);
                EnsureFoxNumberIfNeeded(entity);
                EnsureFoxPersonality(entity);
                SendPerkState(entity, fromPlayer, string.Empty);
                break;
            case FoxSocialRequestAction.BuyPerk:
                BuyPerk(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.ResetPerks:
                ResetPerks(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.ThreatSurvey:
                SendThreatSurvey(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.RetrieveHeldItem:
                RetrieveFoxStorageCargo(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.SetActivity:
                SetCompanionActivity(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetFollowDistance:
                SetCompanionFollowDistance(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetCombatStyle:
                SetCompanionCombatStyle(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetRiskTolerance:
                SetCompanionRiskTolerance(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetGroundCleanup:
                SetCompanionGroundCleanup(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetDutySubtask:
                SetCompanionDutySubtask(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetMowLawn:
                SetCompanionMowLawn(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetFinishedProducts:
                SetCompanionFinishedProducts(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetFlowerRemoval:
                SetCompanionFlowerRemoval(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetSnowShoveling:
                SetCompanionSnowShoveling(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetCharcoalShoveling:
                SetCompanionCharcoalShoveling(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetSnowballCollection:
                SetCompanionSnowballCollection(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.SetGeneralStorageSorting:
                SetCompanionGeneralStorageSorting(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.ToggleBreeding:
                ToggleCompanionBreeding(entity, fromPlayer);
                break;
            case FoxSocialRequestAction.Rename:
                RenameCompanion(entity, fromPlayer, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.RequestNameSuggestion:
                RequestCompanionNameSuggestion(entity, fromPlayer);
                break;
        }
    }

    private void TellBadFoxJoke(Entity entity, IServerPlayer owner)
    {
        if (StfuModeEnabled) return;

        string joke = Math.Abs(entity.EntityId % 7) switch
        {
            0 => "Why did the fox cross the road? To prove it wasn't chicken.",
            1 => "What do you call an owl who does magic? Hoo-dini.",
            2 => "Why was the cow such a good musician? It had perfect moosic.",
            3 => "What do you call a bear with no teeth? A gummy bear.",
            4 => "Why don't sheep use computers? They fear the baaa-d sectors.",
            5 => "What do you call an alligator in a vest? An investigator.",
            _ => "What did the buffalo say to his son when he left? Bison.",
        };
        CompanionDialogueContext context = CreateDialogueContext(entity, CompanionDialogueEvent.DebugFoxJoke);
        if (dialogueService?.TryEmit(
                entity,
                owner,
                CompanionDialogueEvent.DebugFoxJoke,
                context,
                joke,
                fallbackPriority: 5,
                fallbackSemanticGroup: "debug-joke") == true)
        {
            SendState(entity, owner, "The companion has shared an animal joke.");
        }
    }

    private void SendDialoguePacket(
        IServerPlayer owner,
        Entity entity,
        CompanionDialogueSelection selection)
    {
        serverChannel?.SendPacket(new CompanionThoughtPacket
        {
            TargetEntityId = entity.EntityId,
            Text = selection.Text,
            DurationMs = 5000,
            Priority = selection.Priority,
            ExpiresAtUtcMs = selection.ExpiresAtUtcMs,
            SemanticGroup = selection.SemanticGroup,
            FollowOwnerX = owner.Entity.Pos.X,
            FollowOwnerY = owner.Entity.Pos.Y,
            FollowOwnerZ = owner.Entity.Pos.Z,
            FollowOwnerDimension = owner.Entity.Pos.Dimension
        }, owner);
    }

    private CompanionDialogueContext CreateDialogueContext(
        Entity entity,
        string eventId,
        IReadOnlyDictionary<string, string>? additionalFacts = null)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string foxId = status?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        string personality = status?.GetString(PersonalityKey, string.Empty) ?? string.Empty;
        CompanionSpeciesProfile species = GetCompanionSpecies(entity);
        Dictionary<string, string> facts = new(StringComparer.OrdinalIgnoreCase)
        {
            ["companion"] = GetFoxDisplayName(entity),
            ["personality"] = personality,
            ["mood"] = status == null ? string.Empty : GetMood(entity)
        };
        if (additionalFacts != null)
        {
            foreach ((string key, string value) in additionalFacts)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;
                facts[key] = value ?? string.Empty;
            }
        }

        return new CompanionDialogueContext
        {
            EventId = eventId,
            FoxId = foxId,
            Personality = personality,
            SpeciesId = species.Id,
            SpeciesSource = entity.Code?.Domain ?? string.Empty,
            AgeStage = IsCompanionJuvenile(entity) ? "juvenile" : "adult",
            Mood = status == null ? string.Empty : GetMood(entity),
            Facts = facts
        };
    }

    private void RegisterDialogueCommands(ICoreServerAPI api)
    {
        api.ChatCommands.Create("companiondialoguevalidate")
            .WithDescription("Validate the loaded Feral Kinship dialogue catalog.")
            .RequiresPlayer()
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(args => TextCommandResult.Success(
                StfuModeEnabled
                    ? "STFU mode is enabled; dialogue catalogs were not loaded."
                    : dialogueService?.Validate() ?? "Dialogue service is unavailable."));
    }

    /// <summary>
    /// Sends a short thought through the owner-visible speech-bubble channel.
    /// First-sighting and other companion events can reuse this without
    /// knowing anything about the client renderer.
    /// </summary>
    internal void SendCompanionThought(
        Entity entity,
        IServerPlayer owner,
        string text,
        int durationMs = 5000)
    {
        if (StfuModeEnabled || serverChannel == null || string.IsNullOrWhiteSpace(text)) return;

        serverChannel.SendPacket(new CompanionThoughtPacket
        {
            TargetEntityId = entity.EntityId,
            Text = text.Trim(),
            DurationMs = durationMs
        }, owner);
    }

    private void RequestCompanionNameSuggestion(Entity entity, IServerPlayer owner)
    {
        EnsureFoxPersonality(entity);
        string suggestedName = GenerateCompanionName(
            entity,
            GetFoxDisplayName(entity),
            GetUsedCompanionNames(entity));
        AssignCompanionName(entity, suggestedName);

        RegisterFoxInPack(entity);
        packRepository?.Save();
        SendState(entity, owner, $"A fresh name suggestion is ready: {suggestedName}.");
        serverChannel?.SendPacket(new CompanionNameSuggestionPacket
        {
            TargetEntityId = entity.EntityId,
            SuggestedName = suggestedName,
            IsNewborn = false
        }, owner);
    }

    private void RenameCompanion(Entity entity, IServerPlayer owner, string requestedName)
    {
        string name = (requestedName ?? string.Empty).Trim();
        if (name.Length > 32 || name.Any(char.IsControl))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "That name is too long or contains unsupported characters.");
            return;
        }

        SetCompanionName(entity, name);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(AutoNameHandledKey, true);
        MarkSocialStateDirty(entity);

        RegisterFoxInPack(entity);
        packRepository?.Save();
        SendState(entity, owner, string.IsNullOrWhiteSpace(name)
            ? "Companion name cleared."
            : $"Companion renamed to {name}.");
    }

    private void SetCompanionActivity(Entity entity, IServerPlayer owner, string requestedMode, bool showFeedback = true)
    {
        if (!CompanionActivityMode.IsValid(requestedMode))
        {
            if (showFeedback)
            {
                SendOwnerSound(owner, CompanionSoundCue.Rejected);
                SendState(entity, owner, "Unknown activity command.");
            }
            return;
        }
        string mode = CompanionActivityMode.Normalize(requestedMode);
        packCartCallTargets.Remove(entity.EntityId);
        ClearAutomaticRetreat(entity, restorePreviousActivity: false);
        CancelTargetedAttack(entity);
        if (mode == CompanionActivityMode.ReturnHome)
        {
            DisengageHostileTargets(entity);
        }
        SyncBrambleActivityCommand(entity, owner, mode);
        SetCompanionActivityState(entity, mode);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (showFeedback)
        {
            EmitCommandAcknowledgement(entity, owner, mode switch
            {
                CompanionActivityMode.Follow => "command.follow",
                CompanionActivityMode.Rest => "command.rest",
                CompanionActivityMode.ReturnHome => "command.return_home",
                _ => "command.at_ease"
            });
            PlayActivityCommandSound(owner, mode);
            SendState(entity, owner, $"Activity set to {CompanionActivityMode.DisplayName(mode)}.");
        }
    }

    private void SetCompanionFollowDistance(Entity entity, IServerPlayer owner, string requestedDistance)
    {
        if (!CompanionFollowDistance.IsValid(requestedDistance))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown follow distance.");
            return;
        }
        string distance = CompanionFollowDistance.Normalize(requestedDistance);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetString(FollowDistanceKey, distance);
        MarkSocialStateDirty(entity);
        if (entity is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
        }
        RegisterFoxInPack(entity);
        packRepository?.Save();
        EmitCommandAcknowledgement(entity, owner, "command.follow_distance." + distance);
        PlayControlSetSound(owner);
        SendState(entity, owner, $"Follow distance set to {CompanionFollowDistance.DisplayName(distance)}.");
    }

    private void SetCompanionCombatStyle(Entity entity, IServerPlayer owner, string requestedStyle, bool showFeedback = true)
    {
        if (!CompanionCombatStyle.IsValid(requestedStyle))
        {
            if (showFeedback)
            {
                SendOwnerSound(owner, CompanionSoundCue.Rejected);
                SendState(entity, owner, "Unknown combat style.");
            }
            return;
        }
        string style = CompanionCombatStyle.Normalize(requestedStyle);
        ClearAutomaticRetreat(entity);
        CancelTargetedAttack(entity);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetString(CombatStyleKey, style);
        MarkSocialStateDirty(entity);
        if (entity is EntityAgent agent)
        {
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.Controls.StopAllMovement();
        }
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (showFeedback)
        {
            EmitCommandAcknowledgement(entity, owner, "command.combat." + style);
            PlayControlSetSound(owner);
            SendState(entity, owner, $"Combat style set to {CompanionCombatStyle.DisplayName(style)}.");
        }
    }

    private void CancelTargetedAttack(Entity entity)
    {
        if (GetDomesticationStatus(entity)?.GetBool(TargetedAttackActiveKey, false) == true)
        {
            RestoreTargetedAttack(entity);
        }
        else
        {
            companionAggressiveTargets.Remove(entity.EntityId);
        }
    }

    private void SetCompanionRiskTolerance(Entity entity, IServerPlayer owner, string requestedTolerance, bool showFeedback = true)
    {
        if (!CompanionRiskTolerance.IsValid(requestedTolerance))
        {
            if (showFeedback)
            {
                SendOwnerSound(owner, CompanionSoundCue.Rejected);
                SendState(entity, owner, "Unknown risk tolerance.");
            }
            return;
        }

        string tolerance = CompanionRiskTolerance.Normalize(requestedTolerance);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetString(RiskToleranceKey, tolerance);
        UpdateAutomaticRetreatRecovery(entity, status);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (showFeedback)
        {
            EmitCommandAcknowledgement(entity, owner, "command.risk." + tolerance);
            PlayControlSetSound(owner);
            SendState(entity, owner, $"Risk tolerance set to {CompanionRiskTolerance.DisplayName(tolerance)}.");
        }
    }

    private void SetCompanionGroundCleanup(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        bool enabled;
        if (string.Equals(requestedState, "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
        }
        else if (string.Equals(requestedState, "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
        }
        else
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(GroundCleanupEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.ground_cleanup.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Ground Cleanup enabled. This companion will collect dropped items while At Ease."
            : "Ground Cleanup disabled. This companion will leave dropped items alone while idle.");
    }

    private void SetCompanionDutySubtask(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        string[] parts = (requestedState ?? string.Empty).Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryParseDutyState(parts[1], out bool enabled)
            || !CompanionDuty.IsSubtask(parts[0]))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty option.");
            return;
        }

        SetCompanionDutyOption(entity, parts[0], enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, GetDutyCommandEvent(parts[0]));
        PlayControlSetSound(owner);
        SendState(entity, owner, $"{CompanionDuty.DisplaySubtaskName(parts[0])} {(enabled ? "enabled" : "disabled")}.");
    }

    private static void SetCompanionDutyOption(Entity entity, string option, bool enabled)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(CompanionDuty.OptionKey(option), enabled);
    }

    private void SetCompanionMowLawn(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        bool enabled;
        if (string.Equals(requestedState, "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
        }
        else if (string.Equals(requestedState, "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
        }
        else
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(MowLawnEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.mowing.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Mow the Lawn enabled. This companion will cut ordinary grass while At Ease."
            : "Mow the Lawn disabled. This companion will leave ordinary grass alone while idle.");
    }

    private void SetCompanionFinishedProducts(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        bool enabled;
        if (string.Equals(requestedState, "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
        }
        else if (string.Equals(requestedState, "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
        }
        else
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(FinishedProductsEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.crops.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Gather Finished Products enabled. This companion will collect mature crops, ripe berries, and ready mushrooms while At Ease."
            : "Gather Finished Products disabled. This companion will leave harvestable products alone while idle.");
    }

    private void SetCompanionFlowerRemoval(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        if (!TryParseDutyState(requestedState, out bool enabled))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(FlowerRemovalEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.flower_removal.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Flower Removal enabled. This companion will remove naturally spawned flowers while At Ease."
            : "Flower Removal disabled. This companion will leave naturally spawned flowers alone while idle.");
    }

    private void SetCompanionSnowShoveling(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        if (!TryParseDutyState(requestedState, out bool enabled))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(SnowShovelingEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.snow_shoveling.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Snow Shoveling enabled. This companion will clear thin ground snow while At Ease."
            : "Snow Shoveling disabled. This companion will leave thin ground snow alone while idle.");
    }

    private void SetCompanionCharcoalShoveling(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        if (!TryParseDutyState(requestedState, out bool enabled))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown companion duty setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(CharcoalShovelingEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.snow_shoveling.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Charcoal Shoveling enabled. This companion will clear completed charcoal piles into configured storage while At Ease."
            : "Charcoal Shoveling disabled. This companion will leave charcoal piles alone while idle.");
    }

    private void SetCompanionSnowballCollection(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }

        if (!TryParseDutyState(requestedState, out bool enabled))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown snowball collection setting.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetBool(SnowballCollectionEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.snowball_collection.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "Snowball collection enabled. Snow shoveling will carry the snowballs to storage."
            : "Snowball collection disabled. Snow shoveling will clear snow without creating snowballs.");
    }

    private void SetCompanionGeneralStorageSorting(Entity entity, IServerPlayer owner, string requestedState)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-action-locked"));
            return;
        }
        if (!TryParseDutyState(requestedState, out bool enabled))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, "Unknown storage sorting setting.");
            return;
        }

        GetDomesticationStatus(entity, true)!.SetBool(GeneralStorageSortingEnabledKey, enabled);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        if (enabled) EmitCommandAcknowledgement(entity, owner, "command.duty.general_storage_sorting.enabled");
        PlayControlSetSound(owner);
        SendState(entity, owner, enabled
            ? "General storage sorting enabled. This companion may move stored items into matching specific boxes."
            : "General storage sorting disabled. New pickups still use configured routing, but this companion will not rearrange stored items.");
    }

    private static bool TryParseDutyState(string requestedState, out bool enabled)
    {
        if (string.Equals(requestedState, "on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }
        if (string.Equals(requestedState, "off", StringComparison.OrdinalIgnoreCase))
        {
            enabled = false;
            return true;
        }

        enabled = false;
        return false;
    }

    private void SendThreatSurvey(Entity fox, IServerPlayer owner)
    {
        if (GetFoxPerkRank(fox, "monster-knowledge") <= 0 || serverApi == null)
        {
            SendState(fox, owner, "This companion has not learned Monster Knowledge.");
            return;
        }

        float range = PredatorSearchRange + 16f;
        Entity[] threats = serverApi.World.GetEntitiesAround(fox.Pos.XYZ, range, range)
            .Where(IsPredator)
            .OrderBy(entity => entity.Pos.SquareDistanceTo(fox.Pos))
            .Take(4)
            .ToArray();
        if (threats.Length == 0)
        {
            SendState(fox, owner, $"Threat survey: no known large predators within {range:0} blocks.");
            return;
        }

        string report = string.Join("; ", threats.Select(threat =>
        {
            GetHealth(threat, out float health, out float maxHealth);
            double distance = Math.Sqrt(threat.Pos.SquareDistanceTo(fox.Pos));
            string name = (threat.Code?.Path ?? "unknown creature").Replace('-', ' ');
            return $"{name} at {distance:0}m ({health:0.#}/{maxHealth:0.#} health)";
        }));
        SendState(fox, owner, "Threat survey: " + report + ".");
    }

    private static bool IsPackViewAction(int action)
    {
        return action == FoxSocialRequestAction.LocatePackFox
            || action == FoxSocialRequestAction.CallPackFoxToCart
            || action == FoxSocialRequestAction.StartPackExpedition
            || action == FoxSocialRequestAction.ClaimPackLoot
            || action == FoxSocialRequestAction.ClaimSelectedPackLoot
            || action == FoxSocialRequestAction.SendPackLootToStorage
            || action == FoxSocialRequestAction.ClaimRecruitment
            || action == FoxSocialRequestAction.AddPackLootDeveloper
            || action == FoxSocialRequestAction.ForceFoxMiaDeveloper
            || action == FoxSocialRequestAction.UnlockExpeditionRoute
            || action == FoxSocialRequestAction.UnlockPackTalent
            || action == FoxSocialRequestAction.ArchivePackFox
            || action == FoxSocialRequestAction.UnarchivePackFox
            || action == FoxSocialRequestAction.DeleteExpeditionReport
            || action == FoxSocialRequestAction.AbandonScavengeSite;
    }

    private static bool IsCartOnlyPackAction(int action)
    {
        return action == FoxSocialRequestAction.StartPackExpedition
            || action == FoxSocialRequestAction.AddPackLootDeveloper
            || action == FoxSocialRequestAction.ForceFoxMiaDeveloper
            || action == FoxSocialRequestAction.UnlockExpeditionRoute
            || action == FoxSocialRequestAction.UnlockPackTalent
            || action == FoxSocialRequestAction.SendPackLootToStorage
            || action == FoxSocialRequestAction.DeleteExpeditionReport;
    }

    private void HandlePackViewAction(IServerPlayer owner, FoxSocialRequestPacket packet)
    {
        switch (packet.Action)
        {
            case FoxSocialRequestAction.LocatePackFox:
                LocatePackFox(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.CallPackFoxToCart:
                CallPackFoxToCart(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.StartPackExpedition:
                StartPackExpedition(
                    owner,
                    packet.RequestedJob,
                    packet.SelectedFoxIds,
                    packet.SelectedMissingFoxId,
                    packet.PrepareExpedition,
                    packet.ScavengeSiteId,
                    packet.ScavengeFocus,
                    packet.ScoutDuration
                );
                break;
            case FoxSocialRequestAction.AbandonScavengeSite:
                AbandonScavengeSite(owner, packet.ScavengeSiteId);
                break;
            case FoxSocialRequestAction.UnlockExpeditionRoute:
                UnlockExpeditionRoute(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.UnlockPackTalent:
                UnlockPackTalent(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.ClaimPackLoot:
                ClaimPackLoot(owner);
                break;
            case FoxSocialRequestAction.ClaimSelectedPackLoot:
                ClaimPackLoot(owner, packet.SelectedFoxIds);
                break;
            case FoxSocialRequestAction.SendPackLootToStorage:
                SendPackLootToStorage(owner);
                break;
            case FoxSocialRequestAction.ClaimRecruitment:
                ClaimRecruitment(owner);
                break;
            case FoxSocialRequestAction.AddPackLootDeveloper:
                AddPackLootDeveloper(owner, packet.RequestedJob, packet.SelectedFoxIds);
                break;
            case FoxSocialRequestAction.ForceFoxMiaDeveloper:
                ForceFoxMiaDeveloper(owner, packet.SelectedFoxIds);
                break;
            case FoxSocialRequestAction.ArchivePackFox:
                ArchivePackFox(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.UnarchivePackFox:
                UnarchivePackFox(owner, packet.RequestedJob);
                break;
            case FoxSocialRequestAction.DeleteExpeditionReport:
                DeleteExpeditionReport(owner, packet.ExpeditionId);
                break;
        }
    }

    private void CallPackFoxToCart(IServerPlayer owner, string foxId)
    {
        if (string.IsNullOrWhiteSpace(foxId)
            || packRepository?.TryGetRecord(foxId, out FoxPackRecordV2? record) != true
            || record == null || record.Archived
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal))
        {
            SendPackState(owner, "Choose an active companion to call.");
            return;
        }
        Entity? fox = FindLoadedCompanionByFoxId(foxId);
        if (fox == null || !fox.Alive || !IsTamedFox(fox) || IsFoxAwayFromWorld(fox)
            || !TryGetPackCartIdleTarget(fox, out Vec3d? cartTarget) || cartTarget == null)
        {
            SendPackState(owner, "That companion and the Pack Cart must both be loaded in the same dimension.");
            return;
        }
        BlockPos? cartPos = GetActiveCairnPosition(owner.PlayerUID);
        if (cartPos == null)
        {
            SendPackState(owner, "The Pack Cart is not available.");
            return;
        }
        Vec3d destination = FindSafeEntityPosition(cartPos, fox.Properties, fox, 2, 2) ?? cartTarget;
        RequestCompanionHomeTravel(fox);
        packCartCallTargets[fox.EntityId] = destination;
        packRepository.Save();
        SendPackState(owner, $"Called {GetFoxDisplayName(fox)} to the Pack Cart.");
    }

    private static bool IsDeveloperAction(int action)
    {
        return action == FoxSocialRequestAction.GenerateDeveloper
            || action == FoxSocialRequestAction.TellBadJokeDeveloper
            || action == FoxSocialRequestAction.CancelDeveloper
            || action == FoxSocialRequestAction.SetMoodDeveloper
            || action == FoxSocialRequestAction.ResetCooldownsDeveloper
            || action == FoxSocialRequestAction.AddFoxPointDeveloper
            || action == FoxSocialRequestAction.RemoveFoxPointDeveloper
            || action == FoxSocialRequestAction.AddPackPointDeveloper
            || action == FoxSocialRequestAction.RemovePackPointDeveloper
            || action == FoxSocialRequestAction.SetHealthOneDeveloper
            || action == FoxSocialRequestAction.AddExperienceDeveloper
            || action == FoxSocialRequestAction.AdvanceLevelDeveloper
            || action == FoxSocialRequestAction.RebuildFoxDeveloper
            || action == FoxSocialRequestAction.DeleteAllCompanionsDeveloper
            || action == FoxSocialRequestAction.AddPackLootDeveloper
            || action == FoxSocialRequestAction.ForceFoxMiaDeveloper;
    }

    private bool IsMatchingDeveloperView(string foxId, long viewedEntityId, string ownerUid)
    {
        if (string.IsNullOrWhiteSpace(foxId)
            || packRepository?.TryGetRecord(foxId, out FoxPackRecordV2? record) != true
            || record == null
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            return false;
        }

        if (record.EntityId == viewedEntityId)
        {
            return true;
        }

        return FindLoadedCompanionForRecovery(foxId)?.EntityId == viewedEntityId;
    }

    private void OnCompanionRecoveryRequest(
        IServerPlayer fromPlayer,
        CompanionRecoveryRequestPacket packet)
    {
        if (serverApi == null
            || packRepository?.Loaded != true
            || !CanUseDeveloperTools(fromPlayer))
        {
            return;
        }

        // Inspection never grants recovery authority over someone else's
        // records. Check ownership before recovery can refresh shared state.
        if (packet.Action is CompanionRecoveryRequestPacket.Recover
                or CompanionRecoveryRequestPacket.NuclearRebuild
                or CompanionRecoveryRequestPacket.Teleport
                or CompanionRecoveryRequestPacket.OpenDeveloper
            && !string.IsNullOrWhiteSpace(packet.FoxId)
            && (!packRepository.TryGetRecord(packet.FoxId, out FoxPackRecordV2? requestedRecord)
                || requestedRecord == null
                || !string.Equals(requestedRecord.OwnerUid, fromPlayer.PlayerUID, StringComparison.Ordinal)))
        {
            serverChannel?.SendPacket(new CompanionRecoveryStatePacket
            {
                Message = "That companion is not in your pack ledger."
            }, fromPlayer);
            return;
        }

        if (packet.Action == CompanionRecoveryRequestPacket.OpenDeveloper
            && !string.IsNullOrWhiteSpace(packet.FoxId))
        {
            OpenDeveloperCompanion(fromPlayer, packet.FoxId);
            return;
        }

        if (packet.Action is CompanionRecoveryRequestPacket.RerollScavengeSites
            or CompanionRecoveryRequestPacket.RerollScavengeSitesSurveyed)
        {
            bool surveyed = packet.Action == CompanionRecoveryRequestPacket.RerollScavengeSitesSurveyed;
            if (!packRepository.TryRerollScavengeSites(
                    fromPlayer.PlayerUID, serverApi.World.Rand, surveyed))
            {
                SendCompanionRecoveryState(fromPlayer,
                    "Finish active Scout and Scavenge parties before rerolling sites.");
                return;
            }
            packRepository.Save();
            SendCompanionRecoveryState(fromPlayer,
                surveyed
                    ? "Replaced your leads with three fully surveyed Scavenge sites."
                    : "Replaced your leads with three unexplored Scavenge sites.");
            return;
        }

        if (packet.Action == CompanionRecoveryRequestPacket.Recover
            && !string.IsNullOrWhiteSpace(packet.FoxId))
        {
            RecoverCompanionDeveloper(fromPlayer, packet.FoxId);
            SendCompanionRecoveryState(fromPlayer);
            return;
        }

        if (packet.Action == CompanionRecoveryRequestPacket.NuclearRebuild
            && !string.IsNullOrWhiteSpace(packet.FoxId))
        {
            bool rebuilt = NuclearRebuildCompanionDeveloper(fromPlayer, packet.FoxId);
            SendCompanionRecoveryState(fromPlayer);
            if (rebuilt)
            {
                Entity? rebuiltEntity = FindLoadedCompanionForRecovery(packet.FoxId);
                if (rebuiltEntity != null)
                {
                    SendState(rebuiltEntity, fromPlayer, "Rebuild complete.");
                }
            }
            return;
        }

        if (packet.Action == CompanionRecoveryRequestPacket.Teleport
            && !string.IsNullOrWhiteSpace(packet.FoxId))
        {
            TeleportCompanionDeveloper(fromPlayer, packet.FoxId);
            SendCompanionRecoveryState(fromPlayer);
            return;
        }

        if (packet.Action == CompanionRecoveryRequestPacket.RecoverAll)
        {
            int recovered = 0;
            foreach (FoxPackRecordV2 record in packRepository.GetRecordsForOwner(fromPlayer.PlayerUID))
            {
                if (RecoverCompanionDeveloper(fromPlayer, record.FoxId, sendResult: false))
                {
                    recovered++;
                }
            }

            SendCompanionRecoveryState(
                fromPlayer,
                recovered > 0
                    ? $"Recovered {recovered} companion{(recovered == 1 ? string.Empty : "s")}."
                    : "No companion could be recovered.");
            return;
        }

        SendCompanionRecoveryState(fromPlayer);
    }

    private void SendCompanionRecoveryState(IServerPlayer owner, string message = "")
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        RefreshLoadedFoxPackRecords();
        List<CompanionRecoveryEntryPacket> entries = new();
        foreach (FoxPackRecordV2 record in packRepository.GetRecordsForOwner(owner.PlayerUID))
        {
            Entity? loaded = FindLoadedCompanionForRecovery(record.FoxId);
            bool awaitingSafeReturn = !string.IsNullOrWhiteSpace(record.PendingReturnStatus)
                || string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase);
            bool activeExpeditionMember = !awaitingSafeReturn
                && packRepository.GetExpeditionForMember(owner.PlayerUID, record.FoxId) != null;
            bool ledgerRecoveryAvailable = CanRestoreFromLedger(record, out string typeReason);
            string blockedReason = string.Empty;
            if (loaded != null)
            {
                blockedReason = "Already present in the world";
            }
            else if (pendingRecoveryChecks.Contains(record.FoxId)
                || pendingAdminCompanionTeleports.Contains(owner.PlayerUID + ":" + record.FoxId))
            {
                blockedReason = "A saved-location lookup is still in progress";
            }
            else if (activeExpeditionMember)
            {
                blockedReason = "Still assigned to an active expedition";
            }
            else if (owner.Entity == null)
            {
                blockedReason = "Player entity is not available";
            }
            else if (!ledgerRecoveryAvailable)
            {
                blockedReason = typeReason;
            }
            else if (string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase))
            {
                blockedReason = $"Ledger status is {record.Status}";
            }

            // NUCLEAR is the escape hatch for exactly the states that make
            // ordinary recovery unavailable: active expeditions, terminal
            // ledger statuses, missing world entities, and stale runtime
            // state. Keep the button available for every real ledger record;
            // the server-side action still validates ownership and reports
            // genuinely impossible materialization failures.
            bool canNuclearRebuild = !string.IsNullOrWhiteSpace(record.FoxId)
                && (record.DurableStateInitialized || loaded != null);

            entries.Add(new CompanionRecoveryEntryPacket
            {
                FoxId = record.FoxId,
                Number = record.Number,
                Name = string.IsNullOrWhiteSpace(record.Name) ? "Unnamed companion" : record.Name,
                SpeciesDisplayName = GetCompanionSpeciesDisplayName(record),
                Status = record.Status,
                RecoveryStatus = record.RecoveryStatus,
                EntityLoaded = loaded != null,
                CanRecover = string.IsNullOrWhiteSpace(blockedReason),
                CanNuclearRebuild = canNuclearRebuild,
                BlockedReason = blockedReason,
                EntityCode = record.EntityCode,
                EntityId = record.EntityId
            });
        }

        serverChannel?.SendPacket(new CompanionRecoveryStatePacket
        {
            Records = entries,
            Message = message
        }, owner);
    }

    private static bool CanUseDeveloperTools(IServerPlayer player)
    {
        return player.WorldData.CurrentGameMode == EnumGameMode.Creative
            || player.HasPrivilege(Privilege.controlserver)
            || player.HasPrivilege(Privilege.root);
    }

    private static bool IsSocialActionInRange(Entity entity, IServerPlayer player)
    {
        return player.Entity != null
            && player.Entity.Pos.Dimension == entity.Pos.Dimension
            && player.Entity.Pos.SquareDistanceTo(entity.Pos)
                <= SocialActionRangeSquared * Math.Pow(1d + GetFoxPackTalentRank(entity, "far-call") * 0.25d, 2d);
    }

    private void GenerateRequest(Entity entity, IServerPlayer owner, string requestedJob, bool developerOverride)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (!string.IsNullOrEmpty(status.GetString(ActiveRequestKey)))
        {
            SendState(entity, owner, "Cancel the active request before generating another.");
            return;
        }

        float requestCooldown = GetRemainingSeconds(
            status,
            RequestCooldownEndsUtcMsKey,
            RequestCooldownRemainingKey
        );
        if (!developerOverride && requestCooldown > 0f)
        {
            SendState(entity, owner, $"Another request will be available in {FormatCooldown(requestCooldown)}.");
            return;
        }

        int generated = status.GetInt(RequestsGeneratedKey, 0);
        string request;

        if (string.IsNullOrEmpty(requestedJob))
        {
            request = SelectRandomRequest(entity, owner, out long predatorTargetId);
            status.SetLong(PredatorTargetKey, predatorTargetId);
            status.SetString(PredatorTargetClueKey, request == FoxRequestType.Predator
                ? BuildPredatorTargetClue(entity, serverApi?.World.GetEntityById(predatorTargetId))
                : string.Empty);
            status.SetBool(PredatorTargetKilledKey, false);
        }
        else if (IsCompanionJuvenile(entity))
        {
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-request-unsafe"));
            return;
        }
        else if (string.Equals(requestedJob, FoxRequestType.Predator, StringComparison.OrdinalIgnoreCase))
        {
            Entity? predator = FindNearestPredator(entity);
            if (predator == null)
            {
                SendState(entity, owner, "No nearby predator found for that test job.");
                return;
            }

            request = FoxRequestType.Predator;
            status.SetLong(PredatorTargetKey, predator.EntityId);
            status.SetString(PredatorTargetClueKey, BuildPredatorTargetClue(entity, predator));
            status.SetBool(PredatorTargetKilledKey, false);
        }
        else if (FoxRequestCatalog.TryGet(requestedJob, out _))
        {
            request = FoxRequestCatalog.NormalizeId(requestedJob.ToLowerInvariant());
            status.SetLong(PredatorTargetKey, 0);
            status.SetString(PredatorTargetClueKey, string.Empty);
            status.SetBool(PredatorTargetKilledKey, false);
        }
        else
        {
            SendState(entity, owner, "Unknown test job.");
            return;
        }

        if (!TryInitializeItemRequest(entity, owner, request, status, out string itemMessage))
        {
            SendState(entity, owner, itemMessage);
            return;
        }

        status.SetString(ActiveRequestKey, request);
        status.SetFloat(RequestProgressKey, 0f);
        status.SetLong(RequestQualifiedSinceUtcMsKey, 0);
        status.SetLong(RequestExpiresUtcMsKey, UtcNowMs() + SecondsToMilliseconds(ActiveRequestLifetimeSeconds));
        status.SetBool(RequestIsDeveloperKey, developerOverride);
        status.SetDouble(RequestStartXKey, entity.Pos.X);
        status.SetDouble(RequestStartYKey, entity.Pos.Y);
        status.SetDouble(RequestStartZKey, entity.Pos.Z);
        status.SetDouble(RequestStartDayKey, Math.Floor(entity.World.Calendar.TotalDays));
        if (!developerOverride)
        {
            status.SetInt(RequestsGeneratedKey, generated + 1);
        }
        MarkSocialStateDirty(entity);
        string message = request == FoxRequestType.Predator
            ? GetRequestLabel(entity, request, status.GetString(PersonalityKey, string.Empty))
            : string.IsNullOrEmpty(itemMessage) ? "Request generated." : itemMessage;
        if (developerOverride)
        {
            message = $"Developer test generated; completion awards no points. {message}";
        }
        RegisterFoxInPack(entity);
        packRepository?.Save();
        SendState(entity, owner, message);
    }

    private string SelectRandomRequest(Entity entity, IServerPlayer owner, out long predatorTargetId)
    {
        var candidates = new List<(string Request, float Weight)>();
        predatorTargetId = 0;

        if (IsCompanionJuvenile(entity))
        {
            AddRequestCandidate(candidates, FoxRequestType.StayClose, 20f);
            AddRequestCandidate(candidates, FoxRequestType.NearOwnerStill, 15f);
            AddRequestCandidate(candidates, FoxRequestType.NearTamedAnimal, 10f);
            AddRequestCandidate(candidates, FoxRequestType.AnotherAnimal, 8f);
            AddRequestCandidate(candidates, FoxRequestType.Inside, 8f);
            AddRequestCandidate(candidates, FoxRequestType.NearLight, 6f);
            AddRequestCandidate(candidates, FoxRequestType.LargeTree, 4f);
            ApplyRequestWeightModifiers(entity, candidates);
            return SelectWeightedRequest(entity, candidates);
        }

        // These remain the background social layer. Contextual requests below
        // add weight rather than replacing this baseline entirely.
        AddRequestCandidate(candidates, FoxRequestType.StayClose, 14f);
        AddRequestCandidate(candidates, FoxRequestType.StayAway, 14f);
        AddRequestCandidate(candidates, FoxRequestType.NearOwnerStill, 10f);
        AddRequestCandidate(candidates, FoxRequestType.TravelDistance, 8f);
        AddRequestCandidate(candidates, FoxRequestType.HigherGround, 6f);

        EnvironmentSnapshot environment = GetEnvironmentSnapshot(entity);
        bool inside = IsInside(entity);
        bool precipitating = IsPrecipitating(entity);
        bool dark = IsDark(entity);
        bool lowHealth = IsLowHealth(entity);

        Entity? predator = FindNearestPredator(entity);
        long nearbyPredatorId = 0;
        if (predator != null)
        {
            AddRequestCandidate(candidates, FoxRequestType.Predator, 50f);
            nearbyPredatorId = predator.EntityId;
        }

        if (!inside)
        {
            AddRequestCandidate(candidates, FoxRequestType.Inside, lowHealth ? 38f : 8f);
        }

        if ((IsCold(entity) || precipitating) && !environment.NearHeat)
        {
            AddRequestCandidate(candidates, FoxRequestType.NearHeat, 28f);
        }

        if (dark && !environment.NearLight)
        {
            AddRequestCandidate(candidates, FoxRequestType.NearLight, 22f);
        }

        if (precipitating && !inside)
        {
            AddRequestCandidate(candidates, FoxRequestType.Shelter, 26f);
        }
        else if (!precipitating && inside)
        {
            AddRequestCandidate(candidates, FoxRequestType.OutsideClear, 7f);
        }

        if (!environment.NearWater)
        {
            AddRequestCandidate(candidates, FoxRequestType.NearWater, 9f);
        }

        if (FindNearbyTamedAnimal(entity) == null)
        {
            AddRequestCandidate(candidates, FoxRequestType.NearTamedAnimal, 6f);
        }

        if (FindNearbyAnimal(entity) == null)
        {
            AddRequestCandidate(candidates, FoxRequestType.AnotherAnimal, 6f);
        }

        if (!environment.NearLargeTree)
        {
            AddRequestCandidate(candidates, FoxRequestType.LargeTree, 4f);
        }

        if (!environment.NearCropField)
        {
            AddRequestCandidate(candidates, FoxRequestType.CropField, 4f);
        }

        if (FindNearbyEntity(entity, IsTrader) == null)
        {
            AddRequestCandidate(candidates, FoxRequestType.Trader, 3f);
        }

        if (!environment.NearMechanicalDevice)
        {
            AddRequestCandidate(candidates, FoxRequestType.MechanicalDevice, 3f);
        }

        ApplyRequestWeightModifiers(entity, candidates);
        string selectedRequest = SelectWeightedRequest(entity, candidates);
        predatorTargetId = selectedRequest == FoxRequestType.Predator ? nearbyPredatorId : 0;
        return selectedRequest;
    }

    private static void AddRequestCandidate(List<(string Request, float Weight)> candidates, string request, float weight)
    {
        if (weight > 0f)
        {
            candidates.Add((request, weight));
        }
    }

    private static void ApplyRequestWeightModifiers(
        Entity entity,
        List<(string Request, float Weight)> candidates)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string personality = status?.GetString(PersonalityKey) ?? string.Empty;
        string lastCompleted = FoxRequestCatalog.NormalizeId(
            status?.GetString(LastCompletedKey) ?? string.Empty
        );

        for (int index = 0; index < candidates.Count; index++)
        {
            (string request, float weight) = candidates[index];
            float modifier = string.Equals(request, lastCompleted, StringComparison.OrdinalIgnoreCase)
                ? (string.Equals(personality, "stubborn", StringComparison.OrdinalIgnoreCase) ? 0.8f : 0.2f)
                : 1f;

            modifier *= personality switch
            {
                "homebody" when request is FoxRequestType.Inside or FoxRequestType.NearHeat or FoxRequestType.NearLight or FoxRequestType.Shelter => 1.35f,
                "homebody" when request is FoxRequestType.TravelDistance or FoxRequestType.HigherGround or FoxRequestType.OutsideClear => 0.75f,
                "restless" when request is FoxRequestType.TravelDistance or FoxRequestType.HigherGround or FoxRequestType.OutsideClear => 1.35f,
                "restless" when request is FoxRequestType.Inside or FoxRequestType.NearOwnerStill => 0.75f,
                "protective" when request == FoxRequestType.Predator => 1.5f,
                "timid" or "skittish" when request == FoxRequestType.Predator => 0.55f,
                "timid" or "skittish" when request is FoxRequestType.StayClose or FoxRequestType.Shelter or FoxRequestType.Inside => 1.25f,
                "social" or "affectionate" when request is FoxRequestType.StayClose or FoxRequestType.NearOwnerStill or FoxRequestType.NearTamedAnimal or FoxRequestType.AnotherAnimal => 1.3f,
                "solitary" or "independent" when request == FoxRequestType.StayAway => 1.35f,
                "solitary" or "independent" when request is FoxRequestType.StayClose or FoxRequestType.NearTamedAnimal or FoxRequestType.AnotherAnimal => 0.75f,
                "curious" when request is FoxRequestType.Trader or FoxRequestType.MechanicalDevice or FoxRequestType.LargeTree or FoxRequestType.CropField or FoxRequestType.HigherGround => 1.3f,
                "bold" when request is FoxRequestType.Predator or FoxRequestType.HigherGround => 1.25f,
                "playful" when request is FoxRequestType.TravelDistance or FoxRequestType.AnotherAnimal or FoxRequestType.NearTamedAnimal or FoxRequestType.StayClose => 1.35f,
                "greedy" when request is FoxRequestType.CropField or FoxRequestType.Trader or FoxRequestType.NearTamedAnimal => 1.35f,
                "demanding" when request is FoxRequestType.NearHeat or FoxRequestType.NearLight or FoxRequestType.NearWater or FoxRequestType.Shelter => 1.3f,
                "territorial" when request is FoxRequestType.Predator or FoxRequestType.OutsideClear or FoxRequestType.HigherGround => 1.4f,
                "stubborn" when request is FoxRequestType.StayAway or FoxRequestType.NearOwnerStill or FoxRequestType.Inside => 1.2f,
                _ => 1f
            };

            candidates[index] = (request, weight * modifier);
        }
    }

    private static string SelectWeightedRequest(Entity entity, List<(string Request, float Weight)> candidates)
    {
        if (candidates.Count == 0)
        {
            return FoxRequestType.StayClose;
        }

        float totalWeight = candidates.Sum(candidate => candidate.Weight);
        double selection = entity.World.Rand.NextDouble() * totalWeight;
        foreach ((string request, float weight) in candidates)
        {
            selection -= weight;
            if (selection <= 0d)
            {
                return request;
            }
        }

        return candidates[^1].Request;
    }

    private void CancelRequest(Entity entity, IServerPlayer owner, bool developerOverride)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (string.IsNullOrEmpty(status.GetString(ActiveRequestKey)))
        {
            SendState(entity, owner, "There is no active request to cancel.");
            return;
        }

        float cancelCooldown = GetRemainingSeconds(
            status,
            CancelCooldownEndsUtcMsKey,
            CancelCooldownRemainingKey
        );
        if (!developerOverride && cancelCooldown > 0f)
        {
            SendState(entity, owner, $"Cancellation will be available in {FormatCooldown(cancelCooldown)}.");
            return;
        }

        ClearActiveRequest(status);
        if (!developerOverride)
        {
            SetDeadline(status, RequestCooldownEndsUtcMsKey, GetFoxRequestCooldownSeconds(entity));
            SetDeadline(status, CancelCooldownEndsUtcMsKey, GetFoxCancelCooldownSeconds(entity));
        }
        MarkSocialStateDirty(entity);
        SendState(entity, owner, "Request cancelled.");
    }

    private void ApplyPackTalentSnapshot(Entity entity)
    {
        if (packRepository?.Loaded != true || !IsTamedFox(entity))
        {
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        string ownerUid = status.GetString("owner", string.Empty);
        string next = string.Join(",", string.IsNullOrWhiteSpace(ownerUid)
            ? Array.Empty<string>()
            : packRepository.GetUnlockedPackTalents(ownerUid));
        if (!string.Equals(status.GetString(PackTalentSnapshotKey, string.Empty), next, StringComparison.Ordinal))
        {
            status.SetString(PackTalentSnapshotKey, next);
            MarkSocialStateDirty(entity);
        }
        int rangeRank = string.IsNullOrWhiteSpace(ownerUid)
            ? 0
            : packRepository.GetPackTalentRank(ownerUid, "far-reaching-pack");
        if (status.GetInt(PackRangeTalentRankKey, 0) != rangeRank)
        {
            status.SetInt(PackRangeTalentRankKey, rangeRank);
            MarkSocialStateDirty(entity);
        }
    }

    private void RefreshPackTalentSnapshots(string ownerUid)
    {
        foreach (Entity entity in loadedFoxes.Values.ToArray())
        {
            if (IsTamedFox(entity)
                && string.Equals(GetCompanionOwnerUid(entity), ownerUid, StringComparison.Ordinal))
            {
                ApplyPackTalentSnapshot(entity);
                ApplyFoxDerivedStats(entity);
            }
        }
    }

    internal void RegisterLoadedFox(Entity entity)
    {
        if (serverApi == null)
        {
            return;
        }

        if (IsGuideFox(entity))
        {
            RegisterLoadedBramble(entity);
            return;
        }

        if (!IsTamedFox(entity))
        {
            return;
        }

        ITreeAttribute? existingStatus = GetDomesticationStatus(entity);
        string existingFoxId = existingStatus?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (packRepository?.Loaded == true && packRepository.IsPermanentlyDeleted(existingFoxId))
        {
            PermanentlyDeleteLoadedCompanionEntity(entity);
            return;
        }
        bool hadEstablishedCompanionIdentity = existingStatus?.GetInt(NumberKey, 0) > 0
            || !string.IsNullOrWhiteSpace(existingFoxId);
        bool wasAlreadyRegistered = loadedFoxes.ContainsKey(entity.EntityId);
        loadedFoxes[entity.EntityId] = entity;

        EnsureFoxNumberIfNeeded(entity);
        EnsureFoxPersonality(entity);
        if (EnsureCompanionAutoName(entity, out string suggestedName)
            && !hadEstablishedCompanionIdentity
            && serverApi.World.PlayerByUid(GetCompanionOwnerUid(entity)) is IServerPlayer owner)
        {
            serverChannel?.SendPacket(new CompanionNameSuggestionPacket
            {
                TargetEntityId = entity.EntityId,
                SuggestedName = suggestedName,
                IsNewborn = IsCompanionJuvenile(entity)
            }, owner);
        }
        ApplyPackTalentSnapshot(entity);
        ApplyFoxDerivedStats(entity);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        MigrateLegacyDeadlines(status);

        // Timed requests require continuous qualification. A chunk unload or
        // reconnect must never count as unseen progress.
        if (!wasAlreadyRegistered
            && FoxRequestCatalog.GetDurationSeconds(status.GetString(ActiveRequestKey, string.Empty)) > 0f)
        {
            status.SetLong(RequestQualifiedSinceUtcMsKey, 0);
            status.SetFloat(RequestProgressKey, 0f);
        }

        RegisterFoxInPack(entity);
        if (!wasAlreadyRegistered && !hadEstablishedCompanionIdentity
            && serverApi.World.PlayerByUid(GetCompanionOwnerUid(entity)) is IServerPlayer dialogueOwner)
        {
            bool recruited = status.GetBool(RecruitmentArrivalPendingKey, false);
            status.SetBool(RecruitmentArrivalPendingKey, false);
            MarkSocialStateDirty(entity);
            EmitDialogueEvent(entity, dialogueOwner,
                recruited ? "recruitment.converted_arrival" : "pack.join",
                string.Empty, CompanionDialoguePriority.High);
        }
    }

    internal void UnregisterLoadedFox(Entity entity, string statusLabel = "Not currently loaded")
    {
        bool suppressLedgerStatus = rebuildingEntityIds.Contains(entity.EntityId)
            || permanentlyDeletingEntityIds.Contains(entity.EntityId);
        loadedFoxes.Remove(entity.EntityId);
        packCartCallTargets.Remove(entity.EntityId);
        ReleaseWorkCartTreeReservations(entity);
        adultStateValidatedEntities.Remove(entity.EntityId);
        lastPersistedHealthStates.Remove(entity.EntityId);
        ReleaseAmbientLifeForEntity(entity);
        ClearUndergroundRecoveryState(entity.EntityId);
        ledgerHydratedEntities.Remove(entity.EntityId);
        environmentSnapshots.Remove(entity.EntityId);
        companionAttackers.Remove(entity.EntityId);
        nextConditionCheckAtMs.Remove(entity.EntityId);
        nextEarlyWarningScanAtMs.Remove(entity.EntityId);
        nextBreedingPartnerScanAtMs.Remove(entity.EntityId);
        nextBirthRetryAtMsByEntity.Remove(entity.EntityId);
        lastProgressPacketAtMs.Remove(entity.EntityId);
        foreach (long itemId in droppedItemReservations.Where(entry => entry.Value == entity.EntityId).Select(entry => entry.Key).ToArray())
        {
            droppedItemReservations.Remove(itemId);
        }
        foreach (string key in mowingWorksiteReservations
                     .Where(entry => entry.Value == entity.EntityId)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            mowingWorksiteReservations.Remove(key);
        }
        foreach (string key in naturalCleanupReservations
                     .Where(entry => entry.Value == entity.EntityId)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            naturalCleanupReservations.Remove(key);
        }
        foreach (string key in charcoalTargetReservations
                     .Where(entry => entry.Value == entity.EntityId)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            charcoalTargetReservations.Remove(key);
        }
        foreach (string key in snowTargetReservations
                     .Where(entry => entry.Value.EntityId == entity.EntityId)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            snowTargetReservations.Remove(key);
        }
        foreach (string key in snowApproachReservations
                     .Where(entry => entry.Value.EntityId == entity.EntityId)
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            snowApproachReservations.Remove(key);
        }

        ITreeAttribute? status = GetDomesticationStatus(entity);
        if (status == null || suppressLedgerStatus)
        {
            return;
        }

        if (FoxRequestCatalog.GetDurationSeconds(status.GetString(ActiveRequestKey, string.Empty)) > 0f)
        {
            status.SetLong(RequestQualifiedSinceUtcMsKey, 0);
            status.SetFloat(RequestProgressKey, 0f);
        }

        string foxId = status.GetString(FoxIdKey, string.Empty);
        if (packRepository?.Loaded == true && !string.IsNullOrWhiteSpace(foxId))
        {
            packRepository.MarkStatus(foxId, statusLabel, entity.World.Calendar.TotalDays);
        }
    }

    internal void OnFoxDeath(Entity entity)
    {
        if (permanentlyDeletingEntityIds.Contains(entity.EntityId))
        {
            UnregisterLoadedFox(entity, "Permanently deleted");
            return;
        }

        if (GetDomesticationStatus(entity)?.GetBool(GrowthInProgressKey, false) == true)
        {
            UnregisterLoadedFox(entity, "Growing");
            return;
        }
        HandleCompanionBackpackDeath(entity);
        DropFoxStorageCargo(entity);
        RegisterFoxInPack(entity);
        UnregisterLoadedFox(entity, "Dead");
    }

    internal void UpdateFox(Entity entity, float dt)
    {
        if (serverApi == null)
        {
            return;
        }

        if (!IsTamedFox(entity))
        {
            DropFoxStorageCargo(entity);
            ITreeAttribute? oldStatus = GetDomesticationStatus(entity);
            string oldFoxId = oldStatus?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(oldFoxId) && packRepository?.Loaded == true)
            {
                packRepository.MarkStatus(oldFoxId, "Unowned", entity.World.Calendar.TotalDays);
            }
            UnregisterLoadedFox(entity, "Unowned");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;

        if (!loadedFoxes.ContainsKey(entity.EntityId))
        {
            RegisterLoadedFox(entity);
        }
        TryRepairStaleAdultCompanionState(entity);
        UpdateCompanionHealthStateAudio(entity, status);
        PersistHealthStateIfChanged(entity);
        if (!UpdateMortallyWoundedState(entity, status))
        {
            return;
        }

        if (UpdateBackpackDelivery(entity))
        {
            return;
        }

        UpdateCompanionFood(entity);
        // A companion that is already beside its board should not depend on
        // the asynchronous navigation callback to complete the transaction.
        // This is especially important for Cats, whose source AI and mouth
        // inventory behavior can win the same task-manager tick.
        TryConsumeNearbyDiningBoardFood(entity);

        ApplyPendingSecondWind(entity, status);
        RefreshAdrenalineRush(entity, 0f);
        ApplyFoxDerivedStats(entity);
        UpdateAutomaticRetreatRecovery(entity, status);
        UpdateMood(entity, status);
        UpdateEarlyWarning(entity, status);
        UpdateBreedingAndChildren(entity);

        if (IsCompanionFoodRestricted(entity))
        {
            return;
        }

        string activeRequest = FoxRequestCatalog.NormalizeId(
            status.GetString(ActiveRequestKey, string.Empty)
        );
        if (activeRequest != status.GetString(ActiveRequestKey, string.Empty))
        {
            status.SetString(ActiveRequestKey, activeRequest);
            MarkSocialStateDirty(entity);
        }

        long nowUtcMs = UtcNowMs();

        if (string.IsNullOrEmpty(activeRequest))
        {
            return;
        }

        double nowTotalHours = serverApi.World.Calendar.TotalHours;
        long expiresAtMs = status.GetLong(RequestExpiresUtcMsKey, 0);
        if (expiresAtMs <= 0)
        {
            expiresAtMs = nowUtcMs + SecondsToMilliseconds(ActiveRequestLifetimeSeconds);
            status.SetLong(RequestExpiresUtcMsKey, expiresAtMs);
        }
        else if (nowUtcMs >= expiresAtMs)
        {
            bool developerRequest = status.GetBool(RequestIsDeveloperKey, false);
            ClearActiveRequest(status);
            if (!developerRequest)
            {
                SetDeadline(status, RequestCooldownEndsUtcMsKey, GetFoxRequestCooldownSeconds(entity));
            }
            MarkSocialStateDirty(entity);
            RegisterFoxInPack(entity);
            SendStateToOwner(entity, "That request expired after thirty minutes. No points were lost.");
            SendPackStateToOwner(entity);
            return;
        }

        // Item and placement requests complete only through their explicit
        // interaction events; inventory totals are never used as evidence.
        if (FoxRequestCatalog.GetDeliveryMode(activeRequest) != FoxRequestDeliveryMode.None)
        {
            return;
        }

        long worldNowMs = entity.World.ElapsedMilliseconds;
        long checkIntervalMs = FoxRequestCatalog.RequiresEnvironmentScan(activeRequest) ? 3000 : 1000;
        if (nextConditionCheckAtMs.TryGetValue(entity.EntityId, out long nextCheckMs)
            && worldNowMs < nextCheckMs)
        {
            return;
        }
        nextConditionCheckAtMs[entity.EntityId] = worldNowMs + checkIntervalMs;

        bool conditionMet = IsRequestConditionMet(entity, status, activeRequest);
        float durationSeconds = FoxRequestCatalog.GetDurationSeconds(activeRequest);
        if (!conditionMet)
        {
            if (status.GetLong(RequestQualifiedSinceUtcMsKey, 0) != 0
                || status.GetFloat(RequestProgressKey, 0f) > 0f)
            {
                status.SetLong(RequestQualifiedSinceUtcMsKey, 0);
                status.SetFloat(RequestProgressKey, 0f);
                SendProgressStateIfViewed(entity);
            }
            return;
        }

        if (durationSeconds <= 0f)
        {
            CompleteRequest(entity, status, activeRequest);
            return;
        }

        long qualifiedSinceMs = status.GetLong(RequestQualifiedSinceUtcMsKey, 0);
        if (qualifiedSinceMs <= 0 || qualifiedSinceMs > nowUtcMs)
        {
            qualifiedSinceMs = nowUtcMs;
            status.SetLong(RequestQualifiedSinceUtcMsKey, qualifiedSinceMs);
        }

        float progress = Math.Min(durationSeconds, (nowUtcMs - qualifiedSinceMs) / 1000f);
        status.SetFloat(RequestProgressKey, progress);
        if (progress >= durationSeconds)
        {
            CompleteRequest(entity, status, activeRequest);
        }
        else
        {
            SendProgressStateIfViewed(entity);
        }
    }

    private void UpdateCompanionHealthStateAudio(Entity entity, ITreeAttribute status)
    {
        int currentState = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        int previousState = status.GetInt(LastAnnouncedHealthStateKey, -1);
        if (previousState < 0)
        {
            status.SetInt(LastAnnouncedHealthStateKey, currentState);
            return;
        }
        if (previousState == currentState)
        {
            return;
        }

        status.SetInt(LastAnnouncedHealthStateKey, currentState);
        string ownerUid = status.GetString("owner", string.Empty);
        if (serverApi?.World.PlayerByUid(ownerUid) is not IServerPlayer owner)
        {
            return;
        }

        if (currentState == MortallyWoundedHealthState)
        {
            EmitDialogueEvent(entity, owner, "injury.mortal_warning", string.Empty, CompanionDialoguePriority.Critical);
            serverApi.Event.RegisterCallback(_ =>
            {
                if (entity.Alive && IsFoxMortallyWounded(entity) && !IsFoxAwayFromWorld(entity))
                    EmitDialogueEvent(entity, owner, "injury.needs_healing_item", string.Empty, CompanionDialoguePriority.Critical);
            }, 3200);
            SendOwnerSound(owner, CompanionSoundCue.MortalWarning);
            owner.SendMessage(
                GlobalConstants.GeneralChatGroup,
                $"{GetFoxDisplayName(entity)} is mortally wounded!",
                EnumChatType.Notification
            );
        }
        else if (currentState == RecoveringHealthState)
        {
            if (previousState == MortallyWoundedHealthState)
            {
                EmitDialogueEvent(entity, owner, "recovery.poultice_received", string.Empty, CompanionDialoguePriority.High);
                serverApi.Event.RegisterCallback(_ =>
                {
                    if (entity.Alive && entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0) == RecoveringHealthState)
                        EmitDialogueEvent(entity, owner, "recovery.begin", string.Empty, CompanionDialoguePriority.High);
                }, 3200);
            }
            else EmitDialogueEvent(entity, owner, "recovery.begin", string.Empty, CompanionDialoguePriority.High);
            SendOwnerSound(owner, CompanionSoundCue.RecoveryStarted);
        }
        else if (currentState == 0 && previousState == RecoveringHealthState)
        {
            EmitDialogueEvent(entity, owner, "recovery.complete", string.Empty, CompanionDialoguePriority.High);
            SendOwnerSound(owner, CompanionSoundCue.RecoveryComplete);
        }
    }

    private bool UpdateMortallyWoundedState(Entity entity, ITreeAttribute status)
    {
        if (!IsFoxMortallyWounded(entity))
        {
            return true;
        }

        double nowHours = entity.World.Calendar.TotalHours;
        double woundStartHours = entity.WatchedAttributes.GetDouble("mortallyWoundedTotalHours", 0d);
        if (woundStartHours <= 0d)
        {
            woundStartHours = nowHours;
            entity.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", woundStartHours);
        }

        int stabilizedRank = GetFoxPerkRank(entity, "catlike-landing");
        double adjustedStartHours = status.GetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
        int appliedRank = status.GetInt(StabilizedWindowAppliedRankKey, 0);
        if (adjustedStartHours < 0d || Math.Abs(adjustedStartHours - woundStartHours) > 0.001d)
        {
            // The vanilla behavior treats this watched value as the wound's
            // start time. Moving it forward extends the rescue deadline.
            woundStartHours += stabilizedRank * StabilizedRescueHoursPerRank;
            entity.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", woundStartHours);
            status.SetDouble(StabilizedWindowAdjustedStartHoursKey, woundStartHours);
            status.SetInt(StabilizedWindowAppliedRankKey, stabilizedRank);
            MarkSocialStateDirty(entity);
        }
        else if (stabilizedRank > appliedRank)
        {
            // Handle buying another rank while the fox is already wounded.
            woundStartHours += (stabilizedRank - appliedRank) * StabilizedRescueHoursPerRank;
            entity.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", woundStartHours);
            status.SetDouble(StabilizedWindowAdjustedStartHoursKey, woundStartHours);
            status.SetInt(StabilizedWindowAppliedRankKey, stabilizedRank);
            MarkSocialStateDirty(entity);
        }

        if (GetFoxPerkRank(entity, "self-stabilizing") > 0)
        {
            // Stabilized changes the vanilla rescue deadline, but Deathless
            // should still resolve from the actual moment the fox was wounded.
            // Recover that original timestamp from the adjusted value instead
            // of making the two perks unintentionally stack their timers.
            double deathlessWoundStartHours = woundStartHours;
            double effectiveAdjustedStartHours = status.GetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
            int effectiveAppliedRank = status.GetInt(StabilizedWindowAppliedRankKey, 0);
            if (effectiveAdjustedStartHours >= 0d && effectiveAppliedRank > 0)
            {
                deathlessWoundStartHours = effectiveAdjustedStartHours
                    - effectiveAppliedRank * StabilizedRescueHoursPerRank;
            }

            double deathlessRecoveryAt = status.GetDouble(DeathlessRecoveryAtHoursKey, -1d);
            double deathlessFallbackAt = status.GetDouble(DeathlessFallbackAtHoursKey, -1d);
            double stabilizationShiftHours = Math.Max(0d, effectiveAdjustedStartHours - deathlessWoundStartHours);
            if (stabilizationShiftHours > 0d
                && deathlessFallbackAt > deathlessWoundStartHours + DeathlessFallbackHours + 0.001d)
            {
                // One-time migration for wounds created by 0.1.87/0.1.88,
                // where Deathless was accidentally based on Stabilized's
                // shifted timestamp.
                deathlessRecoveryAt -= stabilizationShiftHours;
                deathlessFallbackAt -= stabilizationShiftHours;
                status.SetDouble(DeathlessRecoveryAtHoursKey, deathlessRecoveryAt);
                status.SetDouble(DeathlessFallbackAtHoursKey, deathlessFallbackAt);
                MarkSocialStateDirty(entity);
            }
            if (deathlessRecoveryAt < 0d)
            {
                float recoveryDelay = DeathlessRecoveryHours[entity.World.Rand.Next(DeathlessRecoveryHours.Length)];
                deathlessRecoveryAt = deathlessWoundStartHours + recoveryDelay;
                status.SetDouble(DeathlessRecoveryAtHoursKey, deathlessRecoveryAt);
                MarkSocialStateDirty(entity);
            }
            if (deathlessFallbackAt < 0d)
            {
                deathlessFallbackAt = deathlessWoundStartHours + DeathlessFallbackHours;
                status.SetDouble(DeathlessFallbackAtHoursKey, deathlessFallbackAt);
                MarkSocialStateDirty(entity);
            }

            if (nowHours >= deathlessRecoveryAt || nowHours >= deathlessFallbackAt)
            {
                AutoStabilizeFox(entity, status);
                if (TryGetOwnerPlayer(entity, out IServerPlayer? deathlessOwner))
                    EmitDialogueEvent(entity, deathlessOwner!, "recovery.deathless", string.Empty, CompanionDialoguePriority.Critical);
                return false;
            }
        }

        // The built-in mortallywoundable behavior owns the final death event.
        // Keep this social tick paused while that behavior owns the downed fox.
        return false;
    }

    private static void AutoStabilizeFox(Entity entity, ITreeAttribute status)
    {
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null)
        {
            health.Health = Math.Max(1f, Math.Min(1f, health.MaxHealth));
            health.MarkDirty();
        }

        entity.WatchedAttributes.SetInt(EntityHealthStateKey, RecoveringHealthState);
        entity.WatchedAttributes.SetFloat("regenSpeed", 1f);
        entity.WatchedAttributes.MarkPathDirty("regenSpeed");
        entity.AnimManager?.StopAnimation("wounded-idle");
        entity.AnimManager?.StopAnimation("wounded-resthead");
        entity.AnimManager?.StartAnimation("wounded-stand");
        entity.AnimManager?.StartAnimation("idle");
        status.SetInt(StabilizedWindowAppliedRankKey, 0);
        status.SetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
        status.SetDouble(DeathlessRecoveryAtHoursKey, -1d);
        status.SetDouble(DeathlessFallbackAtHoursKey, -1d);
        MarkSocialStateDirty(entity);
    }

    private void CompleteRequest(Entity entity, ITreeAttribute status, string activeRequest)
    {
        bool developerRequest = status.GetBool(RequestIsDeveloperKey, false);
        string personality = status.GetString(PersonalityKey, string.Empty);
        string requestLabel = GetRequestLabel(entity, activeRequest, personality);
        if (!developerRequest)
        {
            status.SetInt(RequestsCompletedKey, status.GetInt(RequestsCompletedKey, 0) + 1);
            status.SetString(LastCompletedKey, activeRequest);
            AwardRequestPoints(entity, status);
            SetDeadline(status, RequestCooldownEndsUtcMsKey, GetFoxRequestCooldownSeconds(entity));
        }

        ClearActiveRequest(status);
        ApplyPostRequestMood(entity, status, activeRequest);
        NotifyFoxRequestCompleted(entity, activeRequest, developerRequest);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        if (!developerRequest && TryGetOwnerPlayer(entity, out IServerPlayer? requestOwner))
            EmitDialogueEvent(entity, requestOwner!, "request_lifecycle.completed", string.Empty, CompanionDialoguePriority.Medium);

        string prefix = developerRequest ? "Developer test completed: " : "Completed: ";
        SendStateToOwner(entity, prefix + requestLabel);
        SendPackStateToOwner(entity);
    }

    private void AwardRequestPoints(Entity entity, ITreeAttribute status)
    {
        AwardIndividualTalentPoint(entity, status);
        string ownerUid = status.GetString("owner", string.Empty);
        if (!string.IsNullOrWhiteSpace(ownerUid))
        {
            int packPointAward = 1 + GetFoxPerkRank(entity, "pack-contributor");
            packRepository?.AdjustPackPoints(ownerUid, packPointAward);
        }
    }

    private void AwardIndividualTalentPoint(Entity entity, ITreeAttribute status)
    {
        AwardIndividualTalentPoints(entity, status, 1);
    }

    private int GetPackPoints(string ownerUid)
    {
        return packRepository?.GetPackPoints(ownerUid) ?? 0;
    }

    private static long UtcNowMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private static long SecondsToMilliseconds(float seconds)
    {
        return (long)Math.Ceiling(Math.Max(0f, seconds) * 1000f);
    }

    private static float GetRemainingSeconds(
        ITreeAttribute status,
        string deadlineKey,
        string legacyRemainingKey)
    {
        long deadline = status.GetLong(deadlineKey, 0);
        if (deadline <= 0)
        {
            float legacyRemaining = Math.Max(0f, status.GetFloat(legacyRemainingKey, 0f));
            if (legacyRemaining > 0f)
            {
                deadline = UtcNowMs() + SecondsToMilliseconds(legacyRemaining);
                status.SetLong(deadlineKey, deadline);
                status.SetFloat(legacyRemainingKey, 0f);
            }
        }

        return deadline <= 0
            ? 0f
            : Math.Max(0f, (deadline - UtcNowMs()) / 1000f);
    }

    private static void SetDeadline(ITreeAttribute status, string deadlineKey, float seconds)
    {
        status.SetLong(
            deadlineKey,
            seconds <= 0f ? 0 : UtcNowMs() + SecondsToMilliseconds(seconds)
        );

        string? legacyKey = deadlineKey switch
        {
            MoodOverrideEndsUtcMsKey => MoodOverrideRemainingKey,
            PackMoodOverrideEndsUtcMsKey => PackMoodOverrideRemainingKey,
            MoodRandomEndsUtcMsKey => MoodRandomRemainingKey,
            RequestCooldownEndsUtcMsKey => RequestCooldownRemainingKey,
            CancelCooldownEndsUtcMsKey => CancelCooldownRemainingKey,
            _ => null
        };
        if (legacyKey != null)
        {
            status.SetFloat(legacyKey, 0f);
        }
    }

    private static void MigrateLegacyDeadlines(ITreeAttribute status)
    {
        GetRemainingSeconds(status, MoodOverrideEndsUtcMsKey, MoodOverrideRemainingKey);
        GetRemainingSeconds(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideRemainingKey);
        GetRemainingSeconds(status, MoodRandomEndsUtcMsKey, MoodRandomRemainingKey);
        GetRemainingSeconds(status, RequestCooldownEndsUtcMsKey, RequestCooldownRemainingKey);
        GetRemainingSeconds(status, CancelCooldownEndsUtcMsKey, CancelCooldownRemainingKey);
    }

    private static void ClearActiveRequest(ITreeAttribute status)
    {
        status.SetString(ActiveRequestKey, string.Empty);
        status.SetFloat(RequestProgressKey, 0f);
        status.SetLong(RequestQualifiedSinceUtcMsKey, 0);
        status.SetLong(RequestExpiresUtcMsKey, 0);
        status.SetBool(RequestIsDeveloperKey, false);
        status.SetLong(PredatorTargetKey, 0);
        status.SetString(PredatorTargetClueKey, string.Empty);
        status.SetBool(PredatorTargetKilledKey, false);
        status.SetString(RequestedItemCodeKey, string.Empty);
        status.SetInt(RequestedItemCountKey, 0);
    }

    private void SendProgressStateIfViewed(Entity entity)
    {
        string ownerUid = GetDomesticationStatus(entity)?.GetString("owner", string.Empty) ?? string.Empty;
        if (!socialViewByOwner.TryGetValue(ownerUid, out long viewedEntityId)
            || viewedEntityId != entity.EntityId)
        {
            return;
        }

        long nowMs = entity.World.ElapsedMilliseconds;
        if (lastProgressPacketAtMs.TryGetValue(entity.EntityId, out long lastSentMs)
            && nowMs - lastSentMs < ProgressPacketIntervalMs)
        {
            return;
        }

        lastProgressPacketAtMs[entity.EntityId] = nowMs;
        SendStateToOwner(entity, string.Empty);
    }

    private void SetMoodDeveloper(Entity entity, IServerPlayer owner, string mood)
    {
        if (!DeveloperMoodValues.Contains(mood, StringComparer.OrdinalIgnoreCase))
        {
            SendState(entity, owner, "Unknown developer mood.");
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (GetRemainingSeconds(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideRemainingKey) > 0f)
        {
            SendState(entity, owner, "Mood is locked by a forced pack mood for another 30 seconds.");
            return;
        }

        if (string.IsNullOrEmpty(mood))
        {
            SetRandomMood(entity, status);
            status.SetBool(DeveloperMoodForcedKey, false);
            status.SetString(MoodOverrideKey, string.Empty);
            SetDeadline(status, MoodOverrideEndsUtcMsKey, 0f);
            SetDeadline(status, MoodRandomEndsUtcMsKey, GetNextMoodRandomInterval(entity));
            MarkSocialStateDirty(entity);
            RegisterFoxInPack(entity);
            SendState(entity, owner, "Mood returned to normal random behavior.");
            SendPackStateToOwner(entity);
            return;
        }

        status.SetString(MoodKey, mood.ToLowerInvariant());
        status.SetBool(DeveloperMoodForcedKey, true);
        status.SetString(MoodOverrideKey, mood.ToLowerInvariant());
        SetDeadline(status, MoodOverrideEndsUtcMsKey, PostRequestMoodDurationSeconds);
        SetDeadline(status, MoodRandomEndsUtcMsKey, PostRequestMoodDurationSeconds);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        SendState(entity, owner, $"Mood forced to {mood} for five minutes.");
        SendPackStateToOwner(entity);
    }

    private void ResetCooldownsDeveloper(Entity entity, IServerPlayer owner)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        SetDeadline(status, RequestCooldownEndsUtcMsKey, 0f);
        SetDeadline(status, CancelCooldownEndsUtcMsKey, 0f);
        MarkSocialStateDirty(entity);
        SendState(entity, owner, "Request and cancellation cooldowns reset.");
    }

    private void AdjustFoxPointsDeveloper(Entity entity, IServerPlayer owner, int delta)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        int points = Math.Max(0, status.GetInt(FoxPointsKey, 0) + delta);
        status.SetInt(FoxPointsKey, points);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        SendState(entity, owner, $"Companion points: {points}.");
        SendPackStateToOwner(entity);
    }

    private void AdjustPackPointsDeveloper(Entity entity, IServerPlayer owner, int delta)
    {
        string ownerUid = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        int points = packRepository?.AdjustPackPoints(ownerUid, delta) ?? 0;
        SendState(entity, owner, $"Pack points: {points}.");
        SendPackState(owner);
    }

    private void SetHealthOneDeveloper(Entity entity, IServerPlayer owner)
    {
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health == null)
        {
            SendState(entity, owner, "This companion has no health behavior.");
            return;
        }

        health.Health = Math.Min(1f, health.MaxHealth);
        health.MarkDirty();
        RegisterFoxInPack(entity);
        SendState(entity, owner, "Companion health set to 1 for regeneration testing.");
        SendPackStateToOwner(entity);
    }

    private bool RecoverCompanionDeveloper(
        IServerPlayer owner,
        string foxId,
        bool sendResult = true,
        bool allowTerminalStatus = false,
        bool allowActiveExpedition = false,
        BlockPos? preferredSpawnOrigin = null)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return false;
        }

        // The recovery ledger is deliberately allowed to repair an entity
        // that vanished between ticks, so refresh the live lookup before
        // deciding that a second copy is safe to create.
        RefreshLoadedFoxPackRecords();

        if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal))
        {
            if (sendResult)
            {
                serverChannel?.SendPacket(new CompanionRecoveryStatePacket
                {
                    Message = "Recovery refused: that companion is not in your pack ledger."
                }, owner);
            }
            return false;
        }

        if (FindLoadedCompanionForRecovery(record.FoxId) != null)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "That companion is already present in the world.");
            }
            return false;
        }

        if (pendingRecoveryChecks.Contains(record.FoxId)
            || pendingAdminCompanionTeleports.Contains(record.OwnerUid + ":" + record.FoxId))
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery is paused while the saved chunk is being checked for the original companion.");
            }
            return false;
        }

        if (!record.DurableStateInitialized)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery refused: the saved companion record is incomplete, so creating a replacement could lose state or duplicate the original.");
            }
            return false;
        }

        bool awaitingSafeReturn = !string.IsNullOrWhiteSpace(record.PendingReturnStatus)
            || string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase);
        if (!allowActiveExpedition
            && !awaitingSafeReturn
            && packRepository.GetExpeditionForMember(owner.PlayerUID, record.FoxId) != null)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery is unavailable while that companion is assigned to an active expedition.");
            }
            return false;
        }

        if (!allowTerminalStatus
            && (string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase)))
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, $"Recovery is unavailable for a {record.Status.ToLowerInvariant()} ledger record.");
            }
            return false;
        }

        if (owner.Entity == null)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery refused: the player entity is not ready.");
            }
            return false;
        }

        EntityProperties? entityType = ResolveRecoveryEntityType(record, out string typeReason);
        if (entityType == null)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, $"Recovery refused: {typeReason}.");
            }
            return false;
        }

        Vec3d? spawnPosition = FindSafeEntityPosition(
            preferredSpawnOrigin ?? owner.Entity.Pos.AsBlockPos,
            entityType,
            null,
            8,
            3);
        if (spawnPosition == null)
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery refused: no safe open ground was found near the player.");
            }
            return false;
        }

        Entity? rebuilt = null;
        string recoveryStage = "create entity";
        try
        {
            rebuilt = serverApi.World.ClassRegistry.CreateEntity(entityType);
            if (rebuilt == null)
            {
                throw new InvalidOperationException("The entity factory returned null.");
            }

            recoveryStage = "initialize entity position";
            rebuilt.EntityId = 0;
            rebuilt.Pos.X = spawnPosition.X;
            rebuilt.Pos.Y = spawnPosition.Y;
            rebuilt.Pos.Z = spawnPosition.Z;
            rebuilt.Pos.Yaw = owner.Entity.Pos.Yaw;
            rebuilt.PositionBeforeFalling.Set(spawnPosition.X, spawnPosition.Y, spawnPosition.Z);

            recoveryStage = "initialize companion status";
            ITreeAttribute status = GetDomesticationStatus(rebuilt, true)!;
            status.SetString("owner", owner.PlayerUID);
            status.SetString("domesticationLevel", "DOMESTICATED");
            status.SetInt(NumberKey, Math.Max(1, record.Number));
            status.SetString(FoxIdKey, record.FoxId);
            status.SetFloat("obedience", 1f);
            status.SetInt("generation", 0);
            status.SetBool(RecruitmentArrivalPendingKey, false);

            if (!string.IsNullOrWhiteSpace(record.Name))
            {
                TreeAttribute nametag = new();
                nametag.SetString("name", record.Name);
                rebuilt.WatchedAttributes["nametag"] = nametag;
            }

            recoveryStage = "spawn replacement entity";
            serverApi.World.SpawnEntity(rebuilt);
            if (!rebuilt.Alive || rebuilt.EntityId <= 0 || !IsTamedFox(rebuilt))
            {
                throw new InvalidOperationException("The replacement did not spawn as a living companion.");
            }

            // Entity behaviors, including health, are initialized by the
            // world spawn lifecycle. Hydrating before SpawnEntity causes
            // Entity.GetBehavior to dereference an uninitialized behavior
            // collection and makes every recovery fail, including fresh
            // companions with otherwise valid ledger data.
            recoveryStage = "hydrate companion from ledger";
            HydrateFoxFromLedger(rebuilt, status, record);
            SetFoxAwayFromWorld(rebuilt, false);

            recoveryStage = "register replacement companion";
            RegisterLoadedFox(rebuilt);
            recoveryStage = "restore replacement health";
            EntityBehaviorHealth? health = rebuilt.GetBehavior<EntityBehaviorHealth>();
            if (health != null && record.MaxHealth > 0f)
            {
                health.Health = Math.Clamp(record.CurrentHealth, 0f, health.MaxHealth);
                health.MarkDirty();
            }

            record.EntityId = rebuilt.EntityId;
            record.EntityCode = rebuilt.Code?.ToShortString() ?? record.EntityCode;
            record.PendingReturnStatus = string.Empty;
            record.PendingReturnAtUtcMs = 0;
            record.PendingDepartureStatus = string.Empty;
            record.PendingDepartureAtUtcMs = 0;
            record.PendingDepartureDeadlineUtcMs = 0;
            record.Status = IsFoxMortallyWounded(rebuilt) ? "Mortally wounded" : "Present";
            record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
            record.DurableStateInitialized = true;
            recoveryStage = "save rebuilt ledger record";
            packRepository.Save();

            if (sendResult)
            {
                SendCompanionRecoveryState(owner, $"Recovered {GetFoxRecordLabel(record)} beside you.");
            }
            return true;
        }
        catch (Exception exception)
        {
            if (rebuilt != null)
            {
                try
                {
                    if (rebuilt.Alive)
                    {
                        rebuilt.Die(EnumDespawnReason.Removed);
                    }
                }
                catch (Exception cleanupException)
                {
                    serverApi.Logger.Error(
                        "[FeralKinshipCompanions] Recovery cleanup failed for companion {0}: {1}",
                        record.FoxId,
                        cleanupException
                    );
                }

                try
                {
                    UnregisterLoadedFox(rebuilt, "Recovery failed");
                }
                catch (Exception cleanupException)
                {
                    serverApi.Logger.Error(
                        "[FeralKinshipCompanions] Recovery unregister cleanup failed for companion {0}: {1}",
                        record.FoxId,
                        cleanupException
                    );
                }
            }

            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Developer recovery failed for companion {0} during {1}: {2}",
                record.FoxId,
                recoveryStage,
                exception
            );
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Recovery failed; the original ledger record was left intact.");
            }
            return false;
        }
    }

    private bool NuclearRebuildCompanionDeveloper(
        IServerPlayer owner,
        string foxId,
        bool sendResult = true)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return false;
        }

        // Refresh first so the persistent record contains the live entity's
        // latest talents, health, name, duties, and other durable state before
        // the old world instance is destroyed.
        RefreshLoadedFoxPackRecords();
        if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal))
        {
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Nuclear rebuild refused: that companion is not in your pack ledger.");
            }
            return false;
        }

        Entity? oldEntity = FindLoadedCompanionByFoxId(record.FoxId);

        FoxPackRecordV2 recordSnapshot;
        try
        {
            recordSnapshot = ClonePackRecordForRebuild(record);
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not snapshot ledger record {0} for nuclear rebuild: {1}",
                foxId,
                exception
            );
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Nuclear rebuild refused: the pack record could not be safely stored.");
            }
            return false;
        }

        long oldEntityId = oldEntity?.EntityId ?? 0;
        Entity? rebuilt = null;
        try
        {
            if (oldEntity != null)
            {
                rebuildingEntityIds.Add(oldEntityId);
                UnregisterLoadedFox(oldEntity, "Nuclear recovery-ledger rebuild");
                // Do not let the developer escape hatch destroy a carried
                // item just because the companion is in a broken state.
                DropFoxStorageCargo(oldEntity);
                oldEntity.Die(EnumDespawnReason.Removed);
                companionAggressiveTargets.Remove(oldEntityId);
                companionAttackers.Remove(oldEntityId);
                if (socialViewByOwner.TryGetValue(owner.PlayerUID, out long viewedEntityId)
                    && viewedEntityId == oldEntityId)
                {
                    socialViewByOwner.Remove(owner.PlayerUID);
                    EndMenuAttention(oldEntityId);
                }
                if (perkViewByOwner.TryGetValue(owner.PlayerUID, out long perkEntityId)
                    && perkEntityId == oldEntityId)
                {
                    perkViewByOwner.Remove(owner.PlayerUID);
                    EndMenuAttention(oldEntityId);
                }
            }

            // The record remains in place as the durable source of truth. A
            // zero entity ID tells the normal ledger recovery builder that it
            // must materialize a fresh world entity.
            record.EntityId = 0;
            packRepository.Save();
            if (!RecoverCompanionDeveloper(
                    owner,
                    foxId,
                    sendResult: false,
                    allowTerminalStatus: true,
                    allowActiveExpedition: true))
            {
                throw new InvalidOperationException("The recovery-ledger builder could not spawn the replacement.");
            }

            rebuilt = FindLoadedCompanionByFoxId(foxId);
            if (rebuilt == null || !rebuilt.Alive)
            {
                throw new InvalidOperationException("The recovery-ledger builder returned no living replacement.");
            }

            if (sendResult)
            {
                SendCompanionRecoveryState(owner, $"Nuclear rebuild complete. {GetFoxRecordLabel(recordSnapshot)} was replaced from the ledger.");
            }
            if (oldEntityId > 0)
            {
                serverApi.Event.RegisterCallback(_ => rebuildingEntityIds.Remove(oldEntityId), 2000);
            }
            return true;
        }
        catch (Exception exception)
        {
            if (rebuilt != null)
            {
                try
                {
                    if (rebuilt.Alive)
                    {
                        rebuilt.Die(EnumDespawnReason.Removed);
                    }
                }
                catch (Exception cleanupException)
                {
                    serverApi.Logger.Error(
                        "[FeralKinshipCompanions] Nuclear rebuild cleanup failed for companion {0}: {1}",
                        foxId,
                        cleanupException
                    );
                }

                try
                {
                    UnregisterLoadedFox(rebuilt, "Nuclear recovery-ledger rebuild failed");
                }
                catch (Exception cleanupException)
                {
                    serverApi.Logger.Error(
                        "[FeralKinshipCompanions] Nuclear rebuild unregister cleanup failed for companion {0}: {1}",
                        foxId,
                        cleanupException
                    );
                }
            }

            packRepository.RestoreRecord(recordSnapshot);
            packRepository.Save();
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Nuclear recovery-ledger rebuild failed for companion {0}: {1}",
                foxId,
                exception
            );
            if (sendResult)
            {
                SendCompanionRecoveryState(owner, "Nuclear rebuild failed. The stored pack record was restored; the old entity was already removed.");
            }
            if (oldEntityId > 0)
            {
                serverApi.Event.RegisterCallback(_ => rebuildingEntityIds.Remove(oldEntityId), 2000);
            }
            return false;
        }
    }

    private EntityProperties? ResolveRecoveryEntityType(FoxPackRecordV2 record, out string reason)
    {
        reason = string.Empty;
        if (serverApi == null)
        {
            reason = "the server API is unavailable";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(record.EntityCode))
        {
            EntityProperties? exact = serverApi.World.GetEntityType(new AssetLocation(record.EntityCode));
            if (exact != null)
            {
                return exact;
            }

            // Adapter profiles span unrelated source animals. Never substitute
            // another species/family when that animal's source pack disappears.
            if (CompanionContentIdentity.RequiresExactRecovery(record.SpeciesId, record.EntityCode))
            {
                reason = $"exact entity type {record.EntityCode} is not registered";
                return null;
            }

            // Runtime variant codes can outlive the source mod or a changed
            // variant list. If the durable species identity is still known,
            // fall back to the registered tame definition instead of making
            // an otherwise recoverable record permanently unusable.
            if (CompanionSpeciesCatalog.TryGetById(record.SpeciesId, out CompanionSpeciesProfile fallbackProfile))
            {
                EntityProperties? fallback = serverApi.World.EntityTypes.FirstOrDefault(
                    type => fallbackProfile.MatchesTameEntityCode(type.Code));
                if (fallback != null)
                {
                    return fallback;
                }
            }

            reason = $"entity type {record.EntityCode} is not registered and no tame fallback is available";
            return null;
        }

        if (!CompanionContentIdentity.RequiresExactRecovery(record.SpeciesId, record.EntityCode)
            && CompanionSpeciesCatalog.TryGetById(record.SpeciesId, out CompanionSpeciesProfile profile))
        {
            EntityProperties? fallback = serverApi.World.EntityTypes.FirstOrDefault(
                type => profile.MatchesTameEntityCode(type.Code));
            if (fallback != null)
            {
                return fallback;
            }
        }

        reason = string.IsNullOrWhiteSpace(record.SpeciesId)
            ? "the record has no species or entity identity"
            : $"no registered tame entity matches species {record.SpeciesId}";
        return null;
    }

    private void RebuildCompanionDeveloper(Entity entity, IServerPlayer owner)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        string ownerUid = owner.PlayerUID;
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string foxId = status?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
        {
            SendState(entity, owner, "Rebuild refused: the companion has no valid pack record.");
            return;
        }

        if (packRepository.GetExpeditionForMember(ownerUid, record.FoxId) != null
            || HasFoxStorageCargo(entity)
            || HasFoxPendingCartPickup(entity))
        {
            SendState(entity, owner, "Rebuild refused while this companion is busy with an expedition or cargo.");
            return;
        }

        FoxPackRecordV2 recordSnapshot;
        try
        {
            recordSnapshot = ClonePackRecordForRebuild(record);
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not snapshot ledger record {0} for developer rebuild: {1}",
                foxId,
                exception
            );
            SendState(entity, owner, "Rebuild refused: the pack record could not be safely snapshotted.");
            return;
        }

        long oldEntityId = entity.EntityId;
        Entity? rebuilt = null;
        try
        {
            rebuildingEntityIds.Add(oldEntityId);
            UnregisterLoadedFox(entity, "Nuclear ledger rebuild");
            entity.Die(EnumDespawnReason.Removed);
            companionAggressiveTargets.Remove(oldEntityId);
            companionAttackers.Remove(oldEntityId);
            if (socialViewByOwner.TryGetValue(ownerUid, out long viewedEntityId)
                && viewedEntityId == oldEntityId)
            {
                socialViewByOwner.Remove(ownerUid);
                EndMenuAttention(oldEntityId);
            }
            if (perkViewByOwner.TryGetValue(ownerUid, out long perkEntityId)
                && perkEntityId == oldEntityId)
            {
                perkViewByOwner.Remove(ownerUid);
                EndMenuAttention(oldEntityId);
            }

            // Discard the old world identity. The recovery builder resolves a
            // currently registered tame type and hydrates a brand-new entity
            // from the persistent ledger, including talent ranks and health.
            record.EntityId = 0;
            packRepository.Save();
            if (!RecoverCompanionDeveloper(owner, foxId, sendResult: false))
            {
                throw new InvalidOperationException("The ledger recovery builder could not spawn the replacement.");
            }

            rebuilt = FindLoadedCompanionByFoxId(foxId);
            if (rebuilt == null || !rebuilt.Alive)
            {
                throw new InvalidOperationException("The ledger recovery builder returned no living replacement.");
            }

            serverChannel?.SendPacket(
                new CompanionDeveloperOpenPacket { TargetEntityId = rebuilt.EntityId },
                owner
            );
            SendPackState(owner, $"Nuclear ledger rebuild complete. {GetFoxRecordLabel(recordSnapshot)} now has entity ID {rebuilt.EntityId}.");
            serverApi.Event.RegisterCallback(_ => rebuildingEntityIds.Remove(oldEntityId), 2000);
        }
        catch (Exception exception)
        {
            if (rebuilt != null && rebuilt.Alive)
            {
                rebuilt.Die(EnumDespawnReason.Removed);
                UnregisterLoadedFox(rebuilt, "Nuclear rebuild failed");
            }

            packRepository.RestoreRecord(recordSnapshot);
            packRepository.Save();
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Nuclear ledger rebuild failed for companion {0}: {1}",
                foxId,
                exception
            );
            SendPackState(owner, "Nuclear ledger rebuild failed. The persistent pack record was restored; the old entity was already removed.");
            serverApi.Event.RegisterCallback(_ => rebuildingEntityIds.Remove(oldEntityId), 2000);
        }
    }

    private void PermanentlyDeleteAllCompanionsDeveloper(IServerPlayer owner)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        RefreshLoadedFoxPackRecords();
        Entity[] loadedOwnedCompanions = serverApi.World.LoadedEntities.Values
            .Where(entity => IsTamedFox(entity) && IsOwner(entity, owner.PlayerUID))
            .ToArray();

        int recordCount = packRepository.PermanentlyDeleteCompanionsForOwner(owner.PlayerUID);
        packRepository.Save();

        socialViewByOwner.Remove(owner.PlayerUID);
        perkViewByOwner.Remove(owner.PlayerUID);
        packViewers.Remove(owner.PlayerUID);
        activeConversationByOwner.Remove(owner.PlayerUID);
        lastConversationContextByOwner.Remove(owner.PlayerUID);
        nextConversationByOwner.Remove(owner.PlayerUID);
        perimeterWalkerByOwner.Remove(owner.PlayerUID);
        seeOffByOwner.Remove(owner.PlayerUID);
        seeOffCooldownByOwner.Remove(owner.PlayerUID);
        lookoutByOwner.Remove(owner.PlayerUID);

        foreach (Entity companion in loadedOwnedCompanions)
        {
            PermanentlyDeleteLoadedCompanionEntity(companion);
        }

        serverApi.Logger.Notification(
            "[FeralKinshipCompanions] Developer {0} permanently deleted {1} companion record(s) and {2} loaded companion entity/entities for owner {3}.",
            owner.PlayerName,
            recordCount,
            loadedOwnedCompanions.Length,
            owner.PlayerUID
        );
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            recordCount == 0
                ? "No companions were found to delete."
                : $"Permanently deleted {recordCount} companion{(recordCount == 1 ? string.Empty : "s")}. Recovery records, active expeditions, assignments, and companion expedition history were removed.",
            EnumChatType.Notification
        );
    }

    private void PermanentlyDeleteLoadedCompanionEntity(Entity entity)
    {
        if (serverApi == null || !permanentlyDeletingEntityIds.Add(entity.EntityId))
        {
            return;
        }

        long entityId = entity.EntityId;
        try
        {
            DropFoxStorageCargo(entity);
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not drop cargo while permanently deleting companion entity {0}: {1}",
                entityId,
                exception
            );
        }

        try
        {
            UnregisterLoadedFox(entity, "Permanently deleted");
            companionAggressiveTargets.Remove(entityId);
            companionAttackers.Remove(entityId);
            companionIdleInvitations.Remove(entityId);
            forcedIdlePlans.Remove(entityId);
            lastFoodUiLevelByEntity.Remove(entityId);
            lastObservedPetAiFeedCooldownByEntity.Remove(entityId);
            triggeredConversationContexts.Remove(entityId);
            conversationPriorityBlocks.Remove(entityId);
            nextDialogueStateScan.Remove(entityId);
            lastDialogueWeather.Remove(entityId);
            dialogueWeatherChangedAt.Remove(entityId);
            spontaneousMoodEpisode.Remove(entityId);
            spontaneousMoodSince.Remove(entityId);
            dialogueCombatState.Remove(entityId);
            dialogueSleepingState.Remove(entityId);
            nextNightDialogue.Remove(entityId);
            nextRecoveryDialogue.Remove(entityId);
            foreach (string key in blueberryModeReservations
                         .Where(pair => pair.Value == entityId)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                blueberryModeReservations.Remove(key);
            }
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Runtime cleanup failed while permanently deleting companion entity {0}: {1}",
                entityId,
                exception
            );
        }

        try
        {
            if (entity.Alive)
            {
                entity.Die(EnumDespawnReason.Removed);
            }
        }
        catch (Exception exception)
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Could not remove permanently deleted companion entity {0}: {1}",
                entityId,
                exception
            );
        }

        serverApi.Event.RegisterCallback(_ => permanentlyDeletingEntityIds.Remove(entityId), 2000);
    }

    private static FoxPackRecordV2 ClonePackRecordForRebuild(FoxPackRecordV2 record)
    {
        using MemoryStream stream = new();
        Serializer.Serialize(stream, record);
        stream.Position = 0;
        return Serializer.Deserialize<FoxPackRecordV2>(stream);
    }

    private static string FormatCooldown(float seconds)
    {
        int totalSeconds = Math.Max(1, (int)Math.Ceiling(seconds));
        int minutes = totalSeconds / 60;
        int remainder = totalSeconds % 60;
        return minutes > 0 ? $"{minutes}:{remainder:00}" : $"0:{remainder:00}";
    }

    private void UpdateMood(Entity entity, ITreeAttribute status)
    {
        bool moodChanged = false;
        bool overrideEnded = false;
        bool forcedMoodEnded = false;

        string forcedMood = status.GetString(PackMoodOverrideKey, string.Empty);
        if (!string.IsNullOrEmpty(forcedMood))
        {
            float forcedMoodRemaining = GetRemainingSeconds(
                status,
                PackMoodOverrideEndsUtcMsKey,
                PackMoodOverrideRemainingKey
            );
            if (forcedMoodRemaining > 0f)
            {
                return;
            }

            status.SetString(PackMoodOverrideKey, string.Empty);
            SetDeadline(status, PackMoodOverrideEndsUtcMsKey, 0f);
            forcedMoodEnded = true;
        }

        string mood = status.GetString(MoodKey, string.Empty);

        if (string.IsNullOrEmpty(mood))
        {
            SetRandomMood(entity, status);
            SetDeadline(status, MoodRandomEndsUtcMsKey, GetNextMoodRandomInterval(entity));
            moodChanged = true;
        }

        string moodOverride = status.GetString(MoodOverrideKey, string.Empty);
        float overrideRemaining = GetRemainingSeconds(
            status,
            MoodOverrideEndsUtcMsKey,
            MoodOverrideRemainingKey
        );
        if (!string.IsNullOrEmpty(moodOverride))
        {
            if (overrideRemaining > 0f)
            {
                if (forcedMoodEnded)
                {
                    MarkSocialStateDirty(entity);
                    RegisterFoxInPack(entity);
                    SendStateToOwner(entity, "");
                    SendPackStateToOwner(entity);
                }
                return;
            }

            status.SetString(MoodOverrideKey, string.Empty);
            status.SetBool(DeveloperMoodForcedKey, false);
            SetDeadline(status, MoodOverrideEndsUtcMsKey, 0f);
            overrideEnded = true;
            if (GetRemainingSeconds(status, MoodRandomEndsUtcMsKey, MoodRandomRemainingKey) <= 0f)
            {
                SetRandomMood(entity, status);
                SetDeadline(status, MoodRandomEndsUtcMsKey, GetNextMoodRandomInterval(entity));
                moodChanged = true;
            }
        }
        else if (GetRemainingSeconds(status, MoodRandomEndsUtcMsKey, MoodRandomRemainingKey) <= 0f)
        {
            SetRandomMood(entity, status);
            SetDeadline(status, MoodRandomEndsUtcMsKey, GetNextMoodRandomInterval(entity));
            moodChanged = true;
        }

        if (moodChanged || overrideEnded || forcedMoodEnded)
        {
            MarkSocialStateDirty(entity);
            RegisterFoxInPack(entity);
            SendStateToOwner(entity, "");
            SendPackStateToOwner(entity);
        }
    }

    private void ApplyPostRequestMood(Entity entity, ITreeAttribute status, string request)
    {
        string mood = request switch
        {
            FoxRequestType.NearOwnerStill => "resting",
            FoxRequestType.NearLight => "resting",
            FoxRequestType.NearHeat => "resting",
            FoxRequestType.Inside => "resting",
            FoxRequestType.StayClose => "content",
            FoxRequestType.StayAway => "calm",
            FoxRequestType.Predator => "relieved",
            FoxRequestType.NearWater => "refreshed",
            FoxRequestType.HigherGround => "alert",
            FoxRequestType.NearTamedAnimal => "social",
            FoxRequestType.AnotherAnimal => "social",
            FoxRequestType.RegularFood => "content",
            FoxRequestType.LuxuryFood => "happy",
            FoxRequestType.HealingItem => "recovered",
            FoxRequestType.OutsideUntilMorning => "rested",
            FoxRequestType.InsideUntilMorning => "rested",
            _ => "content"
        };

        float postRequestDuration = PostRequestMoodDurationSeconds
            + GetFoxPerkRank(entity, "goodwill") * 60f
            + GetFoxPackTalentRank(entity, "goodwill") * PostRequestMoodDurationSeconds * PackTalentCooldownReduction;
        float stableMoodDuration = postRequestDuration
            + GetFoxPerkRank(entity, "gentle-routine") * 60f
            + GetFoxPerkRank(entity, "true-companion") * 120f;
        status.SetString(MoodKey, mood);
        status.SetBool(DeveloperMoodForcedKey, false);
        status.SetString(MoodOverrideKey, mood);
        SetDeadline(status, MoodOverrideEndsUtcMsKey, postRequestDuration);
        SetDeadline(status, MoodRandomEndsUtcMsKey, stableMoodDuration);
    }

    internal void AdjustFoxIncomingDamage(Entity fox, DamageSource? damageSource, ref float damage)
    {
        if (damage <= 0f || !IsTamedFox(fox))
        {
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        long now = UtcNowMs();
        status.SetLong(LastCombatUtcMsKey, now);
        RefreshAdrenalineRush(fox, damage);

        if (damageSource != null
            && IsDirectPhysicalAttack(damageSource)
            && GetFoxDodgeChance(fox) > 0f
            && fox.World.Rand.NextDouble() < GetFoxDodgeChance(fox))
        {
            damage = 0f;
            return;
        }

        float resistance = GetFoxDamageResistance(fox);
        if (damageSource != null && IsNaturalDamage(damageSource))
        {
            resistance += GetFoxNaturalDamageResistance(fox);
        }
        if (resistance > 0f)
        {
            damage *= Math.Max(0f, 1f - Math.Min(0.90f, resistance));
        }

        if (damageSource != null && IsPhysicalDamage(damageSource.Type))
        {
            damage = Math.Max(0f, damage - GetFoxArmorBonus(fox));
            // Pack Guard is intentionally applied after the companion's flat
            // armor talents so percentage and flat defenses have a stable,
            // player-visible order.
            float packGuardResistance = GetFoxPackTalentRank(fox, "pack-guard") * PackTalentPercent;
            damage *= Math.Max(0f, 1f - packGuardResistance);
        }

        EntityBehaviorHealth? health = fox.GetBehavior<EntityBehaviorHealth>();
        if (health != null
            && health.Health > 0f
            && damage >= health.Health
            && !IsFoxMortallyWounded(fox))
        {
            // A fresh lethal hit starts a new mortally-wounded timer. Clear
            // the one-wound stabilization marker before vanilla changes the
            // health state, so Stabilized also works on later wounds.
            status.SetInt(StabilizedWindowAppliedRankKey, 0);
            status.SetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
            status.SetDouble(DeathlessRecoveryAtHoursKey, -1d);
            status.SetDouble(DeathlessFallbackAtHoursKey, -1d);
        }

        int lastStandRank = GetFoxPerkRank(fox, "last-stand");
        long lastStandReadyAt = status.GetLong(LastStandCooldownEndsUtcMsKey, 0);
        if (lastStandRank > 0
            && health != null
            && fox.Alive
            && health.Health > 0f
            && damage >= health.Health
            && (lastStandReadyAt <= 0 || lastStandReadyAt <= now))
        {
            damage = Math.Max(0f, health.Health - 1f);
            status.SetLong(
                LastStandCooldownEndsUtcMsKey,
                now + SecondsToMilliseconds(LastStandCooldownSeconds)
            );

            int secondWindRank = GetFoxPerkRank(fox, "second-wind");
            if (secondWindRank > 0)
            {
                status.SetFloat(
                    SecondWindPendingHealthKey,
                    status.GetFloat(SecondWindPendingHealthKey, 0f) + 2f * secondWindRank
                );
            }

            PlaySoundAtEntity(fox, "feralkinshipcompanions:sounds/perks/last-stand", 0.92f, 28f);
            if (TryGetOwnerPlayer(fox, out IServerPlayer? lastStandOwner))
                EmitDialogueEvent(fox, lastStandOwner!, "perk.refuses_to_die", string.Empty, CompanionDialoguePriority.Critical);
            MarkSocialStateDirty(fox);
        }
    }

    internal void NotifyFoxAttack(Entity fox)
    {
        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        long now = UtcNowMs();
        status.SetLong(LastCombatUtcMsKey, now);
        status.SetLong(LastAttackUtcMsKey, now);

        int battleRhythmRank = GetFoxPerkRank(fox, "battle-rhythm");
        if (battleRhythmRank > 0)
        {
            status.SetLong(
                BattleRhythmEndsUtcMsKey,
                now + SecondsToMilliseconds(BattleRhythmDurationSeconds)
            );
        }

        int freshMeatRank = GetFoxPerkRank(fox, "fresh-meat");
        if (freshMeatRank > 0)
        {
            status.SetLong(
                FreshMeatEndsUtcMsKey,
                now + SecondsToMilliseconds(FreshMeatDurationSeconds)
            );
        }

        if (GetFoxPerkRank(fox, "rallying-scent") > 0
            || GetFoxPackTalentRank(fox, "rallying-scent") > 0)
        {
            ApplyPackMoodToNearby(fox, "rallied");
            if (TryGetOwnerPlayer(fox, out IServerPlayer? owner))
                EmitDialogueEvent(fox, owner!, "combat.rallying_scent", string.Empty, CompanionDialoguePriority.High);
        }
        else if (TryGetOwnerPlayer(fox, out IServerPlayer? owner))
        {
            EmitDialogueEvent(fox, owner!, "combat.attack", string.Empty, CompanionDialoguePriority.Low);
        }
    }

    internal void NotifyFoxDamaged(Entity fox, float damage)
    {
        if (damage <= 0f)
        {
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        long now = UtcNowMs();
        status.SetLong(LastCombatUtcMsKey, now);
        status.SetLong(LastDamageUtcMsKey, now);
        RefreshAdrenalineRush(fox, 0f);

        if (GetFoxPerkRank(fox, "panic-sprint") > 0)
        {
            status.SetLong(
                PanicSprintEndsUtcMsKey,
                now + SecondsToMilliseconds(PanicSprintDurationSeconds)
            );
        }

        if (GetFoxPerkRank(fox, "alarm-call") > 0
            || GetFoxPackTalentRank(fox, "warning-cry") > 0)
        {
            ApplyPackMoodToNearby(fox, "alarmed");
            if (TryGetOwnerPlayer(fox, out IServerPlayer? warningOwner))
                EmitDialogueEvent(fox, warningOwner!, "combat.warning_cry", string.Empty, CompanionDialoguePriority.High);
        }
        if (TryGetOwnerPlayer(fox, out IServerPlayer? owner))
        {
            EmitDialogueEvent(fox, owner!, GetHealthFraction(fox) < .30f ? "injury.badly_wounded" : "combat.hit",
                string.Empty, GetHealthFraction(fox) < .30f ? CompanionDialoguePriority.High : CompanionDialoguePriority.Medium);
        }
    }

    private static bool IsPhysicalDamage(EnumDamageType damageType)
    {
        return damageType is EnumDamageType.BluntAttack
            or EnumDamageType.PiercingAttack
            or EnumDamageType.SlashingAttack;
    }

    private static bool IsDirectPhysicalAttack(DamageSource damageSource)
    {
        return damageSource.Source == EnumDamageSource.Entity
            && damageSource.SourceEntity != null
            && IsPhysicalDamage(damageSource.Type);
    }

    private static bool IsNaturalDamage(DamageSource damageSource)
    {
        return damageSource.Source != EnumDamageSource.Void
            && damageSource.Type is EnumDamageType.Fire
                or EnumDamageType.Frost
                or EnumDamageType.Heat
                or EnumDamageType.Gravity;
    }

    private static void RefreshAdrenalineRush(Entity fox, float incomingDamage)
    {
        int rank = GetFoxPerkRank(fox, "adrenaline-rush");
        if (rank <= 0)
        {
            return;
        }

        EntityBehaviorHealth? health = fox.GetBehavior<EntityBehaviorHealth>();
        if (health == null || health.MaxHealth <= 0f)
        {
            return;
        }

        bool badlyHurt = health.Health <= health.MaxHealth * 0.35f
            || incomingDamage > 0f && health.Health - incomingDamage <= health.MaxHealth * 0.35f;
        if (!badlyHurt)
        {
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(fox, true)!;
        status.SetLong(
            AdrenalineRushEndsUtcMsKey,
            UtcNowMs() + SecondsToMilliseconds(AdrenalineRushDurationSeconds)
        );
    }

    private void ApplyPendingSecondWind(Entity fox, ITreeAttribute status)
    {
        float pendingHealth = status.GetFloat(SecondWindPendingHealthKey, 0f);
        if (pendingHealth <= 0f)
        {
            return;
        }

        EntityBehaviorHealth? health = fox.GetBehavior<EntityBehaviorHealth>();
        if (health != null)
        {
            health.Health = Math.Min(health.MaxHealth, health.Health + pendingHealth);
            health.MarkDirty();
        }

        status.SetFloat(SecondWindPendingHealthKey, 0f);
        if (TryGetOwnerPlayer(fox, out IServerPlayer? owner))
            EmitDialogueEvent(fox, owner!, "perk.one_more_breath", string.Empty, CompanionDialoguePriority.High);
    }

    internal void NotifyFoxRequestCompleted(Entity fox, string request, bool developerRequest)
    {
        if (developerRequest
            || (GetFoxPerkRank(fox, "shared-calm") <= 0
                && GetFoxPackTalentRank(fox, "settling-presence") <= 0))
        {
            return;
        }

        if (request is FoxRequestType.NearOwnerStill
            or FoxRequestType.NearLight
            or FoxRequestType.NearHeat
            or FoxRequestType.Inside
            or FoxRequestType.Shelter
            or FoxRequestType.OutsideUntilMorning
            or FoxRequestType.InsideUntilMorning)
        {
            ApplyPackMoodToNearby(fox, "calm");
        }
    }

    private void ApplyPackMoodToNearby(Entity source, string mood)
    {
        if (serverApi == null || !IsTamedFox(source))
        {
            return;
        }

        ITreeAttribute? sourceStatus = GetDomesticationStatus(source);
        string ownerUid = sourceStatus?.GetString("owner", string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        double rangeSquared = PackMoodRange * PackMoodRange;
        // The fox that sends the signal is part of the response too. This is
        // especially important for Rallying Scent: its damage bonus must be
        // active on the attacker, not only on nearby packmates.
        TryApplyPackMood(source, mood);

        foreach (Entity target in loadedFoxes.Values.ToArray())
        {
            if (target == source
                || !IsTamedFox(target)
                || target.Pos.Dimension != source.Pos.Dimension
                || target.Pos.SquareDistanceTo(source.Pos) > rangeSquared
                || !string.Equals(
                    GetDomesticationStatus(target)?.GetString("owner", string.Empty),
                    ownerUid,
                    StringComparison.Ordinal
                ))
            {
                continue;
            }

            TryApplyPackMood(target, mood);
        }
    }

    private void TryApplyPackMood(Entity target, string mood)
    {
        ITreeAttribute status = GetDomesticationStatus(target, true)!;
        string currentForcedMood = status.GetString(PackMoodOverrideKey, string.Empty);
        if (!string.IsNullOrEmpty(currentForcedMood)
            && GetRemainingSeconds(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideRemainingKey) > 0f)
        {
            return;
        }

        status.SetString(PackMoodOverrideKey, mood);
        SetDeadline(status, PackMoodOverrideEndsUtcMsKey, PackMoodOverrideDurationSeconds);
        MarkSocialStateDirty(target);
        RegisterFoxInPack(target);
        SendStateToOwner(target, string.Empty);
        SendPackStateToOwner(target);
    }

    private static float GetNextMoodRandomInterval(Entity entity)
    {
        return MoodRandomIntervalMinimumSeconds
            + entity.World.Rand.NextSingle() * (MoodRandomIntervalMaximumSeconds - MoodRandomIntervalMinimumSeconds);
    }

    private void SetRandomMood(Entity entity, ITreeAttribute status)
    {
        List<string> moods = new(RandomFoxMoods);
        string personality = status.GetString(PersonalityKey, string.Empty);
        switch (personality)
        {
            case "timid":
            case "skittish":
                moods.AddRange(new[] { "alert", "anxious" });
                break;
            case "bold":
            case "protective":
            case "territorial":
                moods.AddRange(new[] { "alert", "content" });
                break;
            case "curious":
            case "playful":
                moods.AddRange(new[] { "curious", "playful" });
                break;
            case "restless":
                moods.AddRange(new[] { "restless", "curious" });
                break;
            case "homebody":
                moods.AddRange(new[] { "calm", "sleepy" });
                break;
            case "social":
            case "affectionate":
                moods.AddRange(new[] { "content", "playful" });
                break;
            case "solitary":
            case "independent":
                moods.AddRange(new[] { "calm", "alert" });
                break;
            case "greedy":
                moods.AddRange(new[] { "curious", "content" });
                break;
            case "demanding":
                moods.AddRange(new[] { "restless", "alert" });
                break;
            case "stubborn":
                moods.AddRange(new[] { "calm", "content" });
                break;
        }

        if (IsLowHealth(entity))
        {
            moods.AddRange(new[] { "anxious", "sleepy", "anxious" });
        }
        if (IsDark(entity) && GetFoxPerkRank(entity, "night-eyes") <= 0)
        {
            moods.Add("anxious");
        }
        if (IsPrecipitating(entity) && !IsInside(entity) && GetFoxPerkRank(entity, "weatherwise") <= 0)
        {
            moods.AddRange(new[] { "alert", "anxious" });
        }

        status.SetString(MoodKey, moods[entity.World.Rand.Next(moods.Count)]);
    }

    private bool IsRequestConditionMet(Entity entity, ITreeAttribute status, string request)
    {
        switch (request)
        {
            case FoxRequestType.Predator:
            {
                if (status.GetBool(PredatorTargetKilledKey, false)) return true;
                Entity? predator = serverApi?.World.GetEntityById(status.GetLong(PredatorTargetKey, 0));
                return predator != null
                    && (!predator.Alive
                        || entity.Pos.Dimension != predator.Pos.Dimension
                        || entity.Pos.SquareDistanceTo(predator.Pos) >= PredatorFarDistance * PredatorFarDistance);
            }
            case FoxRequestType.StayClose:
                return GetOwnerEntity(entity)?.Pos.SquareDistanceTo(entity.Pos) <= StayCloseDistance * StayCloseDistance;
            case FoxRequestType.StayAway:
                return GetOwnerEntity(entity)?.Pos.SquareDistanceTo(entity.Pos) >= StayAwayDistance * StayAwayDistance;
            case FoxRequestType.Inside:
                return IsInside(entity);
            case FoxRequestType.NearOwnerStill:
            {
                Entity? owner = GetOwnerEntity(entity);
                return owner != null
                    && owner.Pos.SquareDistanceTo(entity.Pos) <= StayCloseDistance * StayCloseDistance
                    && owner.Pos.Motion.Length() <= StillMotionThreshold;
            }
            case FoxRequestType.TravelDistance:
            case FoxRequestType.WalkDistance:
                return DistanceFromRequestStart(entity, status) >= TravelDistance;
            case FoxRequestType.HigherGround:
                return entity.Pos.Y >= status.GetDouble(RequestStartYKey, entity.Pos.Y) + HigherGround;
            case FoxRequestType.NearWater:
                return GetEnvironmentSnapshot(entity).NearWater;
            case FoxRequestType.NearLight:
                return GetEnvironmentSnapshot(entity).NearLight;
            case FoxRequestType.NearHeat:
                return GetEnvironmentSnapshot(entity).NearHeat;
            case FoxRequestType.Shelter:
                return IsPrecipitating(entity) && IsInside(entity);
            case FoxRequestType.OutsideClear:
                return !IsPrecipitating(entity) && !IsInside(entity);
            case FoxRequestType.NearTamedAnimal:
                return FindNearbyTamedAnimal(entity) != null;
            case FoxRequestType.LargeTree:
                return GetEnvironmentSnapshot(entity).NearLargeTree;
            case FoxRequestType.CropField:
                return GetEnvironmentSnapshot(entity).NearCropField;
            case FoxRequestType.Trader:
                return FindNearbyEntity(entity, IsTrader) != null;
            case FoxRequestType.MechanicalDevice:
                return GetEnvironmentSnapshot(entity).NearMechanicalDevice;
            case FoxRequestType.AnotherAnimal:
                return FindNearbyAnimal(entity) != null;
            case FoxRequestType.RegularFood:
            case FoxRequestType.LuxuryFood:
            case FoxRequestType.NonFoodConsumed:
            case FoxRequestType.NonFoodNearby:
            case FoxRequestType.HealingItem:
            case FoxRequestType.PlaceableNearby:
                return false;
            case FoxRequestType.OutsideUntilMorning:
                return HasReachedMorning(entity, status) && !IsInside(entity);
            case FoxRequestType.InsideUntilMorning:
                return HasReachedMorning(entity, status) && IsInside(entity);
            default:
                return false;
        }
    }

    private bool TryInitializeItemRequest(Entity entity, IServerPlayer owner, string request, ITreeAttribute status, out string message)
    {
        message = string.Empty;
        status.SetString(RequestedItemCodeKey, string.Empty);
        status.SetInt(RequestedItemCountKey, 0);

        FoxRequestDeliveryMode deliveryMode = FoxRequestCatalog.GetDeliveryMode(request);
        if (deliveryMode == FoxRequestDeliveryMode.None)
        {
            return true;
        }

        ItemStack? requestedStack = FindFirstMatchingItem(owner, request);
        if (requestedStack?.Collectible?.Code == null)
        {
            message = "No item in your inventory matches that item-test category.";
            return false;
        }

        string code = requestedStack.Collectible.Code.ToShortString();
        status.SetString(RequestedItemCodeKey, code);
        message = deliveryMode switch
        {
            FoxRequestDeliveryMode.ShowHeldItem => $"Request generated for {code}. Hold it and right-click this companion; it will not be consumed.",
            FoxRequestDeliveryMode.PlaceBlock => $"Request generated for {code}. Place that exact block within ten blocks of this companion.",
            _ => $"Request generated for {code}. Hold it and right-click this companion to hand over one."
        };
        return true;
    }

    private static ItemStack? FindFirstMatchingItem(IPlayer player, string request)
    {
        foreach (InventoryBase inventory in player.InventoryManager.InventoriesOrdered)
        {
            if (inventory == null || string.Equals(inventory.ClassName, GlobalConstants.creativeInvClassName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int slotIndex = 0; slotIndex < inventory.Count; slotIndex++)
            {
                ItemStack? stack = inventory[slotIndex].Itemstack;
                if (stack?.Collectible?.Code != null && IsValidHardItem(request, stack, player.Entity))
                {
                    return stack;
                }
            }
        }

        return null;
    }

    private static bool IsValidHardItem(string request, ItemStack stack, Entity owner)
    {
        FoodNutritionProperties? nutrition = stack.Collectible?.GetNutritionProperties(owner.World, stack, owner);
        string path = stack.Collectible?.Code?.Path ?? string.Empty;
        bool isFood = nutrition != null;
        bool isLuxury = isFood && (nutrition!.Satiety >= 80f
            || path.Contains("meal", StringComparison.OrdinalIgnoreCase)
            || path.Contains("cake", StringComparison.OrdinalIgnoreCase)
            || path.Contains("pie", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bread", StringComparison.OrdinalIgnoreCase)
            || path.Contains("cheese", StringComparison.OrdinalIgnoreCase)
            || path.Contains("jam", StringComparison.OrdinalIgnoreCase)
            || path.Contains("honey", StringComparison.OrdinalIgnoreCase));
        bool isHealing = nutrition?.Health > 0f
            || path.Contains("bandage", StringComparison.OrdinalIgnoreCase)
            || path.Contains("medicine", StringComparison.OrdinalIgnoreCase)
            || path.Contains("healing", StringComparison.OrdinalIgnoreCase)
            || path.Contains("poultice", StringComparison.OrdinalIgnoreCase);

        return request switch
        {
            FoxRequestType.RegularFood => isFood && !isLuxury,
            FoxRequestType.LuxuryFood => isLuxury,
            FoxRequestType.NonFoodConsumed => !isFood && !isHealing,
            FoxRequestType.NonFoodNearby => !isFood && !isHealing,
            FoxRequestType.HealingItem => isHealing,
            FoxRequestType.PlaceableNearby => stack.Collectible is Block,
            _ => false
        };
    }

    private static bool IsMatchingItem(ItemStack? stack, string code)
    {
        return stack?.Collectible?.Code != null
            && string.Equals(stack.Collectible.Code.ToShortString(), code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasReachedMorning(Entity entity, ITreeAttribute status)
    {
        double startDay = status.GetDouble(RequestStartDayKey, Math.Floor(entity.World.Calendar.TotalDays));
        double hour = entity.World.Calendar.HourOfDay;
        return Math.Floor(entity.World.Calendar.TotalDays) > startDay
            && hour >= entity.World.Calendar.HoursPerDay * 0.25f
            && hour <= entity.World.Calendar.HoursPerDay * 0.75f;
    }

    private static double DistanceFromRequestStart(Entity entity, ITreeAttribute status)
    {
        double dx = entity.Pos.X - status.GetDouble(RequestStartXKey, entity.Pos.X);
        double dy = entity.Pos.Y - status.GetDouble(RequestStartYKey, entity.Pos.Y);
        double dz = entity.Pos.Z - status.GetDouble(RequestStartZKey, entity.Pos.Z);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private bool IsPrecipitating(Entity entity)
    {
        return weatherSystem?.GetPrecipitation(entity.Pos.XYZ) > 0.1f;
    }

    private void UpdateEarlyWarning(Entity entity, ITreeAttribute status)
    {
        if (GetFoxPerkRank(entity, "early-warning") <= 0
            || status.GetLong(EarlyWarningEndsUtcMsKey, 0) > UtcNowMs())
        {
            return;
        }

        long nowMs = entity.World.ElapsedMilliseconds;
        if (nextEarlyWarningScanAtMs.TryGetValue(entity.EntityId, out long nextScanAtMs)
            && nowMs < nextScanAtMs)
        {
            return;
        }
        nextEarlyWarningScanAtMs[entity.EntityId] = nowMs + EarlyWarningScanIntervalMs;
        if (FindNearestPredator(entity) == null) return;

        if (entity.Properties?.ResolvedSounds != null
            && entity.Properties.ResolvedSounds.TryGetValue("idle", out AssetLocation[]? warningSounds)
            && warningSounds?.Length > 0)
        {
            entity.World.PlaySoundAt(warningSounds[entity.World.Rand.Next(warningSounds.Length)], entity, null, true, 24f, 1.15f);
        }
        SetDeadline(status, EarlyWarningEndsUtcMsKey, 45f);
    }

    private bool IsCold(Entity entity)
    {
        if (serverApi == null)
        {
            return false;
        }

        float temperature = serverApi.World.BlockAccessor
            .GetClimateAt(entity.Pos.AsBlockPos, EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, entity.World.Calendar.TotalDays)
            .Temperature;
        return temperature < ColdTemperatureThreshold;
    }

    private bool IsDark(Entity entity)
    {
        return serverApi != null
            && serverApi.World.BlockAccessor.GetLightLevel(entity.Pos.AsBlockPos, EnumLightLevelType.MaxTimeOfDayLight) < LightSourceLevel;
    }

    private Entity? FindNearbyTamedAnimal(Entity fox)
    {
        string ownerId = GetDomesticationStatus(fox)?.GetString("owner") ?? string.Empty;
        if (serverApi == null || string.IsNullOrEmpty(ownerId))
        {
            return null;
        }

        float packSenseDistance = StayCloseDistance
            + GetFoxPerkRank(fox, "pack-sense") * 2f
            + GetFoxPackTalentRank(fox, "pack-awareness") * 4f
            + (string.Equals(GetMood(fox), "social", StringComparison.OrdinalIgnoreCase) ? 4f : 0f);
        return serverApi.World.GetEntitiesAround(fox.Pos.XYZ, packSenseDistance, packSenseDistance)
            .Where(entity => entity != fox && entity.Alive && entity is EntityAgent)
            .FirstOrDefault(entity => string.Equals(GetDomesticationStatus(entity)?.GetString("owner"), ownerId, StringComparison.Ordinal));
    }

    private float GetFoxObjectProximityDistance(Entity fox)
    {
        return ObjectProximityDistance
            + (string.Equals(GetMood(fox), "curious", StringComparison.OrdinalIgnoreCase) ? 4f : 0f)
            + GetFoxPerkRank(fox, "alert-senses") * AlertSensesRangePerRank
            + (IsDark(fox) && GetFoxPerkRank(fox, "night-eyes") > 0 ? 4f : 0f);
    }

    private Entity? FindNearbyAnimal(Entity fox)
    {
        if (serverApi == null)
        {
            return null;
        }

        return serverApi.World.GetEntitiesAround(fox.Pos.XYZ, StayCloseDistance, StayCloseDistance)
            .Where(entity => entity != fox
                && entity.Alive
                && entity is EntityAgent
                && entity is not EntityPlayer
                && entity.Tags.Overlaps(animalTag))
            .FirstOrDefault();
    }

    private Entity? FindNearbyEntity(Entity source, System.Func<Entity, bool> matcher)
    {
        if (serverApi == null)
        {
            return null;
        }

        float proximityDistance = GetFoxObjectProximityDistance(source);
        return serverApi.World.GetEntitiesAround(source.Pos.XYZ, proximityDistance, proximityDistance)
            .Where(entity => entity != source && entity.Alive)
            .FirstOrDefault(matcher);
    }

    private static bool IsActiveHeatSource(Block block, IBlockAccessor accessor, BlockPos position)
    {
        string path = block.Code?.Path ?? string.Empty;
        bool namedHeatSource = path.Contains("firepit", StringComparison.OrdinalIgnoreCase)
            || path.Contains("torch", StringComparison.OrdinalIgnoreCase)
            || path.Contains("lantern", StringComparison.OrdinalIgnoreCase)
            || path.Contains("lamp", StringComparison.OrdinalIgnoreCase)
            || path.Contains("brazier", StringComparison.OrdinalIgnoreCase)
            || path.Contains("forge", StringComparison.OrdinalIgnoreCase)
            || path.Contains("oven", StringComparison.OrdinalIgnoreCase);
        if (!namedHeatSource)
        {
            return false;
        }

        byte[]? lightHsv = block.GetLightHsv(accessor, position);
        return lightHsv?.Length >= 3 && lightHsv[2] > 0;
    }

    private static bool IsTreeBlock(Block block)
    {
        string path = block.Code?.Path ?? string.Empty;
        return path.StartsWith("log-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("log", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLargeTreeAt(Block block, IBlockAccessor accessor, BlockPos position)
    {
        if (!IsTreeBlock(block))
        {
            return false;
        }

        BlockPos check = new(position.X, position.Y, position.Z, position.dimension);
        int verticalLogs = 1;
        for (int offset = 1; offset <= 4; offset++)
        {
            check.Set(position.X, position.Y + offset, position.Z);
            if (IsTreeBlock(accessor.GetBlock(check)))
            {
                verticalLogs++;
            }
        }
        if (verticalLogs < 3)
        {
            return false;
        }

        for (int x = position.X - 2; x <= position.X + 2; x++)
        {
            for (int y = position.Y + 2; y <= position.Y + 5; y++)
            {
                for (int z = position.Z - 2; z <= position.Z + 2; z++)
                {
                    check.Set(x, y, z);
                    string path = accessor.GetBlock(check).Code?.Path ?? string.Empty;
                    if (path.StartsWith("leaves", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool IsCropFieldBlock(Block block)
    {
        string path = block.Code?.Path ?? string.Empty;
        return path.StartsWith("farmland-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("crop-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMechanicalBlock(Block block)
    {
        string path = block.Code?.Path ?? string.Empty;
        return path.Contains("mechanical", StringComparison.OrdinalIgnoreCase)
            || path.Contains("quern", StringComparison.OrdinalIgnoreCase)
            || path.Contains("helvehammer", StringComparison.OrdinalIgnoreCase)
            || path.Contains("windmill", StringComparison.OrdinalIgnoreCase)
            || path.Contains("pulverizer", StringComparison.OrdinalIgnoreCase)
            || path.Contains("fruitpress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTrader(Entity entity)
    {
        return entity.Code?.Path.Contains("trader", StringComparison.OrdinalIgnoreCase) == true;
    }

    private Entity? FindNearestPredator(Entity fox)
    {
        if (serverApi == null)
        {
            return null;
        }

        float searchRange = PredatorSearchRange
            + GetFoxPerkRank(fox, "predator-awareness") * PredatorAwarenessRangePerRank
            + GetFoxPackTalentRank(fox, "danger-sense") * PredatorAwarenessRangePerRank
            + (GetMood(fox) is "alert" or "alarmed" ? AlertPredatorRangeBonus : 0f)
            + (IsDark(fox) && GetFoxPerkRank(fox, "night-eyes") > 0 ? 4f : 0f);
        float horizontalRangeSquared = searchRange * searchRange;
        return serverApi.World.GetEntitiesAround(fox.Pos.XYZ, searchRange, searchRange)
            .Where(entity => IsPredator(entity)
                && entity.Pos.Dimension == fox.Pos.Dimension
                && Math.Abs(entity.Pos.Y - fox.Pos.Y) <= 8d
                && HorizontalDistanceSquared(entity.Pos.X - fox.Pos.X, entity.Pos.Z - fox.Pos.Z)
                    <= horizontalRangeSquared
                && !IsTamedOrBeingTamed(entity))
            .OrderBy(entity => HorizontalDistanceSquared(
                entity.Pos.X - fox.Pos.X,
                entity.Pos.Z - fox.Pos.Z))
            .FirstOrDefault();
    }

    private static double HorizontalDistanceSquared(double deltaX, double deltaZ)
    {
        return deltaX * deltaX + deltaZ * deltaZ;
    }

    private static bool IsTamedOrBeingTamed(Entity entity)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string level = status?.GetString("domesticationLevel", string.Empty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(status?.GetString("owner", string.Empty))
            || string.Equals(level, "TAMING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(level, "DOMESTICATED", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPredatorTargetClue(Entity fox, Entity? target)
    {
        if (target == null || target.Code == null) return string.Empty;

        string name = target.GetName();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = target.Code.Path.Replace('-', ' ');
        }

        double deltaX = target.Pos.X - fox.Pos.X;
        double deltaZ = target.Pos.Z - fox.Pos.Z;
        double distance = Math.Sqrt(HorizontalDistanceSquared(deltaX, deltaZ));
        int approximateDistance = Math.Max(5, (int)Math.Round(distance / 5d) * 5);
        double bearing = Math.Atan2(deltaX, -deltaZ) * 180d / Math.PI;
        if (bearing < 0d) bearing += 360d;
        string[] directions = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
        string direction = directions[(int)Math.Round(bearing / 45d) % directions.Length];
        return $"Target: {name}, roughly {approximateDistance} blocks {direction} of me when I asked.";
    }

    private static bool IsPredator(Entity entity)
    {
        if (!entity.Alive || entity is not EntityAgent || entity is EntityPlayer || entity.Code == null)
        {
            return false;
        }

        return IsPredatorSpecies(entity);
    }

    private static bool IsPredatorSpecies(Entity entity)
    {
        if (entity is not EntityAgent || entity is EntityPlayer || entity.Code == null) return false;
        string path = entity.Code.Path;
        return path.StartsWith("wolf-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bear-", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("hyena-", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsInside(Entity entity)
    {
        BlockPos position = entity.Pos.AsBlockPos;
        Room? room = roomRegistry?.GetRoomForPosition(position);
        if (room != null)
        {
            // This matches the vanilla enclosed-room test used by body temperature.
            return room.ExitCount == 0 || room.SkylightCount < room.NonSkylightCount;
        }

        return serverApi?.World.BlockAccessor.GetRainMapHeightAt(position) > entity.Pos.Y;
    }

    private static bool IsLowHealth(Entity entity)
    {
        ITreeAttribute? health = entity.WatchedAttributes.GetTreeAttribute("health");
        float maxHealth = health?.GetFloat("maxhealth", 0f) ?? 0f;
        float currentHealth = health?.GetFloat("currenthealth", maxHealth) ?? maxHealth;
        return maxHealth > 0f && currentHealth <= maxHealth * 0.5f;
    }

    private void RefreshLoadedFoxPackRecords()
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        packRepository.MarkAllUnloaded();
        foreach (Entity entity in serverApi.World.LoadedEntities.Values)
        {
            if (!IsTamedFox(entity))
            {
                continue;
            }

            string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty)
                ?? string.Empty;
            if (packRepository.IsPermanentlyDeleted(foxId))
            {
                PermanentlyDeleteLoadedCompanionEntity(entity);
                continue;
            }

            loadedFoxes[entity.EntityId] = entity;
            EnsureFoxNumberIfNeeded(entity);
            EnsureFoxPersonality(entity);
            RegisterFoxInPack(entity);
        }

        RefreshUnloadedRecoveryStates();
    }

    private void RefreshUnloadedRecoveryStates()
    {
        if (!companionContentReady || serverApi == null || packRepository?.Loaded != true)
            return;

        using IDisposable saveBatch = packRepository.BatchSaves();
        foreach (FoxPackRecordV2 record in packRepository.GetAllRecords())
        {
            ReconcileMissingCompanionContent(record);
            if (record.Archived
                || string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase)
                || record.Status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
                || record.Status.StartsWith("Leaving —", StringComparison.OrdinalIgnoreCase))
                continue;

            Entity? loaded = FindLoadedCompanionForRecovery(record.FoxId);
            if (loaded != null)
            {
                record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
                continue;
            }

            if (CanRestoreFromLedger(record, out _))
            {
                // Old saves may have classified missing content permanently.
                if (string.Equals(record.Status, "Unrecoverable", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(record.Status, "Present", StringComparison.OrdinalIgnoreCase))
                    record.Status = "Not currently loaded";
                record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
            }
            else
            {
                // An incomplete legacy record or unavailable definition is not
                // evidence that the original animal or its owner is gone.
                record.RecoveryStatus = CompanionRecoveryStatus.Unknown;
            }
        }
    }

    private bool IsRecordedEntityTypeAvailable(FoxPackRecordV2 record, out string reason)
    {
        return ResolveRecoveryEntityType(record, out reason) != null;
    }

    private void RegisterFoxInPack(Entity entity)
    {
        if (serverApi == null || packRepository?.Loaded != true || !IsTamedFox(entity)
            || !TryGetCompanionSpecies(entity, out CompanionSpeciesProfile species))
        {
            return;
        }

        ITreeAttribute? status = GetDomesticationStatus(entity);
        int number = status?.GetInt(NumberKey, 0) ?? 0;
        string ownerUid = status?.GetString("owner") ?? string.Empty;
        if (status == null || number <= 0 || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        ApplyPackTalentSnapshot(entity);

        string existingFoxId = status.GetString(FoxIdKey, string.Empty);
        if (packRepository.IsPermanentlyDeleted(existingFoxId))
        {
            return;
        }
        string foxId = packRepository.ResolveOrCreateFoxId(existingFoxId, ownerUid, number, species.Id);
        if (!string.Equals(existingFoxId, foxId, StringComparison.Ordinal))
        {
            status.SetString(FoxIdKey, foxId);
            MarkSocialStateDirty(entity);
        }

        int legacyEntityPackPoints = status.GetInt(PackPointsKey, -1);
        packRepository.TryMigrateEntityPackPoints(ownerUid, legacyEntityPackPoints);
        if (legacyEntityPackPoints >= 0)
        {
            status.RemoveAttribute(PackPointsKey);
            MarkSocialStateDirty(entity);
        }

        packRepository.ObserveNumber(number);
        bool recordAlreadyExisted = packRepository.TryGetRecord(foxId, out FoxPackRecordV2? existingRecord)
            && existingRecord != null;
        FoxPackRecordV2 record = packRepository.GetOrCreateRecord(foxId, ownerUid, number, species.Id);
        ReconcileMissingCompanionContent(record);
        if (recordAlreadyExisted
            && record.DurableStateInitialized
            && record.HealthState == MortallyWoundedHealthState
            && IsLiveHealthStateNotMortal(entity))
        {
            // Do not hydrate a stale expedition injury back onto a companion
            // that was already healed in the live world.
            int liveHealthState = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
            record.HealthState = liveHealthState;
            record.MortallyWoundedStartHours = liveHealthState == RecoveringHealthState
                ? entity.WatchedAttributes.GetDouble("mortallyWoundedTotalHours", 0d)
                : 0d;
            record.StabilizedWindowAppliedRank = 0;
            record.StabilizedWindowAdjustedStartHours = -1d;
            record.DeathlessRecoveryAtHours = -1d;
            record.DeathlessFallbackAtHours = -1d;
            if (string.Equals(record.Status, "Mortally wounded", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = liveHealthState == RecoveringHealthState
                    ? "Recovering"
                    : "Present";
            }
        }
        if (recordAlreadyExisted
            && record.DurableStateInitialized
            && ledgerHydratedEntities.Add(entity.EntityId))
        {
            HydrateFoxFromLedger(entity, status, record);
            HydrateCompanionBackpackFromRecord(entity, status, record);
            ApplyFoxDerivedStats(entity);
        }
        else if (!recordAlreadyExisted || !record.DurableStateInitialized)
        {
            ledgerHydratedEntities.Add(entity.EntityId);
        }

        EnsureCompanionCommandState(entity, status);

        RetireRemovedTalentRanks(entity, status);

        record.EntityId = entity.EntityId;
        record.EntityCode = entity.Code?.ToShortString() ?? record.EntityCode;
        record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
        if (string.IsNullOrWhiteSpace(record.SpeciesId))
        {
            record.SpeciesId = species.Id;
        }
        else if (!string.Equals(record.SpeciesId, species.Id, StringComparison.Ordinal))
        {
            serverApi.Logger.Error(
                "[FeralKinshipCompanions] Refused to rewrite companion identity {0} from species {1} to {2}.",
                record.FoxId,
                record.SpeciesId,
                species.Id
            );
            return;
        }
        record.Name = GetFoxDisplayName(entity);
        record.ActivityMode = GetCompanionActivityMode(entity);
        record.FollowDistance = GetCompanionFollowDistance(entity);
        record.CombatStyle = GetCompanionCombatStyle(entity);
        record.RiskTolerance = GetCompanionRiskTolerance(entity);
        record.GroundCleanupEnabled = IsGroundCleanupEnabled(entity);
        record.GroundDroppedItemsEnabled = IsGroundDroppedItemsEnabled(entity);
        record.GroundCattailsEnabled = IsGroundCattailsEnabled(entity);
        record.GroundFlintEnabled = IsGroundFlintEnabled(entity);
        record.GroundSticksEnabled = IsGroundSticksEnabled(entity);
        record.GroundBouldersEnabled = IsGroundBouldersEnabled(entity);
        record.GroundRocksEnabled = IsGroundRocksEnabled(entity);
        record.DutyOptionsInitialized = true;
        record.MowLawnEnabled = IsMowLawnEnabled(entity);
        record.FinishedProductsEnabled = IsFinishedProductsEnabled(entity);
        record.FinishedCropsEnabled = IsFinishedCropsEnabled(entity);
        record.FinishedBerriesEnabled = IsFinishedBerriesEnabled(entity);
        record.FinishedMushroomsEnabled = IsFinishedMushroomsEnabled(entity);
        record.FlowerRemovalEnabled = IsFlowerRemovalEnabled(entity);
        record.SnowShovelingEnabled = IsSnowShovelingEnabled(entity);
        record.CharcoalShovelingEnabled = IsCharcoalShovelingEnabled(entity);
        record.SnowballCollectionEnabled = IsSnowballCollectionEnabled(entity);
        record.GeneralStorageSortingEnabled = IsGeneralStorageSortingEnabled(entity);
        EnsureCompanionFoodState(entity);
        record.FoodStateInitialized = status.GetBool(FoodStateInitializedKey, false);
        record.FoodLevel = Math.Clamp(status.GetFloat(FoodLevelKey, 1f), 0f, 1f);
        record.FoodLastUpdateTotalHours = status.GetDouble(
            FoodLastUpdateTotalHoursKey,
            entity.World.Calendar.TotalHours);
        record.BreedingEnabled = status.GetBool(BreedingEnabledKey, false);
        record.BondedPartnerId = status.GetString(BondedPartnerIdKey, string.Empty);
        record.BondedPartnerName = status.GetString(BondedPartnerNameKey, string.Empty);
        record.LastBreedingAttemptNight = status.GetLong(LastBreedingAttemptNightKey, long.MinValue);
        record.BreedingFeedback = status.GetString(BreedingFeedbackKey, string.Empty);
        record.PregnancyActive = status.GetBool(PregnancyActiveKey, false);
        record.PregnancyStartTotalHours = status.GetDouble(PregnancyStartTotalHoursKey, 0d);
        record.PregnancyDueTotalHours = status.GetDouble(PregnancyDueTotalHoursKey, 0d);
        record.PregnancyFatherId = status.GetString(PregnancyFatherIdKey, string.Empty);
        record.PregnancyFatherName = status.GetString(PregnancyFatherNameKey, string.Empty);
        record.PendingBirth = status.GetBool(PendingBirthKey, false);
        record.IsJuvenile = status.GetBool(JuvenileKey, false);
        record.ParentMotherId = status.GetString(ParentMotherIdKey, string.Empty);
        record.ParentMotherName = status.GetString(ParentMotherNameKey, string.Empty);
        record.ParentFatherId = status.GetString(ParentFatherIdKey, string.Empty);
        record.ParentFatherName = status.GetString(ParentFatherNameKey, string.Empty);
        record.BankedTalentPoints = Math.Max(0, status.GetInt(BankedTalentPointsKey, 0));
        record.JuvenilePointsTransferred = status.GetBool(JuvenilePointsTransferredKey, false);
        record.ChildAdultEntityCode = status.GetString(ChildAdultEntityCodeKey, string.Empty);
        record.Level = Math.Max(CompanionProgressionRules.StartingLevel, status.GetInt(CompanionLevelKey, CompanionProgressionRules.StartingLevel));
        record.CurrentLevelExperience = Math.Max(0L, status.GetLong(CompanionCurrentExperienceKey, 0L));
        record.LifetimeExperience = Math.Max(record.CurrentLevelExperience, status.GetLong(CompanionLifetimeExperienceKey, 0L));
        record.AdulthoodExperienceGranted = status.GetBool(AdulthoodExperienceGrantedKey, false);
        record.CleanupDutyUnits = Math.Clamp(
            status.GetInt(CleanupDutyUnitsKey, 0),
            0,
            CompanionProgressionRules.CleanupActionsPerExperience - 1);
        record.ActivityStartedUtcMs = Math.Max(0, status.GetLong(ActivityStartedUtcMsKey, 0));
        record.ActivityArrivedUtcMs = Math.Max(0, status.GetLong(ActivityArrivedUtcMsKey, 0));
        record.Personality = status.GetString(PersonalityKey) ?? string.Empty;
        record.Mood = GetMoodDisplay(entity);
        record.RequestsGenerated = status.GetInt(RequestsGeneratedKey, 0);
        record.RequestsCompleted = status.GetInt(RequestsCompletedKey, 0);
        record.Points = Math.Max(0, status.GetInt(FoxPointsKey, 0));
        int minimumLifetimePoints = record.Points;
        ITreeAttribute? perkTree = GetFoxPerkTree(entity);
        if (perkTree != null)
        {
            minimumLifetimePoints += FoxPerkCatalog.GetTotalSpent(perkTree);
        }
        int lifetimePoints = Math.Max(
            minimumLifetimePoints,
            status.GetInt(FoxLifetimePointsKey, record.LifetimePoints)
        );
        record.LifetimePoints = Math.Max(record.LifetimePoints, lifetimePoints);
        if (status.GetInt(FoxLifetimePointsKey, -1) != record.LifetimePoints)
        {
            status.SetInt(FoxLifetimePointsKey, record.LifetimePoints);
            MarkSocialStateDirty(entity);
        }
        record.ActiveRequest = GetRequestLabel(
            entity,
            status.GetString(ActiveRequestKey) ?? string.Empty,
            record.Personality
        );
        record.LastCompleted = GetRequestLabel(
            entity,
            status.GetString(LastCompletedKey) ?? string.Empty,
            record.Personality
        );
        record.ActiveRequestId = status.GetString(ActiveRequestKey, string.Empty);
        record.LastCompletedId = status.GetString(LastCompletedKey, string.Empty);
        record.PredatorTargetEntityId = status.GetLong(PredatorTargetKey, 0);
        record.PredatorTargetClue = status.GetString(PredatorTargetClueKey, string.Empty);
        record.PredatorTargetKilled = status.GetBool(PredatorTargetKilledKey, false);
        record.RequestProgress = status.GetFloat(RequestProgressKey, 0f);
        record.RequestQualifiedSinceUtcMs = status.GetLong(RequestQualifiedSinceUtcMsKey, 0);
        record.RequestExpiresUtcMs = status.GetLong(RequestExpiresUtcMsKey, 0);
        record.RequestIsDeveloper = status.GetBool(RequestIsDeveloperKey, false);
        record.RequestedItemCode = status.GetString(RequestedItemCodeKey, string.Empty);
        record.RequestedItemCount = status.GetInt(RequestedItemCountKey, 0);
        record.RequestCooldownEndsUtcMs = status.GetLong(RequestCooldownEndsUtcMsKey, 0);
        record.CancelCooldownEndsUtcMs = status.GetLong(CancelCooldownEndsUtcMsKey, 0);
        record.MoodId = status.GetString(MoodKey, string.Empty);
        record.AutomaticRetreatActive = status.GetBool(AutomaticRetreatActiveKey, false);
        record.AutomaticRetreatPreviousCombatStyle = status.GetString(AutomaticRetreatPreviousCombatStyleKey, string.Empty);
        record.AutomaticRetreatPreviousActivity = status.GetString(AutomaticRetreatPreviousActivityKey, string.Empty);
        record.HealthState = entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0);
        record.MortallyWoundedStartHours = entity.WatchedAttributes.GetDouble("mortallyWoundedTotalHours", 0d);
        record.StabilizedWindowAppliedRank = status.GetInt(StabilizedWindowAppliedRankKey, 0);
        record.StabilizedWindowAdjustedStartHours = status.GetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
        record.DeathlessRecoveryAtHours = status.GetDouble(DeathlessRecoveryAtHoursKey, -1d);
        record.DeathlessFallbackAtHours = status.GetDouble(DeathlessFallbackAtHoursKey, -1d);
        record.TalentRanks = FoxPerkCatalog.All
            .Select(definition => new FoxPerkRankEntry
            {
                Id = definition.Id,
                Rank = perkTree == null ? 0 : FoxPerkCatalog.GetRank(perkTree, definition)
            })
            .ToList();
        record.DurableStateInitialized = true;
        string persistedExpeditionStatus = status.GetString(ExpeditionStatusKey, string.Empty);
        if (!IsFoxMortallyWounded(entity)
            && string.Equals(persistedExpeditionStatus, "Mortally wounded", StringComparison.OrdinalIgnoreCase))
        {
            // Older expedition returns stored mortal injury both in the
            // vanilla health behavior and as an expedition label. The latter
            // outlived healing and could make the durable record unavailable
            // again after the behavior had recovered.
            persistedExpeditionStatus = string.Empty;
            status.SetString(ExpeditionStatusKey, string.Empty);
            status.SetDouble(ExpeditionStatusExpiresDayKey, 0d);
            MarkSocialStateDirty(entity);
        }
        double currentDay = entity.World.Calendar.TotalDays;
        double statusExpiresDay = status.GetDouble(ExpeditionStatusExpiresDayKey, 0d);
        if (IsTransientExpeditionStatus(persistedExpeditionStatus))
        {
            if (statusExpiresDay <= 0d)
            {
                statusExpiresDay = currentDay + TransientExpeditionStatusDisplayDays;
                status.SetDouble(ExpeditionStatusExpiresDayKey, statusExpiresDay);
                MarkSocialStateDirty(entity);
            }
            else if (currentDay >= statusExpiresDay)
            {
                persistedExpeditionStatus = string.Empty;
                status.SetString(ExpeditionStatusKey, string.Empty);
                status.SetDouble(ExpeditionStatusExpiresDayKey, 0d);
                MarkSocialStateDirty(entity);
            }
        }
        string activeExpeditionStatus = string.Empty;
        FoxExpeditionRecord? activeExpedition = packRepository.GetExpeditionForMember(ownerUid, foxId);
        if (activeExpedition != null)
        {
            activeExpeditionStatus = !string.IsNullOrWhiteSpace(record.PendingDepartureStatus)
                ? $"Leaving — {GetExpeditionDisplayName(activeExpedition.Type)}"
                : activeExpedition.RunningLate
                ? "Running late"
                : $"Away — {GetExpeditionDisplayName(activeExpedition.Type)}";
        }

        string ledgerStatus = record.Status;
        bool ledgerStatusIsAuthoritative = string.Equals(ledgerStatus, "MIA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ledgerStatus, "Mortally wounded", StringComparison.OrdinalIgnoreCase)
                && IsFoxMortallyWounded(entity)
            || string.Equals(ledgerStatus, "Running late", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ledgerStatus, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
            || ledgerStatus.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
            || ledgerStatus.StartsWith("Leaving —", StringComparison.OrdinalIgnoreCase);
        record.Status = !entity.Alive
            ? "Dead"
            : IsFoxMortallyWounded(entity) ? "Mortally wounded"
            : entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0) == RecoveringHealthState ? "Recovering"
            : !string.IsNullOrWhiteSpace(activeExpeditionStatus) ? activeExpeditionStatus
            : ledgerStatusIsAuthoritative ? ledgerStatus
            : !string.IsNullOrWhiteSpace(persistedExpeditionStatus) ? persistedExpeditionStatus
            : "Present";
        SetFoxAwayFromWorld(
            entity,
            string.Equals(record.Status, "MIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Running late", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Awaiting safe return", StringComparison.OrdinalIgnoreCase)
                || record.Status.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
        );
        record.LastSeenDay = currentDay;
        record.StatusExpiresDay = IsTransientExpeditionStatus(record.Status)
            ? statusExpiresDay
            : 0d;

        BlockPos position = entity.Pos.AsBlockPos;
        record.LastKnownX = position.X;
        record.LastKnownY = position.Y;
        record.LastKnownZ = position.Z;
        record.LastKnownDimension = position.dimension;
        record.HasLastKnownPosition = true;
        record.LastKnownPositionExpiresDay = entity.World.Calendar.TotalDays
            + ScentLedgerBaseRetentionDays
            + GetFoxPerkRank(entity, "scent-ledger") * ScentLedgerAdditionalRetentionDaysPerRank
            + GetFoxPackTalentRank(entity, "scent-ledger") * PackScentLedgerAdditionalRetentionDays;

        GetHealth(entity, out float currentHealth, out float maxHealth);
        if (maxHealth > 0f)
        {
            record.CurrentHealth = currentHealth;
            record.MaxHealth = maxHealth;
        }

        lastPersistedHealthStates[entity.EntityId] = record.HealthState;
        PersistCompanionBackpack(entity);
    }

    private static bool IsLiveHealthStateNotMortal(Entity entity)
    {
        return entity.WatchedAttributes.GetInt(EntityHealthStateKey, 0) != MortallyWoundedHealthState;
    }

    private void RetireRemovedTalentRanks(Entity entity, ITreeAttribute status)
    {
        ITreeAttribute? perks = GetFoxPerkTree(entity);
        int refund = perks == null
            ? 0
            : FoxPerkCatalog.GetSpentOn(perks, "lightfooted")
                + FoxPerkCatalog.GetSpentOn(perks, "steeplechaser");
        if (perks == null || refund <= 0)
        {
            return;
        }

        perks.SetInt("lightfooted", 0);
        perks.SetInt("steeplechaser", 0);
        status.SetInt(FoxPointsKey, Math.Max(0, status.GetInt(FoxPointsKey, 0)) + refund);
        MarkSocialStateDirty(entity);
    }

    private static void HydrateFoxFromLedger(Entity entity, ITreeAttribute status, FoxPackRecordV2 record)
    {
        status.SetString(PersonalityKey, record.Personality ?? string.Empty);
        status.SetInt(RequestsGeneratedKey, Math.Max(0, record.RequestsGenerated));
        status.SetInt(RequestsCompletedKey, Math.Max(0, record.RequestsCompleted));
        status.SetInt(FoxPointsKey, Math.Max(0, record.Points));
        status.SetInt(FoxLifetimePointsKey, Math.Max(record.LifetimePoints, record.Points));
        status.SetString(ActiveRequestKey, record.ActiveRequestId ?? string.Empty);
        status.SetString(LastCompletedKey, record.LastCompletedId ?? string.Empty);
        status.SetLong(PredatorTargetKey, Math.Max(0L, record.PredatorTargetEntityId));
        status.SetString(PredatorTargetClueKey, record.PredatorTargetClue ?? string.Empty);
        status.SetBool(PredatorTargetKilledKey, record.PredatorTargetKilled);
        status.SetFloat(RequestProgressKey, Math.Max(0f, record.RequestProgress));
        status.SetLong(RequestQualifiedSinceUtcMsKey, Math.Max(0L, record.RequestQualifiedSinceUtcMs));
        status.SetLong(RequestExpiresUtcMsKey, Math.Max(0L, record.RequestExpiresUtcMs));
        status.SetBool(RequestIsDeveloperKey, record.RequestIsDeveloper);
        status.SetString(RequestedItemCodeKey, record.RequestedItemCode ?? string.Empty);
        status.SetInt(RequestedItemCountKey, Math.Max(0, record.RequestedItemCount));
        status.SetLong(RequestCooldownEndsUtcMsKey, Math.Max(0L, record.RequestCooldownEndsUtcMs));
        status.SetLong(CancelCooldownEndsUtcMsKey, Math.Max(0L, record.CancelCooldownEndsUtcMs));
        if (!string.IsNullOrWhiteSpace(record.MoodId))
        {
            status.SetString(MoodKey, record.MoodId);
        }
        status.SetBool(AutomaticRetreatActiveKey, record.AutomaticRetreatActive);
        status.SetString(AutomaticRetreatPreviousCombatStyleKey, record.AutomaticRetreatPreviousCombatStyle ?? string.Empty);
        status.SetString(AutomaticRetreatPreviousActivityKey, record.AutomaticRetreatPreviousActivity ?? string.Empty);
        if (CompanionActivityMode.IsValid(record.ActivityMode))
        {
            status.SetString(ActivityModeKey, CompanionActivityMode.Normalize(record.ActivityMode));
            status.SetLong(ActivityStartedUtcMsKey, Math.Max(0, record.ActivityStartedUtcMs));
            status.SetLong(ActivityArrivedUtcMsKey, Math.Max(0, record.ActivityArrivedUtcMs));
        }
        if (CompanionFollowDistance.IsValid(record.FollowDistance))
        {
            status.SetString(FollowDistanceKey, CompanionFollowDistance.Normalize(record.FollowDistance));
        }
        if (CompanionCombatStyle.IsValid(record.CombatStyle))
        {
            status.SetString(CombatStyleKey, CompanionCombatStyle.Normalize(record.CombatStyle));
        }
        if (CompanionRiskTolerance.IsValid(record.RiskTolerance))
        {
            status.SetString(RiskToleranceKey, CompanionRiskTolerance.Normalize(record.RiskTolerance));
        }
        status.SetBool(GroundCleanupEnabledKey, record.GroundCleanupEnabled);
        status.SetBool(GroundDroppedItemsEnabledKey, record.GroundDroppedItemsEnabled);
        status.SetBool(GroundCattailsEnabledKey, record.GroundCattailsEnabled);
        status.SetBool(GroundFlintEnabledKey, record.GroundFlintEnabled);
        status.SetBool(GroundSticksEnabledKey, record.GroundSticksEnabled);
        status.SetBool(GroundBouldersEnabledKey, record.GroundBouldersEnabled);
        status.SetBool(GroundRocksEnabledKey, record.GroundRocksEnabled);
        status.SetBool(MowLawnEnabledKey, record.MowLawnEnabled);
        status.SetBool(FinishedProductsEnabledKey, record.FinishedProductsEnabled);
        status.SetBool(FinishedCropsEnabledKey, record.FinishedCropsEnabled);
        status.SetBool(FinishedBerriesEnabledKey, record.FinishedBerriesEnabled);
        status.SetBool(FinishedMushroomsEnabledKey, record.FinishedMushroomsEnabled);
        status.SetBool(FlowerRemovalEnabledKey, record.FlowerRemovalEnabled);
        status.SetBool(SnowShovelingEnabledKey, record.SnowShovelingEnabled);
        status.SetBool(CharcoalShovelingEnabledKey, record.CharcoalShovelingEnabled);
        status.SetBool(SnowballCollectionEnabledKey, record.SnowballCollectionEnabled);
        status.SetBool(GeneralStorageSortingEnabledKey, record.GeneralStorageSortingEnabled);
        if (record.FoodStateInitialized)
        {
            status.SetBool(FoodStateInitializedKey, true);
            status.SetFloat(FoodLevelKey, Math.Clamp(record.FoodLevel, 0f, 1f));
            status.SetDouble(FoodLastUpdateTotalHoursKey, record.FoodLastUpdateTotalHours);
        }
        status.SetBool(BreedingEnabledKey, record.BreedingEnabled);
        status.SetString(BondedPartnerIdKey, record.BondedPartnerId ?? string.Empty);
        status.SetString(BondedPartnerNameKey, record.BondedPartnerName ?? string.Empty);
        status.SetLong(LastBreedingAttemptNightKey, record.LastBreedingAttemptNight);
        status.SetString(BreedingFeedbackKey, record.BreedingFeedback ?? string.Empty);
        status.SetBool(PregnancyActiveKey, record.PregnancyActive);
        status.SetDouble(PregnancyStartTotalHoursKey, record.PregnancyStartTotalHours);
        status.SetDouble(PregnancyDueTotalHoursKey, record.PregnancyDueTotalHours);
        status.SetString(PregnancyFatherIdKey, record.PregnancyFatherId ?? string.Empty);
        status.SetString(PregnancyFatherNameKey, record.PregnancyFatherName ?? string.Empty);
        status.SetBool(PendingBirthKey, record.PendingBirth);
        status.SetBool(JuvenileKey, record.IsJuvenile);
        status.SetString(JuvenileSpeciesKey, record.SpeciesId ?? string.Empty);
        status.SetString(ParentMotherIdKey, record.ParentMotherId ?? string.Empty);
        status.SetString(ParentMotherNameKey, record.ParentMotherName ?? string.Empty);
        status.SetString(ParentFatherIdKey, record.ParentFatherId ?? string.Empty);
        status.SetString(ParentFatherNameKey, record.ParentFatherName ?? string.Empty);
        status.SetInt(BankedTalentPointsKey, Math.Max(0, record.BankedTalentPoints));
        status.SetBool(JuvenilePointsTransferredKey, record.JuvenilePointsTransferred);
        status.SetString(ChildAdultEntityCodeKey, record.ChildAdultEntityCode ?? string.Empty);
        status.SetInt(CompanionLevelKey, Math.Max(CompanionProgressionRules.StartingLevel, record.Level));
        status.SetLong(CompanionCurrentExperienceKey, Math.Max(0L, record.CurrentLevelExperience));
        status.SetLong(CompanionLifetimeExperienceKey, Math.Max(record.CurrentLevelExperience, record.LifetimeExperience));
        status.SetBool(AdulthoodExperienceGrantedKey, record.AdulthoodExperienceGranted);
        status.SetInt(CleanupDutyUnitsKey, Math.Clamp(
            record.CleanupDutyUnits,
            0,
            CompanionProgressionRules.CleanupActionsPerExperience - 1));

        ITreeAttribute? perks = GetFoxPerkTree(entity, true);
        if (perks != null)
        {
            foreach (FoxPerkDefinition definition in FoxPerkCatalog.All)
            {
                int rank = record.TalentRanks?
                    .FirstOrDefault(entry => string.Equals(entry.Id, definition.Id, StringComparison.Ordinal))?
                    .Rank ?? 0;
                perks.SetInt(definition.Id, Math.Clamp(rank, 0, definition.MaxRank));
            }
            status.SetInt(DamagePerkRankKey, FoxPerkCatalog.GetRank(perks, "damage-training"));
        }

        entity.WatchedAttributes.SetInt(EntityHealthStateKey, record.HealthState);
        entity.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", record.MortallyWoundedStartHours);
        status.SetInt(StabilizedWindowAppliedRankKey, record.StabilizedWindowAppliedRank);
        status.SetDouble(StabilizedWindowAdjustedStartHoursKey, record.StabilizedWindowAdjustedStartHours);
        status.SetDouble(DeathlessRecoveryAtHoursKey, record.DeathlessRecoveryAtHours);
        status.SetDouble(DeathlessFallbackAtHoursKey, record.DeathlessFallbackAtHours);
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null && record.MaxHealth > 0f)
        {
            health.Health = Math.Clamp(record.CurrentHealth, 0f, health.MaxHealth);
            health.MarkDirty();
        }
        MarkSocialStateDirty(entity);
    }

    private static void EnsureCompanionCommandState(Entity entity, ITreeAttribute status)
    {
        // Ledger hydration, when required, has already copied saved command
        // values into the entity above. From this point onward the live watched
        // state is authoritative: command handlers update it first, then call
        // RegisterFoxInPack to persist the new value. Reading the old record
        // here would silently undo every freshly accepted command.
        string mode = GetCompanionActivityMode(entity);
        string distance = GetCompanionFollowDistance(entity);
        string combat = GetCompanionCombatStyle(entity);
        string risk = GetCompanionRiskTolerance(entity);
        bool groundCleanup = IsGroundCleanupEnabled(entity);
        bool mowLawn = IsMowLawnEnabled(entity);
        bool finishedProducts = IsFinishedProductsEnabled(entity);
        bool flowerRemoval = IsFlowerRemovalEnabled(entity);
        bool snowShoveling = IsSnowShovelingEnabled(entity);
        bool charcoalShoveling = IsCharcoalShovelingEnabled(entity);
        bool snowballCollection = IsSnowballCollectionEnabled(entity);
        bool groundDroppedItems = IsGroundDroppedItemsEnabled(entity);
        bool groundCattails = IsGroundCattailsEnabled(entity);
        bool groundFlint = IsGroundFlintEnabled(entity);
        bool groundSticks = IsGroundSticksEnabled(entity);
        bool groundBoulders = IsGroundBouldersEnabled(entity);
        bool groundRocks = IsGroundRocksEnabled(entity);
        bool finishedCrops = IsFinishedCropsEnabled(entity);
        bool finishedBerries = IsFinishedBerriesEnabled(entity);
        bool finishedMushrooms = IsFinishedMushroomsEnabled(entity);

        bool changed = false;
        if (!string.Equals(status.GetString(ActivityModeKey, string.Empty), mode, StringComparison.Ordinal))
        {
            status.SetString(ActivityModeKey, mode);
            changed = true;
        }
        if (!string.Equals(status.GetString(FollowDistanceKey, string.Empty), distance, StringComparison.Ordinal))
        {
            status.SetString(FollowDistanceKey, distance);
            changed = true;
        }
        if (!string.Equals(status.GetString(CombatStyleKey, string.Empty), combat, StringComparison.Ordinal))
        {
            status.SetString(CombatStyleKey, combat);
            changed = true;
        }
        if (!string.Equals(status.GetString(RiskToleranceKey, string.Empty), risk, StringComparison.Ordinal))
        {
            status.SetString(RiskToleranceKey, risk);
            changed = true;
        }
        if (status.GetBool(GroundCleanupEnabledKey, false) != groundCleanup)
        {
            status.SetBool(GroundCleanupEnabledKey, groundCleanup);
            changed = true;
        }
        changed |= EnsureDutyOption(status, GroundDroppedItemsEnabledKey, groundDroppedItems);
        changed |= EnsureDutyOption(status, GroundCattailsEnabledKey, groundCattails);
        changed |= EnsureDutyOption(status, GroundFlintEnabledKey, groundFlint);
        changed |= EnsureDutyOption(status, GroundSticksEnabledKey, groundSticks);
        changed |= EnsureDutyOption(status, GroundBouldersEnabledKey, groundBoulders);
        changed |= EnsureDutyOption(status, GroundRocksEnabledKey, groundRocks);
        if (status.GetBool(MowLawnEnabledKey, false) != mowLawn)
        {
            status.SetBool(MowLawnEnabledKey, mowLawn);
            changed = true;
        }
        if (status.GetBool(FinishedProductsEnabledKey, false) != finishedProducts)
        {
            status.SetBool(FinishedProductsEnabledKey, finishedProducts);
            changed = true;
        }
        changed |= EnsureDutyOption(status, FinishedCropsEnabledKey, finishedCrops);
        changed |= EnsureDutyOption(status, FinishedBerriesEnabledKey, finishedBerries);
        changed |= EnsureDutyOption(status, FinishedMushroomsEnabledKey, finishedMushrooms);
        if (status.GetBool(FlowerRemovalEnabledKey, false) != flowerRemoval)
        {
            status.SetBool(FlowerRemovalEnabledKey, flowerRemoval);
            changed = true;
        }
        if (status.GetBool(SnowShovelingEnabledKey, false) != snowShoveling)
        {
            status.SetBool(SnowShovelingEnabledKey, snowShoveling);
            changed = true;
        }
        if (status.GetBool(CharcoalShovelingEnabledKey, false) != charcoalShoveling)
        {
            status.SetBool(CharcoalShovelingEnabledKey, charcoalShoveling);
            changed = true;
        }
        if (status.GetBool(SnowballCollectionEnabledKey, false) != snowballCollection)
        {
            status.SetBool(SnowballCollectionEnabledKey, snowballCollection);
            changed = true;
        }
        if (status.GetLong(ActivityStartedUtcMsKey, 0) <= 0)
        {
            status.SetLong(ActivityStartedUtcMsKey, UtcNowMs());
            changed = true;
        }
        if (status.GetLong(ActivityArrivedUtcMsKey, 0) < 0)
        {
            status.SetLong(ActivityArrivedUtcMsKey, 0);
            changed = true;
        }
        if (changed) MarkSocialStateDirty(entity);
    }

    private static bool EnsureDutyOption(ITreeAttribute status, string key, bool value)
    {
        if (status.GetBool(key, value) == value) return false;
        status.SetBool(key, value);
        return true;
    }

    private void StartPackExpedition(
        IServerPlayer owner,
        string type,
        IEnumerable<string> selectedFoxIds,
        string targetFoxId,
        bool prepareExpedition,
        long scavengeSiteId = 0,
        string scavengeFocus = "",
        string scoutDuration = "")
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        if (definition == null || definition.Retired)
        {
            SendPackState(owner, "Unknown or retired expedition type.");
            return;
        }

        if (!packRepository.IsExpeditionUnlocked(ownerUid, type))
        {
            SendPackState(owner, $"{definition.Name} has not been unlocked in Pack routes.");
            return;
        }
        if (!packRepository.AreExpeditionPrerequisitesMet(ownerUid, type))
        {
            string requirements = string.Join(", ", definition.RequiredRouteIds
                .Select(required => FoxExpeditionCatalog.Get(required)?.Name ?? required));
            SendPackState(owner, $"{definition.Name} requires these routes first: {requirements}.");
            return;
        }

        FoxScavengeSiteRecord? selectedSite = null;
        if (type == FoxExpeditionType.Scout)
        {
            if (!FoxScavengeSites.IsDuration(scoutDuration)
                || (scavengeSiteId == 0
                    ? packRepository.GetScavengeSites(ownerUid).Count >= FoxScavengeSites.MaximumRememberedSites
                    : !packRepository.TryGetScavengeSite(ownerUid, scavengeSiteId, out selectedSite)
                        || packRepository.IsScavengeSiteBusy(ownerUid, scavengeSiteId)))
            {
                SendPackState(owner, "Scout refused: choose a valid duration and an available site.");
                return;
            }
            if (selectedSite != null && FoxScavengeSites.HasLearnedAllFromOutside(selectedSite))
            {
                SendPackState(owner, "The scouts think they have found everything they can from outside this site.");
                return;
            }
        }
        else if (type == FoxExpeditionType.Scavenge)
        {
            if (!FoxScavengeSites.IsFocus(scavengeFocus)
                || !packRepository.TryGetScavengeSite(ownerUid, scavengeSiteId, out selectedSite)
                || selectedSite == null || selectedSite.RemainingVisits <= 0
                || packRepository.IsScavengeSiteBusy(ownerUid, scavengeSiteId))
            {
                SendPackState(owner, "Scavenge refused: choose an available discovered site and search focus.");
                return;
            }
        }

        RefreshLoadedFoxPackRecords();
        if (GetActiveCairnPosition(ownerUid) == null)
        {
            SendPackState(owner, "Place a Pack Cart before starting an expedition.");
            return;
        }
        if (string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
            && !HasRescueTarget(ownerUid))
        {
            SendPackState(owner, "Search for lost is only available when a companion is recoverable.");
            return;
        }
        if (string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
            && (!packRepository.TryGetRecord(targetFoxId, out FoxPackRecordV2? targetRecord)
                || targetRecord == null
                || !string.Equals(targetRecord.OwnerUid, ownerUid, StringComparison.Ordinal)
                || !IsRescueTarget(targetRecord)))
        {
            SendPackState(owner, "Choose which recoverable companion the search party should look for.");
            return;
        }
        if (packRepository.GetExpeditions(ownerUid).Count >= packRepository.GetExpeditionCapacity(ownerUid))
        {
            SendPackState(owner, "Expedition refused: every active expedition slot is already in use.");
            return;
        }
        if (string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
            && packRepository.IsSearchTargetActive(ownerUid, targetFoxId))
        {
            SendPackState(owner, "Expedition refused: a search party is already looking for that companion.");
            return;
        }

        if (!TryBuildExpeditionParty(
                ownerUid,
                selectedFoxIds,
                out List<string> party,
                out float expeditionStrength,
                out float averageSpeedBonus,
                out string refusal))
        {
            SendPackState(owner, refusal);
            return;
        }

        if (party.Count < definition.MinimumFoxes)
        {
            SendPackState(owner,
                $"{definition.Name} needs at least {definition.MinimumFoxes} companions to depart. "
                + $"Only {party.Count} eligible companion{(party.Count == 1 ? " was" : "s were")} selected.");
            return;
        }
        if (definition.MaximumFoxes > 0 && party.Count > definition.MaximumFoxes)
        {
            SendPackState(owner,
                $"{definition.Name} can take at most {definition.MaximumFoxes} companions. "
                + $"Remove {party.Count - definition.MaximumFoxes} from the selected party.");
            return;
        }

        float targetStrength = definition.TargetStrength;
        ExpeditionPerkSnapshot perkSnapshot = GetExpeditionPerkSnapshot(ownerUid, type, party);
        expeditionStrength += perkSnapshot.StrengthBonus;
        List<FoxExpeditionMemberSummaryPacket> memberSnapshots = BuildExpeditionMemberSnapshots(ownerUid, type, party);
        CompanionSpeciesExpeditionBonus speciesBonus = SnapshotSpeciesTraits(type, memberSnapshots);
        expeditionStrength += speciesBonus.Strength;
        long startedUtcMs = UtcNowMs();
        double startedTotalHours = serverApi.World.Calendar.TotalHours;
        float durationHours = GetExpeditionDurationHours(type, averageSpeedBonus + perkSnapshot.SpeedBonus)
            * (1f - speciesBonus.TravelReduction);
        float baseDurationHours = definition.BaseDurationHours;
        if (type == FoxExpeditionType.Scout)
        {
            baseDurationHours = FoxScavengeSites.ScoutHours(scoutDuration);
            durationHours *= baseDurationHours / definition.BaseDurationHours;
        }
        if (TryGetPregnancyDepartureRefusal(
                party,
                startedTotalHours + durationHours,
                out string pregnancyRefusal))
        {
            SendPackState(owner, pregnancyRefusal);
            return;
        }
        int preparationCost = prepareExpedition ? definition.PreparationCost : 0;
        if (preparationCost > GetPackPoints(ownerUid))
        {
            SendPackState(owner,
                $"Preparing {definition.Name} costs {preparationCost} pack points; "
                + $"the pack currently has {GetPackPoints(ownerUid)}.");
            return;
        }
        float patrolRiskReduction = !definition.IsPatrol && packRepository.IsPatrolPrepared(ownerUid)
            ? FoxExpeditionCatalog.PatrolRiskReduction
            : 0f;
        if (!packRepository.TryStartExpedition(
                ownerUid,
                type,
                party,
                string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal)
                    ? targetFoxId
                    : string.Empty,
                expeditionStrength,
                targetStrength,
                startedUtcMs,
                startedUtcMs + SecondsToMilliseconds(ExpeditionTestDurationSeconds),
                startedTotalHours,
                startedTotalHours + durationHours,
                baseDurationHours,
                durationHours,
                memberSnapshots,
                prepareExpedition,
                preparationCost,
                patrolRiskReduction,
                out FoxExpeditionRecord? started))
        {
            SendPackState(owner, packRepository.HasLoot(ownerUid) || packRepository.HasPendingRecruitment(ownerUid)
                ? "Expedition refused: claim the pending pack reward before starting another."
                : "Expedition refused: the party overlaps another expedition or no expedition slot is available.");
            return;
        }

        if (started != null)
        {
            if (type == FoxExpeditionType.Scout)
            {
                started.ScavengeSiteId = scavengeSiteId;
                started.ScoutDuration = scoutDuration;
            }
            else if (type == FoxExpeditionType.Scavenge)
            {
                bool blockedAtDoor = selectedSite?.LayoutRule == "heavy-door"
                    && party.Count < selectedSite.LayoutThreshold;
                if (!packRepository.TryReserveScavengeSite(
                    ownerUid, scavengeSiteId, started, scavengeFocus, blockedAtDoor))
                {
                    packRepository.CancelNewExpedition(started, preparationCost);
                    SendPackState(owner, "Scavenge refused: the site changed before the party could depart.");
                    return;
                }
            }
            started.SpeciesBonus = speciesBonus;
            started.PerkStrengthBonus = perkSnapshot.StrengthBonus;
            started.PerkSpeedBonus = perkSnapshot.SpeedBonus;
            started.PerkRiskReduction = perkSnapshot.RiskReduction;
            started.PerkInjuryRiskReduction = perkSnapshot.InjuryRiskReduction;
            started.RecruitmentChanceBonus = perkSnapshot.RecruitmentChanceBonus;
            started.PerkRewardBonus = perkSnapshot.RewardBonus;
            started.PerkMiaRiskReduction = perkSnapshot.MiaRiskReduction;
            started.PerkCargoRiskReduction = perkSnapshot.CargoRiskReduction;
            started.ScoutInformationBonus = perkSnapshot.ScoutInformationBonus;
            started.FavorDifficultScavengeSites = perkSnapshot.FavorDifficultScavengeSites;
            started.QuietScavengeParty = perkSnapshot.QuietScavengeParty;
            started.LargeScavengeParty = perkSnapshot.LargeScavengeParty;
            if (definition.ProducesRandomLoot
                && !(type == FoxExpeditionType.Scavenge && started.ScavengeSiteId > 0))
            {
                started.Story = CreateExpeditionStory(started);
            }
        }

        if (patrolRiskReduction > 0f)
            packRepository.SetPatrolPrepared(ownerUid, false);

        QueueExpeditionDepartures(ownerUid, party, $"Away — {GetExpeditionDisplayName(type)}");
        PlayExpeditionDepartureSounds(ownerUid);

        packRepository.Save();
        bool showStrength = type is not (FoxExpeditionType.Scout or FoxExpeditionType.Scavenge
            or FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths);
        float progress = showStrength ? Math.Clamp(expeditionStrength / targetStrength * 100f, 0f, 100f) : 0f;
        SendPackState(owner,
            $"{GetExpeditionDisplayName(type)} expedition started with {party.Count} companion{(party.Count == 1 ? string.Empty : "s")}. "
            + (showStrength ? $"Strength: {expeditionStrength:0.#}/{targetStrength:0.#} ({progress:0}%). " : string.Empty)
            + (prepareExpedition ? $"Prepared for {preparationCost} pack point{(preparationCost == 1 ? string.Empty : "s")}. " : string.Empty)
            + (patrolRiskReduction > 0f ? "The patrol's secured trails improve safety. " : string.Empty)
            + $"Expected travel time: {FormatExpeditionHours(durationHours)}. "
            + CompanionSpeciesTraits.SynergySummary(speciesBonus, type));
    }

    private void UnlockExpeditionRoute(IServerPlayer owner, string type)
    {
        if (packRepository == null)
        {
            return;
        }

        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        if (definition == null || definition.DefaultUnlocked || definition.Retired)
        {
            SendPackState(owner, "That route does not need to be unlocked.");
            return;
        }
        if (packRepository.IsExpeditionUnlocked(owner.PlayerUID, type))
        {
            SendPackState(owner, $"{definition.Name} is already unlocked.");
            return;
        }
        if (!packRepository.AreExpeditionPrerequisitesMet(owner.PlayerUID, type))
        {
            string requirements = string.Join(", ", definition.RequiredRouteIds
                .Select(required => FoxExpeditionCatalog.Get(required)?.Name ?? required));
            SendPackState(owner, $"Unlock the prerequisite routes first: {requirements}.");
            return;
        }
        if (GetPackPoints(owner.PlayerUID) < definition.UnlockCost)
        {
            SendPackState(owner,
                $"Unlocking {definition.Name} costs {definition.UnlockCost} pack points; "
                + $"the pack currently has {GetPackPoints(owner.PlayerUID)}.");
            return;
        }
        if (!packRepository.TryUnlockExpedition(owner.PlayerUID, type, definition.UnlockCost))
        {
            SendPackState(owner, $"{definition.Name} could not be unlocked.");
            return;
        }

        packRepository.Save();
        SendPackState(owner,
            $"{definition.Name} unlocked permanently for {definition.UnlockCost} pack points.");
    }

    private void UnlockPackTalent(IServerPlayer owner, string id)
    {
        if (packRepository == null)
        {
            return;
        }

        PackTalentDefinition? talent = PackTalentCatalog.GetTalent(id);
        if (talent?.Implemented != true)
        {
            SendPackState(owner, "That pack talent is not available yet.");
            return;
        }
        if (!talent.Repeatable && packRepository.IsPackTalentUnlocked(owner.PlayerUID, id))
        {
            SendPackState(owner, $"{talent.Name} is already unlocked.");
            return;
        }
        if (!packRepository.ArePackTalentPrerequisitesMet(owner.PlayerUID, id))
        {
            PackTalentDefinition? parent = PackTalentCatalog.GetTalent(talent.ParentId);
            SendPackState(owner, $"Unlock {parent?.Name ?? talent.ParentId} first.");
            return;
        }
        int cost = talent.Repeatable
            ? GetNextRepeatablePackTalentCost(packRepository.GetPackTalentRank(owner.PlayerUID, id))
            : talent.UnlockCost;
        if (GetPackPoints(owner.PlayerUID) < cost)
        {
            SendPackState(owner,
                $"Unlocking {talent.Name} costs {cost} pack points; "
                + $"the pack currently has {GetPackPoints(owner.PlayerUID)}.");
            return;
        }
        if (!packRepository.TryUnlockPackTalent(owner.PlayerUID, id, cost))
        {
            SendPackState(owner, $"{talent.Name} could not be unlocked.");
            return;
        }

        RefreshPackTalentSnapshots(owner.PlayerUID);
        packRepository.Save();
        SendPackState(owner,
            talent.Repeatable
                ? $"{talent.Name} rank {packRepository.GetPackTalentRank(owner.PlayerUID, id)} purchased permanently for {cost} pack points."
                : $"{talent.Name} unlocked permanently for {cost} pack points.");
    }

    private static int GetNextRepeatablePackTalentCost(int rank)
    {
        return rank >= int.MaxValue ? int.MaxValue : Math.Max(1, rank + 1);
    }

    private static List<FoxPackLootItemPacket> GetAddedPackLoot(
        IEnumerable<FoxPackLootItemPacket> before,
        IEnumerable<FoxPackLootItemPacket> after)
    {
        Dictionary<string, int> oldCounts = before
            .GroupBy(item => item.Code ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Count), StringComparer.Ordinal);
        return after
            .GroupBy(item => item.Code ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new FoxPackLootItemPacket
            {
                Code = group.Key,
                Name = group.Last().Name,
                Count = Math.Max(0, group.Sum(item => item.Count) - oldCounts.GetValueOrDefault(group.Key))
            })
            .Where(item => item.Count > 0)
            .ToList();
    }

    private void MaintainWorkCartAssignments(IReadOnlyList<Entity> loadedCompanions)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        foreach (Entity fox in loadedCompanions)
        {
            if (!fox.Alive || !IsTamedFox(fox) || IsFoxAwayFromWorld(fox) || IsFoxIncapacitated(fox))
            {
                continue;
            }

            string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(foxId)
                || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !record.HasHome
                || !string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Vec3d? target = null;
            double homeDx = fox.Pos.X - (record.HomeX + 0.5d);
            double homeDz = fox.Pos.Z - (record.HomeZ + 0.5d);
            // Work Cart territory is a cylinder: vertical distance is
            // irrelevant, and a fox can roam the complete work radius without
            // the maintenance pass repeatedly pulling it back to the cart.
            bool insideWorkCartTerritory = fox.Pos.Dimension == record.HomeDimension
                && homeDx * homeDx + homeDz * homeDz <= GetWorkCartRadius(fox) * GetWorkCartRadius(fox);
            if (insideWorkCartTerritory)
            {
                if (GetCompanionActivityMode(fox) == CompanionActivityMode.ReturnHome)
                {
                    CompleteCompanionActivity(fox, CompanionActivityMode.ReturnHome);
                }
                continue;
            }

            // A Work Cart is a home anchor, not a leash. Existing duties are
            // allowed to carry a companion away from the cart while they work;
            // the duty task already owns its target and its return route.
            if (HasActiveWorkCartDuty(fox) || HasFoxStorageJob(fox))
            {
                continue;
            }

            if (GetCompanionActivityMode(fox) != CompanionActivityMode.ReturnHome)
            {
                RequestCompanionHomeTravel(fox);
                continue;
            }

            if (GetCompanionActivityElapsedMs(fox) < 12000
                || !TryResolveCompanionCommandHome(fox, out target)
                || target == null)
            {
                continue;
            }

            if (TryTeleportCompanionToCommandHome(fox, target))
            {
                CompleteCompanionActivity(fox, CompanionActivityMode.ReturnHome);
            }
        }

        foreach (FoxWorkCartRecord cart in packRepository.GetAllWorkCarts())
        {
            if (serverApi.World.PlayerByUid(cart.OwnerUid) == null)
            {
                continue;
            }

            foreach (string foxId in cart.AssignedFoxIds)
            {
                if (FindLoadedCompanionByFoxId(foxId) != null
                    || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                    || record == null
                    || !record.HasLastKnownPosition)
                {
                    continue;
                }

                RequestWorkCartFoxChunkLoad(record);
            }
        }
    }

    private static bool HasActiveWorkCartDuty(Entity fox)
    {
        AiTaskManager? manager = fox.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
        return manager?.ActiveTasksBySlot.Any(task => task is AiTaskFeralKinshipMowGrass
            || task is AiTaskFeralKinshipLogging
            || task is AiTaskFeralKinshipFetchDroppedItem fetch && !fetch.CommandedCourier) == true;
    }

    private void RequestWorkCartFoxChunkLoad(FoxPackRecordV2 record)
    {
        if (serverApi == null || !record.HasLastKnownPosition || string.IsNullOrWhiteSpace(record.FoxId))
        {
            return;
        }

        long now = serverApi.World.ElapsedMilliseconds;
        if (workCartChunkLoadAttemptsAtMs.TryGetValue(record.FoxId, out long retryAt) && now < retryAt)
        {
            return;
        }

        BlockPos lastKnown = new(record.LastKnownX, record.LastKnownY, record.LastKnownZ, record.LastKnownDimension);
        int chunkX = (int)Math.Floor(lastKnown.X / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(lastKnown.Z / (double)GlobalConstants.ChunkSize);
        if (lastKnown.dimension == 0)
        {
            serverApi.WorldManager.LoadChunkColumnPriority(
                chunkX,
                chunkZ,
                new ChunkLoadOptions { KeepLoaded = false }
            );
        }
        else
        {
            serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, lastKnown.dimension);
        }

        workCartChunkLoadAttemptsAtMs[record.FoxId] = now + 5000;
    }

    private static void SendExpeditionReturnNotification(IServerPlayer owner, int returnedCount = 1)
    {
        owner.SendMessage(
            GlobalConstants.GeneralChatGroup,
            returnedCount == 1
                ? "Your companion expedition party has returned. The Pack Cart holds its trip report."
                : $"{returnedCount} companion expedition parties have returned. The Pack Cart holds their trip reports.",
            EnumChatType.Notification
        );
    }

    private FoxExpeditionSummaryPacket BuildLastExpeditionReport(
        FoxExpeditionRecord expedition,
        string result,
        double completedTotalHours,
        List<FoxPackLootItemPacket> lootItems)
    {
        if (expedition.SpeciesBonus is { } speciesBonus)
        {
            if (speciesBonus.SynergySpecies.Length > 0)
                result += " " + CompanionSpeciesTraits.SynergySummary(speciesBonus, expedition.Type);
            if (speciesBonus.TravelReduction > 0f) result += $" Species travel reduction: {speciesBonus.TravelReduction:P0}.";
            result += " " + string.Join(" ", speciesBonus.Notes.Distinct());
        }
        List<FoxExpeditionMemberSummaryPacket> members = expedition.MemberSnapshots?.ToList()
            ?? new List<FoxExpeditionMemberSummaryPacket>();
        if (members.Count == 0)
        {
            members = BuildExpeditionMemberSnapshots(
                expedition.OwnerUid,
                expedition.Type,
                expedition.SelectedFoxIds
            );
        }

        foreach (FoxExpeditionMemberSummaryPacket member in members)
        {
            if (packRepository?.TryGetRecord(member.FoxId, out FoxPackRecordV2? record) == true
                && record != null)
            {
                member.Outcome = !string.IsNullOrWhiteSpace(record.PendingReturnStatus)
                    ? record.PendingReturnStatus
                    : record.Status;
            }
            if (string.IsNullOrWhiteSpace(member.Outcome))
            {
                member.Outcome = "Returned";
            }
        }

        string sceneSiteLabel = string.Empty;
        if (expedition.Type == FoxExpeditionType.Scavenge && expedition.ScavengeSiteId > 0
            && packRepository != null
            && packRepository.TryGetScavengeSite(expedition.OwnerUid, expedition.ScavengeSiteId,
                out FoxScavengeSiteRecord? sceneSite)
            && sceneSite != null)
            sceneSiteLabel = FoxScavengeSites.DisplayLabel(sceneSite);

        return new FoxExpeditionSummaryPacket
        {
            ExpeditionId = expedition.ExpeditionId,
            OwnerUid = expedition.OwnerUid,
            Type = expedition.Type,
            Name = GetExpeditionDisplayName(expedition.Type),
            StartedTotalHours = expedition.StartedTotalHours,
            CompletedTotalHours = completedTotalHours,
            BaseDurationHours = expedition.BaseDurationHours,
            PlannedDurationHours = expedition.PlannedDurationHours,
            RanLate = expedition.RunningLate,
            LateDurationHours = expedition.LateDurationHours,
            Prepared = expedition.Prepared,
            PatrolProtected = expedition.PatrolRiskReduction > 0f,
            ExpeditionStrength = expedition.ExpeditionStrength,
            TargetStrength = expedition.TargetStrength,
            Members = members,
            LootItems = lootItems ?? new List<FoxPackLootItemPacket>(),
            Result = result ?? string.Empty,
            NotificationPending = true,
            Story = expedition.Story,
            SceneSiteLabel = sceneSiteLabel
        };
    }

    private bool TryBuildExpeditionParty(
        string ownerUid,
        IEnumerable<string> requestedFoxIds,
        out List<string> selectedFoxIds,
        out float expeditionStrength,
        out float averageSpeedBonus,
        out string refusal)
    {
        selectedFoxIds = new List<string>();
        expeditionStrength = 0f;
        averageSpeedBonus = 0f;
        refusal = string.Empty;
        if (serverApi == null || packRepository == null)
        {
            refusal = "Expeditions are unavailable right now.";
            return false;
        }

        List<string> requested = requestedFoxIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (requested.Count == 0)
        {
            refusal = "Select at least one present companion for the expedition.";
            return false;
        }

        foreach (string foxId in requested)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                refusal = "One of the selected companions is not part of this pack.";
                return false;
            }

            if (!IsExpeditionSelectableStatus(record.Status))
            {
                refusal = $"{GetFoxRecordLabel(record)} cannot join an expedition right now.";
                return false;
            }
            if (record.IsJuvenile)
            {
                refusal = $"{GetFoxRecordLabel(record)} is still a child and cannot join expeditions.";
                return false;
            }

            Entity? entity = record.EntityId > 0
                ? serverApi.World.GetEntityById(record.EntityId)
                : null;
            if (entity == null || !IsTamedFox(entity) || !IsOwner(entity, ownerUid))
            {
                refusal = $"{GetFoxRecordLabel(record)} is not currently available in the world.";
                return false;
            }
            if (IsFoxAwayFromWorld(entity))
            {
                refusal = $"{GetFoxRecordLabel(record)} is still marked away from the world and cannot join another expedition.";
                return false;
            }
            if (IsCompanionFoodRestricted(entity))
            {
                refusal = $"{GetFoxRecordLabel(record)} is starving and must eat before joining an expedition.";
                return false;
            }
            GetHealth(entity, out float currentHealth, out float maxHealth);
            expeditionStrength += GetFoxExpeditionCoreStrength(
                entity,
                currentHealth,
                maxHealth,
                out _,
                out _,
                out _
            );
            selectedFoxIds.Add(foxId);
        }

        // Expedition timing is intentionally based only on permanent
        // expedition talents captured below, never on mood or temporary
        // in-world movement effects.
        averageSpeedBonus = 0f;
        return expeditionStrength > 0f;
    }

    private static bool IsExpeditionSelectableStatus(string status)
    {
        return status is "Present" or "Returned healthy" or "Returned injured";
    }

    private static string GetFoxRecordLabel(FoxPackRecordV2 record)
    {
        return string.IsNullOrWhiteSpace(record.Name) ? "Unnamed companion" : record.Name.Trim();
    }

    private static float GetExpeditionTargetStrength(string type)
    {
        return FoxExpeditionCatalog.Get(type)?.TargetStrength ?? 0f;
    }

    private ExpeditionPerkSnapshot GetExpeditionPerkSnapshot(
        string ownerUid,
        string expeditionType,
        IEnumerable<string> foxIds)
    {
        ExpeditionPerkSnapshot snapshot = new();
        if (serverApi == null || packRepository == null)
        {
            return snapshot;
        }

        List<string> partyIds = foxIds.ToList();
        foreach (string foxId in partyIds)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                continue;
            }

            Entity? fox = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
            if (fox == null || !IsTamedFox(fox) || !IsOwner(fox, ownerUid))
            {
                continue;
            }

            snapshot.StrengthBonus += GetFoxExpeditionStrengthBonus(fox);
            snapshot.SpeedBonus += GetFoxExpeditionSpeedBonus(fox);
            snapshot.RiskReduction += GetFoxExpeditionRiskReduction(fox);
            snapshot.InjuryRiskReduction += GetFoxExpeditionInjuryRiskReduction(fox);
            snapshot.MiaRiskReduction = Math.Max(snapshot.MiaRiskReduction,
                GetFoxPerkRank(fox, "trail-marker") > 0 ? 0.15f : 0f);
            snapshot.RewardBonus = Math.Max(snapshot.RewardBonus,
                GetFoxExpeditionRewardBonus(fox, expeditionType));
            if (expeditionType == FoxExpeditionType.Scout && GetFoxPerkRank(fox, "far-seeker") > 0)
                snapshot.FavorDifficultScavengeSites = true;
            if (expeditionType == FoxExpeditionType.Scavenge && partyIds.Count <= 3
                && GetFoxPerkRank(fox, "quiet-surveyor") > 0)
                snapshot.QuietScavengeParty = true;
            if (expeditionType == FoxExpeditionType.Scavenge && partyIds.Count >= 6
                && GetFoxPerkRank(fox, "many-paws") > 0)
                snapshot.LargeScavengeParty = true;

            if (string.Equals(expeditionType, FoxExpeditionType.Recruitment, StringComparison.Ordinal)
                && GetFoxPerkRank(fox, "recruiters-nose") > 0)
            {
                snapshot.RecruitmentChanceBonus += GetFoxRecruitmentChanceBonus(fox);
            }
        }

        Entity? packRepresentative = null;
        foreach (string foxId in partyIds)
        {
            if (packRepository.TryGetRecord(foxId, out FoxPackRecordV2? representativeRecord)
                && representativeRecord != null
                && representativeRecord.EntityId > 0)
            {
                packRepresentative = serverApi.World.GetEntityById(representativeRecord.EntityId);
                if (packRepresentative != null)
                {
                    break;
                }
            }
        }
        if (packRepresentative != null)
        {
            snapshot.SpeedBonus += GetFoxPackTalentRank(packRepresentative, "trailcraft") * 0.20f;
            snapshot.RiskReduction += GetFoxPackTalentRank(packRepresentative, "pathfinder") * 0.10f;
            snapshot.RiskReduction += GetFoxPackTalentRank(packRepresentative, "expedition-hardiness") * 0.10f;
            snapshot.InjuryRiskReduction += GetFoxPackTalentRank(packRepresentative, "safe-passage") * 0.10f;
            snapshot.MiaRiskReduction += GetFoxPackTalentRank(packRepresentative, "scent-trail")
                * PackTalentScentTrailReduction;
            snapshot.CargoRiskReduction += GetFoxPackTalentRank(packRepresentative, "cargo-care")
                * PackTalentCargoCareReduction;
            snapshot.RewardBonus += GetFoxPackTalentRank(packRepresentative, "deep-pockets") * 0.10f
                + GetPackExpeditionSpecialtyRewardBonus(packRepresentative, expeditionType);
            if (string.Equals(expeditionType, FoxExpeditionType.Recruitment, StringComparison.Ordinal))
            {
                snapshot.RecruitmentChanceBonus += GetFoxPackTalentRank(packRepresentative, "recruiters-nose") * 0.20f;
            }
        }

        if (expeditionType == FoxExpeditionType.Scout
            && packRepository.IsPackTalentUnlocked(ownerUid, "survey-training"))
            snapshot.ScoutInformationBonus = 1;

        snapshot.RiskReduction = Math.Clamp(snapshot.RiskReduction, 0f, 0.50f);
        snapshot.InjuryRiskReduction = Math.Clamp(
            snapshot.InjuryRiskReduction,
            0f,
            MaximumExpeditionInjuryRiskReduction
        );
        snapshot.RecruitmentChanceBonus = Math.Clamp(snapshot.RecruitmentChanceBonus, 0f, 0.50f);
        snapshot.MiaRiskReduction = Math.Clamp(snapshot.MiaRiskReduction, 0f, 0.35f);
        snapshot.CargoRiskReduction = Math.Clamp(snapshot.CargoRiskReduction, 0f, 0.50f);
        snapshot.RewardBonus = Math.Clamp(snapshot.RewardBonus, 0f, 0.50f);
        return snapshot;
    }

    private static float GetPackExpeditionSpecialtyRewardBonus(Entity representative, string expeditionType)
    {
        // Several routes match more than one specialty. As with individual
        // cargo auras, take only the strongest matching specialty rather than
        // stacking every matching label. All current pack specialties are 10%.
        string[] candidates = expeditionType switch
        {
            FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt or FoxExpeditionType.ApexHunt =>
                new[] { "husbandry-cargo" },
            FoxExpeditionType.Scavenge => new[] { "scavenger-cargo" },
            FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths =>
                new[] { "prospector-cargo", "scavenger-cargo" },
            FoxExpeditionType.Forage or FoxExpeditionType.DistantForage or FoxExpeditionType.PrimevalReach =>
                new[] { "forager-cargo", "cultivator-cargo" },
            FoxExpeditionType.DeepWilds =>
                new[] { "husbandry-cargo", "scavenger-cargo", "prospector-cargo", "cultivator-cargo", "forager-cargo" },
            _ => Array.Empty<string>()
        };
        return candidates.Any(id => GetFoxPackTalentRank(representative, id) > 0) ? 0.10f : 0f;
    }

    private List<FoxExpeditionMemberSummaryPacket> BuildExpeditionMemberSnapshots(
        string ownerUid,
        string expeditionType,
        IEnumerable<string> foxIds)
    {
        List<FoxExpeditionMemberSummaryPacket> snapshots = new();
        if (serverApi == null || packRepository == null)
        {
            return snapshots;
        }

        foreach (string foxId in foxIds)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                continue;
            }

            Entity? fox = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
            bool injured = record.MaxHealth > 0f && record.CurrentHealth < record.MaxHealth * 0.5f;
            List<string> perks = new();
            float conditionStrength = record.MaxHealth > 0f
                ? 0.50f + Math.Clamp(record.CurrentHealth / record.MaxHealth, 0f, 1f) * 0.50f
                : 0.50f;
            float healthStrength = Math.Clamp((record.MaxHealth - FoxBaseMaxHealth) * 0.02f, 0f, 0.50f);
            float movementStrength = 0f;
            float perkStrength = 0f;
            if (fox != null)
            {
                float coreStrength = GetFoxExpeditionCoreStrength(
                    fox,
                    record.CurrentHealth,
                    record.MaxHealth,
                    out conditionStrength,
                    out healthStrength,
                    out movementStrength
                );
                _ = coreStrength;
                perkStrength = GetFoxExpeditionStrengthBonus(fox);
                AddExpeditionPerkSummary(perks, fox, "scout", "Scout");
                AddExpeditionPerkSummary(perks, fox, "pack-leader", "Lead the Way");
                AddExpeditionPerkSummary(perks, fox, "pack-steward", "Packwise");
                if (injured)
                {
                    AddExpeditionPerkSummary(perks, fox, "featherfall", "Sturdy Traveler");
                }
                AddExpeditionPerkSummary(perks, fox, "burst-movement", "Burst Movement");
                AddExpeditionPerkSummary(perks, fox, "trailblazer", "Long Stride");
                AddExpeditionPerkSummary(perks, fox, "sure-feet", "Expedition Hardiness");
                AddExpeditionPerkSummary(perks, fox, "trail-marker", "Leave a Scent");
                AddExpeditionPerkSummary(perks, fox, GetMatchingAuraId(expeditionType, fox), "Cargo Sense");
                if (string.Equals(expeditionType, FoxExpeditionType.Recruitment, StringComparison.Ordinal))
                {
                    AddExpeditionPerkSummary(perks, fox, "recruiters-nose", "Scent for Strays");
                }
            }

            snapshots.Add(new FoxExpeditionMemberSummaryPacket
            {
                FoxId = foxId,
                Number = record.Number,
                Name = record.Name ?? string.Empty,
                BaseStrength = conditionStrength + healthStrength + movementStrength,
                Perks = perks,
                ConditionStrength = conditionStrength,
                HealthStrength = healthStrength,
                MovementStrength = movementStrength,
                PerkStrength = perkStrength,
                TotalStrength = conditionStrength + healthStrength + movementStrength + perkStrength,
                Personality = record.Personality ?? string.Empty,
                MoodAtDeparture = record.Mood ?? string.Empty,
                SpeciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId
            });
        }
        return snapshots;
    }

    private static void AddExpeditionPerkSummary(
        List<string> summaries,
        Entity fox,
        string perkId,
        string name)
    {
        int rank = GetFoxPerkRank(fox, perkId);
        if (rank > 0)
        {
            summaries.Add(rank > 1 ? $"{name} {rank}" : name);
        }
    }

    private static float GetFoxExpeditionStrengthBonus(Entity fox)
    {
        float bonus = 0f;
        if (GetFoxPerkRank(fox, "scout") > 0)
        {
            bonus += 0.5f;
        }
        if (GetFoxPerkRank(fox, "pack-leader") > 0)
        {
            bonus += 0.5f;
        }
        if (GetFoxPerkRank(fox, "pack-steward") > 0)
        {
            bonus += 0.5f;
        }
        GetHealth(fox, out float currentHealth, out float maxHealth);
        if (maxHealth > 0f && currentHealth < maxHealth * 0.5f)
        {
            bonus += GetFoxPerkRank(fox, "featherfall") * 0.125f;
        }
        return IsCompanionPregnant(fox)
            ? bonus * GetPregnancyExpeditionMultiplier(fox)
            : bonus;
    }

    private static float GetFoxExpeditionCoreStrength(
        Entity fox,
        float currentHealth,
        float maxHealth,
        out float conditionStrength,
        out float healthStrength,
        out float movementStrength)
    {
        float healthFraction = maxHealth > 0f
            ? Math.Clamp(currentHealth / maxHealth, 0f, 1f)
            : 0f;
        conditionStrength = 0.50f + healthFraction * 0.50f;
        healthStrength = Math.Clamp((maxHealth - FoxBaseMaxHealth) * 0.02f, 0f, 0.50f);

        float permanentMovementBonus = GetFoxPerkRank(fox, "more-speed") * 0.10f
            + GetFoxPerkRank(fox, "even-more-speed") * 0.05f;
        movementStrength = Math.Clamp(permanentMovementBonus, 0f, 0.45f);
        if (IsCompanionPregnant(fox))
        {
            float pregnancyMultiplier = GetPregnancyExpeditionMultiplier(fox);
            conditionStrength *= pregnancyMultiplier;
            healthStrength *= pregnancyMultiplier;
            movementStrength *= pregnancyMultiplier;
        }
        return conditionStrength + healthStrength + movementStrength;
    }

    private static float GetFoxExpeditionSpeedBonus(Entity fox)
    {
        float bonus = GetFoxPerkRank(fox, "trailblazer") > 0 ? 0.50f : 0f;
        bonus += GetFoxPerkRank(fox, "burst-movement") * 0.10f;
        return bonus;
    }

    private static float GetFoxExpeditionRewardBonus(Entity fox, string expeditionType)
    {
        string auraId = GetMatchingAuraId(expeditionType, fox);
        int rank = string.IsNullOrEmpty(auraId) ? 0 : GetFoxPerkRank(fox, auraId);
        return rank <= 0 ? 0f : rank * 0.10f + (GetFoxPerkRank(fox, "greater-aura") > 0 ? 0.10f : 0f);
    }

    private static string GetMatchingAuraId(string expeditionType, Entity fox)
    {
        string[] candidates = expeditionType switch
        {
            FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt or FoxExpeditionType.ApexHunt => new[] { "aura-of-husbandry" },
            FoxExpeditionType.Scavenge => new[] { "aura-of-scavenging" },
            FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths => new[] { "aura-of-prospecting", "aura-of-scavenging" },
            FoxExpeditionType.Forage or FoxExpeditionType.DistantForage or FoxExpeditionType.PrimevalReach => new[] { "aura-of-foraging", "aura-of-cultivation" },
            FoxExpeditionType.DeepWilds => new[] { "aura-of-husbandry", "aura-of-scavenging", "aura-of-prospecting", "aura-of-cultivation", "aura-of-foraging" },
            _ => Array.Empty<string>()
        };
        return candidates.OrderByDescending(id => GetFoxPerkRank(fox, id)).FirstOrDefault(id => GetFoxPerkRank(fox, id) > 0) ?? string.Empty;
    }

    private static float GetFoxExpeditionRiskReduction(Entity fox)
    {
        return GetFoxPerkRank(fox, "pack-leader") > 0 ? 0.15f : 0f;
    }

    private static float GetFoxExpeditionInjuryRiskReduction(Entity fox)
    {
        return GetFoxPerkRank(fox, "sure-feet") * ExpeditionHardinessRiskReductionPerRank;
    }

    private static float GetFoxRecruitmentChanceBonus(Entity fox)
    {
        return GetFoxPerkRank(fox, "recruiters-nose") * RecruitmentChanceBonusPerRank;
    }

    private static float GetRecruitmentBaseChance(float expeditionStrength, float targetStrength)
    {
        if (targetStrength <= 0f)
        {
            return 0f;
        }

        return Math.Clamp(
            Math.Min(RecruitmentBaseChanceCap, expeditionStrength / targetStrength * RecruitmentBaseChanceScale),
            0f,
            RecruitmentBaseChanceCap
        );
    }

    private ExpeditionStoryState CreateExpeditionStory(FoxExpeditionRecord expedition)
    {
        FoxExpeditionDefinition definition = FoxExpeditionCatalog.Get(expedition.Type)
            ?? throw new InvalidOperationException($"Unknown expedition route '{expedition.Type}'.");
        int injuredPartyMembers = packRepository?.GetRecordsForOwner(expedition.OwnerUid)
            .Count(record => expedition.SelectedFoxIds.Contains(record.FoxId, StringComparer.Ordinal)
                && record.MaxHealth > 0f
                && record.CurrentHealth < record.MaxHealth * 0.5f) ?? 0;
        ExpeditionStoryState story = ExpeditionStoryResolver.Resolve(
            expedition,
            definition,
            serverApi?.World.Rand ?? Random.Shared,
            injuredPartyMembers);
        LogExpeditionStory(expedition, story);
        return story;
    }

    private void LogExpeditionStory(FoxExpeditionRecord expedition, ExpeditionStoryState story)
    {
        serverApi?.Logger.Debug(
            "[FeralKinshipCompanions] Expedition story #{0}: route={1}, profiles={2}/{3}, strength={4:0.###}/{5:0.###}, raw={6:0.###}, completion={7:0.###}, overcap={8:0.###}, risk={9:0.###}, events={10}, return={11}, injury={12}, mia={13}, cargo={14}, modifiers=ordinary+{15}, goalx{16:0.###}, special+{17}, debris+{18}, vessel+{19}",
            expedition.ExpeditionId,
            expedition.Type,
            story.PrimaryProfile,
            story.SecondaryProfile,
            story.ExpeditionStrength,
            story.TargetStrength,
            story.RawCompletion,
            story.Completion,
            story.OvercapRewardFactor,
            story.EffectiveRiskMultiplier,
            string.Join(",", story.Events.Select(item => $"{item.Id}:{item.Outcome}")),
            story.ReturnState,
            story.InjuryState,
            story.MiaCause,
            story.CargoState,
            story.RewardModifiers.AdditionalOrdinaryRolls,
            story.RewardModifiers.GoalQuantityMultiplier,
            story.RewardModifiers.AdditionalSpecialRolls,
            story.RewardModifiers.AdditionalDebrisRolls,
            story.RewardModifiers.AdditionalVesselRolls);
    }

    private static float GetExpeditionDurationHours(string type, float averageSpeedBonus)
    {
        float timeReduction = Math.Clamp(averageSpeedBonus * 0.50f, 0f, 0.25f);
        float baseDuration = FoxExpeditionCatalog.Get(type)?.BaseDurationHours ?? 0f;
        return baseDuration * (1f - timeReduction);
    }

    private static string FormatExpeditionHours(double hours)
    {
        hours = Math.Max(0d, hours);
        int wholeHours = (int)Math.Floor(hours);
        int minutes = (int)Math.Round((hours - wholeHours) * 60d);
        if (minutes >= 60)
        {
            wholeHours++;
            minutes = 0;
        }
        if (wholeHours <= 0)
        {
            int displayedMinutes = Math.Max(1, minutes);
            return $"{displayedMinutes} in-game {(displayedMinutes == 1 ? "minute" : "minutes")}";
        }
        return minutes > 0
            ? $"{wholeHours}h {minutes}m in-game"
            : $"{wholeHours} in-game {(wholeHours == 1 ? "hour" : "hours")}";
    }

    private void ApplyExpeditionPerks(FoxExpeditionRecord expedition)
    {
        ExpeditionPerkSnapshot snapshot = GetExpeditionPerkSnapshot(
            expedition.OwnerUid,
            expedition.Type,
            expedition.SelectedFoxIds
        );
        expedition.ExpeditionStrength += snapshot.StrengthBonus;
        expedition.PerkStrengthBonus = snapshot.StrengthBonus;
        expedition.PerkSpeedBonus = snapshot.SpeedBonus;
        expedition.PerkRiskReduction = snapshot.RiskReduction;
        expedition.PerkInjuryRiskReduction = snapshot.InjuryRiskReduction;
        expedition.RecruitmentChanceBonus = snapshot.RecruitmentChanceBonus;
        expedition.PerkRewardBonus = snapshot.RewardBonus;
        expedition.PerkMiaRiskReduction = snapshot.MiaRiskReduction;
        expedition.PerkCargoRiskReduction = snapshot.CargoRiskReduction;
    }

    private bool HasRescueTarget(string ownerUid)
    {
        return packRepository?.GetRecordsForOwner(ownerUid)
            .Any(IsRescueTarget) == true;
    }

    private static bool IsRescueTarget(FoxPackRecordV2 record)
    {
        return record != null
            && !record.Archived
            && record.DurableStateInitialized
            && record.HasLastKnownPosition
            && string.Equals(record.RecoveryStatus, CompanionRecoveryStatus.Recoverable, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(record.Status, "MIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, "Not currently loaded", StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldExpeditionRunLate(FoxExpeditionRecord expedition)
    {
        GetExpeditionRiskRates(expedition.Type, out float lateChance, out _, out _, out _, out _);
        float multiplier = GetExpeditionRiskMultiplier(expedition);
        float scavengeDetourChance = 0f;
        if (expedition.Type == FoxExpeditionType.Scavenge && packRepository != null
            && packRepository.TryGetScavengeSite(
                expedition.OwnerUid, expedition.ScavengeSiteId, out FoxScavengeSiteRecord? site)
            && site?.DangerRule == "predator-tracks")
            scavengeDetourChance = 0.10f;
        bool late = serverApi?.World.Rand.NextDouble()
            < Math.Clamp(lateChance * multiplier + scavengeDetourChance, 0f, 1f);
        return late && CompanionSpeciesTraits.Mitigate(expedition.SpeciesBonus,
            scavengeDetourChance > 0 ? "predator-tracks" : "difficult-terrain", "late",
            serverApi?.World.Rand ?? Random.Shared).Length > 0;
    }

    private string RollExpeditionRiskOutcome(
        FoxExpeditionRecord expedition,
        out string affectedFoxId,
        ISet<string>? excludedFoxIds = null)
    {
        affectedFoxId = string.Empty;
        GetExpeditionRiskRates(
            expedition.Type,
            out _,
            out float cargoChance,
            out float injuryChance,
            out float miaChance,
            out float mortalChance
        );
        double roll = serverApi?.World.Rand.NextDouble() ?? 1d;
        float multiplier = GetExpeditionRiskMultiplier(expedition);
        float injuryMultiplier = Math.Max(
            0.50f,
            multiplier - expedition.PerkInjuryRiskReduction
        );
        float scaledCargo = cargoChance * Math.Max(
            0.25f,
            multiplier - expedition.PerkCargoRiskReduction
        );
        float scaledInjury = injuryChance * injuryMultiplier;
        float scaledMia = miaChance * Math.Max(0.35f, multiplier - expedition.PerkMiaRiskReduction);
        float scaledMortal = mortalChance * injuryMultiplier;

        if (roll < scaledMortal)
        {
            affectedFoxId = PickSelectedFoxId(expedition, excludedFoxIds);
            return "Mortally wounded";
        }

        roll -= scaledMortal;
        if (roll < scaledMia)
        {
            affectedFoxId = PickSelectedFoxId(expedition, excludedFoxIds);
            return "MIA";
        }

        roll -= scaledMia;
        if (roll < scaledInjury)
        {
            affectedFoxId = PickSelectedFoxId(expedition, excludedFoxIds);
            return "Returned injured";
        }

        roll -= scaledInjury;
        return roll < scaledCargo ? "Abandoned cargo" : "Returned healthy";
    }

    private ExpeditionRiskResults RollExpeditionRiskOutcomes(
        FoxExpeditionRecord expedition,
        float completion)
    {
        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(expedition.Type);
        int rollCount = 1;
        float shortfall = 1f - Math.Clamp(completion, 0f, 1f);
        if (definition?.IsCrownJewel == true)
        {
            rollCount = 2 + (int)Math.Ceiling(shortfall * 6f);
        }
        else if ((definition?.Tier ?? 0) >= 3)
        {
            rollCount = 1 + (int)Math.Ceiling(shortfall * 3f);
        }

        ExpeditionRiskResults results = new();
        HashSet<string> affectedFoxIds = new(StringComparer.Ordinal);
        for (int roll = 0; roll < rollCount; roll++)
        {
            string outcome = RollExpeditionRiskOutcome(
                expedition,
                out string affectedFoxId,
                affectedFoxIds
            );
            if (string.Equals(outcome, "Abandoned cargo", StringComparison.Ordinal))
            {
                results.CargoDamaged = true;
            }
            else if (!string.Equals(outcome, "Returned healthy", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(affectedFoxId))
            {
                affectedFoxIds.Add(affectedFoxId);
                results.FoxOutcomes[affectedFoxId] = outcome;
            }
        }
        return results;
    }

    private static void GetExpeditionRiskRates(
        string type,
        out float lateChance,
        out float cargoChance,
        out float injuryChance,
        out float miaChance,
        out float mortalChance)
    {
        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        lateChance = definition?.LateChance ?? 0f;
        cargoChance = definition?.CargoChance ?? 0f;
        injuryChance = definition?.InjuryChance ?? 0f;
        miaChance = definition?.MiaChance ?? 0f;
        mortalChance = definition?.MortalChance ?? 0f;
    }

    private float GetExpeditionRiskMultiplier(FoxExpeditionRecord expedition)
    {
        float completion = expedition.TargetStrength <= 0f
            ? 0f
            : Math.Clamp(expedition.ExpeditionStrength / expedition.TargetStrength, 0f, 1f);
        int injured = packRepository?.GetRecordsForOwner(expedition.OwnerUid)
            .Count(record => expedition.SelectedFoxIds.Contains(record.FoxId, StringComparer.Ordinal)
                && record.MaxHealth > 0f
                && record.CurrentHealth < record.MaxHealth * 0.5f) ?? 0;
        return Math.Max(
            0.50f,
            1f + (1f - completion) * 1.50f + injured * 0.10f
                - expedition.PerkRiskReduction
                - expedition.PreparationRiskReduction
                - expedition.PatrolRiskReduction
        );
    }

    private string PickSelectedFoxId(
        FoxExpeditionRecord expedition,
        ISet<string>? excludedFoxIds = null)
    {
        if (expedition.SelectedFoxIds.Count == 0 || serverApi == null)
        {
            return string.Empty;
        }

        List<string> candidates = expedition.SelectedFoxIds
            .Where(foxId => excludedFoxIds == null || !excludedFoxIds.Contains(foxId))
            .ToList();
        return candidates.Count == 0
            ? string.Empty
            : candidates[serverApi.World.Rand.Next(candidates.Count)];
    }

    private ExpeditionResolution ResolveCompletedExpedition(FoxExpeditionRecord expedition, int lootBatchStartIndex)
    {
        if (expedition.Type == FoxExpeditionType.Scout)
            return ResolveScavengeScout(expedition);
        if (expedition.Type == FoxExpeditionType.Scavenge && expedition.ScavengeSiteId > 0)
            return ResolveScavengeSite(expedition);

        if (expedition.Story != null
            && FoxExpeditionCatalog.Get(expedition.Type)?.ProducesRandomLoot == true)
        {
            return ResolveStoryExpedition(expedition, lootBatchStartIndex);
        }

        float completion = ExpeditionCalculations.GetCompletion(
            expedition.ExpeditionStrength,
            expedition.TargetStrength);

        if (string.Equals(expedition.Type, FoxExpeditionType.SearchLost, StringComparison.Ordinal))
        {
            List<(string FoxId, string Status)> returning = expedition.SelectedFoxIds
                .Select(foxId => (foxId, "Returned healthy"))
                .ToList();

            bool found = serverApi != null && serverApi.World.Rand.NextDouble() <= completion;
            FoxPackRecordV2? missing = null;
            if (found
                && packRepository?.TryGetRecord(expedition.TargetFoxId, out FoxPackRecordV2? requestedMissing) == true
                && requestedMissing != null
                && string.Equals(requestedMissing.OwnerUid, expedition.OwnerUid, StringComparison.Ordinal)
                && IsRescueTarget(requestedMissing))
            {
                missing = requestedMissing;
            }
            if (missing != null)
            {
                Entity? targetEntity = FindLoadedCompanionByFoxId(missing.FoxId);
                if (targetEntity == null)
                {
                    QueueExpeditionReturns(expedition.OwnerUid, returning, "search_lost.success");
                    missing.WasMiaBeforeReturn = true;
                    missing.PendingDialogueEvent = "expedition.mia_return";
                    BeginRecoverableCompanionRecovery(missing);
                    return new ExpeditionResolution(
                        "The rescue party found a trail. Its saved location is being checked before recovery is confirmed."
                    );
                }

                GetExpeditionRiskRates(
                    expedition.Type,
                    out _,
                    out _,
                    out float injuryChance,
                    out _,
                    out _
                );
                bool returnedInjured = serverApi != null
                    && serverApi.World.Rand.NextDouble()
                        < Math.Clamp(injuryChance * GetExpeditionRiskMultiplier(expedition), 0f, 0.75f);
                string recoveryStatus = returnedInjured ? "Returned injured" : "Returned healthy";
                returning.Add((missing.FoxId, recoveryStatus));
                QueueExpeditionReturns(expedition.OwnerUid, returning, "search_lost.success");
                return new ExpeditionResolution(
                    returnedInjured
                        ? $"Search for lost found {GetFoxRecordLabel(missing)}, but the companion returned injured."
                        : $"Search for lost returned successfully. {GetFoxRecordLabel(missing)} was found and brought home."
                );
            }

            QueueExpeditionReturns(expedition.OwnerUid, returning, "search_lost.failure");

            return new ExpeditionResolution(
                completion >= 1f
                    ? "Search for lost returned, but no MIA companion was located."
                    : $"Search for lost returned without finding the missing companions ({completion * 100f:0}% search coverage)."
            );
        }

        if (string.Equals(expedition.Type, FoxExpeditionType.Recruitment, StringComparison.Ordinal))
        {
            string recruitmentOutcome = RollExpeditionRiskOutcome(expedition, out string recruitmentAffectedFoxId);
            (string FoxId, string Status)[] recruitmentReturns = expedition.SelectedFoxIds.Select(foxId => (
                foxId,
                foxId == recruitmentAffectedFoxId ? recruitmentOutcome : "Returned healthy")).ToArray();

            float recruitmentChance = Math.Clamp(
                GetRecruitmentBaseChance(expedition.ExpeditionStrength, expedition.TargetStrength)
                    + expedition.RecruitmentChanceBonus,
                0f,
                1f
            );
            if (serverApi == null || serverApi.World.Rand.NextDouble() > recruitmentChance)
            {
                QueueExpeditionReturns(expedition.OwnerUid, recruitmentReturns, "recruitment.failure");
                return new ExpeditionResolution(
                    $"Recruitment returned without finding a new companion. Work covered: {recruitmentChance * 100f:0}%.");
            }

            if (packRepository == null)
            {
                return new ExpeditionResolution(
                    "Recruitment succeeded, but its claim could not be recorded.");
            }

            if (!TryChooseRecruitmentReward(
                    expedition,
                    out CompanionSpeciesProfile recruitedSpecies,
                    out CompanionRecruitmentVariant recruitedVariant))
            {
                QueueExpeditionReturns(expedition.OwnerUid, recruitmentReturns, "recruitment.failure");
                return new ExpeditionResolution(
                    "Recruitment succeeded, but no safe supported animal could be found. No invalid reward was created.");
            }

            packRepository.SetPendingRecruitment(
                expedition.OwnerUid,
                recruitedSpecies.Id,
                recruitedVariant.EntityCode
            );
            QueueExpeditionReturns(expedition.OwnerUid, recruitmentReturns, "recruitment.success");

            return new ExpeditionResolution(
                $"Recruitment succeeded at {recruitmentChance * 100f:0}% chance. "
                    + $"A {GetRecruitmentDisplayName(recruitedSpecies, recruitedVariant).ToLowerInvariant()} "
                    + "is ready to claim from the pack cache.",
                recruitmentSucceeded: true);
        }

        if (string.Equals(expedition.Type, FoxExpeditionType.PackPatrol, StringComparison.Ordinal))
        {
            string patrolOutcome = RollExpeditionRiskOutcome(expedition, out string patrolAffectedFoxId);
            QueueExpeditionReturns(
                expedition.OwnerUid,
                expedition.SelectedFoxIds.Select(foxId => (
                    foxId,
                    foxId == patrolAffectedFoxId ? patrolOutcome : "Returned healthy")),
                expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal"
            );

            int awardedPoints = Math.Max(1, (int)Math.Round(3f * completion));
            packRepository?.AdjustPackPoints(expedition.OwnerUid, awardedPoints);
            bool securedTrails = completion >= 1f
                && string.Equals(patrolOutcome, "Returned healthy", StringComparison.Ordinal);
            if (securedTrails)
            {
                packRepository?.SetPatrolPrepared(expedition.OwnerUid, true);
            }

            string consequence = patrolOutcome switch
            {
                "Returned injured" => " One companion returned injured.",
                "MIA" => " One companion is MIA.",
                "Mortally wounded" => " One companion was carried home mortally wounded.",
                _ => string.Empty
            };
            return new ExpeditionResolution(
                $"Pack patrol returned and earned {awardedPoints} pack {(awardedPoints == 1 ? "point" : "points")}. "
                + (securedTrails
                    ? "The secured trails will reduce risk on the next non-patrol expedition."
                    : $"The party covered {completion * 100f:0}% of the patrol; the trails were not fully secured.")
                + consequence
            );
        }

        ExpeditionRiskResults riskResults = RollExpeditionRiskOutcomes(expedition, completion);
        int itemCount = GenerateExpeditionLoot(
            expedition.OwnerUid,
            expedition.Type,
            completion,
            expedition.SelectedFoxIds.Count,
            expedition.PerkRewardBonus,
            party: expedition.MemberSnapshots
        );
        if (riskResults.CargoDamaged)
        {
            itemCount = Math.Max(0, itemCount - DiscardRandomPackLoot(expedition.OwnerUid, lootBatchStartIndex));
        }

        QueueExpeditionReturns(
            expedition.OwnerUid,
            expedition.SelectedFoxIds.Select(foxId => (
                foxId,
                riskResults.FoxOutcomes.TryGetValue(foxId, out string? outcome)
                    ? outcome
                    : "Returned healthy")),
            expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal"
        );

        string message = $"{GetExpeditionDisplayName(expedition.Type)} returned with {itemCount} {(itemCount == 1 ? "item" : "items")}. "
            + DescribeExpeditionConsequences(riskResults, completion);
        return new ExpeditionResolution(message);
    }

    private ExpeditionResolution ResolveStoryExpedition(FoxExpeditionRecord expedition, int lootBatchStartIndex)
    {
        ExpeditionStoryState story = expedition.Story!;
        ExpeditionStoryResolver.NormalizeStory(story);
        ExpeditionRiskResults riskResults = new();
        foreach (FoxExpeditionStoryOutcome outcome in story.MemberOutcomes)
        {
            if (!string.IsNullOrWhiteSpace(outcome.FoxId)
                && !string.IsNullOrWhiteSpace(outcome.Status))
            {
                riskResults.FoxOutcomes[outcome.FoxId] = outcome.Status;
            }
        }
        riskResults.CargoDamaged = string.Equals(story.CargoState, "damaged", StringComparison.Ordinal);

        int itemCount = GenerateExpeditionLoot(
            expedition.OwnerUid,
            expedition.Type,
            story.Completion,
            expedition.SelectedFoxIds.Count,
            expedition.PerkRewardBonus,
            story,
            expedition.MemberSnapshots,
            expedition.SpeciesBonus);
        if (riskResults.CargoDamaged)
        {
            itemCount = Math.Max(0,
                itemCount - DiscardRandomPackLoot(expedition.OwnerUid, lootBatchStartIndex));
        }

        QueueExpeditionReturns(
            expedition.OwnerUid,
            expedition.SelectedFoxIds.Select(foxId => (
                foxId,
                riskResults.FoxOutcomes.TryGetValue(foxId, out string? outcome)
                    ? outcome
                    : "Returned healthy")),
            expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal");

        string eventSummary = story.Events.Count == 0
            ? "No field note was recorded."
            : $"{story.Events.Count} field note{(story.Events.Count == 1 ? " was" : "s were")} recorded in the expedition report.";
        string message = $"{GetExpeditionDisplayName(expedition.Type)} returned with {itemCount} "
            + $"{(itemCount == 1 ? "item" : "items")}. {eventSummary} "
            + DescribeExpeditionConsequences(riskResults, story.Completion);
        return new ExpeditionResolution(message);
    }

    private static string DescribeExpeditionConsequences(
        ExpeditionRiskResults results,
        float completion)
    {
        int injured = results.FoxOutcomes.Values.Count(outcome =>
            string.Equals(outcome, "Returned injured", StringComparison.Ordinal));
        int mia = results.FoxOutcomes.Values.Count(outcome =>
            string.Equals(outcome, "MIA", StringComparison.Ordinal));
        int mortal = results.FoxOutcomes.Values.Count(outcome =>
            string.Equals(outcome, "Mortally wounded", StringComparison.Ordinal));

        List<string> consequences = new();
        if (results.CargoDamaged)
        {
            consequences.Add("Some cargo was damaged");
        }
        if (injured > 0)
        {
            consequences.Add(injured == 1
                ? "one companion returned injured"
                : $"{injured} companions returned injured");
        }
        if (mia > 0)
        {
            consequences.Add(mia == 1 ? "one companion is MIA" : $"{mia} companions are MIA");
        }
        if (mortal > 0)
        {
            consequences.Add(mortal == 1
                ? "one companion was carried home mortally wounded"
                : $"{mortal} companions were carried home mortally wounded");
        }
        if (consequences.Count == 0)
        {
            return $"Work covered: {completion * 100f:0}%.";
        }

        string summary = string.Join("; ", consequences) + ".";
        if (mia > 0)
        {
            summary += " Search for lost is now available.";
        }
        return summary;
    }

    private void BeginRecoverableCompanionRecovery(FoxPackRecordV2 record)
    {
        if (serverApi == null || packRepository == null)
        {
            return;
        }

        Entity? loaded = FindLoadedCompanionForRecovery(record.FoxId);
        if (loaded != null)
        {
            record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
            SetFoxAwayFromWorld(loaded, true);
            SetExpeditionMemberStatus(record.OwnerUid, record.FoxId, "Returned healthy");
            packRepository.Save();
            return;
        }

        // Preserve a usable state before starting asynchronous chunk loading.
        // A missing entity in the first callback is not proof that its durable
        // ledger snapshot is unrecoverable.
        record.RecoveryStatus = CompanionRecoveryStatus.Recoverable;
        packRepository.Save();

        if (!record.HasLastKnownPosition)
        {
            SendRecoveryLookupResult(record, null);
            return;
        }

        if (!pendingRecoveryChecks.Add(record.FoxId))
        {
            SendPackStateIfOwnerOnline(record.OwnerUid, "The rescue party is already checking that companion's saved location.");
            return;
        }

        int chunkX = (int)Math.Floor(record.LastKnownX / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(record.LastKnownZ / (double)GlobalConstants.ChunkSize);
        int checks = 0;
        const int MaximumChecks = 12;

        void CheckLoadedEntity()
        {
            if (!pendingRecoveryChecks.Contains(record.FoxId) || serverApi == null) return;
            RefreshLoadedFoxPackRecords();
            Entity? recovered = FindLoadedCompanionForRecovery(record.FoxId);
            if (recovered == null)
            {
                checks++;
                if (checks < MaximumChecks)
                {
                    serverApi.World.RegisterCallback(_ => CheckLoadedEntity(), 500);
                    return;
                }

                pendingRecoveryChecks.Remove(record.FoxId);
                SendRecoveryLookupResult(record, null);
                return;
            }

            pendingRecoveryChecks.Remove(record.FoxId);
            SendRecoveryLookupResult(record, recovered);
        }

        try
        {
            if (record.LastKnownDimension == 0)
            {
                serverApi.WorldManager.LoadChunkColumnPriority(
                    chunkX,
                    chunkZ,
                    new ChunkLoadOptions { KeepLoaded = false });
            }
            else
            {
                serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, record.LastKnownDimension);
            }

            SendPackStateIfOwnerOnline(record.OwnerUid, "The rescue party found a trail and is checking its saved chunk before confirming recovery.");
            serverApi.World.RegisterCallback(_ => CheckLoadedEntity(), 250);
        }
        catch (Exception exception)
        {
            pendingRecoveryChecks.Remove(record.FoxId);
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Could not load the saved chunk while recovering companion {0}: {1}",
                record.FoxId,
                exception.Message);
            SendRecoveryLookupResult(record, null);
        }
    }

    private void MarkCompanionUnrecoverable(FoxPackRecordV2 record, string reason)
    {
        ReconcileMissingCompanionContent(record);
        if (record.MissingContentArchive?.Active == true) return;
        if (!companionContentReady || !IsRecordedEntityTypeAvailable(record, out _))
        {
            record.RecoveryStatus = CompanionRecoveryStatus.Unknown;
            return;
        }
        record.RecoveryStatus = CompanionRecoveryStatus.Unrecoverable;
        record.Status = "Unrecoverable";
        serverApi?.Logger.Warning(
            "[FeralKinshipCompanions] Companion {0} marked unrecoverable: {1}",
            record.FoxId,
            reason
        );
    }

    private void SendPackStateIfOwnerOnline(string ownerUid, string message)
    {
        if (serverApi?.World.PlayerByUid(ownerUid) is IServerPlayer owner)
        {
            SendPackState(owner, message);
        }
    }

    private void QueueExpeditionReturns(
        string ownerUid,
        IEnumerable<(string FoxId, string Status)> returns,
        string healthyDialogueEvent = "expedition.return_normal")
    {
        if (serverApi == null || packRepository == null)
        {
            return;
        }

        long returnAtUtcMs = Math.Max(
            UtcNowMs(),
            packRepository.GetPendingReturnRecords()
                .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
                .Select(record => record.PendingReturnAtUtcMs + ExpeditionReturnStaggerMs)
                .Concat(packRepository.GetPendingDepartureRecords()
                    .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
                    .Select(record => record.PendingDepartureAtUtcMs + ExpeditionReturnStaggerMs))
                .DefaultIfEmpty(0)
                .Max());
        foreach ((string foxId, string statusLabel) in returns)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                continue;
            }

            record.WasMiaBeforeReturn = string.Equals(record.Status, "MIA", StringComparison.OrdinalIgnoreCase);
            record.PendingDialogueEvent = record.WasMiaBeforeReturn
                ? "expedition.mia_return"
                : statusLabel switch
                {
                    "Returned injured" => "expedition.return_injured",
                    "Mortally wounded" => "expedition.return_mortally_wounded",
                    "MIA" => string.Empty,
                    _ => healthyDialogueEvent
                };
            record.PendingReturnStatus = statusLabel;
            record.PendingReturnAtUtcMs = returnAtUtcMs;
            record.PendingDepartureStatus = string.Empty;
            record.PendingDepartureAtUtcMs = 0;
            record.PendingDepartureDeadlineUtcMs = 0;
            record.PendingDepartureMoment = false;
            record.Status = "Returning";
            record.StatusExpiresDay = 0d;
            Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
            if (entity != null && IsTamedFox(entity) && IsOwner(entity, ownerUid))
            {
                ITreeAttribute entityStatus = GetDomesticationStatus(entity, true)!;
                entityStatus.SetString(ExpeditionStatusKey, "Returning");
                entityStatus.SetDouble(ExpeditionStatusExpiresDayKey, 0d);
                SetFoxAwayFromWorld(entity, true);
                MarkSocialStateDirty(entity);
            }

            returnAtUtcMs += ExpeditionReturnStaggerMs;
        }

        // Resolve the first member immediately unless another persisted pack
        // transition already owns that moment. Later members retain their
        // quarter-second offsets, so separate parties never pop in at once.
        TryProcessPendingExpeditionReturns(UtcNowMs());
    }

    private void QueueExpeditionDepartures(
        string ownerUid,
        IEnumerable<string> foxIds,
        string awayStatus)
    {
        if (serverApi == null || packRepository == null)
        {
            return;
        }

        long departureAtUtcMs = Math.Max(
            UtcNowMs(),
            packRepository.GetPendingDepartureRecords()
                .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
                .Select(record => record.PendingDepartureAtUtcMs + ExpeditionReturnStaggerMs)
                .Concat(packRepository.GetPendingReturnRecords()
                    .Where(record => string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
                    .Select(record => record.PendingReturnAtUtcMs + ExpeditionReturnStaggerMs))
                .DefaultIfEmpty(0)
                .Max());
        string leavingStatus = awayStatus.Replace("Away —", "Leaving —", StringComparison.OrdinalIgnoreCase);
        string[] selectedFoxIds = foxIds.ToArray();
        string departureMomentFoxId = selectedFoxIds.FirstOrDefault(foxId =>
        {
            return packRepository.TryGetRecord(foxId, out FoxPackRecordV2? candidate)
                && candidate != null
                && string.Equals(candidate.OwnerUid, ownerUid, StringComparison.Ordinal);
        }) ?? string.Empty;
        foreach (string foxId in selectedFoxIds)
        {
            if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
                || record == null
                || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                continue;
            }

            record.PendingDepartureStatus = awayStatus;
            record.PendingDepartureAtUtcMs = departureAtUtcMs;
            record.PendingDepartureDeadlineUtcMs = departureAtUtcMs + ExpeditionDepartureTimeoutMs;
            record.PendingDepartureMoment = string.Equals(record.FoxId, departureMomentFoxId, StringComparison.Ordinal);
            record.Status = leavingStatus;
            record.StatusExpiresDay = 0d;

            Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
            if (entity != null && IsTamedFox(entity) && IsOwner(entity, ownerUid))
            {
                // Expeditions take the fox out of local logistics. Any held
                // load is safely dropped at departure instead of blocking the
                // party or making the fox appear unavailable.
                DropFoxStorageCargo(entity);
                ITreeAttribute entityStatus = GetDomesticationStatus(entity, true)!;
                entityStatus.SetString(ExpeditionStatusKey, leavingStatus);
                entityStatus.SetDouble(ExpeditionStatusExpiresDayKey, 0d);
                SetFoxAwayFromWorld(entity, false);
                if (entity is EntityAgent agent)
                {
                    // Give the dedicated departure task exclusive ownership
                    // of movement before it asks the pathfinder to start.
                    agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
                    agent.Controls.StopAllMovement();
                    entity.Pos.Motion.Set(0, 0, 0);
                }
                MarkSocialStateDirty(entity);
            }

            departureAtUtcMs += ExpeditionReturnStaggerMs;
        }

        if (!string.IsNullOrWhiteSpace(departureMomentFoxId)
            && packRepository.TryGetRecord(departureMomentFoxId, out FoxPackRecordV2? departureRecord)
            && departureRecord?.EntityId > 0
            && serverApi.World.GetEntityById(departureRecord.EntityId) is Entity departureCompanion)
        {
            RememberTriggeredConversationContext(departureCompanion, "expedition_departure", 60 * 1000);
            TryStartTriggeredRelationshipConversation(ownerUid, departureCompanion,
                "expedition_departure", UtcNowMs());
        }

        // The departure task owns the walk. The expedition tick still force-
        // departs a straggler after the persisted timeout.
        TryProcessPendingExpeditionDepartures(UtcNowMs());
    }

    private bool TryProcessPendingExpeditionDepartures(long nowUtcMs)
    {
        if (packRepository == null)
        {
            return false;
        }

        bool changed = false;
        foreach (FoxPackRecordV2 record in packRepository.GetPendingDepartureRecords()
                     .Where(candidate => candidate.PendingDepartureDeadlineUtcMs > 0
                         ? candidate.PendingDepartureDeadlineUtcMs <= nowUtcMs
                         : candidate.PendingDepartureAtUtcMs <= nowUtcMs)
                     .ToArray())
        {
            string pendingStatus = record.PendingDepartureStatus;
            record.PendingDepartureStatus = string.Empty;
            record.PendingDepartureAtUtcMs = 0;
            record.PendingDepartureDeadlineUtcMs = 0;
            SetExpeditionMemberStatus(record.OwnerUid, record.FoxId, pendingStatus);
            changed = true;
        }
        return changed;
    }

    private bool TryProcessPendingExpeditionReturns(long nowUtcMs)
    {
        if (packRepository == null)
        {
            return false;
        }

        bool changed = false;
        foreach (FoxPackRecordV2 record in packRepository.GetPendingReturnRecords()
                     .Where(candidate => candidate.PendingReturnAtUtcMs <= nowUtcMs)
                     .ToArray())
        {
            string pendingStatus = record.PendingReturnStatus;
            SetExpeditionMemberStatus(record.OwnerUid, record.FoxId, pendingStatus);
            changed = true;
        }
        return changed;
    }

    private void SetExpeditionMemberStatus(string ownerUid, string foxId, string statusLabel)
    {
        if (serverApi == null || packRepository == null || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record) || record == null)
        {
            return;
        }

        if (record.MissingContentArchive?.Active == true) return;
        bool departureMoment = record.PendingDepartureMoment;
        record.Status = statusLabel;
        record.PendingDepartureStatus = string.Empty;
        record.PendingDepartureAtUtcMs = 0;
        record.PendingDepartureDeadlineUtcMs = 0;
        record.PendingDepartureMoment = false;
        record.LastSeenDay = serverApi.World.Calendar.TotalDays;
        record.StatusExpiresDay = IsTransientExpeditionStatus(statusLabel)
            ? record.LastSeenDay + TransientExpeditionStatusDisplayDays
            : 0d;
        if (record.MaxHealth > 0f)
        {
            record.CurrentHealth = statusLabel == "Returned injured"
                ? Math.Max(1f, record.MaxHealth * 0.5f)
                : record.CurrentHealth;
        }
        Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
        if (entity == null || !IsTamedFox(entity) || !IsOwner(entity, ownerUid))
        {
            if (!string.IsNullOrWhiteSpace(record.PendingReturnStatus))
            {
                record.Status = "Awaiting safe return";
                record.StatusExpiresDay = 0d;
                record.PendingReturnAtUtcMs = UtcNowMs() + ExpeditionReturnRetryMs;
            }
            return;
        }

        if (departureMoment && statusLabel.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
            && serverApi.World.PlayerByUid(ownerUid) is IServerPlayer departureOwner)
        {
            EmitDialogueEvent(entity, departureOwner, "expedition.depart", string.Empty, CompanionDialoguePriority.High);
        }

        bool remainsAway = string.Equals(statusLabel, "MIA", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statusLabel, "Running late", StringComparison.OrdinalIgnoreCase)
            || statusLabel.StartsWith("Away —", StringComparison.OrdinalIgnoreCase);
        if (!remainsAway
            && IsFoxAwayFromWorld(entity)
            && !TryPlaceReturningFoxSafely(entity, ownerUid))
        {
            record.PendingReturnStatus = statusLabel;
            record.PendingReturnAtUtcMs = UtcNowMs() + ExpeditionReturnRetryMs;
            record.Status = "Awaiting safe return";
            record.StatusExpiresDay = 0d;
            ITreeAttribute waitingStatus = GetDomesticationStatus(entity, true)!;
            waitingStatus.SetString(ExpeditionStatusKey, "Awaiting safe return");
            waitingStatus.SetDouble(ExpeditionStatusExpiresDayKey, 0d);
            SetFoxAwayFromWorld(entity, true);
            MarkSocialStateDirty(entity);
            return;
        }

        record.PendingReturnStatus = string.Empty;
        record.PendingReturnAtUtcMs = 0;

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        // Mortal injury is owned by EntityBehaviorMortallyWoundable and the
        // durable health fields below. Do not also persist it as an expedition
        // label: that duplicate label would survive vanilla recovery and make
        // the companion unavailable again.
        string persistedStatusLabel = string.Equals(
            statusLabel,
            "Mortally wounded",
            StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : statusLabel;
        status.SetString(ExpeditionStatusKey, persistedStatusLabel);
        status.SetDouble(
            ExpeditionStatusExpiresDayKey,
            IsTransientExpeditionStatus(persistedStatusLabel)
                ? serverApi.World.Calendar.TotalDays + TransientExpeditionStatusDisplayDays
                : 0d
        );
        MarkSocialStateDirty(entity);
        SetFoxAwayFromWorld(
            entity,
            string.Equals(statusLabel, "MIA", StringComparison.OrdinalIgnoreCase)
                || string.Equals(statusLabel, "Running late", StringComparison.OrdinalIgnoreCase)
                || statusLabel.StartsWith("Away —", StringComparison.OrdinalIgnoreCase)
        );

        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health != null && statusLabel == "Returned injured")
        {
            health.Health = Math.Max(1f, health.MaxHealth * 0.5f);
            health.MarkDirty();
        }
        else if (health != null && statusLabel == "Mortally wounded")
        {
            SetExpeditionReturnMortallyWounded(entity, status, record, health);
        }

        RegisterFoxInPack(entity);
    }

    private void SetExpeditionReturnMortallyWounded(
        Entity entity,
        ITreeAttribute status,
        FoxPackRecordV2 record,
        EntityBehaviorHealth health)
    {
        double woundStartHours = entity.World.Calendar.TotalHours;
        health.Health = Math.Min(1f, health.MaxHealth);
        health.MarkDirty();

        EntityBehaviorMortallyWoundable? mortality =
            entity.GetBehavior<EntityBehaviorMortallyWoundable>();
        if (mortality != null)
        {
            mortality.HealthState = EnumEntityHealthState.MortallyWounded;
            mortality.MortallyWoundedTotalHours = woundStartHours;
            mortality.HealthHealed = 0d;
            mortality.HealthDamaged = 0d;
        }
        else
        {
            // Supported companions receive this behavior from Feral Kinship.
            // Keep the old attribute fallback for third-party definitions, but
            // make the missing healing owner visible instead of silently
            // creating an unexplained state mismatch.
            entity.WatchedAttributes.SetInt(EntityHealthStateKey, MortallyWoundedHealthState);
            entity.WatchedAttributes.SetDouble("mortallyWoundedTotalHours", woundStartHours);
            entity.WatchedAttributes.SetDouble("healthHealed", 0d);
            entity.WatchedAttributes.SetDouble("healthDamaged", 0d);
            serverApi?.Logger.Warning(
                "[FeralKinshipCompanions] Expedition returned entity {0} mortally wounded, but it has no mortallywoundable behavior.",
                entity.Code
            );
        }

        entity.WatchedAttributes.MarkPathDirty(EntityHealthStateKey);
        entity.WatchedAttributes.MarkPathDirty("mortallyWoundedTotalHours");
        entity.WatchedAttributes.MarkPathDirty("healthHealed");
        entity.WatchedAttributes.MarkPathDirty("healthDamaged");
        status.SetInt(StabilizedWindowAppliedRankKey, 0);
        status.SetDouble(StabilizedWindowAdjustedStartHoursKey, -1d);
        status.SetDouble(DeathlessRecoveryAtHoursKey, -1d);
        status.SetDouble(DeathlessFallbackAtHoursKey, -1d);
        entity.WatchedAttributes.SetFloat("regenSpeed", 0f);
        entity.WatchedAttributes.MarkPathDirty("regenSpeed");
        entity.AnimManager?.StartAnimation("wounded-idle");
        if (entity is EntityAgent agent)
        {
            agent.Controls.StopAllMovement();
            agent.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager.StopTasks();
            agent.GetBehavior<EntityBehaviorRideable>()?.UnmountPassengers();
        }

        // Keep the durable record synchronized immediately. RegisterFoxInPack
        // below will capture the same live values and all unrelated state.
        record.HealthState = MortallyWoundedHealthState;
        record.CurrentHealth = health.Health;
        record.MortallyWoundedStartHours = woundStartHours;
        record.StabilizedWindowAppliedRank = 0;
        record.StabilizedWindowAdjustedStartHours = -1d;
        record.DeathlessRecoveryAtHours = -1d;
        record.DeathlessFallbackAtHours = -1d;
    }

    private static bool IsTransientExpeditionStatus(string status)
    {
        return string.Equals(status, "Returned healthy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Returned injured", StringComparison.OrdinalIgnoreCase);
    }

    private int DiscardRandomPackLoot(string ownerUid, int firstEligibleEntryIndex = 0)
    {
        if (serverApi == null || packRepository == null)
        {
            return 0;
        }

        List<byte[]> entries = packRepository.GetLootBytes(ownerUid).ToList();
        firstEligibleEntryIndex = Math.Clamp(firstEligibleEntryIndex, 0, entries.Count);
        if (firstEligibleEntryIndex >= entries.Count)
        {
            return 0;
        }

        int index = serverApi.World.Rand.Next(firstEligibleEntryIndex, entries.Count);
        ItemStack stack = new(entries[index]);
        if (!stack.ResolveBlockOrItem(serverApi.World) || stack.StackSize <= 0)
        {
            entries.RemoveAt(index);
            packRepository.ReplaceLoot(ownerUid, entries);
            return 0;
        }

        int removed = Math.Max(1, stack.StackSize / 2);
        stack.StackSize -= removed;
        if (stack.StackSize <= 0)
        {
            entries.RemoveAt(index);
        }
        else
        {
            entries[index] = stack.ToBytes();
        }
        packRepository.ReplaceLoot(ownerUid, entries);
        return removed;
    }

    private sealed class ExpeditionResolution
    {
        public ExpeditionResolution(string message, bool recruitmentSucceeded = false)
        {
            Message = message;
            RecruitmentSucceeded = recruitmentSucceeded;
        }

        public string Message { get; }
        public bool RecruitmentSucceeded { get; }
    }

    private sealed class ExpeditionRiskResults
    {
        public Dictionary<string, string> FoxOutcomes { get; } = new(StringComparer.Ordinal);
        public bool CargoDamaged { get; set; }
    }

    private int GenerateExpeditionLoot(
        string ownerUid,
        string type,
        float completionFraction,
        int selectedCount,
        float rewardBonus = 0f,
        ExpeditionStoryState? story = null,
        IReadOnlyList<FoxExpeditionMemberSummaryPacket>? party = null,
        CompanionSpeciesExpeditionBonus? speciesBonus = null)
    {
        if (serverApi == null || packRepository == null)
        {
            return 0;
        }

        FoxExpeditionDefinition? definition = FoxExpeditionCatalog.Get(type);
        if (definition == null || !definition.ProducesRandomLoot)
        {
            return 0;
        }

        completionFraction = Math.Clamp(completionFraction, 0.05f, 1f);
        float overcapRewardFactor = story?.OvercapRewardFactor ?? 1f;
        ExpeditionRewardModifiers modifiers = story?.RewardModifiers ?? new ExpeditionRewardModifiers();
        if (modifiers.GoalQuantityMultiplier <= 0f) modifiers.GoalQuantityMultiplier = 1f;
        float rewardFactor = completionFraction
            * Math.Max(0.1f, definition.RewardScale)
            * (1f + Math.Clamp(rewardBonus, 0f, 0.50f))
            * Math.Clamp(overcapRewardFactor, 1f, 1.30f);
        string lootProfile = story != null && !string.IsNullOrWhiteSpace(story.PrimaryProfile)
            ? story.PrimaryProfile
            : string.Equals(definition.LootProfile, FoxExpeditionType.DeepWilds, StringComparison.Ordinal)
                ? PickDeepWildsLootProfile()
                : definition.LootProfile;
        float goalSuccessCompletion = story == null ? rewardFactor : completionFraction;
        int huntBundleCount = 0;
        bool generatedHuntBundle = lootProfile == FoxExpeditionType.Hunt
            && TryGenerateHuntBundle(ownerUid, party, out huntBundleCount);
        int totalItems = generatedHuntBundle
            ? huntBundleCount
            : lootProfile == FoxExpeditionType.Hunt && party != null
                && HasAvailableLoot(SmallHuntCorpseCodes)
                ? AddRawHuntMeat(ownerUid, rewardFactor)
                : GenerateExpeditionGoal(
                ownerUid,
                lootProfile,
                rewardFactor * modifiers.GoalQuantityMultiplier
                    * (CompanionSpeciesTraits.IsForage(lootProfile) ? 1f + (speciesBonus?.PlantYield ?? 0f) : 1f),
                goalSuccessCompletion);

        string[] codes = lootProfile switch
        {
            FoxExpeditionType.Hunt => HuntLootCodes,
            FoxExpeditionType.GreatHunt => GreatHuntLootCodes,
            FoxExpeditionType.ApexHunt => ApexHuntLootCodes,
            FoxExpeditionType.Scavenge => ScavengeLootCodes,
            FoxExpeditionType.Forage => ForageLootCodes,
            FoxExpeditionType.DistantForage => DistantForageLootCodes,
            FoxExpeditionType.PrimevalReach => DistantForageLootCodes,
            _ => Array.Empty<string>()
        };

        if (lootProfile is FoxExpeditionType.Forage
            or FoxExpeditionType.DistantForage
            or FoxExpeditionType.PrimevalReach)
        {
            codes = codes
                .Concat(ArtOfGrowingForageLootCodes)
                .Concat(ArtOfGrowingBreedingLootCodes)
                .ToArray();
        }

        // A real Butchering corpse already contains the animal's eventual
        // processing path.  Do not add a second set of generic meat/hide
        // results in the same hunt when that provider is installed.
        if (generatedHuntBundle || IsHuntLootProfile(lootProfile)
            && HasAvailableLoot(lootProfile == FoxExpeditionType.Hunt
                ? SmallHuntCorpseCodes
                : LargeHuntCorpseCodes))
        {
            codes = Array.Empty<string>();
        }

        int maximumRolls = Math.Clamp(
            (int)Math.Ceiling((selectedCount + 1) * Math.Min(1.5f, definition.RewardScale)),
            2,
            10
        );
        int rolls = serverApi.World.Rand.Next(1, maximumRolls + 1)
            + Math.Clamp(modifiers.AdditionalOrdinaryRolls, 0, 3);
        for (int roll = 0; roll < rolls; roll++)
        {
            Item? item = PickAvailableLootItem(codes);
            if (item == null)
            {
                continue;
            }

            int baseQuantity = serverApi.World.Rand.Next(1, IsHuntLootProfile(lootProfile) ? 4 : 5);
            float plantFactor = item.Code?.Path is string itemPath
                && (itemPath.StartsWith("fruit-", StringComparison.Ordinal) || itemPath.StartsWith("vegetable-", StringComparison.Ordinal))
                ? 1f + (speciesBonus?.PlantYield ?? 0f) : 1f;
            int quantity = Math.Max(1, (int)Math.Round(baseQuantity * rewardFactor));
            if (plantFactor > 1f) quantity += GameMath.RoundRandom(serverApi.World.Rand, quantity * (plantFactor - 1f));
            packRepository.AddLoot(ownerUid, new ItemStack(item, quantity));
            totalItems += quantity;
        }

        if (lootProfile is FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths)
        {
            int minimumDebris = lootProfile == FoxExpeditionType.ResonantDepths ? 4 : 2;
            int maximumDebris = lootProfile == FoxExpeditionType.ResonantDepths ? 6 : 4;
            int debrisRolls = serverApi.World.Rand.Next(minimumDebris, maximumDebris + 1);
            debrisRolls = Math.Max(1, (int)Math.Round(debrisRolls * completionFraction))
                + Math.Clamp(modifiers.AdditionalDebrisRolls, 0, 3);
            for (int roll = 0; roll < debrisRolls; roll++)
            {
                totalItems += AddResolvedRandomizerLoot(ownerUid, PickWeightedCode(
                    RuinDebrisRandomizerCodes,
                    RuinDebrisRandomizerWeights
                ));
            }
        }

        totalItems += GenerateExpeditionSpecialBonus(ownerUid, lootProfile, completionFraction);
        for (int roll = 0; roll < Math.Clamp(modifiers.AdditionalSpecialRolls, 0, 2); roll++)
        {
            totalItems += GenerateExpeditionSpecialBonus(ownerUid, lootProfile, completionFraction);
        }
        for (int roll = 0; roll < Math.Clamp(modifiers.AdditionalVesselRolls, 0, 2); roll++)
        {
            totalItems += GenerateExpeditionVessel(ownerUid, lootProfile, completionFraction);
        }
        for (int roll = 0; roll < (generatedHuntBundle ? 0 : Math.Clamp(modifiers.AdditionalGoalRolls, 0, 1)); roll++)
        {
            totalItems += GenerateExpeditionGoal(ownerUid, lootProfile, rewardFactor * 0.50f, completionFraction);
        }
        if (string.Equals(definition.LootProfile, FoxExpeditionType.DeepWilds, StringComparison.Ordinal))
        {
            string secondProfile = story != null && !string.IsNullOrWhiteSpace(story.SecondaryProfile)
                ? story.SecondaryProfile
                : PickDeepWildsLootProfile(lootProfile);
            totalItems += GenerateExpeditionGoal(
                ownerUid,
                secondProfile,
                rewardFactor * 0.60f * modifiers.GoalQuantityMultiplier,
                goalSuccessCompletion);
            totalItems += GenerateExpeditionSpecialBonus(ownerUid, secondProfile, completionFraction);
            if (secondProfile == FoxExpeditionType.ResonantDepths)
            {
                int extraDebrisRolls = Math.Max(1, (int)Math.Round(
                    serverApi.World.Rand.Next(2, 4) * completionFraction
                ));
                for (int roll = 0; roll < extraDebrisRolls; roll++)
                {
                    totalItems += AddResolvedRandomizerLoot(ownerUid, PickWeightedCode(
                        RuinDebrisRandomizerCodes,
                        RuinDebrisRandomizerWeights
                    ));
                }
            }
        }


        for (int roll = 0; roll < definition.AncientFindRolls; roll++)
        {
            if (serverApi.World.Rand.NextDouble() <= definition.AncientFindChance * completionFraction)
            {
                totalItems += GenerateAncientFind(ownerUid);
            }
        }
        totalItems += AddSpeciesExpeditionLoot(ownerUid, type, speciesBonus, party, completionFraction);
        if (speciesBonus is { PlantYield: > 0f })
            speciesBonus.Notes.Add($"Woodland Grazer improved plant yields by {speciesBonus.PlantYield:P0}.");
        return totalItems;
    }

    private string PickDeepWildsLootProfile(string exclude = "")
    {
        if (serverApi == null)
        {
            return FoxExpeditionType.PrimevalReach;
        }

        string[] profiles =
        {
            FoxExpeditionType.ApexHunt,
            FoxExpeditionType.ResonantDepths,
            FoxExpeditionType.PrimevalReach
        };
        string[] choices = profiles.Where(profile => !string.Equals(profile, exclude, StringComparison.Ordinal)).ToArray();
        return choices[serverApi.World.Rand.Next(choices.Length)];
    }

    private int GenerateExpeditionGoal(
        string ownerUid,
        string type,
        float quantityFactor,
        float? successCompletion = null)
    {
        if (serverApi == null || packRepository == null)
        {
            return 0;
        }

        float successFraction = successCompletion ?? quantityFactor;
        switch (type)
        {
            case FoxExpeditionType.Hunt:
            {
                if (TryAddHuntCorpse(ownerUid, SmallHuntCorpseCodes))
                {
                    return 1;
                }

                bool redMeat = serverApi.World.Rand.NextDouble() < 0.40;
                Item? item = serverApi.World.GetItem(new AssetLocation(redMeat
                    ? "game:redmeat-raw"
                    : "game:bushmeat-raw"));
                int baseQuantity = redMeat
                    ? serverApi.World.Rand.Next(12, 25)
                    : serverApi.World.Rand.Next(18, 31);
                int quantity = Math.Max(1, (int)Math.Round(baseQuantity * quantityFactor));
                return AddExpeditionItem(ownerUid, item, quantity);
            }

            case FoxExpeditionType.GreatHunt:
            case FoxExpeditionType.ApexHunt:
            {
                if (TryAddHuntCorpse(ownerUid, LargeHuntCorpseCodes))
                {
                    return 1;
                }

                Item? item = serverApi.World.GetItem(new AssetLocation("game:redmeat-raw"));
                bool apex = type == FoxExpeditionType.ApexHunt;
                int baseQuantity = apex
                    ? serverApi.World.Rand.Next(24, 41)
                    : serverApi.World.Rand.Next(18, 31);
                int quantity = Math.Max(1, (int)Math.Round(baseQuantity * quantityFactor));
                return AddExpeditionItem(ownerUid, item, quantity);
            }

            case FoxExpeditionType.Scavenge:
            {
                if (serverApi.World.Rand.NextDouble() > successFraction)
                {
                    return 0;
                }

                Block? vessel = PickWeightedAvailableLootBlock(ScavengeVesselCodes, ScavengeVesselWeights);
                if (vessel == null)
                {
                    return 0;
                }

                packRepository.AddLoot(ownerUid, new ItemStack(vessel));
                return 1;
            }

            case FoxExpeditionType.RuinDelve:
            case FoxExpeditionType.ResonantDepths:
            {
                if (serverApi.World.Rand.NextDouble() > Math.Min(1f, successFraction))
                {
                    return 0;
                }

                Block? vessel = PickWeightedAvailableLootBlock(ScavengeVesselCodes, RuinVesselWeights);
                if (vessel == null)
                {
                    return 0;
                }

                packRepository.AddLoot(ownerUid, new ItemStack(vessel));
                return 1;
            }

            case FoxExpeditionType.Forage:
            {
                double category = serverApi.World.Rand.NextDouble();
                string[] codes;
                int minimum;
                int maximum;
                if (category < 0.50)
                {
                    codes = ForageGoalBerryCodes;
                    minimum = 24;
                    maximum = 36;
                }
                else if (category < 0.80)
                {
                    codes = ForageGoalVegetableCodes;
                    minimum = 8;
                    maximum = 12;
                }
                else
                {
                    codes = ForageGoalGrainCodes;
                    minimum = 12;
                    maximum = 24;
                }

                Item? item = PickAvailableLootItem(codes);
                int baseQuantity = serverApi.World.Rand.Next(minimum, maximum + 1);
                return AddExpeditionItem(
                    ownerUid,
                    item,
                    Math.Max(1, (int)Math.Round(baseQuantity * quantityFactor))
                );
            }

            case FoxExpeditionType.DistantForage:
            case FoxExpeditionType.PrimevalReach:
            {
                bool primeval = type == FoxExpeditionType.PrimevalReach;
                double category = serverApi.World.Rand.NextDouble();
                Item? item;
                int minimum;
                int maximum;
                if (category < 0.40)
                {
                    item = PickAvailableLootItem(ForageRareSeedCodes);
                    minimum = primeval ? 4 : 2;
                    maximum = primeval ? 8 : 4;
                }
                else if (category < 0.65)
                {
                    item = serverApi.World.GetItem(new AssetLocation("game:resin"));
                    minimum = primeval ? 6 : 3;
                    maximum = primeval ? 12 : 6;
                }
                else if (category < 0.85)
                {
                    item = serverApi.World.GetItem(new AssetLocation("game:honeycomb"));
                    minimum = primeval ? 4 : 2;
                    maximum = primeval ? 8 : 4;
                }
                else
                {
                    item = PickAvailableLootItem(ForageRareVegetableCodes);
                    minimum = primeval ? 3 : 2;
                    maximum = primeval ? 5 : 3;
                }

                int quantity = Math.Max(1, (int)Math.Round(
                    serverApi.World.Rand.Next(minimum, maximum + 1) * quantityFactor
                ));
                return AddExpeditionItem(ownerUid, item, quantity);
            }

            default:
                return 0;
        }
    }

    private bool TryAddHuntCorpse(string ownerUid, string[] codes)
    {
        if (serverApi == null || packRepository == null)
        {
            return false;
        }

        Item? corpse = PickAvailableLootItem(codes);
        if (corpse == null)
        {
            return false;
        }

        // Corpse items are max-stack-size one in Butchering.  The pack
        // repository serializes this ItemStack with the same transaction and
        // claim path as every other expedition result, so reloads cannot
        // recreate an entity or duplicate a delivered body.
        packRepository.AddLoot(ownerUid, new ItemStack(corpse));
        return true;
    }

    private bool HasAvailableLoot(string[] codes)
    {
        if (serverApi == null)
        {
            return false;
        }

        return codes.Any(code => ResolveExpeditionLootItem(code) != null);
    }

    private int GenerateExpeditionVessel(string ownerUid, string profile, float completionFraction)
    {
        if (serverApi == null || packRepository == null
            || serverApi.World.Rand.NextDouble() > Math.Clamp(completionFraction, 0f, 1f))
        {
            return 0;
        }

        bool ruin = profile is FoxExpeditionType.RuinDelve or FoxExpeditionType.ResonantDepths;
        Block? vessel = PickWeightedAvailableLootBlock(
            ScavengeVesselCodes,
            ruin ? RuinVesselWeights : ScavengeVesselWeights);
        if (vessel == null)
        {
            return 0;
        }
        packRepository.AddLoot(ownerUid, new ItemStack(vessel));
        return 1;
    }

    private int GenerateExpeditionSpecialBonus(string ownerUid, string type, float completionFraction)
    {
        if (serverApi == null || packRepository == null)
        {
            return 0;
        }

        double roll = serverApi.World.Rand.NextDouble() * 100d;
        double chanceScale = Math.Clamp(completionFraction, 0.05f, 1f);
        switch (type)
        {
            case FoxExpeditionType.Hunt:
                if (HasAvailableLoot(SmallHuntCorpseCodes))
                {
                    return 0;
                }

                if (roll < 6d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:redmeat-raw")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(10, 26) * chanceScale)));
                }
                if (roll < 9d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:fat")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(5, 11) * chanceScale)));
                }
                if (roll < 11d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:hide-raw-large")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(2, 5) * chanceScale)));
                }
                return 0;

            case FoxExpeditionType.GreatHunt:
            case FoxExpeditionType.ApexHunt:
            {
                if (HasAvailableLoot(LargeHuntCorpseCodes))
                {
                    return 0;
                }

                bool apex = type == FoxExpeditionType.ApexHunt;
                double redMeatEnd = apex ? 10d : 8d;
                double fatEnd = redMeatEnd + (apex ? 8d : 5d);
                double largeHideEnd = fatEnd + (apex ? 8d : 5d);
                double hugeHideEnd = largeHideEnd + (apex ? 8d : 3d);
                if (roll < redMeatEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:redmeat-raw")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(apex ? 18 : 12, apex ? 37 : 27) * chanceScale)));
                }
                if (roll < fatEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:fat")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(apex ? 8 : 5, apex ? 17 : 12) * chanceScale)));
                }
                if (roll < largeHideEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:hide-raw-large")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(2, apex ? 7 : 5) * chanceScale)));
                }
                if (roll < hugeHideEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:hide-raw-huge")), Math.Max(1, (int)Math.Round(serverApi.World.Rand.Next(1, apex ? 5 : 3) * chanceScale)));
                }
                return 0;
            }

            case FoxExpeditionType.Scavenge:
                if (roll < 15d * chanceScale)
                {
                    Block? vessel = PickWeightedAvailableLootBlock(ScavengeVesselCodes, ScavengeVesselWeights);
                    if (vessel == null)
                    {
                        return 0;
                    }

                    packRepository.AddLoot(ownerUid, new ItemStack(vessel));
                    return 1;
                }
                if (roll < 17d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:scrapweaponkit")), 1);
                }
                if (roll < 18.5d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativecopper")), serverApi.World.Rand.Next(5, 11));
                }
                if (roll < 19.25d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativesilver")), serverApi.World.Rand.Next(5, 11));
                }
                if (roll < 19.75d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:metalbit-meteoriciron")), serverApi.World.Rand.Next(5, 11));
                }
                if (roll < 20d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativegold")), serverApi.World.Rand.Next(5, 11));
                }
                return 0;

            case FoxExpeditionType.RuinDelve:
            case FoxExpeditionType.ResonantDepths:
            {
                bool resonant = type == FoxExpeditionType.ResonantDepths;
                double vesselEnd = resonant ? 15d : 12d;
                double scrapEnd = vesselEnd + (resonant ? 12d : 8d);
                double copperEnd = scrapEnd + (resonant ? 10d : 6d);
                double silverEnd = copperEnd + (resonant ? 6d : 3d);
                double meteoricEnd = silverEnd + (resonant ? 5d : 2d);
                double goldEnd = meteoricEnd + (resonant ? 2d : 1d);
                if (roll < vesselEnd * chanceScale)
                {
                    Block? vessel = PickWeightedAvailableLootBlock(ScavengeVesselCodes, RuinVesselWeights);
                    if (vessel != null)
                    {
                        packRepository.AddLoot(ownerUid, new ItemStack(vessel));
                        return 1;
                    }
                    return 0;
                }
                if (roll < scrapEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:scrapweaponkit")), 1);
                }
                if (roll < copperEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativecopper")), serverApi.World.Rand.Next(6, resonant ? 17 : 13));
                }
                if (roll < silverEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativesilver")), serverApi.World.Rand.Next(5, resonant ? 13 : 10));
                }
                if (roll < meteoricEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:metalbit-meteoriciron")), serverApi.World.Rand.Next(5, resonant ? 13 : 10));
                }
                if (roll < goldEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:nugget-nativegold")), serverApi.World.Rand.Next(5, resonant ? 13 : 10));
                }
                return 0;
            }

            case FoxExpeditionType.Forage:
                if (roll < 4d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:honeycomb")), serverApi.World.Rand.Next(1, 3));
                }
                if (roll < 7d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:resin")), serverApi.World.Rand.Next(1, 4));
                }
                if (roll < 10d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, PickAvailableLootItem(ForageRareSeedCodes), serverApi.World.Rand.Next(1, 3));
                }
                if (roll < 11.75d * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, PickAvailableLootItem(ForageRareVegetableCodes), serverApi.World.Rand.Next(1, 3));
                }
                if (roll < 12d * chanceScale)
                {
                    Block? cutting = PickAvailableLootBlock(ForageBerryCuttingCodes);
                    if (cutting == null)
                    {
                        return 0;
                    }

                    packRepository.AddLoot(ownerUid, new ItemStack(cutting));
                    return 1;
                }
                return 0;

            case FoxExpeditionType.DistantForage:
            case FoxExpeditionType.PrimevalReach:
            {
                bool primeval = type == FoxExpeditionType.PrimevalReach;
                double honeyEnd = primeval ? 8d : 6d;
                double resinEnd = honeyEnd + (primeval ? 8d : 5d);
                double seedEnd = resinEnd + (primeval ? 8d : 5d);
                double vegetableEnd = seedEnd + (primeval ? 6d : 3d);
                double cuttingEnd = vegetableEnd + (primeval ? 2d : 1d);
                if (roll < honeyEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:honeycomb")), serverApi.World.Rand.Next(2, primeval ? 7 : 5));
                }
                if (roll < resinEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:resin")), serverApi.World.Rand.Next(3, primeval ? 10 : 7));
                }
                if (roll < seedEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, PickAvailableLootItem(ForageRareSeedCodes), serverApi.World.Rand.Next(2, primeval ? 6 : 4));
                }
                if (roll < vegetableEnd * chanceScale)
                {
                    return AddExpeditionItem(ownerUid, PickAvailableLootItem(ForageRareVegetableCodes), serverApi.World.Rand.Next(2, primeval ? 5 : 4));
                }
                if (roll < cuttingEnd * chanceScale)
                {
                    Block? cutting = PickAvailableLootBlock(ForageBerryCuttingCodes);
                    if (cutting != null)
                    {
                        packRepository.AddLoot(ownerUid, new ItemStack(cutting, primeval ? serverApi.World.Rand.Next(1, 3) : 1));
                        return 1;
                    }
                }
                return 0;
            }

            default:
                return 0;
        }
    }

    private static bool IsHuntLootProfile(string type)
    {
        return type is FoxExpeditionType.Hunt or FoxExpeditionType.GreatHunt or FoxExpeditionType.ApexHunt;
    }

    private int GenerateAncientFind(string ownerUid)
    {
        if (serverApi == null)
        {
            return 0;
        }

        double category = serverApi.World.Rand.NextDouble() * 100d;
        if (category < 30d)
        {
            return AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-jonasparts");
        }
        if (category < 50d)
        {
            return AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-jonasframes");
        }
        if (category < 70d)
        {
            return AddResolvedRandomizerLoot(ownerUid, AncientLoreRandomizerCodes[
                serverApi.World.Rand.Next(AncientLoreRandomizerCodes.Length)
            ]);
        }
        if (category < 85d)
        {
            return AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-tuningcylinder");
        }
        if (category < 95d)
        {
            return AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-painting");
        }
        if (category < 99d)
        {
            return AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-lantern");
        }

        return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation("game:gear-temporal")), 1);
    }

    private int AddResolvedRandomizerLoot(string ownerUid, string? randomizerCode)
    {
        if (packRepository == null || string.IsNullOrWhiteSpace(randomizerCode))
        {
            return 0;
        }

        ItemStack? resolved = ResolveVanillaRandomizer(randomizerCode);
        if (resolved == null || resolved.StackSize <= 0)
        {
            return 0;
        }

        packRepository.AddLoot(ownerUid, resolved);
        return resolved.StackSize;
    }

    private ItemStack? ResolveVanillaRandomizer(string randomizerCode)
    {
        if (serverApi == null)
        {
            return null;
        }

        Item? randomizer = serverApi.World.GetItem(new AssetLocation(randomizerCode));
        if (randomizer == null)
        {
            serverApi.Logger.Warning("[FeralKinshipCompanions] Could not find vanilla loot randomizer {0}.", randomizerCode);
            return null;
        }

        DummySlot slot = new(new ItemStack(randomizer));
        for (int depth = 0; depth < 4 && slot.Itemstack?.Collectible is IResolvableCollectible resolver; depth++)
        {
            resolver.Resolve(slot, serverApi.World, true);
        }
        return slot.Itemstack?.Clone();
    }

    private string? PickWeightedCode(string[] codes, int[] weights)
    {
        if (serverApi == null || codes.Length == 0 || codes.Length != weights.Length)
        {
            return null;
        }

        int totalWeight = weights.Sum(weight => Math.Max(0, weight));
        int selected = serverApi.World.Rand.Next(totalWeight);
        for (int index = 0; index < codes.Length; index++)
        {
            selected -= Math.Max(0, weights[index]);
            if (selected < 0)
            {
                return codes[index];
            }
        }
        return codes[^1];
    }

    private int AddExpeditionItem(string ownerUid, Item? item, int quantity)
    {
        if (packRepository == null || item == null || quantity <= 0)
        {
            return 0;
        }

        packRepository.AddLoot(ownerUid, new ItemStack(item, quantity));
        return quantity;
    }

    private Item? ResolveExpeditionLootItem(string code)
    {
        if (serverApi == null) return null;
        AssetLocation location = new(code);
        // Saved item mappings can retain removed mods as non-null placeholders.
        // A registered code alone does not mean its reward provider is available.
        if (location.Domain == "butchering" && !serverApi.ModLoader.IsModEnabled("butchering"))
            return null;
        Item? item = serverApi.World.GetItem(location);
        return item is { IsMissing: false } ? item : null;
    }

    private Item? PickAvailableLootItem(string[] codes)
    {
        if (serverApi == null || codes.Length == 0)
        {
            return null;
        }

        int start = serverApi.World.Rand.Next(codes.Length);
        for (int offset = 0; offset < codes.Length; offset++)
        {
            Item? item = ResolveExpeditionLootItem(codes[(start + offset) % codes.Length]);
            if (item != null)
            {
                return item;
            }
        }

        return null;
    }

    private Block? PickAvailableLootBlock(string[] codes)
    {
        if (serverApi == null || codes.Length == 0)
        {
            return null;
        }

        int start = serverApi.World.Rand.Next(codes.Length);
        for (int offset = 0; offset < codes.Length; offset++)
        {
            Block? block = serverApi.World.GetBlock(new AssetLocation(codes[(start + offset) % codes.Length]));
            if (block != null)
            {
                return block;
            }
        }

        return null;
    }

    private Block? PickWeightedAvailableLootBlock(string[] codes, int[] weights)
    {
        if (serverApi == null || codes.Length == 0 || codes.Length != weights.Length)
        {
            return null;
        }

        int totalWeight = weights.Sum(weight => Math.Max(0, weight));
        if (totalWeight <= 0)
        {
            return null;
        }

        int selected = serverApi.World.Rand.Next(totalWeight);
        for (int index = 0; index < codes.Length; index++)
        {
            selected -= Math.Max(0, weights[index]);
            if (selected < 0)
            {
                return serverApi.World.GetBlock(new AssetLocation(codes[index]));
            }
        }

        return null;
    }

    private static bool IsRandomLootExpedition(string type)
    {
        return FoxExpeditionCatalog.Get(type)?.ProducesRandomLoot == true;
    }

    private static string GetExpeditionDisplayName(string type)
    {
        return FoxExpeditionCatalog.Get(type)?.Name ?? "Expedition";
    }

    private bool TryChooseRecruitmentReward(
        FoxExpeditionRecord expedition,
        out CompanionSpeciesProfile selectedSpecies,
        out CompanionRecruitmentVariant selectedVariant)
    {
        selectedSpecies = null!;
        selectedVariant = null!;
        if (serverApi == null)
        {
            return false;
        }

        Dictionary<string, int> representedSpecies = new(StringComparer.OrdinalIgnoreCase);
        foreach (FoxExpeditionMemberSummaryPacket member in expedition.MemberSnapshots ?? new())
        {
            string speciesId = string.IsNullOrWhiteSpace(member.SpeciesId) ? "fox" : member.SpeciesId;
            representedSpecies[speciesId] = representedSpecies.GetValueOrDefault(speciesId) + 1;
        }

        if (representedSpecies.Count == 0 && packRepository != null)
        {
            foreach (string foxId in expedition.SelectedFoxIds)
            {
                if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record) || record == null)
                {
                    continue;
                }
                string speciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId;
                representedSpecies[speciesId] = representedSpecies.GetValueOrDefault(speciesId) + 1;
            }
        }

        List<(CompanionSpeciesProfile Profile, List<CompanionRecruitmentVariant> Variants, float Weight)> pool = new();
        foreach ((string speciesId, int count) in representedSpecies)
        {
            if (!CompanionSpeciesCatalog.TryGetById(speciesId, out CompanionSpeciesProfile profile)
                || !profile.HasCapability(CompanionCapabilities.Recruitment))
            {
                serverApi.Logger.Warning(
                    "[FeralKinshipCompanions] Recruitment ignored unsupported party species '{0}'.",
                    speciesId
                );
                continue;
            }

            List<CompanionRecruitmentVariant> validVariants = profile.RecruitmentVariants
                .Where(variant => CompanionRecruitmentResolver.Resolve(profile, variant, serverApi.World.GetEntityType) != null)
                .ToList();
            if (validVariants.Count == 0)
            {
                serverApi.Logger.Warning(
                    "[FeralKinshipCompanions] Recruitment profile '{0}' has no available tame entity variants.",
                    profile.Id
                );
                continue;
            }

            pool.Add((profile, validVariants, Math.Max(0.01f, profile.RecruitmentWeight) * count));
        }

        float totalSpeciesWeight = pool.Sum(entry => entry.Weight);
        if (totalSpeciesWeight <= 0f)
        {
            return false;
        }
        double speciesRoll = serverApi.World.Rand.NextDouble() * totalSpeciesWeight;
        (CompanionSpeciesProfile Profile, List<CompanionRecruitmentVariant> Variants, float Weight) chosen = pool[^1];
        foreach (var entry in pool)
        {
            speciesRoll -= entry.Weight;
            if (speciesRoll <= 0d)
            {
                chosen = entry;
                break;
            }
        }

        float totalVariantWeight = chosen.Variants.Sum(variant => Math.Max(0.01f, variant.Weight));
        double variantRoll = serverApi.World.Rand.NextDouble() * totalVariantWeight;
        CompanionRecruitmentVariant variantChoice = chosen.Variants[^1];
        foreach (CompanionRecruitmentVariant variant in chosen.Variants)
        {
            variantRoll -= Math.Max(0.01f, variant.Weight);
            if (variantRoll <= 0d)
            {
                variantChoice = variant;
                break;
            }
        }

        selectedSpecies = chosen.Profile;
        selectedVariant = variantChoice;
        return true;
    }

    private static string GetRecruitmentDisplayName(
        CompanionSpeciesProfile species,
        CompanionRecruitmentVariant variant)
    {
        return string.Equals(species.Id, "chicken", StringComparison.Ordinal)
            ? variant.DisplayName
            : $"{variant.DisplayName} {species.DisplayName}";
    }

    private Entity? SpawnRecruitmentCompanion(IServerPlayer owner, FoxRecruitmentRewardRecord reward, out string failure)
    {
        failure = "The recruited animal's tame entity type is unavailable. Check the server log and required animal mods.";
        if (serverApi == null)
        {
            return null;
        }

        if (!CompanionSpeciesCatalog.TryGetRecruitmentVariant(
                reward.WildEntityCode,
                out CompanionSpeciesProfile profile,
                out CompanionRecruitmentVariant variant)
            || !string.Equals(profile.Id, reward.SpeciesId, StringComparison.OrdinalIgnoreCase))
        {
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Recruitment reward has invalid species/code pairing: {0} / {1}.",
                reward.SpeciesId,
                reward.WildEntityCode
            );
            return null;
        }

        EntityProperties? type = CompanionRecruitmentResolver.Resolve(profile, variant, serverApi.World.GetEntityType);
        if (type == null)
        {
            serverApi.Logger.Warning("[FeralKinshipCompanions] Recruitment could not resolve a matching tame companion entity type for {0}.", variant.EntityCode);
            return null;
        }

        Entity? entity = serverApi.World.ClassRegistry.CreateEntity(type);
        if (entity == null)
        {
            return null;
        }

        Vec3d? spawnPosition = FindSafeRecruitmentPosition(owner, type);
        if (spawnPosition == null)
        {
            failure = "No safe open ground was found nearby.";
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Recruitment claim for {0} found no safe open ground near the player.",
                owner.PlayerUID
            );
            return null;
        }

        entity.Pos.X = spawnPosition.X;
        entity.Pos.Y = spawnPosition.Y;
        entity.Pos.Z = spawnPosition.Z;
        entity.Pos.Yaw = owner.Entity.Pos.Yaw;
        entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        status.SetString("owner", owner.PlayerUID);
        status.SetString("domesticationLevel", "DOMESTICATED");
        status.SetFloat("obedience", 1f);
        status.SetInt("generation", 0);
        status.SetString(ActivityModeKey, CompanionActivityMode.AtEase);
        status.SetString(CombatStyleKey, CompanionCombatStyle.Passive);
        status.SetString(RiskToleranceKey, CompanionRiskTolerance.Steady);
        status.SetBool(RecruitmentArrivalPendingKey, true);
        serverApi.World.SpawnEntity(entity);
        RegisterLoadedFox(entity);
        RegisterFoxInPack(entity);
        packRepository?.Save();
        return entity;
    }

    private Vec3d? FindSafeRecruitmentPosition(IServerPlayer owner, EntityProperties type)
    {
        if (serverApi == null || owner.Entity == null)
        {
            return null;
        }

        return FindSafeEntityPosition(owner.Entity.Pos.AsBlockPos, type, null, 4, 2);
    }

    private Vec3d? FindSafeEntityPosition(
        BlockPos origin,
        EntityProperties type,
        Entity? excludedEntity,
        int maximumRadius,
        int verticalRange,
        IReadOnlyList<Vec3d>? reservedPositions = null)
    {
        if (serverApi == null)
        {
            return null;
        }

        for (int radius = 1; radius <= maximumRadius; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != radius)
                    {
                        continue;
                    }

                    for (int dy = verticalRange; dy >= -verticalRange; dy--)
                    {
                        int x = origin.X + dx;
                        int y = origin.Y + dy;
                        int z = origin.Z + dz;
                        Vec3d candidate = new Vec3d(
                            x + 0.5,
                            y + origin.dimension * BlockPos.DimensionBoundary,
                            z + 0.5
                        );
                        BlockPos feet = new BlockPos(x, y, z, origin.dimension);
                        if (reservedPositions != null
                            && reservedPositions.Any(reserved =>
                                Math.Abs(reserved.X - candidate.X) < 0.9d
                                && Math.Abs(reserved.Y - candidate.Y) < 1.5d
                                && Math.Abs(reserved.Z - candidate.Z) < 0.9d))
                        {
                            continue;
                        }

                        if (!IsSafeEntityPosition(feet, candidate, type, excludedEntity))
                        {
                            continue;
                        }

                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private bool IsSafeEntityPosition(
        BlockPos feet,
        Vec3d candidate,
        EntityProperties type,
        Entity? excludedEntity)
    {
        if (serverApi == null)
        {
            return false;
        }

        IBlockAccessor blocks = serverApi.World.BlockAccessor;
        BlockPos below = feet.DownCopy();
        return blocks.GetBlock(below).SideSolid[BlockFacing.UP.Index]
            && !serverApi.World.CollisionTester.IsColliding(
                blocks,
                type.SpawnCollisionBox,
                candidate,
                false
            )
            && serverApi.World.GetEntitiesAround(
                candidate,
                0.9f,
                1.5f,
                nearby => nearby != excludedEntity
                    && nearby.Alive
                    && !IsFoxAwayFromWorld(nearby)
            ).Length == 0;
    }

    private bool TryPlaceReturningFoxSafely(Entity fox, string ownerUid)
    {
        if (serverApi == null)
        {
            return false;
        }

        BlockPos? cairn = GetActiveCairnPosition(ownerUid);
        BlockPos origin;
        Vec3d? preferredPosition = null;
        if (cairn != null)
        {
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(cairn) == null)
            {
                RequestCairnChunkLoad(cairn);
                return false;
            }
            origin = cairn;
            Block markerBlock = serverApi.World.BlockAccessor.GetBlock(cairn);
            BlockPos portal = GetPackMarkerPortalBlock(cairn, markerBlock);
            Vec3d portalPosition = new(
                portal.X + 0.5,
                portal.Y + portal.dimension * BlockPos.DimensionBoundary,
                portal.Z + 0.5
            );
            if (IsSafeEntityPosition(portal, portalPosition, fox.Properties, fox))
            {
                preferredPosition = portalPosition;
            }
        }
        else if (serverApi.World.PlayerByUid(ownerUid)?.Entity is EntityPlayer ownerEntity)
        {
            origin = ownerEntity.Pos.AsBlockPos;
        }
        else
        {
            return false;
        }

        Vec3d? safePosition = preferredPosition
            ?? FindSafeEntityPosition(origin, fox.Properties, fox, 8, 4);
        if (safePosition == null)
        {
            return false;
        }

        fox.TeleportTo(safePosition);
        fox.PositionBeforeFalling.Set(safePosition.X, safePosition.Y, safePosition.Z);
        return true;
    }

    private BlockPos? GetActiveCairnPosition(string ownerUid, bool discoverNearby = false)
    {
        if (serverApi == null || packRepository == null)
        {
            return null;
        }

        bool removedInvalidRecord = false;
        foreach (FoxPackCairnRecord cairn in packRepository.GetCairnsForOwner(ownerUid))
        {
            BlockPos pos = new(cairn.X, cairn.Y, cairn.Z, cairn.Dimension);
            if (serverApi.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                return pos;
            }

            Block block = serverApi.World.BlockAccessor.GetBlock(pos);
            if (block.Code != null && IsPackMarkerCode(block.Code))
            {
                PackCartOwnershipResolution ownership = ResolvePackCartOwnership(pos, applyRepairs: true);
                if (string.Equals(ownership.OwnerUid, ownerUid, StringComparison.Ordinal))
                {
                    return pos;
                }

                // A loaded cart's explicit tag disagreed with this owner's
                // positional index, so do not return it for this owner.
                continue;
            }

            removedInvalidRecord |= packRepository.RemoveCairn(pos);
        }

        if (removedInvalidRecord)
        {
            packRepository.Save();
        }

        if (discoverNearby)
        {
            BlockPos? discovered = DiscoverNearbyPackCart(ownerUid);
            if (discovered != null) return discovered;
        }
        return null;
    }

    private BlockPos? DiscoverNearbyPackCart(string ownerUid)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || serverApi.World.PlayerByUid(ownerUid) is not IServerPlayer ownerPlayer
            || ownerPlayer.ConnectionState != EnumClientState.Playing
            || ownerPlayer.Entity == null)
        {
            return null;
        }

        BlockPos center = ownerPlayer.Entity.Pos.AsBlockPos;
        const int radius = 48;
        const int verticalRadius = 32;
        int minY = Math.Max(0, center.Y - verticalRadius);
        int maxY = Math.Min(serverApi.World.BlockAccessor.MapSizeY - 1, center.Y + verticalRadius);
        BlockPos min = new(center.X - radius, minY, center.Z - radius, center.dimension);
        BlockPos max = new(center.X + radius, maxY, center.Z + radius, center.dimension);
        BlockPos? nearest = null;
        double nearestDistance = double.MaxValue;

        serverApi.World.BlockAccessor.SearchBlocks(min, max, (block, pos) =>
        {
            if (block.Code == null || !IsPackMarkerCode(block.Code)) return true;

            BlockPos candidate = new(pos.X, pos.Y, pos.Z, center.dimension);
            PackCartOwnershipResolution ownership = ResolvePackCartOwnership(
                candidate,
                applyRepairs: false);
            if (!string.Equals(ownership.OwnerUid, ownerUid, StringComparison.Ordinal))
            {
                return true;
            }

            double dx = pos.X - center.X;
            double dy = pos.Y - center.Y;
            double dz = pos.Z - center.Z;
            double distance = dx * dx + dy * dy + dz * dz;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = candidate;
            }
            return true;
        });

        if (nearest == null) return null;
        PackCartOwnershipResolution recovered = ResolvePackCartOwnership(nearest, applyRepairs: true);
        return string.Equals(recovered.OwnerUid, ownerUid, StringComparison.Ordinal)
            ? nearest
            : null;
    }

    internal bool TryGetPackCartIdleTarget(Entity fox, out Vec3d? target)
    {
        target = null;
        if (serverApi == null || packRepository?.Loaded != true) return false;

        string ownerUid = GetCompanionOwnerUid(fox);
        if (string.IsNullOrWhiteSpace(ownerUid)) return false;

        BlockPos? cart = GetActiveCairnPosition(ownerUid, discoverNearby: true);
        if (cart == null
            || cart.dimension != fox.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) == null)
        {
            return false;
        }

        Block block = serverApi.World.BlockAccessor.GetBlock(cart);
        if (block.Code?.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal) != true
            && block.Code?.Path.StartsWith(WorkCartBlockPathPrefix, StringComparison.Ordinal) != true)
        {
            return false;
        }

        target = new Vec3d(
            cart.X + 0.5,
            cart.Y + 1.22 + cart.dimension * BlockPos.DimensionBoundary,
            cart.Z + 0.5
        );
        return true;
    }

    internal bool TryGetWorkCartIdleTarget(Entity fox, out Vec3d? target)
    {
        target = null;
        if (serverApi == null || packRepository?.Loaded != true || !IsTamedFox(fox))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !record.HasHome
            || !string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        BlockPos cart = new(record.HomeX, record.HomeY, record.HomeZ, record.HomeDimension);
        if (cart.dimension != fox.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) == null)
        {
            return false;
        }

        Block block = serverApi.World.BlockAccessor.GetBlock(cart);
        if (!IsWorkCartCode(block.Code))
        {
            return false;
        }

        target = new Vec3d(
            cart.X + 0.5,
            cart.Y + 1.22 + cart.dimension * BlockPos.DimensionBoundary,
            cart.Z + 0.5
        );
        return true;
    }

    internal bool IsFoxAssignedToWorkCart(Entity fox)
    {
        if (packRepository?.Loaded != true)
        {
            return false;
        }

        string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            && record?.HasHome == true
            && string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase);
    }

    internal float GetCompanionTerritoryRadius(Entity fox)
    {
        return IsFoxAssignedToWorkCart(fox) ? GetWorkCartRadius(fox) : GetCompanionCampRadius(fox);
    }

    private bool TryReservePackCartSit(Entity fox, Vec3d target, out string reservationKey)
    {
        string ownerUid = GetCompanionOwnerUid(fox);
        reservationKey = $"{ownerUid}:{fox.Pos.Dimension}:{Math.Floor(target.X)}:{Math.Floor(target.Z)}";
        if (packCartSitReservations.TryGetValue(reservationKey, out long holderId)
            && holderId != fox.EntityId)
        {
            Entity? holder = serverApi?.World.GetEntityById(holderId);
            if (holder?.Alive == true && !IsFoxAwayFromWorld(holder))
            {
                return false;
            }
        }

        packCartSitReservations[reservationKey] = fox.EntityId;
        return true;
    }

    private bool HasPendingExpeditionDeparture(Entity entity)
    {
        if (packRepository?.Loaded != true || !IsTamedFox(entity))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            && record != null
            && record.EntityId == entity.EntityId
            && !string.IsNullOrWhiteSpace(record.PendingDepartureStatus);
    }

    internal bool TryGetPendingExpeditionDepartureTarget(Entity entity, out Vec3d? target)
    {
        target = null;
        if (serverApi == null
            || packRepository?.Loaded != true
            || !IsTamedFox(entity)
            || IsFoxAwayFromWorld(entity)
            || IsFoxIncapacitated(entity)
            || IsCompanionFoodRestricted(entity))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || record.EntityId != entity.EntityId
            || string.IsNullOrWhiteSpace(record.PendingDepartureStatus)
            || record.PendingDepartureAtUtcMs > UtcNowMs())
        {
            return false;
        }

        BlockPos? marker = GetActiveCairnPosition(record.OwnerUid);
        if (marker == null
            || marker.dimension != entity.Pos.Dimension
            || serverApi.World.BlockAccessor.GetChunkAtBlockPos(marker) == null)
        {
            return false;
        }

        Block block = serverApi.World.BlockAccessor.GetBlock(marker);
        BlockPos portal = GetPackMarkerPortalBlock(marker, block);
        target = new Vec3d(
            portal.X + 0.5,
            portal.Y + portal.dimension * BlockPos.DimensionBoundary,
            portal.Z + 0.5
        );
        return true;
    }

    internal bool CompletePendingExpeditionDeparture(Entity entity)
    {
        if (serverApi == null || packRepository?.Loaded != true || !IsTamedFox(entity))
        {
            return false;
        }

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(foxId)
            || !packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || record.EntityId != entity.EntityId
            || string.IsNullOrWhiteSpace(record.PendingDepartureStatus))
        {
            return false;
        }

        string pendingStatus = record.PendingDepartureStatus;
        SetExpeditionMemberStatus(record.OwnerUid, record.FoxId, pendingStatus);
        packRepository.Save();
        return true;
    }

    internal bool ShouldPlayExpeditionDepartureMoment(Entity entity)
    {
        if (packRepository?.Loaded != true || !IsTamedFox(entity)) return false;

        string foxId = GetDomesticationStatus(entity)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(foxId)
            && packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            && record != null
            && record.EntityId == entity.EntityId
            && record.PendingDepartureMoment;
    }

    private static BlockPos GetPackMarkerPortalBlock(BlockPos marker, Block block)
    {
        if (block.Code?.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal) != true)
        {
            return marker.Copy();
        }

        string side = block.Variant.TryGetValue("side", out string? value) ? value : "north";
        return side switch
        {
            "east" => marker.AddCopy(0, 0, -1),
            "south" => marker.AddCopy(1, 0, 0),
            "west" => marker.AddCopy(0, 0, 1),
            _ => marker.AddCopy(-1, 0, 0)
        };
    }

    private void RequestCairnChunkLoad(BlockPos pos)
    {
        if (serverApi == null)
        {
            return;
        }

        int chunkX = (int)Math.Floor((double)pos.X / GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor((double)pos.Z / GlobalConstants.ChunkSize);
        if (pos.dimension == 0)
        {
            serverApi.WorldManager.LoadChunkColumnPriority(
                chunkX - 1,
                chunkZ - 1,
                chunkX + 1,
                chunkZ + 1
            );
        }
        else
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    serverApi.WorldManager.LoadChunkColumnForDimension(
                        chunkX + dx,
                        chunkZ + dz,
                        pos.dimension
                    );
                }
            }
        }
    }

    private void ClaimPackLoot(IServerPlayer owner, IEnumerable<string>? selectedStacks = null)
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        HashSet<string>? requested = selectedStacks == null ? null
            : selectedStacks.Where(key => !string.IsNullOrWhiteSpace(key)).ToHashSet(StringComparer.Ordinal);
        if (requested is { Count: 0 })
        {
            SendPackState(owner, "Select one or more cached stacks to claim.");
            return;
        }
        List<byte[]> remaining = new();
        int claimedCount = 0;
        int droppedCount = 0;
        int stackIndex = 0;
        foreach (byte[] bytes in packRepository.GetLootBytes(ownerUid))
        {
            if (requested != null && !requested.Contains(PackLootSelectionKey(stackIndex, bytes)))
            {
                remaining.Add(bytes);
                stackIndex++;
                continue;
            }
            stackIndex++;
            ItemStack stack = new(bytes);
            if (!stack.ResolveBlockOrItem(serverApi.World) || stack.Collectible == null)
            {
                remaining.Add(bytes);
                continue;
            }

            int before = stack.StackSize;
            owner.InventoryManager.TryGiveItemstack(stack, false);
            claimedCount += Math.Max(0, before - stack.StackSize);
            if (stack.StackSize > 0)
            {
                droppedCount += stack.StackSize;
                serverApi.World.SpawnItemEntity(stack, owner.Entity.Pos.XYZ.Add(0, 0.5, 0));
            }
        }

        packRepository.ReplaceLoot(ownerUid, remaining);
        packRepository.Save();
        string message = claimedCount > 0 || droppedCount > 0
            ? $"Claimed {claimedCount} pack item{(claimedCount == 1 ? string.Empty : "s")}."
            : "No pack items could be claimed.";
        if (droppedCount > 0)
        {
            message += $" {droppedCount} item{(droppedCount == 1 ? " was" : "s were")} placed at your feet because your inventory was full.";
        }
        SendPackState(owner, message);
    }

    private static string PackLootSelectionKey(int index, byte[] bytes) =>
        index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
            + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private void SendPackLootToStorage(IServerPlayer owner)
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid)) return;

        if (packRepository.IsCargoUnloadingActive(ownerUid))
        {
            packRepository.SetCargoUnloadingActive(ownerUid, false);
            int released = ClearPendingCartPickupJobs(ownerUid);
            packRepository.Save();
            SendPackState(owner, $"Cart unloading stopped. {released} companion{(released == 1 ? " is" : "s are")} no longer waiting for a load. Carriers already holding cargo will finish safely.");
            return;
        }
        if (!packRepository.HasLoot(ownerUid))
        {
            SendPackState(owner, "The expedition cache is empty.");
            return;
        }

        BlockPos? cart = GetActiveCairnPosition(ownerUid);
        if (cart == null)
        {
            SendPackState(owner, "Place a Pack Cart before asking the companions to unload expedition cargo.");
            return;
        }
        Vec3d cartCenter = new(
            cart.X + 0.5,
            cart.Y + cart.dimension * BlockPos.DimensionBoundary + 0.25,
            cart.Z + 0.5
        );
        if (!TryGetPackStorage(ownerUid, cart.dimension, cartCenter, out BlockPos? storage) || storage == null)
        {
            SendPackState(owner, "Place a Pack Collection Box in the same dimension before unloading cargo with companions.");
            return;
        }
        if (!CanPackStorageAcceptAnyCachedLoot(ownerUid, storage))
        {
            SendPackState(owner, "The Pack Collection Box has no room for the expedition cargo.");
            return;
        }

        packRepository.SetCargoUnloadingActive(ownerUid, true);
        int assignedFoxes = AssignPackCartCourierJobs(ownerUid, cart, cartCenter);
        bool hasExistingCourier = loadedFoxes.Values.Any(fox => IsOwner(fox, ownerUid)
            && HasFoxCommandedStorageJob(fox));
        if (assignedFoxes == 0 && !hasExistingCourier)
        {
            packRepository.SetCargoUnloadingActive(ownerUid, false);
            SendPackState(owner, "No available companion is within 24 blocks of the Pack Cart to begin unloading.");
            return;
        }

        packRepository.Save();
        PlayCartSound(ownerUid, "game:sounds/effect/woodswitch", 0.34f, 18f);
        string message = $"Cart unloading started with {assignedFoxes} available courier{(assignedFoxes == 1 ? string.Empty : "s")}. ";
        message += "Companions will keep returning for saved loads until the cache is empty. Use Stop unloading to end the order.";
        SendPackState(owner, message);
    }

    private void MaintainPackCargoUnloading()
    {
        if (serverApi == null || packRepository?.Loaded != true) return;
        bool saveNeeded = false;
        foreach (string ownerUid in packRepository.GetCargoUnloadingOwners().ToArray())
        {
            if (!packRepository.HasLoot(ownerUid))
            {
                packRepository.SetCargoUnloadingActive(ownerUid, false);
                ClearPendingCartPickupJobs(ownerUid);
                saveNeeded = true;
                if (serverApi.World.PlayerByUid(ownerUid) is IServerPlayer finishedOwner)
                {
                    SendOwnerSound(finishedOwner, CompanionSoundCue.CargoComplete);
                    finishedOwner.SendMessage(GlobalConstants.GeneralChatGroup,
                        "The companions have emptied the Pack Cart's expedition cache.", EnumChatType.Notification);
                    if (packViewers.Contains(ownerUid)) SendPackState(finishedOwner, "Continuous cart unloading finished: the expedition cache is empty.");
                }
                continue;
            }

            BlockPos? cart = GetActiveCairnPosition(ownerUid);
            if (cart == null || serverApi.World.BlockAccessor.GetChunkAtBlockPos(cart) == null) continue;
            Vec3d cartCenter = new(cart.X + 0.5,
                cart.Y + cart.dimension * BlockPos.DimensionBoundary + 0.25, cart.Z + 0.5);
            if (!TryGetPackStorage(ownerUid, cart.dimension, cartCenter, out BlockPos? storage) || storage == null) continue;
            if (!CanPackStorageAcceptAnyCachedLoot(ownerUid, storage))
            {
                packRepository.SetCargoUnloadingActive(ownerUid, false);
                ClearPendingCartPickupJobs(ownerUid);
                saveNeeded = true;
                if (serverApi.World.PlayerByUid(ownerUid) is IServerPlayer fullOwner)
                {
                    fullOwner.SendMessage(GlobalConstants.GeneralChatGroup,
                        "Continuous cart unloading stopped because the Pack Collection Box has no room.", EnumChatType.Notification);
                    if (packViewers.Contains(ownerUid)) SendPackState(fullOwner, "Unloading stopped: make room in the Pack Collection Box, then start it again.");
                }
                continue;
            }

            int assigned = AssignPackCartCourierJobs(ownerUid, cart, cartCenter);
            if (assigned > 0 && packViewers.Contains(ownerUid)
                && serverApi.World.PlayerByUid(ownerUid) is IServerPlayer viewingOwner)
            {
                SendPackState(viewingOwner);
            }
        }
        if (saveNeeded) packRepository.Save();
    }

    private int AssignPackCartCourierJobs(string ownerUid, BlockPos cart, Vec3d cartCenter)
    {
        if (serverApi == null || packRepository?.Loaded != true) return 0;
        int availableItems = 0;
        foreach (byte[] bytes in packRepository.GetLootBytes(ownerUid))
        {
            ItemStack stack = new(bytes);
            if (stack.ResolveBlockOrItem(serverApi.World) && stack.Collectible != null)
            {
                availableItems += Math.Max(0, stack.StackSize);
            }
        }
        if (availableItems <= 0) return 0;

        List<Entity> carriers = loadedFoxes.Values
            .Where(fox => fox.Alive && IsTamedFox(fox) && IsOwner(fox, ownerUid)
                && !IsFoxAwayFromWorld(fox) && !IsFoxIncapacitated(fox)
                && fox.Pos.Dimension == cart.dimension
                && fox.Pos.SquareDistanceTo(cartCenter) <= 24 * 24
                && !HasFoxStorageCargo(fox) && !HasFoxPendingCartPickup(fox))
            .OrderBy(fox => fox.Pos.SquareDistanceTo(cartCenter))
            .Take(availableItems)
            .ToList();
        foreach (Entity carrier in carriers)
        {
            carrier.WatchedAttributes.SetBool(FoxStorageCartPickupKey, true);
            carrier.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
        }
        return carriers.Count;
    }

    private int ClearPendingCartPickupJobs(string ownerUid)
    {
        int cleared = 0;
        foreach (Entity fox in loadedFoxes.Values.Where(fox => IsOwner(fox, ownerUid)
                     && fox.WatchedAttributes.GetBool(FoxStorageCartPickupKey, false)))
        {
            fox.WatchedAttributes.SetBool(FoxStorageCartPickupKey, false);
            fox.WatchedAttributes.MarkPathDirty(FoxStorageCartPickupKey);
            cleared++;
        }
        return cleared;
    }

    private bool CanPackStorageAcceptAnyCachedLoot(string ownerUid, BlockPos storagePos)
    {
        if (serverApi == null || packRepository?.Loaded != true
            || serverApi.World.BlockAccessor.GetBlockEntity(storagePos) is not IBlockEntityContainer container)
        {
            return false;
        }
        foreach (byte[] bytes in packRepository.GetLootBytes(ownerUid))
        {
            ItemStack stack = new(bytes);
            if (!stack.ResolveBlockOrItem(serverApi.World) || stack.Collectible == null || stack.StackSize <= 0
                || CompanionPickupPolicy.IsCorpse(stack.Collectible.GetType())
                || packRepository.GetAmenity(storagePos) is not FoxPackAmenityRecord record
                || !StorageRoutingAccepts(record, stack)) continue;
            ItemStack oneItem = stack.GetEmptyClone();
            oneItem.StackSize = 1;
            if (CanInventoryFullyAccept(container.Inventory, oneItem)) return true;
        }
        return false;
    }

    private static bool CanInventoryFullyAccept(IInventory inventory, ItemStack stack)
    {
        DummySlot source = new(stack);
        int remaining = stack.StackSize;
        foreach (ItemSlot slot in inventory)
        {
            if (FoxStorageBulkInsertion.TryGetRemainingCapacity(slot, stack, out int bulkAvailable))
            {
                remaining -= Math.Min(remaining, bulkAvailable);
                if (remaining <= 0) return true;
                continue;
            }

            // CanHold only answers whether the slot type can contain this
            // item. For an occupied slot, the actual transfer also requires
            // the existing stack to be mergeable with the source stack.
            if (!slot.CanTakeFrom(source)) continue;
            int available = slot.GetRemainingSlotSpace(stack);
            if (!slot.Empty && slot.Itemstack?.Collectible != null)
            {
                available = Math.Min(
                    available,
                    slot.Itemstack.Collectible.GetMergableQuantity(
                        slot.Itemstack,
                        stack,
                        EnumMergePriority.AutoMerge));
            }

            remaining -= Math.Min(remaining, available);
            if (remaining <= 0) return true;
        }
        return false;
    }

    private static bool CanInventoryAcceptOne(IInventory inventory, ItemStack stack)
    {
        ItemStack oneItem = stack.GetEmptyClone();
        oneItem.StackSize = 1;
        DummySlot source = new(oneItem);
        return inventory.GetBestSuitedSlot(source)?.slot != null
            || inventory.Any(slot => FoxStorageBulkInsertion.CanTransfer(slot, oneItem));
    }

    private static bool HasOpenInventorySlot(IInventory inventory)
    {
        foreach (ItemSlot slot in inventory)
        {
            if (slot.Empty || slot.GetRemainingSlotSpace(slot.Itemstack) > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static double GetInventoryFillRatio(IInventory inventory)
    {
        int totalSlots = 0;
        int occupiedSlots = 0;
        foreach (ItemSlot slot in inventory)
        {
            totalSlots++;
            if (!slot.Empty) occupiedSlots++;
        }

        return totalSlots == 0 ? 1d : (double)occupiedSlots / totalSlots;
    }

    private void ClaimRecruitment(IServerPlayer owner)
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        FoxRecruitmentRewardRecord? reward = packRepository.GetPendingRecruitment(ownerUid);
        if (reward == null)
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPackState(owner, "There is no recruitment result waiting to be claimed.");
            return;
        }

        Entity? recruit = SpawnRecruitmentCompanion(owner, reward, out string failure);
        if (recruit == null)
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPackState(owner, $"The recruitment result is still waiting: {failure}");
            return;
        }

        packRepository.ClearPendingRecruitment(ownerUid);
        packRepository.Save();
        string rewardName = CompanionSpeciesCatalog.TryGetRecruitmentVariant(
            reward.WildEntityCode,
            out CompanionSpeciesProfile species,
            out CompanionRecruitmentVariant variant)
                ? GetRecruitmentDisplayName(species, variant).ToLowerInvariant()
                : "companion";
        SendOwnerSound(owner, CompanionSoundCue.RecruitmentSuccess);
        SendPackState(owner, $"Claimed recruitment. A tamed {rewardName} joined your pack on safe ground nearby.");
    }

    private void AddPackLootDeveloper(IServerPlayer owner, string type, IEnumerable<string> selectedFoxIds)
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        if (string.Equals(type, FoxExpeditionType.SearchLost, StringComparison.Ordinal))
        {
            RefreshLoadedFoxPackRecords();
            if (!HasRescueTarget(ownerUid))
            {
                SendPackState(owner, "Search for lost is only available when a companion is recoverable.");
                return;
            }

            if (packRepository.HasLoot(ownerUid) || packRepository.HasPendingRecruitment(ownerUid))
            {
                SendPackState(owner, "Expedition refused: claim the pending pack reward before testing another.");
                return;
            }

            if (!TryBuildExpeditionParty(ownerUid, selectedFoxIds, out List<string> searchParty, out float searchStrength, out _, out string searchRefusal))
            {
                SendPackState(owner, searchRefusal);
                return;
            }

            FoxExpeditionRecord test = new()
            {
                OwnerUid = ownerUid,
                Type = type,
                SelectedFoxIds = searchParty,
                ExpeditionStrength = searchStrength,
                TargetStrength = GetExpeditionTargetStrength(type)
            };
            ApplyExpeditionPerks(test);
            ExpeditionResolution resolution = ResolveCompletedExpedition(test, packRepository.GetLootBytes(ownerUid).Count);
            packRepository.Save();
            SendPackState(owner, $"Test {GetExpeditionDisplayName(type)}: {resolution.Message}");
            return;
        }

        if (string.Equals(type, FoxExpeditionType.Recruitment, StringComparison.Ordinal))
        {
            if (packRepository.HasLoot(ownerUid) || packRepository.HasPendingRecruitment(ownerUid))
            {
                SendPackState(owner, "Expedition refused: claim the pending pack reward before testing another.");
                return;
            }

            if (!TryBuildExpeditionParty(ownerUid, selectedFoxIds, out List<string> recruitmentParty, out float recruitmentStrength, out _, out string recruitmentRefusal))
            {
                SendPackState(owner, recruitmentRefusal);
                return;
            }

            FoxExpeditionRecord test = new()
            {
                OwnerUid = ownerUid,
                Type = type,
                SelectedFoxIds = recruitmentParty,
                ExpeditionStrength = recruitmentStrength,
                TargetStrength = GetExpeditionTargetStrength(type)
            };
            ApplyExpeditionPerks(test);
            ExpeditionResolution resolution = ResolveCompletedExpedition(test, packRepository.GetLootBytes(ownerUid).Count);
            packRepository.Save();
            SendPackState(owner, $"Test {GetExpeditionDisplayName(type)}: {resolution.Message}");
            return;
        }

        if (string.Equals(type, FoxExpeditionType.PackPatrol, StringComparison.Ordinal))
        {
            if (!TryBuildExpeditionParty(ownerUid, selectedFoxIds, out List<string> patrolParty, out float patrolStrength, out _, out string patrolRefusal))
            {
                SendPackState(owner, patrolRefusal);
                return;
            }

            FoxExpeditionRecord test = new()
            {
                OwnerUid = ownerUid,
                Type = type,
                SelectedFoxIds = patrolParty,
                ExpeditionStrength = patrolStrength,
                TargetStrength = GetExpeditionTargetStrength(type)
            };
            ApplyExpeditionPerks(test);
            ExpeditionResolution resolution = ResolveCompletedExpedition(test, packRepository.GetLootBytes(ownerUid).Count);
            packRepository.Save();
            SendPackState(owner, $"Test {GetExpeditionDisplayName(type)}: {resolution.Message}");
            return;
        }

        if (!IsRandomLootExpedition(type))
        {
            SendPackState(owner, "Unknown expedition type.");
            return;
        }

        if (packRepository.HasLoot(ownerUid) || packRepository.HasPendingRecruitment(ownerUid))
        {
            SendPackState(owner, "Expedition refused: claim the pending pack reward before starting another.");
            return;
        }

        if (!TryBuildExpeditionParty(
                ownerUid,
                selectedFoxIds,
                out List<string> party,
                out float strength,
                out _,
                out string refusal))
        {
            SendPackState(owner, refusal);
            return;
        }

        FoxExpeditionRecord testExpedition = new()
        {
            OwnerUid = ownerUid,
            Type = type,
            SelectedFoxIds = party,
            ExpeditionStrength = strength,
            TargetStrength = GetExpeditionTargetStrength(type)
        };
        ApplyExpeditionPerks(testExpedition);
        float target = testExpedition.TargetStrength;
        float completion = Math.Clamp(testExpedition.ExpeditionStrength / target, 0f, 1f);
        int itemCount = GenerateExpeditionLoot(ownerUid, type, completion, party.Count);
        packRepository.Save();
        SendPackState(owner,
            $"Test {GetExpeditionDisplayName(type)} expedition returned with {itemCount} {(itemCount == 1 ? "item" : "items")} at "
            + $"{completion * 100f:0}% coverage. The cache is now sealed until claimed.");
    }

    private void ForceFoxMiaDeveloper(IServerPlayer owner, IEnumerable<string> selectedFoxIds)
    {
        string ownerUid = owner.PlayerUID;
        if (serverApi == null || packRepository == null || string.IsNullOrWhiteSpace(ownerUid))
        {
            return;
        }

        if (packRepository.HasLoot(ownerUid) || packRepository.HasPendingRecruitment(ownerUid))
        {
            SendPackState(owner, "Claim the pending pack reward before forcing a companion MIA.");
            return;
        }

        string[] requested = selectedFoxIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requested.Length != 1
            || !packRepository.TryGetRecord(requested[0], out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, ownerUid, StringComparison.Ordinal)
            || !IsExpeditionSelectableStatus(record.Status))
        {
            SendPackState(owner, "Select exactly one available companion to force MIA.");
            return;
        }

        Entity? entity = record.EntityId > 0 ? serverApi.World.GetEntityById(record.EntityId) : null;
        if (entity == null || !IsTamedFox(entity) || !IsOwner(entity, ownerUid))
        {
            SendPackState(owner, $"{GetFoxRecordLabel(record)} is not currently available in the world.");
            return;
        }
        if (HasFoxStorageCargo(entity) || HasFoxPendingCartPickup(entity))
        {
            SendPackState(owner, $"{GetFoxRecordLabel(record)} is busy with Collection Box cargo.");
            return;
        }

        SetExpeditionMemberStatus(ownerUid, record.FoxId, "MIA");
        packRepository.Save();
        SendPackState(owner,
            $"Developer test: {GetFoxRecordLabel(record)} is now MIA. Search for lost is available.");
    }

    private List<FoxPackLootItemPacket> GetPackLootItems(string ownerUid)
    {
        List<FoxPackLootItemPacket> items = new();
        if (serverApi == null || packRepository == null)
        {
            return items;
        }

        foreach (byte[] bytes in packRepository.GetLootBytes(ownerUid))
        {
            ItemStack stack = new(bytes);
            string code = stack.ResolveBlockOrItem(serverApi.World) && stack.Collectible != null
                ? stack.Collectible.Code.ToShortString()
                : "unknown";
            string name = stack.Collectible == null ? "Unknown item" : stack.GetName();
            items.Add(new FoxPackLootItemPacket
            {
                Code = code,
                Name = name,
                Count = stack.StackSize,
                StackBytes = bytes
            });
        }

        return items;
    }

    private void LocatePackFox(IServerPlayer owner, string foxId)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        if (!packRepository.TryGetRecord(foxId, out FoxPackRecordV2? record)
            || record == null
            || !string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal)
            || record.Archived
            || !record.HasLastKnownPosition)
        {
            SendPackState(owner, "That companion has no recoverable last-known location.");
            return;
        }

        int chunkX = (int)Math.Floor(record.LastKnownX / (double)GlobalConstants.ChunkSize);
        int chunkZ = (int)Math.Floor(record.LastKnownZ / (double)GlobalConstants.ChunkSize);
        void OnLocated()
        {
            RefreshLoadedFoxPackRecords();
            if (packRepository.TryGetRecord(foxId, out FoxPackRecordV2? refreshed)
                && refreshed?.EntityId > 0
                && serverApi.World.GetEntityById(refreshed.EntityId) != null)
            {
                SendPackState(owner, $"Located {GetFoxRecordLabel(refreshed)} near {CompanionLocationText.Format(serverApi, refreshed.LastKnownX, refreshed.LastKnownY, refreshed.LastKnownZ)}.");
            }
            else
            {
                SendPackState(owner, $"Checked {CompanionLocationText.Format(serverApi, record.LastKnownX, record.LastKnownY, record.LastKnownZ)}, but the companion was not found in that saved chunk.");
            }
        }

        if (record.LastKnownDimension == 0)
        {
            serverApi.WorldManager.LoadChunkColumnPriority(
                chunkX,
                chunkZ,
                new ChunkLoadOptions
                {
                    KeepLoaded = false,
                    OnLoaded = OnLocated
                }
            );
        }
        else
        {
            serverApi.WorldManager.LoadChunkColumnForDimension(chunkX, chunkZ, record.LastKnownDimension);
            serverApi.World.RegisterCallback(_ => OnLocated(), 1000);
        }

        SendPackState(
            owner,
            $"Checking one saved chunk for {GetFoxRecordLabel(record)}. Last known: "
                + $"{CompanionLocationText.Format(serverApi, record.LastKnownX, record.LastKnownY, record.LastKnownZ)}."
        );
    }

    private void SendState(Entity entity, IServerPlayer owner, string message)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        string activeRequest = status?.GetString(ActiveRequestKey) ?? string.Empty;
        GetHealth(entity, out float currentHealth, out float maxHealth);
        serverChannel?.SendPacket(new FoxSocialStatePacket
        {
            TargetEntityId = entity.EntityId,
            FoxId = status?.GetString(FoxIdKey, string.Empty) ?? string.Empty,
            Number = status?.GetInt(NumberKey, 0) ?? 0,
            Name = GetFoxDisplayName(entity),
            Personality = GetPersonalityLabel(status?.GetString(PersonalityKey) ?? string.Empty),
            RequestsGenerated = status?.GetInt(RequestsGeneratedKey, 0) ?? 0,
            RequestsCompleted = status?.GetInt(RequestsCompletedKey, 0) ?? 0,
            Points = status?.GetInt(FoxPointsKey, 0) ?? 0,
            Mood = status == null ? "Unassigned" : GetMoodDisplay(entity),
            ActiveRequest = GetRequestLabel(
                entity,
                activeRequest,
                status?.GetString(PersonalityKey) ?? string.Empty
            ),
            ProgressSeconds = status?.GetFloat(RequestProgressKey, 0f) ?? 0f,
            DurationSeconds = FoxRequestCatalog.GetDurationSeconds(activeRequest),
            LastCompleted = GetRequestLabel(
                entity,
                status?.GetString(LastCompletedKey) ?? string.Empty,
                status?.GetString(PersonalityKey) ?? string.Empty
            ),
            RequestCooldownSeconds = status == null
                ? 0f
                : GetRemainingSeconds(status, RequestCooldownEndsUtcMsKey, RequestCooldownRemainingKey),
            CancelCooldownSeconds = status == null
                ? 0f
                : GetRemainingSeconds(status, CancelCooldownEndsUtcMsKey, CancelCooldownRemainingKey),
            Message = message,
            PersonalitySummary = FoxPersonalityText.Summary(status?.GetString(PersonalityKey) ?? string.Empty),
            CurrentThought = GetCurrentThoughtText(entity, status),
            HasThreatSurvey = GetFoxPerkRank(entity, "monster-knowledge") > 0,
            CarriedItem = GetFoxStorageCargoDisplay(entity),
            SpeciesId = GetCompanionSpecies(entity).Id,
            SpeciesDisplayName = GetCompanionSpecies(entity).DisplayName,
            AppearanceCode = entity.Code?.ToShortString() ?? string.Empty,
            SpeciesTraining = GetSpeciesTraining(entity, GetCompanionSpecies(entity).Id),
            ActivityMode = GetCompanionActivityMode(entity),
            FollowDistance = GetCompanionFollowDistance(entity),
            CombatStyle = GetCompanionCombatStyle(entity),
            RiskTolerance = GetCompanionRiskTolerance(entity),
            GroundCleanupEnabled = IsGroundCleanupEnabled(entity),
            GroundDroppedItemsEnabled = IsGroundDroppedItemsEnabled(entity),
            GroundCattailsEnabled = IsGroundCattailsEnabled(entity),
            GroundFlintEnabled = IsGroundFlintEnabled(entity),
            GroundSticksEnabled = IsGroundSticksEnabled(entity),
            GroundBouldersEnabled = IsGroundBouldersEnabled(entity),
            GroundRocksEnabled = IsGroundRocksEnabled(entity),
            MowLawnEnabled = IsMowLawnEnabled(entity),
            FinishedProductsEnabled = IsFinishedProductsEnabled(entity),
            FinishedCropsEnabled = IsFinishedCropsEnabled(entity),
            FinishedBerriesEnabled = IsFinishedBerriesEnabled(entity),
            FinishedMushroomsEnabled = IsFinishedMushroomsEnabled(entity),
            FlowerRemovalEnabled = IsFlowerRemovalEnabled(entity),
            SnowShovelingEnabled = IsSnowShovelingEnabled(entity),
            CharcoalShovelingEnabled = IsCharcoalShovelingEnabled(entity),
            SnowballCollectionEnabled = IsSnowballCollectionEnabled(entity),
            GeneralStorageSortingEnabled = IsGeneralStorageSortingEnabled(entity),
            IsJuvenile = IsCompanionJuvenile(entity)
            ,BankedTalentPoints = status?.GetInt(BankedTalentPointsKey, 0) ?? 0
            ,MotherName = status?.GetString(ParentMotherNameKey, string.Empty) ?? string.Empty
            ,FatherName = status?.GetString(ParentFatherNameKey, string.Empty) ?? string.Empty
            ,BreedingSupported = CompanionBreedingCatalog.TryGetForAdult(entity, out _)
            ,BreedingEnabled = status?.GetBool(BreedingEnabledKey, false) == true
            ,PregnancyActive = status?.GetBool(PregnancyActiveKey, false) == true
            ,BreedingPartnerName = status?.GetString(BondedPartnerNameKey, string.Empty) ?? string.Empty
            ,BreedingFeedback = GetBreedingFeedbackText(status?.GetString(BreedingFeedbackKey, string.Empty) ?? string.Empty)
            ,PregnancyRemainingHours = status?.GetBool(PregnancyActiveKey, false) == true
                ? (float)Math.Max(0d, status.GetDouble(PregnancyDueTotalHoursKey, 0d) - entity.World.Calendar.TotalHours)
                : 0f
            ,ExpeditionStrengthFactor = status?.GetBool(PregnancyActiveKey, false) == true
                ? GetPregnancyExpeditionMultiplier(entity)
                : 1f
            ,CurrentHealth = currentHealth
            ,MaxHealth = maxHealth
            ,FoodSystemEnabled = FoodSystemEnabled
            ,FoodLevel = GetCompanionFoodLevel(entity)
            ,FoodState = GetFoodStateLabel(GetCompanionFoodLevel(entity), FoodSystemEnabled)
            ,OwnedCompanionCount = packRepository?.GetAllRecords().Count(record =>
                string.Equals(record.OwnerUid, owner.PlayerUID, StringComparison.Ordinal)) ?? 0
            ,Level = GetCompanionLevel(status)
            ,CurrentLevelExperience = GetCompanionCurrentExperience(status)
            ,RequiredLevelExperience = CompanionProgressionRules.GetRequiredExperience(GetCompanionLevel(status))
            ,LifetimeExperience = GetCompanionLifetimeExperience(status)
            ,DeveloperEntityLoaded = true
        }, owner);
    }

    private void BuyPerk(Entity entity, IServerPlayer owner, string perkId)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-talents-banked"));
            return;
        }
        FoxPerkDefinition? definition = FoxPerkCatalog.Get(perkId);
        ITreeAttribute? perks = GetFoxPerkTree(entity, true);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (definition == null || perks == null)
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPerkState(entity, owner, "That perk does not exist.");
            return;
        }

        CompanionSpeciesProfile species = GetCompanionSpecies(entity);
        if (!FoxPerkCatalog.AppliesToSpecies(definition, species.Id))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPerkState(entity, owner, $"That talent belongs to {definition.SpeciesId}, not {species.Id}.");
            return;
        }

        int availablePoints = Math.Max(0, status.GetInt(FoxPointsKey, 0));
        if (!FoxPerkCatalog.CanBuy(perks, definition, availablePoints, out string reason))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPerkState(entity, owner, reason);
            return;
        }

        int currentRank = FoxPerkCatalog.GetRank(perks, definition);
        int cost = FoxPerkCatalog.GetRankCost(definition, currentRank);
        perks.SetInt(definition.Id, currentRank + 1);
        status.SetInt(FoxPointsKey, availablePoints - cost);
        if (string.Equals(definition.Id, "damage-training", StringComparison.Ordinal))
        {
            status.SetInt(DamagePerkRankKey, currentRank + 1);
        }

        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        SendOwnerSound(owner, CompanionSoundCue.TalentPurchased);
        SendPerkState(entity, owner, $"{definition.Name}: {currentRank + 1}/{definition.MaxRank}. Cost: {cost} point{(cost == 1 ? string.Empty : "s")}.");
        SendStateToOwner(entity, string.Empty);
        SendPackStateToOwner(entity);
    }

    private void ResetPerks(Entity entity, IServerPlayer owner)
    {
        if (IsCompanionJuvenile(entity))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendState(entity, owner, Lang.Get("feralkinshipcompanions:child-talents-banked"));
            return;
        }
        if (IsFoxIncapacitated(entity))
        {
            SendOwnerSound(owner, CompanionSoundCue.Rejected);
            SendPerkState(entity, owner, "Talents cannot be reset while this companion is mortally wounded or recovering.");
            return;
        }

        ITreeAttribute? perks = GetFoxPerkTree(entity, true);
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (perks == null)
        {
            return;
        }

        int refunded = FoxPerkCatalog.GetTotalSpent(perks);
        foreach (FoxPerkDefinition definition in FoxPerkCatalog.All)
        {
            perks.SetInt(definition.Id, 0);
        }

        status.SetInt(DamagePerkRankKey, 0);
        status.SetInt(FoxPointsKey, Math.Max(0, status.GetInt(FoxPointsKey, 0)) + refunded);
        MarkSocialStateDirty(entity);
        RegisterFoxInPack(entity);
        SendOwnerSound(owner, CompanionSoundCue.TalentReset);
        SendPerkState(entity, owner, $"All talents reset. Refunded {refunded} point{(refunded == 1 ? string.Empty : "s")}.");
        SendStateToOwner(entity, string.Empty);
        SendPackStateToOwner(entity);
    }

    private void SendPerkState(Entity entity, IServerPlayer owner, string message)
    {
        ITreeAttribute? status = GetDomesticationStatus(entity);
        ITreeAttribute? perks = GetFoxPerkTree(entity, true);
        if (status == null || perks == null)
        {
            return;
        }

        CompanionSpeciesProfile species = GetCompanionSpecies(entity);
        List<FoxPerkRankEntry> ranks = FoxPerkCatalog.All
            .Where(definition => FoxPerkCatalog.AppliesToSpecies(definition, species.Id))
            .Select(definition => new FoxPerkRankEntry
            {
                Id = definition.Id,
                Rank = FoxPerkCatalog.GetRank(perks, definition)
            })
            .ToList();

        serverChannel?.SendPacket(new FoxPerkStatePacket
        {
            TargetEntityId = entity.EntityId,
            Number = status.GetInt(NumberKey, 0),
            Name = GetFoxDisplayName(entity),
            AvailablePoints = Math.Max(0, status.GetInt(FoxPointsKey, 0)),
            SpentPoints = FoxPerkCatalog.GetTotalSpent(perks),
            CombatInvestment = FoxPerkCatalog.GetTreeInvestment(perks, FoxPerkTreeId.Combat),
            MobilityInvestment = FoxPerkCatalog.GetTreeInvestment(perks, FoxPerkTreeId.Mobility),
            SocialInvestment = FoxPerkCatalog.GetTreeInvestment(perks, FoxPerkTreeId.Social),
            Ranks = ranks,
            Message = message,
            SpeciesId = species.Id,
            SpeciesDisplayName = species.DisplayName,
            Personality = GetPersonalityLabel(status.GetString(PersonalityKey, string.Empty)),
            Mood = GetMoodDisplay(entity),
            ActivityMode = GetCompanionActivityMode(entity),
            CombatStyle = GetCompanionCombatStyle(entity),
            Level = GetCompanionLevel(status),
            CurrentLevelExperience = GetCompanionCurrentExperience(status),
            RequiredLevelExperience = CompanionProgressionRules.GetRequiredExperience(GetCompanionLevel(status))
        }, owner);
    }

    private void OnFoxSocialState(FoxSocialStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (socialDialog?.IsOpened() == true)
                {
                    socialDialog.ApplyState(packet);
                }
                if (developerDialog?.IsOpened() == true)
                {
                    developerDialog.ApplyState(packet);
                }
            },
            "feralkinshipcompanions-update-fox-social"
        );
    }

    private void OnCompanionNameSuggestion(CompanionNameSuggestionPacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () => pendingNameSuggestions.Enqueue(new CompanionNameSuggestionPacket
            {
                TargetEntityId = packet.TargetEntityId,
                SuggestedName = packet.SuggestedName,
                IsNewborn = packet.IsNewborn
            }),
            "feralkinshipcompanions-name-suggestion"
        );
    }

    private void OnNamePromptTick(float _)
    {
        if (clientApi?.World.Player.Entity == null
            || nameSuggestionDialog?.IsOpened() == true
            || renameDialog?.IsOpened() == true)
        {
            return;
        }

        while (pendingNameSuggestions.Count > 0)
        {
            CompanionNameSuggestionPacket packet = pendingNameSuggestions.Dequeue();
            if (TryOpenCompanionNameSuggestionGui(
                    packet.TargetEntityId,
                    packet.SuggestedName,
                    packet.IsNewborn))
            {
                return;
            }
        }
    }

    private void OnFoxPerkState(FoxPerkStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (perkDialog?.IsOpened() == true)
                {
                    perkDialog.ApplyState(packet);
                }
            },
            "feralkinshipcompanions-update-fox-perks"
        );
    }

    private void OnFoxPackState(FoxPackStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                lastPackState = packet;
                if (packDialog?.IsOpened() == true)
                {
                    packDialog.ApplyState(packet);
                }
            },
            "feralkinshipcompanions-update-fox-pack"
        );
    }

    private void OnCompanionRecoveryState(CompanionRecoveryStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (recoveryDialog?.IsOpened() == true)
                {
                    recoveryDialog.ApplyState(packet);
                }
                if (developerDialog?.IsOpened() == true)
                {
                    developerDialog.ApplyRecoveryState(packet);
                }
            },
            "feralkinshipcompanions-update-companion-recovery"
        );
    }

    private void OnFoxPackOpenFromCairn(FoxPackOpenFromCairnPacket packet)
    {
        if (!packet.Open)
        {
            return;
        }

        clientApi?.Event.EnqueueMainThreadTask(
            OpenFoxPackGuiFromCairn,
            "feralkinshipcompanions-open-fox-pack-from-cairn"
        );
    }

    private void OnFoxStorageRoutingState(FoxStorageRoutingStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (clientApi == null)
                {
                    return;
                }

                if (storageRoutingDialog == null
                    || !storageRoutingDialog.IsOpened()
                    || !storageRoutingDialog.IsFor(packet))
                {
                    storageRoutingDialog?.TryClose();
                    storageRoutingDialog = new GuiDialogFeralKinshipStorageRouting(clientApi, this, packet);
                    storageRoutingDialog.TryOpen();
                }

                storageRoutingDialog.ApplyState(packet);
            },
            "feralkinshipcompanions-update-storage-routing"
        );
    }

    private void OnFoxBedAssignmentState(FoxBedAssignmentStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (clientApi == null)
                {
                    return;
                }

                if (foxBedDialog == null || !foxBedDialog.IsOpened() || !foxBedDialog.IsFor(packet))
                {
                    foxBedDialog?.TryClose();
                    foxBedDialog = new GuiDialogFeralKinshipFoxBed(clientApi, this, packet);
                    foxBedDialog.TryOpen();
                }
                foxBedDialog.ApplyState(packet);
            },
            "feralkinshipcompanions-update-fox-bed"
        );
    }

    private void OnFoxBedAssignmentRequest(IServerPlayer fromPlayer, FoxBedAssignmentRequestPacket packet)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        BlockPos pos = new(packet.X, packet.Y, packet.Z, packet.Dimension);
        FoxBedRecord? bed = packRepository.GetBed(pos);
        if (bed == null || !string.Equals(bed.OwnerUid, fromPlayer.PlayerUID, StringComparison.Ordinal))
        {
            fromPlayer.SendMessage(
                GlobalConstants.GeneralChatGroup,
            "Only the player who placed this companion bed can assign it.",
                EnumChatType.Notification
            );
            return;
        }

        EntityPos playerPos = fromPlayer.Entity.Pos;
        double dx = playerPos.X - (packet.X + 0.5d);
        double dy = playerPos.Y - (packet.Y + 0.5d);
        double dz = playerPos.Z - (packet.Z + 0.5d);
        if (playerPos.Dimension != packet.Dimension || dx * dx + dy * dy + dz * dz > 64d)
        {
            return;
        }

        string message;
        string previousBrambleOwner = bed.BrambleOwnerUid;
        bool success;
        string refusal;
        if (packet.Bramble)
        {
            success = TryAssignBrambleToBed(fromPlayer, pos, out refusal);
            message = success ? "Bramble will stay at this bed." : refusal;
        }
        else
        {
            success = packRepository.TryAssignBed(fromPlayer.PlayerUID, pos, packet.FoxId, out refusal);
            if (success && !string.IsNullOrWhiteSpace(previousBrambleOwner))
            {
                ClearBrambleBedAssignment(previousBrambleOwner);
            }
            message = success
                ? string.IsNullOrWhiteSpace(packet.FoxId)
                    ? "Companion bed assignment cleared."
                    : "Companion den assigned."
                : refusal;
        }

        if (success) packRepository.Save();
        SendFoxBedState(fromPlayer, bed, message);
    }

    private void OnFoxStorageRoutingRequest(IServerPlayer fromPlayer, FoxStorageRoutingRequestPacket packet)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        BlockPos pos = NormalizeStorageRoutingPosition(new(packet.X, packet.Y, packet.Z, packet.Dimension));
        EntityPos playerPos = fromPlayer.Entity.Pos;
        double dx = playerPos.X - (pos.X + 0.5d);
        double dy = playerPos.Y - (pos.Y + 0.5d);
        double dz = playerPos.Z - (pos.Z + 0.5d);
        if (playerPos.Dimension != pos.dimension || dx * dx + dy * dy + dz * dz > 64d)
        {
            return;
        }

        Block? block = serverApi.World.BlockAccessor.GetBlock(pos);
        BlockEntity? blockEntity = serverApi.World.BlockAccessor.GetBlockEntity(pos);
        if (!FoxStorageRouting.IsSupportedStorageTarget(block, blockEntity))
        {
            SendFoxStorageRoutingState(fromPlayer, pos, false, "That is not a supported storage container.");
            return;
        }

        if (!serverApi.World.Claims.TryAccess(fromPlayer, pos, EnumBlockAccessFlags.Use))
        {
            SendFoxStorageRoutingState(fromPlayer, pos, false, "You do not have permission to configure this container.");
            return;
        }

        FoxPackAmenityRecord? record = packRepository.GetAmenity(pos);
        if (record != null && !string.Equals(record.OwnerUid, fromPlayer.PlayerUID, StringComparison.Ordinal))
        {
            SendFoxStorageRoutingState(fromPlayer, pos, false, "This storage destination belongs to another player's pack.");
            return;
        }

        string message;
        switch (packet.Action)
        {
            case FoxStorageRoutingRequestPacket.SetMask:
                int nextMask = FoxStorageRouting.NormalizeMask(packet.CategoryMask);
                if (nextMask == 0)
                {
                    message = "Choose a category, or use Remove to clear this destination.";
                    SendFoxStorageRoutingState(fromPlayer, pos, true, message);
                    return;
                }

                packRepository.RegisterStorageTarget(fromPlayer.PlayerUID, pos, nextMask);
                packRepository.Save();
                message = $"This container now accepts {FoxStorageRouting.GetDisplayName(nextMask)}.";
                break;

            case FoxStorageRoutingRequestPacket.EnableAdvanced:
            {
                IInventory? inventory = (blockEntity as IBlockEntityContainer)?.Inventory;
                bool hasSavedRules = record?.StorageAdvancedIncludedItemCodes.Count > 0
                    || record?.StorageAdvancedExcludedItemCodes.Count > 0;
                if (inventory == null || (!hasSavedRules && CountDistinctStorageItems(inventory) == 0))
                {
                    message = "Put sample items in this container first, then enable Advanced mode.";
                    break;
                }

                if (record == null)
                {
                    record = packRepository.RegisterStorageTarget(
                        fromPlayer.PlayerUID,
                        pos,
                        (int)FoxStorageRouting.Category.General);
                }
                else
                {
                    if (record.StorageRoutingMask == 0)
                    {
                        record.StorageRoutingMask = (int)FoxStorageRouting.Category.General;
                    }
                    record.StorageRoutingEnabled = true;
                    if (record.Kind is not ("storage" or "dining")) record.Kind = "storage-target";
                }

                record.StorageAdvancedIncludedItemCodes ??= new List<string>();
                record.StorageAdvancedExcludedItemCodes ??= new List<string>();
                int added = AddCurrentStorageItemRules(inventory, record);
                record.StorageAdvancedMode = true;
                packRepository.Save();
                message = added > 0
                    ? $"Advanced mode enabled. Added {added} exact item type(s) from this container."
                    : "Advanced mode enabled. Existing exact item rules were kept.";
                break;
            }

            case FoxStorageRoutingRequestPacket.UseCategories:
                if (record == null || !record.StorageAdvancedMode)
                {
                    message = "Category routing is already active.";
                    break;
                }

                record.StorageAdvancedMode = false;
                packRepository.Save();
                message = "Category routing is active again. Advanced item rules were kept.";
                break;

            case FoxStorageRoutingRequestPacket.AddCurrentItems:
                if (record == null || !record.StorageAdvancedMode
                    || blockEntity is not IBlockEntityContainer advancedContainer)
                {
                    message = "Enable Advanced mode before adding current items.";
                    break;
                }

                int addedItems = AddCurrentStorageItemRules(advancedContainer.Inventory, record);
                packRepository.Save();
                message = addedItems == 0
                    ? "No new item types were added. Existing choices were preserved."
                    : $"Added {addedItems} exact item type(s) from this container.";
                break;

            case FoxStorageRoutingRequestPacket.ToggleExactItem:
                if (record == null || !record.StorageAdvancedMode
                    || string.IsNullOrWhiteSpace(packet.ItemCode)
                    || packet.ItemCode.Length > 256)
                {
                    message = "That exact-item rule is unavailable.";
                    break;
                }

                string itemCode = packet.ItemCode.Trim();
                if (!StorageInventoryContainsCode(blockEntity, itemCode)
                    && !ContainsStorageItemCode(record.StorageAdvancedIncludedItemCodes, itemCode)
                    && !ContainsStorageItemCode(record.StorageAdvancedExcludedItemCodes, itemCode))
                {
                    message = "That item is no longer in this container. Refresh its contents before changing the rule.";
                    break;
                }

                record.StorageAdvancedIncludedItemCodes ??= new List<string>();
                record.StorageAdvancedExcludedItemCodes ??= new List<string>();
                if (ContainsStorageItemCode(record.StorageAdvancedIncludedItemCodes, itemCode))
                {
                    RemoveStorageItemCode(record.StorageAdvancedIncludedItemCodes, itemCode);
                    if (!ContainsStorageItemCode(record.StorageAdvancedExcludedItemCodes, itemCode))
                    {
                        record.StorageAdvancedExcludedItemCodes.Add(itemCode);
                    }
                    message = $"{itemCode} is now excluded from this destination.";
                }
                else
                {
                    if (!ContainsStorageItemCode(record.StorageAdvancedExcludedItemCodes, itemCode)
                        && record.StorageAdvancedIncludedItemCodes.Count + record.StorageAdvancedExcludedItemCodes.Count
                            >= FoxStorageRouting.MaxExactItemRules)
                    {
                        message = $"Advanced rules are limited to {FoxStorageRouting.MaxExactItemRules} item types.";
                        break;
                    }
                    RemoveStorageItemCode(record.StorageAdvancedExcludedItemCodes, itemCode);
                    if (!ContainsStorageItemCode(record.StorageAdvancedIncludedItemCodes, itemCode))
                    {
                        record.StorageAdvancedIncludedItemCodes.Add(itemCode);
                    }
                    message = $"{itemCode} is now allowed in this destination.";
                }

                packRepository.Save();
                break;

            case FoxStorageRoutingRequestPacket.Clear:
                if (record == null)
                {
                    message = "This container is already unconfigured.";
                    break;
                }

                if (string.Equals(record.Kind, "storage", StringComparison.Ordinal))
                {
                    // Pack Collection Boxes are inherently General storage.
                    // Removing a custom rule returns them to that default.
                    record.StorageRoutingEnabled = true;
                    record.StorageRoutingMask = 0;
                    ClearAdvancedStorageRules(record);
                    packRepository.Save();
                    message = "Custom rules removed. This Pack Collection Box is General storage again.";
                }
                else if (string.Equals(record.Kind, "dining", StringComparison.Ordinal))
                {
                    record.StorageRoutingEnabled = false;
                    record.StorageRoutingMask = 0;
                    ClearAdvancedStorageRules(record);
                    packRepository.Save();
                    message = "Custom rules removed. This Dining Board is no longer a pack destination.";
                }
                else if (packRepository.RemoveStorageTarget(fromPlayer.PlayerUID, pos, out string refusal))
                {
                    packRepository.Save();
                    message = "This container is no longer a pack destination.";
                }
                else
                {
                    message = refusal;
                }
                break;

            default:
                message = "Ready to configure this storage container.";
                break;
        }

        SendFoxStorageRoutingState(fromPlayer, pos, true, message);
    }

    private static int CountDistinctStorageItems(IInventory inventory)
    {
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        foreach (ItemSlot slot in inventory)
        {
            string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
            if (!string.IsNullOrWhiteSpace(code)) codes.Add(code);
        }
        return codes.Count;
    }

    private static int AddCurrentStorageItemRules(IInventory inventory, FoxPackAmenityRecord record)
    {
        record.StorageAdvancedIncludedItemCodes ??= new List<string>();
        record.StorageAdvancedExcludedItemCodes ??= new List<string>();
        HashSet<string> configured = new(
            record.StorageAdvancedIncludedItemCodes.Concat(record.StorageAdvancedExcludedItemCodes),
            StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (ItemSlot slot in inventory)
        {
            string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
            if (string.IsNullOrWhiteSpace(code) || configured.Contains(code)) continue;
            if (record.StorageAdvancedIncludedItemCodes.Count + record.StorageAdvancedExcludedItemCodes.Count
                >= FoxStorageRouting.MaxExactItemRules) break;
            record.StorageAdvancedIncludedItemCodes.Add(code);
            configured.Add(code);
            added++;
        }
        return added;
    }

    private static bool ContainsStorageItemCode(List<string>? codes, string code) =>
        codes?.Contains(code, StringComparer.OrdinalIgnoreCase) == true;

    private static void RemoveStorageItemCode(List<string> codes, string code) =>
        codes.RemoveAll(existing => string.Equals(existing, code, StringComparison.OrdinalIgnoreCase));

    private static bool StorageInventoryContainsCode(BlockEntity? blockEntity, string code)
    {
        if (blockEntity is not IBlockEntityContainer container) return false;
        foreach (ItemSlot slot in container.Inventory)
        {
            if (string.Equals(
                    slot?.Itemstack?.Collectible?.Code?.ToString(),
                    code,
                    StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void ClearAdvancedStorageRules(FoxPackAmenityRecord record)
    {
        record.StorageAdvancedMode = false;
        record.StorageAdvancedIncludedItemCodes?.Clear();
        record.StorageAdvancedExcludedItemCodes?.Clear();
    }

    private BlockPos NormalizeStorageRoutingPosition(BlockPos pos)
    {
        Block? block = serverApi?.World.BlockAccessor.GetBlock(pos);
        if (block is IMultiblockOffset multiblock)
        {
            BlockPos controller = multiblock.GetControlBlockPos(pos);
            Block? controllerBlock = serverApi?.World.BlockAccessor.GetBlock(controller);
            BlockEntity? controllerEntity = serverApi?.World.BlockAccessor.GetBlockEntity(controller);
            if (FoxStorageRouting.IsSupportedStorageTarget(controllerBlock, controllerEntity))
            {
                return controller;
            }
        }

        return pos;
    }

    private void SendFoxStorageRoutingState(IServerPlayer owner, BlockPos pos, bool supported, string message)
    {
        if (serverChannel == null || packRepository == null)
        {
            return;
        }

        FoxPackAmenityRecord? record = packRepository.GetAmenity(pos);
        int mask = record != null && IsStorageRoutingEnabled(record)
            ? GetEffectiveStorageRoutingMask(record)
            : 0;
        Block? block = serverApi?.World.BlockAccessor.GetBlock(pos);
        string storageName = block?.Code?.Path ?? "Storage container";
        if (block != null && serverApi?.World != null)
        {
            string localizedName = block.GetPlacedBlockName(serverApi.World, pos);
            if (!string.IsNullOrWhiteSpace(localizedName))
            {
                storageName = localizedName;
            }
        }

        FoxStorageRoutingStatePacket state = new()
        {
            X = pos.X,
            Y = pos.Y,
            Z = pos.Z,
            Dimension = pos.dimension,
            Supported = supported,
            RoutingEnabled = mask != 0,
            CategoryMask = mask,
            StorageName = storageName,
            Message = message,
            AdvancedMode = record?.StorageAdvancedMode == true,
            AdvancedItems = record?.StorageAdvancedMode == true
                ? BuildAdvancedStorageItemOptions(pos, record)
                : new List<FoxStorageRoutingItemRulePacket>()
        };
        serverChannel.SendPacket(state, owner);
    }

    private List<FoxStorageRoutingItemRulePacket> BuildAdvancedStorageItemOptions(
        BlockPos pos,
        FoxPackAmenityRecord record)
    {
        Dictionary<string, FoxStorageRoutingItemRulePacket> options = new(StringComparer.OrdinalIgnoreCase);
        if (serverApi?.World.BlockAccessor.GetBlockEntity(pos) is IBlockEntityContainer container)
        {
            foreach (ItemSlot slot in container.Inventory)
            {
                ItemStack? stack = slot?.Itemstack;
                if (stack == null || stack.StackSize <= 0) continue;
                if (stack.Collectible == null && !stack.ResolveBlockOrItem(serverApi.World)) continue;
                string? code = stack.Collectible?.Code?.ToString();
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!options.TryGetValue(code, out FoxStorageRoutingItemRulePacket? option))
                {
                    option = new FoxStorageRoutingItemRulePacket
                    {
                        ItemCode = code,
                        DisplayName = stack.GetName(),
                        CategoryName = FoxStorageRouting.GetDisplayName(
                            (int)FoxStorageRouting.Classify(stack, serverApi.World)),
                        PresentInStorage = true
                    };
                    options.Add(code, option);
                }
                option.QuantityInStorage += stack.StackSize;
            }
        }

        foreach (string code in (record.StorageAdvancedIncludedItemCodes ?? new List<string>())
                     .Concat(record.StorageAdvancedExcludedItemCodes ?? new List<string>()))
        {
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (!options.TryGetValue(code, out FoxStorageRoutingItemRulePacket? option))
            {
                ItemStack? stack = ResolveStorageRuleItem(code);
                option = new FoxStorageRoutingItemRulePacket
                {
                    ItemCode = code,
                    DisplayName = stack?.GetName() ?? code,
                    CategoryName = stack?.Collectible == null || serverApi?.World == null
                        ? "Unknown"
                        : FoxStorageRouting.GetDisplayName(
                            (int)FoxStorageRouting.Classify(stack, serverApi.World))
                };
                options.Add(code, option);
            }
        }

        foreach (FoxStorageRoutingItemRulePacket option in options.Values)
        {
            option.Included = ContainsStorageItemCode(record.StorageAdvancedIncludedItemCodes, option.ItemCode);
            option.Excluded = ContainsStorageItemCode(record.StorageAdvancedExcludedItemCodes, option.ItemCode);
        }

        return options.Values
            .OrderBy(option => option.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private ItemStack? ResolveStorageRuleItem(string code)
    {
        if (serverApi?.World == null) return null;
        try
        {
            AssetLocation location = new(code);
            CollectibleObject? collectible = serverApi.World.Collectibles.FirstOrDefault(candidate =>
                string.Equals(candidate.Code?.ToString(), location.ToString(), StringComparison.OrdinalIgnoreCase));
            return collectible == null ? null : new ItemStack(collectible);
        }
        catch
        {
            return null;
        }
    }

    private void OnFoxWorkCartAssignmentState(FoxWorkCartAssignmentStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (clientApi == null)
                {
                    return;
                }

                if (workCartDialog == null || !workCartDialog.IsOpened() || !workCartDialog.IsFor(packet))
                {
                    workCartDialog?.TryClose();
                    workCartDialog = new GuiDialogFeralKinshipWorkCart(clientApi, this, packet);
                    workCartDialog.TryOpen();
                }
                workCartDialog.ApplyState(packet);
            },
            "feralkinshipcompanions-update-work-cart"
        );
    }

    private void OnFoxWorkCartAssignmentRequest(IServerPlayer fromPlayer, FoxWorkCartAssignmentRequestPacket packet)
    {
        if (serverApi == null || packRepository?.Loaded != true)
        {
            return;
        }

        BlockPos pos = new(packet.X, packet.Y, packet.Z, packet.Dimension);
        FoxWorkCartRecord? cart = packRepository.GetWorkCart(pos);
        if (cart == null || !string.Equals(cart.OwnerUid, fromPlayer.PlayerUID, StringComparison.Ordinal))
        {
            fromPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                "Only the player who placed this Work Cart can assign it.", EnumChatType.Notification);
            return;
        }

        EntityPos playerPos = fromPlayer.Entity.Pos;
        double dx = playerPos.X - (packet.X + 0.5d);
        double dy = playerPos.Y - (packet.Y + 0.5d);
        double dz = playerPos.Z - (packet.Z + 0.5d);
        if (playerPos.Dimension != packet.Dimension || dx * dx + dy * dy + dz * dz > 64d)
        {
            return;
        }

        int assigned = 0;
        string message;
        if (packet.SetLogging)
        {
            cart.LoggingEnabled = packet.LoggingEnabled;
            if (cart.LoggingEnabled)
            {
                // Turning logging back on must release any runtime queue or
                // logger task left behind by the previous toggle/reload.
                // The next maintenance pass will rebuild the queue from the
                // cart and its currently assigned workers.
                RearmWorkCartLogging(pos);
            }
            else
            {
                ClearWorkCartLoggingQueue(pos);
            }
            message = cart.LoggingEnabled
                ? "Logging enabled. Assigned adult companions will wait for tree debris to be cleaned before felling another tree."
                : "Logging disabled. Any active logger will stop without changing the tree.";
        }
        else if (packet.AssignWithinRadius)
        {
            foreach (Entity fox in loadedFoxes.Values.ToList())
            {
                if (!fox.Alive || !IsTamedFox(fox) || !IsOwner(fox, fromPlayer.PlayerUID)
                    || fox.Pos.Dimension != cart.Dimension)
                {
                    continue;
                }

                double foxDx = fox.Pos.X - (cart.X + 0.5d);
                double foxDz = fox.Pos.Z - (cart.Z + 0.5d);
                float workRadius = GetWorkCartRadius(fox);
                if (foxDx * foxDx + foxDz * foxDz > workRadius * workRadius)
                {
                    continue;
                }

                string foxId = GetDomesticationStatus(fox)?.GetString(FoxIdKey, string.Empty) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(foxId))
                {
                    continue;
                }

                if (packRepository.TryAssignWorkCart(fromPlayer.PlayerUID, pos, foxId, true, out _))
                {
                    RequestCompanionHomeTravel(fox);
                    EmitCompanionRuntimeDialogue(fox, "work.work_cart.assignment_success", CompanionDialoguePriority.Medium);
                    assigned++;
                }
            }
            message = assigned == 0
                ? "No owned companions are within the Work Cart's horizontal radius."
                : $"Assigned {assigned} companion{(assigned == 1 ? string.Empty : "s")} within the Work Cart's radius.";
        }
        else if (packRepository.TryAssignWorkCart(fromPlayer.PlayerUID, pos, packet.FoxId, packet.Assign, out string refusal))
        {
            if (packet.Assign)
            {
                Entity? fox = FindLoadedCompanionByFoxId(packet.FoxId);
                if (fox != null)
                {
                    RequestCompanionHomeTravel(fox);
                    EmitCompanionRuntimeDialogue(fox, "work.work_cart.assignment_success", CompanionDialoguePriority.Medium);
                }
            }
            message = packet.Assign ? "Companion assigned to the Work Cart." : "Companion removed from the Work Cart.";
        }
        else
        {
            message = refusal;
        }

        packRepository.Save();
        SendFoxWorkCartState(fromPlayer, cart, message);
    }

    private void SendFoxWorkCartState(IServerPlayer owner, FoxWorkCartRecord cart, string message)
    {
        if (serverChannel == null || packRepository == null)
        {
            return;
        }

        List<FoxBedCandidatePacket> candidates = packRepository.GetRecordsForOwner(owner.PlayerUID)
            .Where(record => !string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase))
            .Select(record => new FoxBedCandidatePacket
            {
                FoxId = record.FoxId,
                Number = record.Number,
                Name = record.Name,
                Status = record.Status,
                AssignedHere = cart.AssignedFoxIds.Contains(record.FoxId, StringComparer.Ordinal),
                AssignedElsewhere = record.HasHome
                    && (!string.Equals(record.HomeType, "workcart", StringComparison.OrdinalIgnoreCase)
                        || record.HomeX != cart.X || record.HomeY != cart.Y || record.HomeZ != cart.Z
                        || record.HomeDimension != cart.Dimension),
                SpeciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId,
                SpeciesDisplayName = GetCompanionSpeciesDisplayName(record)
            })
            .ToList();

        serverChannel.SendPacket(new FoxWorkCartAssignmentStatePacket
        {
            X = cart.X,
            Y = cart.Y,
            Z = cart.Z,
            Dimension = cart.Dimension,
            Radius = GetOwnerWorkCartRadius(cart.OwnerUid),
            AssignedCount = cart.AssignedFoxIds.Count,
            WorkersAtCartCount = CountWorkCartFoxesAtCart(cart),
            Candidates = candidates,
            Message = message,
            LoggingEnabled = cart.LoggingEnabled,
            WorkCartKind = string.IsNullOrWhiteSpace(cart.Kind) ? "generic" : cart.Kind
        }, owner);
    }

    private void SendFoxBedState(IServerPlayer owner, FoxBedRecord bed, string message)
    {
        if (serverChannel == null || packRepository == null)
        {
            return;
        }

        List<FoxBedCandidatePacket> candidates = packRepository.GetRecordsForOwner(owner.PlayerUID)
            .Where(record => !string.Equals(record.Status, "Dead", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(record.Status, "Unowned", StringComparison.OrdinalIgnoreCase))
            .Select(record => new FoxBedCandidatePacket
            {
                FoxId = record.FoxId,
                Number = record.Number,
                Name = record.Name,
                Status = record.Status,
                AssignedHere = string.Equals(bed.FoxId, record.FoxId, StringComparison.Ordinal),
                AssignedElsewhere = record.HasHome
                    && !(record.HomeX == bed.X
                        && record.HomeY == bed.Y
                        && record.HomeZ == bed.Z
                        && record.HomeDimension == bed.Dimension),
                SpeciesId = string.IsNullOrWhiteSpace(record.SpeciesId) ? "fox" : record.SpeciesId,
                SpeciesDisplayName = GetCompanionSpeciesDisplayName(record)
            })
            .ToList();

        if (brambleRepository?.TryGet(owner.PlayerUID, out BramblePlayerRecord bramble) == true
            && !bramble.Dismissed)
        {
            candidates.Insert(0, new FoxBedCandidatePacket
            {
                Name = "Bramble",
                Status = "Guide companion",
                AssignedHere = string.Equals(bed.BrambleOwnerUid, owner.PlayerUID, StringComparison.Ordinal),
                AssignedElsewhere = !string.IsNullOrWhiteSpace(bramble.HomeType)
                    && !string.Equals(bramble.HomeType, BrambleBedHomeType, StringComparison.Ordinal),
                SpeciesId = bramble.SpeciesId,
                SpeciesDisplayName = GetBrambleSpeciesDisplayName(bramble.SpeciesId),
                IsBramble = true
            });
        }

        serverChannel.SendPacket(new FoxBedAssignmentStatePacket
        {
            X = bed.X,
            Y = bed.Y,
            Z = bed.Z,
            Dimension = bed.Dimension,
            AssignedFoxId = bed.FoxId,
            Candidates = candidates,
            Message = message,
            AssignedBramble = string.Equals(bed.BrambleOwnerUid, owner.PlayerUID, StringComparison.Ordinal)
        }, owner);
    }

    private void OnTrainingDummyState(TrainingDummyStatePacket packet)
    {
        clientApi?.Event.EnqueueMainThreadTask(
            () =>
            {
                if (trainingDummyDialog == null || !trainingDummyDialog.IsOpened())
                {
                    TryOpenTrainingDummyGui(packet);
                }
                else
                {
                    trainingDummyDialog.ApplyState(packet);
                }
            },
            "feralkinshipcompanions-update-training-dummy"
        );
    }

    private void OnTrainingDummyAction(IServerPlayer fromPlayer, TrainingDummyActionPacket packet)
    {
        if (serverApi == null || packet.Action != TrainingDummyAction.Delete)
        {
            return;
        }

        Entity? entity = serverApi.World.GetEntityById(packet.TargetEntityId);
        if (entity is not EntityFeralKinshipTrainingDummy dummy
            || fromPlayer.Entity == null
            || !dummy.Alive
            || fromPlayer.Entity.Pos.SquareDistanceTo(dummy.Pos) > 64)
        {
            return;
        }

        if (!serverApi.World.Claims.TryAccess(
                fromPlayer,
                dummy.Pos.AsBlockPos,
                EnumBlockAccessFlags.Use))
        {
            return;
        }

        dummy.Die(EnumDespawnReason.Removed);
    }

    private void SendTrainingDummyState(
        EntityFeralKinshipTrainingDummy dummy,
        IServerPlayer player)
    {
        EntityBehaviorHealth? health = dummy.GetBehavior<EntityBehaviorHealth>();
        serverChannel?.SendPacket(
            new TrainingDummyStatePacket
            {
                TargetEntityId = dummy.EntityId,
                CurrentHealth = health?.Health ?? 0f,
                MaxHealth = health?.MaxHealth ?? 0f,
                LastAttacker = dummy.WatchedAttributes.GetString(
                    EntityFeralKinshipTrainingDummy.LastAttackerKey,
                    "None"
                ),
                LastDamage = dummy.WatchedAttributes.GetFloat(
                    EntityFeralKinshipTrainingDummy.LastDamageKey,
                    0f
                ),
                LastDamageTier = dummy.WatchedAttributes.GetInt(
                    EntityFeralKinshipTrainingDummy.LastDamageTierKey,
                    0
                ),
                LastDamageType = dummy.WatchedAttributes.GetString(
                    EntityFeralKinshipTrainingDummy.LastDamageTypeKey,
                    "None"
                ),
                HitCount = dummy.WatchedAttributes.GetInt(
                    EntityFeralKinshipTrainingDummy.HitCountKey,
                    0
                )
            },
            player
        );
    }

    private void OnPlayerInteractEntity(
        Entity entity,
        IPlayer byPlayer,
        ItemSlot slot,
        Vec3d hitPosition,
        int mode,
        ref EnumHandling handling)
    {
        if (mode == (int)EnumInteractMode.Interact
            && entity is EntityAgent
            && IsGuideFox(entity)
            && byPlayer is IServerPlayer guidePlayer)
        {
            handling = EnumHandling.PreventSubsequent;
            if (!BrambleEnabled)
            {
                if (entity.Alive) entity.Die(EnumDespawnReason.Removed);
                return;
            }
            if (slot?.Empty == true && IsOwner(entity, guidePlayer.PlayerUID))
            {
                if (guidePlayer.Entity.Controls.Sneak)
                {
                    ToggleGuideActivity(entity, guidePlayer);
                }
            }
            else if (IsOwner(entity, guidePlayer.PlayerUID))
            {
                guidePlayer.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Bramble does not need the held item. Use an empty hand for help, or sneak-interact to toggle Follow and Stay.",
                    EnumChatType.Notification
                );
            }
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && entity is EntityAgent
            && byPlayer is IServerPlayer backpackOwner
            && backpackOwner.Entity.Controls.Sneak
            && IsTamedFox(entity)
            && !IsCompanionJuvenile(entity)
            && IsOwner(entity, backpackOwner.PlayerUID))
        {
            // The client sends the explicit open request. This server-side
            // interception prevents the same crouch interaction from also
            // feeding, satisfying a request, or opening a source-mod UI.
            handling = EnumHandling.PreventSubsequent;
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && entity is EntityFeralKinshipTrainingDummy dummy
            && byPlayer is IServerPlayer serverPlayer)
        {
            handling = EnumHandling.PreventSubsequent;
            SendTrainingDummyState(dummy, serverPlayer);
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && (entity is EntityFeralKinshipAttackDummy
                || entity is EntityFeralKinshipWeatheredHideDummy)
            && byPlayer is IServerPlayer attackDummyPlayer
            && slot?.Empty == true)
        {
            handling = EnumHandling.PreventSubsequent;
            if (entity.Alive
                && attackDummyPlayer.Entity != null
                && attackDummyPlayer.Entity.Pos.SquareDistanceTo(entity.Pos) <= 64
                && serverApi != null
                && serverApi.World.Claims.TryAccess(
                    attackDummyPlayer,
                    entity.Pos.AsBlockPos,
                    EnumBlockAccessFlags.Use
                ))
            {
                entity.Die(EnumDespawnReason.Removed);
            }

            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && slot != null
            && IsCompanionDeveloperTool(slot)
            && byPlayer is IServerPlayer developerPlayer)
        {
            handling = EnumHandling.PreventSubsequent;
            if (!CanUseDeveloperTools(developerPlayer))
            {
                developerPlayer.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "The Companion Developer Ledger is only available in Creative mode or to server administrators.",
                    EnumChatType.Notification
                );
                return;
            }
            if (!entity.Alive || !IsTamedFox(entity) || !IsOwner(entity, developerPlayer.PlayerUID))
            {
                developerPlayer.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    "Use the Companion Developer Ledger on one of your living companions.",
                    EnumChatType.Notification
                );
                return;
            }

            serverChannel?.SendPacket(
                new CompanionDeveloperOpenPacket { TargetEntityId = entity.EntityId },
                developerPlayer
            );
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && slot != null
            && IsFoxBonusInspector(slot)
            && byPlayer is IServerPlayer inspectorPlayer)
        {
            handling = EnumHandling.PreventSubsequent;
            InspectFoxBonuses(entity, inspectorPlayer);
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && byPlayer is IServerPlayer requestOwner
            && slot != null
            && TryDeliverRequestedItem(entity, requestOwner, slot))
        {
            handling = EnumHandling.PreventSubsequent;
            return;
        }

        if (mode == (int)EnumInteractMode.Interact
            && byPlayer is IServerPlayer feedingOwner
            && slot != null)
        {
            if (TryHandleDirectHandFeed(entity, feedingOwner, slot))
            {
                handling = EnumHandling.PreventSubsequent;
                return;
            }
        }

        if (mode == (int)EnumInteractMode.Interact
            && byPlayer is IServerPlayer thoughtOwner
            && slot?.Empty == true
            && IsDialogueCompanionPresent(entity)
            && !IsFoxIncapacitated(entity)
            && IsOwner(entity, thoughtOwner.PlayerUID))
        {
            string eventId = GetMoodDialogueEvent(GetMood(entity), true) ?? "thought.neutral.rightclick";
            EmitDialogueEvent(entity, thoughtOwner, eventId, string.Empty, CompanionDialoguePriority.Low);
        }
    }

    private bool TryDeliverRequestedItem(Entity entity, IServerPlayer owner, ItemSlot slot)
    {
        if (!IsTamedFox(entity)
            || IsFoxIncapacitated(entity)
            || !IsOwner(entity, owner.PlayerUID)
            || slot?.Itemstack == null)
        {
            return false;
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        string activeRequest = FoxRequestCatalog.NormalizeId(
            status.GetString(ActiveRequestKey, string.Empty)
        );
        long expiresUtcMs = status.GetLong(RequestExpiresUtcMsKey, 0);
        if (expiresUtcMs > 0 && expiresUtcMs <= UtcNowMs())
        {
            ClearActiveRequest(status);
            MarkSocialStateDirty(entity);
            RegisterFoxInPack(entity);
            return false;
        }
        FoxRequestDeliveryMode deliveryMode = FoxRequestCatalog.GetDeliveryMode(activeRequest);
        if (deliveryMode is not (FoxRequestDeliveryMode.ConsumeHeldItem or FoxRequestDeliveryMode.ShowHeldItem))
        {
            return false;
        }

        string requestedCode = status.GetString(RequestedItemCodeKey, string.Empty);
        if (!IsMatchingItem(slot.Itemstack, requestedCode))
        {
            return false;
        }

        if (deliveryMode == FoxRequestDeliveryMode.ConsumeHeldItem
            && owner.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        CompleteRequest(entity, status, activeRequest);
        return true;
    }

    private void OnPlayerDidPlaceBlock(
        IServerPlayer byPlayer,
        int oldBlockId,
        BlockSelection blockSelection,
        ItemStack withItemStack)
    {
        if (blockSelection?.Position == null || withItemStack?.Collectible?.Code == null)
        {
            return;
        }

        string placedCode = withItemStack.Collectible.Code.ToShortString();
        BlockPos placedPosition = blockSelection.Position;
        AssetLocation placedAsset = withItemStack.Collectible.Code;
        if (packRepository?.Loaded == true
            && string.Equals(placedAsset.Domain, "feralkinshipcompanions", StringComparison.Ordinal))
        {
            if (placedAsset.Path.StartsWith(WorkCartBlockPathPrefix, StringComparison.Ordinal))
            {
                packRepository.RegisterWorkCart(
                    byPlayer.PlayerUID,
                    placedPosition,
                    GetWorkCartKind(placedAsset));
                packRepository.Save();
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Work Cart placed. Right-click it to assign companions as its residents.",
                    EnumChatType.Notification);
            }
            else if (string.Equals(placedAsset.Path, PackCairnBlockPath, StringComparison.Ordinal)
                || placedAsset.Path.StartsWith(PackCartBlockPathPrefix, StringComparison.Ordinal))
            {
                if (RegisterPlacedPackCartOwner(byPlayer, placedPosition))
                {
                    byPlayer.SendMessage(
                        GlobalConstants.GeneralChatGroup,
                        "Pack Cart placed. This is now your active expedition departure and return point.",
                        EnumChatType.Notification
                    );
                }
            }
            else if (placedAsset.Path.StartsWith(FoxBedBlockPathPrefix, StringComparison.Ordinal))
            {
                if (packRepository.GetBed(placedPosition) is FoxBedRecord previousBed
                    && !string.IsNullOrWhiteSpace(previousBed.BrambleOwnerUid))
                {
                    ClearBrambleBedAssignment(previousBed.BrambleOwnerUid);
                }
                packRepository.RegisterBed(byPlayer.PlayerUID, placedPosition, placedAsset.ToShortString());
                packRepository.Save();
            }
            else if (placedAsset.Path.StartsWith(PackAmenityBlockPathPrefix, StringComparison.Ordinal))
            {
                string kind = placedAsset.Path[PackAmenityBlockPathPrefix.Length..];
                packRepository.RegisterAmenity(byPlayer.PlayerUID, placedPosition, kind);
                packRepository.Save();
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Communal pack space placed. Any companion in your pack may use it.", EnumChatType.Notification);
            }
            else if (placedAsset.Path.StartsWith(PackStorageBlockPathPrefix, StringComparison.Ordinal))
            {
                packRepository.RegisterAmenity(byPlayer.PlayerUID, placedPosition, "storage");
                packRepository.Save();
                byPlayer.SendMessage(GlobalConstants.GeneralChatGroup,
                    "Pack Collection Box placed. Interested companions may carry nearby dropped items here.", EnumChatType.Notification);
            }
        }

        double invalidationRangeSquared = (ObjectProximityDistance + 2f) * (ObjectProximityDistance + 2f);
        // A placed request block must not repopulate an invalidated entity cache
        // from a shared snapshot taken before that placement.
        sharedEnvironmentSnapshots.Clear();
        foreach (Entity fox in loadedFoxes.Values)
        {
            if (fox.Pos.Dimension != placedPosition.dimension)
            {
                continue;
            }

            double dx = fox.Pos.X - (placedPosition.X + 0.5);
            double dy = fox.Pos.Y - (placedPosition.Y + 0.5);
            double dz = fox.Pos.Z - (placedPosition.Z + 0.5);
            if (dx * dx + dy * dy + dz * dz <= invalidationRangeSquared)
            {
                environmentSnapshots.Remove(fox.EntityId);
            }
        }

        Entity? nearestFox = loadedFoxes.Values
            .Where(fox => IsTamedFox(fox)
                && IsOwner(fox, byPlayer.PlayerUID)
                && fox.Pos.Dimension == placedPosition.dimension
                && FoxRequestCatalog.GetDeliveryMode(
                    GetDomesticationStatus(fox)?.GetString(ActiveRequestKey, string.Empty) ?? string.Empty
                ) == FoxRequestDeliveryMode.PlaceBlock
                && string.Equals(
                    GetDomesticationStatus(fox)?.GetString(RequestedItemCodeKey, string.Empty),
                    placedCode,
                    StringComparison.OrdinalIgnoreCase
                ))
            .Where(fox =>
            {
                double dx = fox.Pos.X - (placedPosition.X + 0.5);
                double dy = fox.Pos.Y - (placedPosition.Y + 0.5);
                double dz = fox.Pos.Z - (placedPosition.Z + 0.5);
                return dx * dx + dy * dy + dz * dz <= ObjectProximityDistance * ObjectProximityDistance;
            })
            .OrderBy(fox =>
            {
                double dx = fox.Pos.X - (placedPosition.X + 0.5);
                double dy = fox.Pos.Y - (placedPosition.Y + 0.5);
                double dz = fox.Pos.Z - (placedPosition.Z + 0.5);
                return dx * dx + dy * dy + dz * dz;
            })
            .FirstOrDefault();

        if (nearestFox == null)
        {
            return;
        }

        ITreeAttribute status = GetDomesticationStatus(nearestFox, true)!;
        CompleteRequest(nearestFox, status, status.GetString(ActiveRequestKey, string.Empty));
    }

    private static bool IsFoxBonusInspector(ItemSlot slot)
    {
        AssetLocation? code = slot?.Itemstack?.Collectible?.Code;
        return code != null
            && string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.OrdinalIgnoreCase)
            && string.Equals(code.Path, "foxbonusinspector", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCompanionDeveloperTool(ItemSlot slot)
    {
        AssetLocation? code = slot?.Itemstack?.Collectible?.Code;
        return code != null
            && string.Equals(code.Domain, "feralkinshipcompanions", StringComparison.OrdinalIgnoreCase)
            && string.Equals(code.Path, "companiondevledger", StringComparison.OrdinalIgnoreCase);
    }

    private void InspectFoxBonuses(Entity entity, IServerPlayer player)
    {
        if (!IsTamedFox(entity))
        {
            player.SendMessage(
                GlobalConstants.GeneralChatGroup,
                    "This diagnostic tool only reads tamed Feral Kinship companions.",
                EnumChatType.Notification
            );
            return;
        }

        string owner = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        if (!string.Equals(owner, player.PlayerUID, StringComparison.Ordinal))
        {
            player.SendMessage(
                GlobalConstants.GeneralChatGroup,
                "Only the companion's owner can inspect its bonuses.",
                EnumChatType.Notification
            );
            return;
        }

        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        float currentHealth = health?.Health ?? 0f;
        float maxHealth = health?.MaxHealth ?? FoxBaseMaxHealth;
        float baseMaxHealth = Math.Max(0f, maxHealth - GetFoxMaxHealthBonus(entity));
        CompanionSpeciesProfile species = GetCompanionSpecies(entity);

        // This is intentionally an effective-stat report. It does not expose
        // talent names, so it remains useful when the perk storage changes.
        float damageBonusPercent = GetFoxDamageBonusPercent(entity);
        float movementBonusPercent = (GetFoxMovementSpeedMultiplier(entity) - 1f) * 100f;
        float attackSpeedBonusPercent = (GetFoxAttackSpeedMultiplier(entity) - 1f) * 100f;
        float armorBonus = GetFoxArmorBonus(entity);
        float damageResistancePercent = (GetFoxDamageResistance(entity) + GetFoxNaturalDamageResistance(entity)) * 100f;
        float dodgeChancePercent = GetFoxDodgeChance(entity) * 100f;
        float requestCooldownReduction = PlayerRequestCooldownSeconds - GetFoxRequestCooldownSeconds(entity);
        float cancelCooldownReduction = PlayerCancelCooldownSeconds - GetFoxCancelCooldownSeconds(entity);
        ITreeAttribute? status = GetDomesticationStatus(entity);
        float freshMeatRemaining = GetDeadlineRemainingSeconds(status, FreshMeatEndsUtcMsKey);
        float lastStandCooldownRemaining = GetDeadlineRemainingSeconds(status, LastStandCooldownEndsUtcMsKey);
        string freshMeatStatus = GetFoxPerkRank(entity, "fresh-meat") <= 0
            ? "not learned"
            : freshMeatRemaining > 0f ? $"active for {FormatCooldown(freshMeatRemaining)}" : "inactive";
        string lastStandStatus = GetFoxPerkRank(entity, "last-stand") <= 0
            ? "not learned"
            : lastStandCooldownRemaining > 0f ? $"cooldown {FormatCooldown(lastStandCooldownRemaining)}" : "ready";
        int deathlessRank = GetFoxPerkRank(entity, "self-stabilizing");
        double deathlessRecoveryAt = status?.GetDouble(DeathlessRecoveryAtHoursKey, -1d) ?? -1d;
        double deathlessFallbackAt = status?.GetDouble(DeathlessFallbackAtHoursKey, -1d) ?? -1d;
        string deathlessStatus = deathlessRank <= 0
            ? "not learned"
            : !IsFoxMortallyWounded(entity) || deathlessRecoveryAt < 0d
                ? "rolls when mortally wounded"
                : $"{Math.Max(0d, deathlessRecoveryAt - entity.World.Calendar.TotalHours):0.#} random / {Math.Max(0d, deathlessFallbackAt - entity.World.Calendar.TotalHours):0.#} fallback hours remaining";
        string[] report =
        {
            $"{species.DisplayName} companion bonus report #{GetDomesticationStatus(entity)?.GetInt(NumberKey, 0) ?? 0}",
            $"Maximum health bonus: +{Math.Max(0f, maxHealth - baseMaxHealth):0.###} (effective {maxHealth:0.###})",
            $"Health regeneration bonus: +{(GetFoxMoodRegenMultiplier(entity) - 1f) * 100f:0.###}%",
            $"Damage bonus: +{damageBonusPercent:0.###}% (effective attack {species.BaseMeleeDamage * GetFoxDamageMultiplier(entity):0.###})",
            $"Armor bonus: +{armorBonus:0.###} damage reduction",
            $"Movement speed bonus: +{movementBonusPercent:0.###}%",
            $"Attack speed bonus: +{attackSpeedBonusPercent:0.###}%",
            $"Fresh Meat: {freshMeatStatus}",
            $"Refuse to Die: {lastStandStatus}",
            $"Dodge chance: +{dodgeChancePercent:0.###}%",
            $"Natural damage resistance: +{damageResistancePercent:0.###}%",
            $"Mortally wounded rescue window: {GetFoxRescueWindowHours(entity):0.#} in-game hours",
            $"Deathless: {deathlessStatus}",
            $"Request cooldown reduction: {requestCooldownReduction:0.###} seconds",
            $"Cancellation cooldown reduction: {cancelCooldownReduction:0.###} seconds",
            $"Pack point contribution: +{1 + GetFoxPerkRank(entity, "pack-contributor")} per normal request",
            "Animal loot bonus: +0%",
            "Vessel drop bonus: +0%",
            "Mining bonus: +0%",
            "Farming bonus: +0%",
            $"Mouthful stack carrying: {(GetFoxPerkRank(entity, "mouthful") > 0 ? "learned" : "not learned")}",
            $"Foxfire: rank {GetFoxPerkRank(entity, "lantern-fox")}/2",
            $"Current health: {currentHealth:0.###} / {maxHealth:0.###}"
        };

        foreach (string line in report)
        {
            player.SendMessage(
                GlobalConstants.GeneralChatGroup,
                line,
                EnumChatType.Notification
            );
        }
    }

    private void EnsureFoxNumberIfNeeded(Entity entity)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        int existingNumber = status.GetInt(NumberKey, 0);
        if (existingNumber != 0)
        {
            AdvanceNextFoxNumber(existingNumber);
            if (IsTamedFox(entity))
            {
                RegisterFoxInPack(entity);
            }
            return;
        }

        int number = AllocateNextFoxNumber();
        status.SetInt(NumberKey, number);
        MarkSocialStateDirty(entity);

        if (IsTamedFox(entity))
        {
            RegisterFoxInPack(entity);
        }

    }

    private int AllocateNextFoxNumber()
    {
        if (packRepository?.Loaded == true)
        {
            return packRepository.AllocateNumber();
        }

        int highestKnownNumber = 0;
        foreach (Entity loadedFox in loadedFoxes.Values)
        {
            highestKnownNumber = Math.Max(
                highestKnownNumber,
                GetDomesticationStatus(loadedFox)?.GetInt(NumberKey, 0) ?? 0
            );
        }

        return highestKnownNumber + 1;
    }

    private void AdvanceNextFoxNumber(int existingNumber)
    {
        packRepository?.ObserveNumber(existingNumber);
    }

    private static void EnsureFoxPersonality(Entity entity)
    {
        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        if (!string.IsNullOrEmpty(status.GetString(PersonalityKey)))
        {
            return;
        }

        status.SetString(PersonalityKey, FoxPersonalities[entity.World.Rand.Next(FoxPersonalities.Length)]);
        MarkSocialStateDirty(entity);
    }

    private static void ApplyFoxDerivedStats(Entity entity)
    {
        EntityBehaviorHealth? health = entity.GetBehavior<EntityBehaviorHealth>();
        if (health == null)
        {
            return;
        }

        bool juvenile = IsCompanionJuvenile(entity);
        float oldMaxHealth = health.MaxHealth;
        float oldHealth = health.Health;
        float healthFraction = oldMaxHealth > 0f
            ? Math.Clamp(oldHealth / oldMaxHealth, 0f, 1f)
            : 1f;
        bool baseHealthChanged = false;
        bool isFotsa = ReferenceEquals(GetCompanionSpecies(entity), CompanionSpeciesCatalog.TamablesFotsa);
        if (!juvenile && isFotsa && TryResolveFotsaStat(entity, true, out float fotsaMaxHealth))
        {
            if (Math.Abs(health.BaseMaxHealth - fotsaMaxHealth) > 0.001f)
            {
                health.BaseMaxHealth = fotsaMaxHealth;
                baseHealthChanged = true;
            }
        }
        else if (!juvenile && !isFotsa && health.BaseMaxHealth < FoxBaseMaxHealth)
        {
            health.BaseMaxHealth = FoxBaseMaxHealth;
            baseHealthChanged = true;
        }

        health.SetMaxHealthModifiers(
            "feralkinshipcompanions:talent-health",
            juvenile
                ? health.BaseMaxHealth * GetFoxPackTalentRank(entity, "healthy-lineage") * PackTalentPercent
                : GetFoxMaxHealthBonus(entity)
        );
        if (health.MaxHealth > 0f && Math.Abs(health.MaxHealth - oldMaxHealth) > 0.001f)
        {
            health.Health = Math.Clamp(health.MaxHealth * healthFraction, 0f, health.MaxHealth);
            health.MarkDirty();
        }

        ITreeAttribute status = GetDomesticationStatus(entity, true)!;
        float baseRegenSpeed = status.GetFloat(MoodBaseRegenSpeedKey, -1f);
        if (baseRegenSpeed < 0f)
        {
            baseRegenSpeed = entity.WatchedAttributes.GetFloat("regenSpeed", 1f);
            status.SetFloat(MoodBaseRegenSpeedKey, baseRegenSpeed);
        }

        float desiredRegenSpeed = IsFoxMortallyWounded(entity)
            ? 0f
            : baseRegenSpeed * GetFoxMoodRegenMultiplier(entity);
        float currentRegenSpeed = entity.WatchedAttributes.GetFloat("regenSpeed", 1f);
        if (Math.Abs(currentRegenSpeed - desiredRegenSpeed) > 0.001f)
        {
            entity.WatchedAttributes.SetFloat("regenSpeed", desiredRegenSpeed);
            entity.WatchedAttributes.MarkPathDirty("regenSpeed");
        }
        if (baseHealthChanged)
        {
            health.MarkDirty();
        }
    }

    private static bool IsOwner(Entity entity, string playerUid)
    {
        return string.Equals(GetDomesticationStatus(entity)?.GetString("owner"), playerUid, StringComparison.Ordinal);
    }

    private Entity? GetOwnerEntity(Entity entity)
    {
        string ownerId = GetDomesticationStatus(entity)?.GetString("owner") ?? string.Empty;
        return serverApi?.World.PlayerByUid(ownerId)?.Entity;
    }

    internal static ITreeAttribute? GetDomesticationStatus(Entity entity, bool create = false)
    {
        ITreeAttribute? status = entity.WatchedAttributes.GetTreeAttribute(DomesticationStatusPath);
        if (status == null && create)
        {
            status = new TreeAttribute();
            entity.WatchedAttributes.SetAttribute(DomesticationStatusPath, status);
        }

        return status;
    }

    private static void MarkSocialStateDirty(Entity entity)
    {
        entity.WatchedAttributes.MarkPathDirty(DomesticationStatusPath);
    }

    private static string GetPersonalityLabel(string personality)
    {
        return personality switch
        {
            "timid" => "Timid",
            "bold" => "Bold",
            "curious" => "Curious",
            "affectionate" => "Affectionate",
            "independent" => "Independent",
            "playful" => "Playful",
            "restless" => "Restless",
            "homebody" => "Homebody",
            "social" => "Social",
            "solitary" => "Solitary",
            "protective" => "Protective",
            "territorial" => "Territorial",
            "greedy" => "Greedy",
            "demanding" => "Demanding",
            "stubborn" => "Stubborn",
            "skittish" => "Skittish",
            _ => "Unassigned"
        };
    }

    private static string GetMoodLabel(string mood)
    {
        return mood switch
        {
            "calm" => "Calm",
            "content" => "Content",
            "curious" => "Curious",
            "restless" => "Restless",
            "playful" => "Playful",
            "sleepy" => "Sleepy",
            "alert" => "Alert",
            "anxious" => "Anxious",
            "resting" => "Resting",
            "relieved" => "Relieved",
            "refreshed" => "Refreshed",
            "social" => "Social",
            "happy" => "Happy",
            "recovered" => "Recovered",
            "rested" => "Rested",
            "alarmed" => "Alarmed",
            "rallied" => "Rallied",
            _ => "Unassigned"
        };
    }

    private static string GetMoodDisplay(Entity entity)
    {
        string label = GetMoodLabel(GetMood(entity));
        return IsForcedMood(entity) ? $"{label} (f)" : label;
    }

    private string GetRequestLabel(Entity entity, string request, string personality)
    {
        string normalized = FoxRequestCatalog.NormalizeId(request);
        string authoringRequestId = string.Equals(request, FoxRequestType.WalkDistance, StringComparison.OrdinalIgnoreCase)
            ? FoxRequestType.WalkDistance
            : normalized;
        string eventId = CompanionDialogueEvent.RequestPrefix + authoringRequestId;
        CompanionDialogueContext context = CreateDialogueContext(entity, eventId);
        string label = dialogueService?.Resolve(
            eventId,
            FoxPersonalityText.Request(normalized, personality),
            context) ?? FoxPersonalityText.Request(normalized, personality);
        string predatorClue = GetDomesticationStatus(entity)?.GetString(PredatorTargetClueKey, string.Empty) ?? string.Empty;
        return normalized == FoxRequestType.Predator && !string.IsNullOrWhiteSpace(predatorClue)
            ? $"{label} {predatorClue}"
            : label;
    }

    private string GetCurrentThoughtText(Entity entity, ITreeAttribute? status)
    {
        string personality = status?.GetString(PersonalityKey) ?? string.Empty;
        string mood = status == null ? string.Empty : GetMoodDisplay(entity);
        string fallback = FoxPersonalityText.Thought(personality, mood);
        CompanionDialogueContext context = CreateDialogueContext(entity, CompanionDialogueEvent.ThoughtMood);
        return dialogueService?.Resolve(CompanionDialogueEvent.ThoughtMood, fallback, context) ?? fallback;
    }

    private static string Capitalize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

}

internal static class FoxRequestType
{
    public const string Predator = "predator";
    public const string StayClose = "stayclose";
    public const string StayAway = "stayaway";
    public const string Inside = "inside";
    public const string NearOwnerStill = "nearownerstill";
    public const string TravelDistance = "traveldistance";
    public const string WalkDistance = "walkdistance";
    public const string HigherGround = "higherground";
    public const string NearWater = "nearwater";
    public const string NearLight = "nearlight";
    public const string NearHeat = "nearheat";
    public const string Shelter = "shelter";
    public const string OutsideClear = "outsideclear";
    public const string NearTamedAnimal = "neartamedanimal";
    public const string LargeTree = "largetree";
    public const string CropField = "cropfield";
    public const string Trader = "trader";
    public const string MechanicalDevice = "mechanicaldevice";
    public const string AnotherAnimal = "anotheranimal";
    public const string RegularFood = "regularfood";
    public const string LuxuryFood = "luxuryfood";
    public const string NonFoodConsumed = "nonfoodconsumed";
    public const string NonFoodNearby = "nonfoodnearby";
    public const string HealingItem = "healingitem";
    public const string PlaceableNearby = "placeablenearby";
    public const string OutsideUntilMorning = "outsideuntilmorning";
    public const string InsideUntilMorning = "insideuntilmorning";
}

internal static class FoxSocialRequestAction
{
    public const int Open = 0;
    public const int Generate = 1;
    public const int Cancel = 2;
    public const int OpenPack = 3;
    public const int GenerateDeveloper = 4;
    public const int SetMoodDeveloper = 5;
    public const int ResetCooldownsDeveloper = 6;
    public const int CancelDeveloper = 7;
    public const int AddFoxPointDeveloper = 8;
    public const int RemoveFoxPointDeveloper = 9;
    public const int AddPackPointDeveloper = 10;
    public const int RemovePackPointDeveloper = 11;
    public const int CloseView = 12;
    public const int ClosePack = 13;
    public const int OpenPerks = 16;
    public const int BuyPerk = 17;
    public const int ResetPerks = 18;
    public const int ClosePerks = 19;
    public const int SetHealthOneDeveloper = 20;
    public const int RebuildFoxDeveloper = 46;
    public const int ClaimPackLoot = 21;
    public const int AddPackLootDeveloper = 22;
    public const int StartPackExpedition = 23;
    public const int ArchivePackFox = 24;
    public const int ForceFoxMiaDeveloper = 25;
    public const int ClaimRecruitment = 26;
    public const int LocatePackFox = 27;
    public const int UnlockExpeditionRoute = 28;
    public const int ThreatSurvey = 29;
    public const int SendPackLootToStorage = 30;
    public const int RetrieveHeldItem = 31;
    public const int UnlockPackTalent = 48;
    public const int SetActivity = 32;
    public const int SetFollowDistance = 33;
    public const int SetCombatStyle = 34;
    public const int SetRiskTolerance = 35;
    public const int ToggleBreeding = 36;
    public const int UnarchivePackFox = 37;
    public const int Rename = 38;
    public const int RequestNameSuggestion = 47;
    public const int SetGroundCleanup = 39;
    public const int SetDutySubtask = 45;
    public const int SetMowLawn = 40;
    public const int SetFinishedProducts = 41;
    public const int SetFlowerRemoval = 42;
    public const int SetSnowShoveling = 43;
    public const int SetSnowballCollection = 44;
    public const int SetCharcoalShoveling = 55;
    public const int SetGeneralStorageSorting = 49;
    public const int TellBadJokeDeveloper = 50;
    public const int DeleteAllCompanionsDeveloper = 51;
    public const int DeleteExpeditionReport = 52;
    public const int AddExperienceDeveloper = 53;
    public const int AdvanceLevelDeveloper = 54;
    public const int AbandonScavengeSite = 56;
    public const int ClaimSelectedPackLoot = 57;
    public const int CallPackFoxToCart = 58;
}

internal static class FoxExpeditionType
{
    public const string Hunt = "hunt";
    public const string Scavenge = "scavenge";
    public const string Scout = "scout";
    public const string Forage = "forage";
    public const string SearchLost = "searchlost";
    public const string Recruitment = "recruitment";
    public const string DistantForage = "distantforage";
    public const string PackPatrol = "packpatrol";
    public const string GreatHunt = "greathunt";
    public const string RuinDelve = "ruindelve";
    public const string PrimevalReach = "primevalreach";
    public const string ApexHunt = "apexhunt";
    public const string ResonantDepths = "resonantdepths";
    public const string DeepWilds = "deepwilds";
}

internal static class CompanionRecoveryStatus
{
    public const string Unknown = "unknown";
    public const string Recoverable = "recoverable";
    public const string Unrecoverable = "unrecoverable";
}

internal static class TrainingDummyAction
{
    public const int Delete = 1;
}

internal static class CompanionSoundCue
{
    public const string Rejected = "rejected";
    public const string TalentPurchased = "talent-purchased";
    public const string TalentReset = "talent-reset";
    public const string RecruitmentSuccess = "recruitment-success";
    public const string RecruitmentFailure = "recruitment-failure";
    public const string MortalWarning = "mortal-warning";
    public const string RecoveryStarted = "recovery-started";
    public const string RecoveryComplete = "recovery-complete";
    public const string CargoComplete = "cargo-complete";
}

internal static class CompanionWhistleCommand
{
    public const string ActivityCategory = "activity";
    public const string CombatCategory = "combat";
    public const string RiskCategory = "risk";
    public const string TargetCategory = "target";
    public const string DutyCategory = "duty";
    public const string UtilityCategory = "utility";
    public const string DropHeldItemsCommand = "drop-held-items";

    public static bool IsValid(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        string normalized = Normalize(command);
        string category = Category(normalized);
        string value = Value(normalized);
        return category switch
        {
            ActivityCategory => CompanionActivityMode.IsValid(value),
            CombatCategory => CompanionCombatStyle.IsValid(value),
            RiskCategory => CompanionRiskTolerance.IsValid(value),
            TargetCategory => string.Equals(value, "attack", StringComparison.Ordinal),
            DutyCategory => value is "cleanup" or "mow" or "finished" or "flowers" or "snow" or "storagesorting" or "all" or "none"
                || IsCustomDutyValue(value),
            UtilityCategory => value == DropHeldItemsCommand,
            _ => false
        };
    }

    public static bool IsDropHeldItems(string? command)
    {
        string normalized = Normalize(command ?? string.Empty);
        return Category(normalized) == UtilityCategory && Value(normalized) == DropHeldItemsCommand;
    }

    public static bool IsDuty(string? command)
    {
        string normalized = Normalize(command ?? string.Empty);
        return Category(normalized) == DutyCategory;
    }

    public static bool IsTargetAttack(string? command)
    {
        string normalized = Normalize(command ?? string.Empty);
        return Category(normalized) == TargetCategory && Value(normalized) == "attack";
    }

    public static string Normalize(string command)
    {
        string[] parts = command.Trim().Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            ? $"{parts[0].Trim().ToLowerInvariant()}:{parts[1].Trim().ToLowerInvariant()}"
            : string.Empty;
    }

    public static string Category(string command)
    {
        int separator = command.IndexOf(':');
        return separator > 0 ? command[..separator] : string.Empty;
    }

    public static string Value(string command)
    {
        int separator = command.IndexOf(':');
        return separator >= 0 && separator + 1 < command.Length
            ? command[(separator + 1)..]
            : string.Empty;
    }

    public static string DisplayName(string command)
    {
        string value = Value(Normalize(command));
        return value switch
        {
            CompanionActivityMode.Follow => "Follow",
            CompanionActivityMode.AtEase => "At Ease",
            CompanionActivityMode.Rest => "Rest",
            CompanionActivityMode.ReturnHome => "Return Home",
            CompanionCombatStyle.Passive => "Passive",
            CompanionCombatStyle.Defensive => "Defensive",
            CompanionCombatStyle.Protect => "Protect",
            CompanionCombatStyle.Assist => "Assist",
            CompanionCombatStyle.Aggressive => "Aggressive",
            CompanionCombatStyle.Flee => "Flee",
            CompanionRiskTolerance.Cautious => "Cautious",
            CompanionRiskTolerance.Steady => "Steady",
            CompanionRiskTolerance.Fearless => "Fearless",
            "cleanup" => "Cleanup only",
            "mow" => "Mow only",
            "finished" => "Harvest only",
            "flowers" => "Flowers only",
            "snow" => "Snow only",
            "charcoal" => "Charcoal only",
            "storagesorting" => "Sort storage only",
            "all" => "All duties",
            "none" => "Clear duties",
            var custom when IsCustomDutyValue(custom) => "Selected duties",
            "attack" => "Attack target",
            DropHeldItemsCommand => "Drop held items",
            _ => "Unknown command"
        };
    }

    public static bool IsCustomDutyValue(string? value)
    {
        return value?.StartsWith("custom-", StringComparison.Ordinal) == true
            && int.TryParse(value[7..], out int mask)
            && mask >= 0
            && mask <= (CompanionDuty.SnowballCollectionBit | CompanionDuty.AllExceptSnowballsMask);
    }

    public static bool TryGetDutyMask(string command, out int mask)
    {
        string value = Value(Normalize(command));
        if (IsCustomDutyValue(value) && int.TryParse(value[7..], out mask)) return true;
        mask = CompanionDuty.GetDutyMask(value switch
        {
            "cleanup" => CompanionDuty.GroundCleanup,
            "mow" => CompanionDuty.MowLawn,
            "finished" => CompanionDuty.GatherFinishedProducts,
            "flowers" => CompanionDuty.FlowerRemoval,
            "snow" => CompanionDuty.SnowShoveling,
            "charcoal" => CompanionDuty.CharcoalShoveling,
            "storagesorting" => CompanionDuty.StorageSorting,
            "all" => "all",
            _ => string.Empty
        });
        return value is "cleanup" or "mow" or "finished" or "flowers" or "snow" or "charcoal" or "storagesorting" or "all";
    }
}

internal static class CompanionIdleTestCommand
{
    public const string BasicSit = "basic-sit";
    public const string BasicSleep = "basic-sleep";
    public const string BasicSniff = "basic-sniff";
    public const string BasicIdle = "basic-idle";
    public const string BasicLookAround = "basic-look-around";
    public const string BasicWander = "basic-wander";
    public const string PairedRest = "paired-rest";
    public const string PairedSleep = "paired-sleep";
    public const string PackmatePlay = "packmate-play";
    public const string ShadowFollow = "shadow-follow";
    public const string PackSpaceVisit = "pack-space-visit";
    public const string PackCartSit = "pack-cart-sit";
    public const string SeeOff = "see-off";
    public const string PackmateGreeting = "packmate-greeting";
    public const string ScentInvestigation = "scent-investigation";
    public const string CampLookout = "camp-lookout";

    public static bool IsValid(string? command)
    {
        return Normalize(command ?? string.Empty) is BasicSit or BasicSleep or BasicSniff or BasicIdle
            or BasicLookAround or BasicWander
            or PairedRest or PairedSleep or PackmatePlay or ShadowFollow or PackSpaceVisit or PackCartSit
            or SeeOff or PackmateGreeting or ScentInvestigation or CampLookout;
    }

    public static bool IsDirectAnimation(string? command)
    {
        return Normalize(command ?? string.Empty) is BasicSit or BasicSleep or BasicSniff or BasicIdle;
    }

    public static string Normalize(string command)
    {
        return command.Trim().ToLowerInvariant() switch
        {
            BasicSit => BasicSit,
            BasicSleep => BasicSleep,
            BasicSniff => BasicSniff,
            BasicIdle => BasicIdle,
            BasicLookAround => BasicLookAround,
            BasicWander => BasicWander,
            PairedRest => PairedRest,
            PairedSleep => PairedSleep,
            PackmatePlay => PackmatePlay,
            ShadowFollow => ShadowFollow,
            PackSpaceVisit => PackSpaceVisit,
            PackCartSit => PackCartSit,
            SeeOff => SeeOff,
            PackmateGreeting => PackmateGreeting,
            ScentInvestigation => ScentInvestigation,
            CampLookout => CampLookout,
            _ => string.Empty
        };
    }

    public static string DisplayName(string command)
    {
        return Normalize(command) switch
        {
            BasicSit => "Sit",
            BasicSleep => "Sleep",
            BasicSniff => "Sniff",
            BasicIdle => "Stand idle",
            BasicLookAround => "Look around",
            BasicWander => "Short wander",
            PairedRest => "Paired rest",
            PairedSleep => "Paired sleep",
            PackmatePlay => "Packmate play",
            ShadowFollow => "Shadow walk",
            PackSpaceVisit => "Visit pack space",
            PackCartSit => "Sit on pack cart",
            SeeOff => "See you off",
            PackmateGreeting => "Packmate greeting",
            ScentInvestigation => "Scent investigation",
            CampLookout => "Camp lookout",
            _ => "Unknown idle behavior"
        };
    }
}

[ProtoContract]
public sealed class FoxSocialRequestPacket
{
    [ProtoMember(1)]
    public int Action;
    [ProtoMember(2)]
    public long TargetEntityId;
    [ProtoMember(3)]
    public string RequestedJob = string.Empty;
    [ProtoMember(4)]
    public List<string> SelectedFoxIds = new();
    [ProtoMember(5)]
    public string SelectedMissingFoxId = string.Empty;
    [ProtoMember(6)]
    public bool PrepareExpedition;
    [ProtoMember(7)]
    public long ExpeditionId;
    [ProtoMember(8)]
    public string DeveloperFoxId = string.Empty;
    [ProtoMember(9)]
    public long ScavengeSiteId;
    [ProtoMember(10)]
    public string ScavengeFocus = string.Empty;
    [ProtoMember(11)]
    public string ScoutDuration = string.Empty;
}

[ProtoContract]
public sealed class CompanionWhistlePacket
{
    public const int SetCommand = 1;
    public const int ClearCommand = 2;

    [ProtoMember(1)]
    public int Action;

    [ProtoMember(2)]
    public string Command = string.Empty;
}

[ProtoContract]
public sealed class CompanionIdleTestPacket
{
    public const int SetCommand = 1;
    public const int ClearCommand = 2;

    [ProtoMember(1)]
    public int Action;

    [ProtoMember(2)]
    public string Command = string.Empty;
}

[ProtoContract]
public sealed class CompanionNameSuggestionPacket
{
    [ProtoMember(1)]
    public long TargetEntityId;

    [ProtoMember(2)]
    public string SuggestedName = string.Empty;

    [ProtoMember(3)]
    public bool IsNewborn;
}

[ProtoContract]
public sealed class FoxStorageRoutingRequestPacket
{
    public const int Open = 1;
    public const int SetMask = 2;
    public const int Clear = 3;
    public const int EnableAdvanced = 4;
    public const int UseCategories = 5;
    public const int AddCurrentItems = 6;
    public const int ToggleExactItem = 7;

    [ProtoMember(1)] public int Action;
    [ProtoMember(2)] public int X;
    [ProtoMember(3)] public int Y;
    [ProtoMember(4)] public int Z;
    [ProtoMember(5)] public int Dimension;
    [ProtoMember(6)] public int CategoryMask;
    [ProtoMember(7)] public string ItemCode = string.Empty;
}

[ProtoContract]
public sealed class FoxStorageRoutingStatePacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public int Dimension;
    [ProtoMember(5)] public bool Supported;
    [ProtoMember(6)] public bool RoutingEnabled;
    [ProtoMember(7)] public int CategoryMask;
    [ProtoMember(8)] public string StorageName = string.Empty;
    [ProtoMember(9)] public string Message = string.Empty;
    [ProtoMember(10)] public bool AdvancedMode;
    [ProtoMember(11)] public List<FoxStorageRoutingItemRulePacket> AdvancedItems = new();
}

[ProtoContract]
public sealed class FoxStorageRoutingItemRulePacket
{
    [ProtoMember(1)] public string ItemCode = string.Empty;
    [ProtoMember(2)] public string DisplayName = string.Empty;
    [ProtoMember(3)] public string CategoryName = string.Empty;
    [ProtoMember(4)] public int QuantityInStorage;
    [ProtoMember(5)] public bool Included;
    [ProtoMember(6)] public bool Excluded;
    [ProtoMember(7)] public bool PresentInStorage;
}

[ProtoContract]
public sealed class FoxBedAssignmentRequestPacket
{
    [ProtoMember(1)]
    public int X;
    [ProtoMember(2)]
    public int Y;
    [ProtoMember(3)]
    public int Z;
    [ProtoMember(4)]
    public int Dimension;
    [ProtoMember(5)]
    public string FoxId = string.Empty;
    [ProtoMember(6)]
    public bool Bramble;
}

[ProtoContract]
public sealed class FoxWorkCartAssignmentRequestPacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public int Dimension;
    [ProtoMember(5)] public string FoxId = string.Empty;
    [ProtoMember(6)] public bool Assign;
    [ProtoMember(7)] public bool AssignWithinRadius;
    [ProtoMember(8)] public bool SetLogging;
    [ProtoMember(9)] public bool LoggingEnabled;
}

[ProtoContract]
public sealed class FoxPackOpenFromCairnPacket
{
    [ProtoMember(1)]
    public bool Open;
}

[ProtoContract]
public sealed class FoxBedCandidatePacket
{
    [ProtoMember(1)]
    public string FoxId = string.Empty;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(3)]
    public string Name = string.Empty;
    [ProtoMember(4)]
    public string Status = string.Empty;
    [ProtoMember(5)]
    public bool AssignedHere;
    [ProtoMember(6)]
    public bool AssignedElsewhere;
    [ProtoMember(7)]
    public string SpeciesId = string.Empty;
    [ProtoMember(8)]
    public string SpeciesDisplayName = string.Empty;
    [ProtoMember(9)]
    public bool IsBramble;
}

[ProtoContract]
public sealed class FoxBedAssignmentStatePacket
{
    [ProtoMember(1)]
    public int X;
    [ProtoMember(2)]
    public int Y;
    [ProtoMember(3)]
    public int Z;
    [ProtoMember(4)]
    public int Dimension;
    [ProtoMember(5)]
    public string AssignedFoxId = string.Empty;
    [ProtoMember(6)]
    public List<FoxBedCandidatePacket> Candidates = new();
    [ProtoMember(7)]
    public string Message = string.Empty;
    [ProtoMember(8)]
    public bool AssignedBramble;
}

[ProtoContract]
public sealed class FoxWorkCartAssignmentStatePacket
{
    [ProtoMember(1)] public int X;
    [ProtoMember(2)] public int Y;
    [ProtoMember(3)] public int Z;
    [ProtoMember(4)] public int Dimension;
    [ProtoMember(5)] public float Radius;
    [ProtoMember(6)] public int AssignedCount;
    [ProtoMember(7)] public List<FoxBedCandidatePacket> Candidates = new();
    [ProtoMember(8)] public string Message = string.Empty;
    [ProtoMember(9)] public bool LoggingEnabled;
    [ProtoMember(10)] public string WorkCartKind = "generic";
    [ProtoMember(11)] public int WorkersAtCartCount;
}

[ProtoContract]
public sealed class FoxSocialStatePacket
{
    [ProtoMember(1)]
    public long TargetEntityId;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(3)]
    public string Personality = string.Empty;
    [ProtoMember(4)]
    public int RequestsGenerated;
    [ProtoMember(5)]
    public int RequestsCompleted;
    [ProtoMember(6)]
    public int Points;
    [ProtoMember(7)]
    public string Mood = string.Empty;
    [ProtoMember(8)]
    public string ActiveRequest = string.Empty;
    [ProtoMember(9)]
    public float ProgressSeconds;
    [ProtoMember(10)]
    public float DurationSeconds;
    [ProtoMember(11)]
    public string LastCompleted = string.Empty;
    [ProtoMember(12)]
    public float RequestCooldownSeconds;
    [ProtoMember(13)]
    public float CancelCooldownSeconds;
    [ProtoMember(14)]
    public string Message = string.Empty;
    [ProtoMember(15)]
    public string PersonalitySummary = string.Empty;
    [ProtoMember(16)]
    public string CurrentThought = string.Empty;
    [ProtoMember(17)]
    public bool HasThreatSurvey;
    [ProtoMember(18)]
    public string CarriedItem = string.Empty;
    [ProtoMember(19)]
    public string SpeciesId = string.Empty;
    [ProtoMember(20)]
    public string SpeciesDisplayName = string.Empty;
    [ProtoMember(21)]
    public string ActivityMode = string.Empty;
    [ProtoMember(22)]
    public string FollowDistance = string.Empty;
    [ProtoMember(23)]
    public string CombatStyle = string.Empty;
    [ProtoMember(24)]
    public string RiskTolerance = string.Empty;
    [ProtoMember(25)]
    public string Name = string.Empty;
    [ProtoMember(26)]
    public bool IsJuvenile;
    [ProtoMember(27)]
    public int BankedTalentPoints;
    [ProtoMember(28)]
    public string MotherName = string.Empty;
    [ProtoMember(29)]
    public string FatherName = string.Empty;
    [ProtoMember(30)]
    public bool BreedingSupported;
    [ProtoMember(31)]
    public bool BreedingEnabled;
    [ProtoMember(32)]
    public bool PregnancyActive;
    [ProtoMember(33)]
    public string BreedingPartnerName = string.Empty;
    [ProtoMember(34)]
    public string BreedingFeedback = string.Empty;
    [ProtoMember(35)]
    public float PregnancyRemainingHours;
    [ProtoMember(36)]
    public float ExpeditionStrengthFactor = 1f;
    [ProtoMember(37)]
    public float CurrentHealth;
    [ProtoMember(38)]
    public float MaxHealth;
    [ProtoMember(39)]
    public bool GroundCleanupEnabled;
    [ProtoMember(45)]
    public bool GroundDroppedItemsEnabled;
    [ProtoMember(46)]
    public bool GroundCattailsEnabled;
    [ProtoMember(47)]
    public bool GroundFlintEnabled;
    [ProtoMember(48)]
    public bool GroundSticksEnabled;
    [ProtoMember(49)]
    public bool GroundBouldersEnabled;
    [ProtoMember(50)]
    public bool GroundRocksEnabled;
    [ProtoMember(40)]
    public bool MowLawnEnabled;
    [ProtoMember(41)]
    public bool FinishedProductsEnabled;
    [ProtoMember(51)]
    public bool FinishedCropsEnabled;
    [ProtoMember(52)]
    public bool FinishedBerriesEnabled;
    [ProtoMember(53)]
    public bool FinishedMushroomsEnabled;
    [ProtoMember(42)]
    public bool FlowerRemovalEnabled;
    [ProtoMember(43)]
    public bool SnowShovelingEnabled;
    [ProtoMember(65)]
    public bool CharcoalShovelingEnabled;
    [ProtoMember(44)]
    public bool SnowballCollectionEnabled;
    [ProtoMember(54)]
    public bool FoodSystemEnabled = true;
    [ProtoMember(55)]
    public float FoodLevel = 1f;
    [ProtoMember(56)]
    public string FoodState = string.Empty;
    [ProtoMember(57)]
    // Network packets must default false because protobuf omits false values.
    // The server still supplies the world-safe enabled default when the fox
    // has no saved setting; a client packet must not turn an explicit false
    // back into true when the field is omitted on the wire.
    public bool GeneralStorageSortingEnabled;
    [ProtoMember(58)]
    public int OwnedCompanionCount;
    [ProtoMember(59)]
    public int Level = CompanionProgressionRules.StartingLevel;
    [ProtoMember(60)]
    public long CurrentLevelExperience;
    [ProtoMember(61)]
    public long RequiredLevelExperience = 40L;
    [ProtoMember(62)]
    public long LifetimeExperience;
    [ProtoMember(63)]
    public string FoxId = string.Empty;
    [ProtoMember(64)]
    public bool DeveloperEntityLoaded;
    [ProtoMember(66)]
    public string AppearanceCode = string.Empty;
    [ProtoMember(67)] public int SpeciesTraining;
}

[ProtoContract]
public sealed class FoxPerkRankEntry
{
    [ProtoMember(1)]
    public string Id = string.Empty;
    [ProtoMember(2)]
    public int Rank;
}

[ProtoContract]
public sealed class FoxPerkStatePacket
{
    [ProtoMember(1)]
    public long TargetEntityId;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(10)]
    public string Name = string.Empty;
    [ProtoMember(11)]
    public string SpeciesId = string.Empty;
    [ProtoMember(12)]
    public string SpeciesDisplayName = string.Empty;
    [ProtoMember(3)]
    public int AvailablePoints;
    [ProtoMember(4)]
    public int SpentPoints;
    [ProtoMember(5)]
    public int CombatInvestment;
    [ProtoMember(6)]
    public int MobilityInvestment;
    [ProtoMember(7)]
    public int SocialInvestment;
    [ProtoMember(8)]
    public List<FoxPerkRankEntry> Ranks = new();
    [ProtoMember(9)]
    public string Message = string.Empty;
    [ProtoMember(13)]
    public string Personality = string.Empty;
    [ProtoMember(14)]
    public string Mood = string.Empty;
    [ProtoMember(15)]
    public string ActivityMode = string.Empty;
    [ProtoMember(16)]
    public string CombatStyle = string.Empty;
    [ProtoMember(17)]
    public int Level;
    [ProtoMember(18)]
    public long CurrentLevelExperience;
    [ProtoMember(19)]
    public long RequiredLevelExperience;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class FoxPackRecord
{
    public int Number;
    public string OwnerUid = string.Empty;
    public long EntityId;
    public string Name = string.Empty;
    public string Personality = string.Empty;
    public string Mood = "Unassigned";
    public string Status = "Not currently loaded";
    public float CurrentHealth;
    public float MaxHealth;
    public int RequestsGenerated;
    public int RequestsCompleted;
    public int Points;
    public int PackPoints;
    public bool HasPackPoints;
    public string ActiveRequest = "None";
    public string LastCompleted = "None";
    public double LastSeenDay;
    public int LastKnownX;
    public int LastKnownY;
    public int LastKnownZ;
    public bool HasLastKnownPosition;
}

[ProtoContract]
public sealed partial class FoxPackRecordV2
{
    [ProtoMember(1)]
    public string FoxId = string.Empty;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(3)]
    public string OwnerUid = string.Empty;
    [ProtoMember(4)]
    public long EntityId;
    [ProtoMember(5)]
    public string Name = string.Empty;
    [ProtoMember(6)]
    public string Personality = string.Empty;
    [ProtoMember(7)]
    public string Mood = "Unassigned";
    [ProtoMember(8)]
    public string Status = "Not currently loaded";
    [ProtoMember(9)]
    public float CurrentHealth;
    [ProtoMember(10)]
    public float MaxHealth;
    [ProtoMember(11)]
    public int RequestsGenerated;
    [ProtoMember(12)]
    public int RequestsCompleted;
    [ProtoMember(13)]
    public int Points;
    [ProtoMember(14)]
    public int PackPoints;
    [ProtoMember(15)]
    public bool HasPackPoints;
    [ProtoMember(16)]
    public string ActiveRequest = "None";
    [ProtoMember(17)]
    public string LastCompleted = "None";
    [ProtoMember(18)]
    public double LastSeenDay;
    [ProtoMember(19)]
    public int LastKnownX;
    [ProtoMember(20)]
    public int LastKnownY;
    [ProtoMember(21)]
    public int LastKnownZ;
    [ProtoMember(22)]
    public bool HasLastKnownPosition;
    [ProtoMember(23)]
    public double LastKnownPositionExpiresDay;
    [ProtoMember(24)]
    public bool Archived;
    [ProtoMember(25)]
    public double StatusExpiresDay;
    [ProtoMember(26)]
    public int LifetimePoints;
    [ProtoMember(27)]
    public int LastKnownDimension;
    [ProtoMember(28)]
    public bool DurableStateInitialized;
    [ProtoMember(29)]
    public List<FoxPerkRankEntry> TalentRanks = new();
    [ProtoMember(30)]
    public string MoodId = string.Empty;
    [ProtoMember(31)]
    public int HealthState;
    [ProtoMember(32)]
    public double MortallyWoundedStartHours;
    [ProtoMember(33)]
    public int StabilizedWindowAppliedRank;
    [ProtoMember(34)]
    public double StabilizedWindowAdjustedStartHours = -1d;
    [ProtoMember(35)]
    public double DeathlessRecoveryAtHours = -1d;
    [ProtoMember(36)]
    public double DeathlessFallbackAtHours = -1d;
    [ProtoMember(37)]
    public string ActiveRequestId = string.Empty;
    [ProtoMember(38)]
    public string LastCompletedId = string.Empty;
    [ProtoMember(39)]
    public float RequestProgress;
    [ProtoMember(40)]
    public long RequestQualifiedSinceUtcMs;
    [ProtoMember(41)]
    public long RequestExpiresUtcMs;
    [ProtoMember(42)]
    public bool RequestIsDeveloper;
    [ProtoMember(43)]
    public string RequestedItemCode = string.Empty;
    [ProtoMember(44)]
    public int RequestedItemCount;
    [ProtoMember(45)]
    public long RequestCooldownEndsUtcMs;
    [ProtoMember(46)]
    public long CancelCooldownEndsUtcMs;
    [ProtoMember(47)]
    public string PendingReturnStatus = string.Empty;
    [ProtoMember(48)]
    public long PendingReturnAtUtcMs;
    [ProtoMember(49)]
    public bool HasHome;
    [ProtoMember(50)]
    public int HomeX;
    [ProtoMember(51)]
    public int HomeY;
    [ProtoMember(52)]
    public int HomeZ;
    [ProtoMember(53)]
    public int HomeDimension;
    [ProtoMember(102)]
    public string HomeType = string.Empty;
    [ProtoMember(103)]
    public bool DutyOptionsInitialized;
    [ProtoMember(104)]
    public string EntityCode = string.Empty;
    [ProtoMember(105)]
    public string RecoveryStatus = CompanionRecoveryStatus.Unknown;
    [ProtoMember(106)]
    public bool GeneralStorageSortingEnabled = true;
    [ProtoMember(107)]
    public string PendingDialogueEvent = string.Empty;
    [ProtoMember(108)]
    public bool WasMiaBeforeReturn;
    [ProtoMember(109)]
    public int Level = CompanionProgressionRules.StartingLevel;
    [ProtoMember(110)]
    public long CurrentLevelExperience;
    [ProtoMember(111)]
    public long LifetimeExperience;
    [ProtoMember(112)]
    public bool AdulthoodExperienceGranted;
    [ProtoMember(113)]
    public int CleanupDutyUnits;
    [ProtoMember(114)]
    public long PredatorTargetEntityId;
    [ProtoMember(115)]
    public string PredatorTargetClue = string.Empty;
    [ProtoMember(116)]
    public bool PredatorTargetKilled;
    [ProtoMember(117)]
    public bool AutomaticRetreatActive;
    [ProtoMember(118)]
    public string AutomaticRetreatPreviousCombatStyle = string.Empty;
    [ProtoMember(119)]
    public string AutomaticRetreatPreviousActivity = string.Empty;
    [ProtoMember(120)]
    public bool CharcoalShovelingEnabled;
    [ProtoMember(121)]
    public byte[] BackpackItem = Array.Empty<byte>();
    [ProtoMember(122)]
    public bool BackpackDeliveryActive;
    [ProtoMember(123)]
    public string BackpackDeliveryPhase = string.Empty;
    [ProtoMember(124)]
    public bool BackpackReturnToPlayer;
    [ProtoMember(125)]
    public string BackpackPreviousActivity = string.Empty;
    [ProtoMember(126)]
    public long BackpackPhaseStartedUtcMs;
    [ProtoMember(127)]
    public long BackpackReturnDueUtcMs;
    [ProtoMember(128)]
    public long BackpackReturnRetryUntilUtcMs;
    [ProtoMember(129)]
    public int BackpackCartX;
    [ProtoMember(130)]
    public int BackpackCartY;
    [ProtoMember(131)]
    public int BackpackCartZ;
    [ProtoMember(132)]
    public int BackpackCartDimension;
    [ProtoMember(133)]
    public int BackpackStorageX;
    [ProtoMember(134)]
    public int BackpackStorageY;
    [ProtoMember(135)]
    public int BackpackStorageZ;
    [ProtoMember(136)]
    public bool BackpackReturnSnapshotTaken;
    [ProtoMember(137)]
    public bool BackpackUnloadAccessFailed;
    [ProtoMember(138)]
    public int BackpackDeliveryMovedItemCount;
    [ProtoMember(139)]
    public string BackpackLastDeliverySummary = string.Empty;
    [ProtoMember(140)]
    public int BackpackDeliveryStartingItemCount;
    [ProtoMember(54)]
    public string PendingDepartureStatus = string.Empty;
    [ProtoMember(55)]
    public long PendingDepartureAtUtcMs;
    [ProtoMember(56)]
    public long PendingDepartureDeadlineUtcMs;
    [ProtoMember(57)]
    public string SpeciesId = string.Empty;
    [ProtoMember(58)]
    public string ActivityMode = string.Empty;
    [ProtoMember(59)]
    public string FollowDistance = string.Empty;
    [ProtoMember(60)]
    public string CombatStyle = string.Empty;
    [ProtoMember(61)]
    public long ActivityStartedUtcMs;
    [ProtoMember(62)]
    public long ActivityArrivedUtcMs;
    [ProtoMember(63)]
    public string RiskTolerance = string.Empty;
    [ProtoMember(64)]
    public bool BreedingEnabled;
    [ProtoMember(65)]
    public string BondedPartnerId = string.Empty;
    [ProtoMember(66)]
    public long LastBreedingAttemptNight = long.MinValue;
    [ProtoMember(67)]
    public string BreedingFeedback = string.Empty;
    [ProtoMember(68)]
    public bool PregnancyActive;
    [ProtoMember(69)]
    public double PregnancyStartTotalHours;
    [ProtoMember(70)]
    public double PregnancyDueTotalHours;
    [ProtoMember(71)]
    public string PregnancyFatherId = string.Empty;
    [ProtoMember(72)]
    public string PregnancyFatherName = string.Empty;
    [ProtoMember(73)]
    public bool PendingBirth;
    [ProtoMember(74)]
    public bool IsJuvenile;
    [ProtoMember(75)]
    public string ParentMotherId = string.Empty;
    [ProtoMember(76)]
    public string ParentMotherName = string.Empty;
    [ProtoMember(77)]
    public string ParentFatherId = string.Empty;
    [ProtoMember(78)]
    public string ParentFatherName = string.Empty;
    [ProtoMember(79)]
    public int BankedTalentPoints;
    [ProtoMember(80)]
    public bool JuvenilePointsTransferred;
    [ProtoMember(81)]
    public string ChildAdultEntityCode = string.Empty;
    [ProtoMember(82)]
    public string BondedPartnerName = string.Empty;
    [ProtoMember(83)]
    public bool PendingDepartureMoment;
    [ProtoMember(84)]
    public bool GroundCleanupEnabled;
    [ProtoMember(90)]
    public bool GroundDroppedItemsEnabled;
    [ProtoMember(91)]
    public bool GroundCattailsEnabled;
    [ProtoMember(92)]
    public bool GroundFlintEnabled;
    [ProtoMember(93)]
    public bool GroundSticksEnabled;
    [ProtoMember(94)]
    public bool GroundBouldersEnabled;
    [ProtoMember(95)]
    public bool GroundRocksEnabled;
    [ProtoMember(85)]
    public bool MowLawnEnabled;
    [ProtoMember(86)]
    public bool FinishedProductsEnabled;
    [ProtoMember(96)]
    public bool FinishedCropsEnabled;
    [ProtoMember(97)]
    public bool FinishedBerriesEnabled;
    [ProtoMember(98)]
    public bool FinishedMushroomsEnabled;
    [ProtoMember(87)]
    public bool FlowerRemovalEnabled;
    [ProtoMember(88)]
    public bool SnowShovelingEnabled;
    [ProtoMember(89)]
    public bool SnowballCollectionEnabled;
    [ProtoMember(99)]
    public bool FoodStateInitialized;
    [ProtoMember(100)]
    public float FoodLevel = 1f;
    [ProtoMember(101)]
    public double FoodLastUpdateTotalHours;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public sealed class FoxPackPointsRecord
{
    public string OwnerUid = string.Empty;
    public int Points;
}

[ProtoContract]
public sealed class FoxPackSaveData
{
    [ProtoMember(1)]
    public int Version;
    [ProtoMember(2)]
    public int NextFoxNumber = 1;
    [ProtoMember(3)]
    public List<FoxPackRecordV2> Records = new();
    [ProtoMember(4)]
    public List<FoxPackPointsRecord> PackPoints = new();
    [ProtoMember(5)]
    public List<FoxPackLootRecord> Loot = new();
    [ProtoMember(6)]
    public List<FoxExpeditionRecord> Expeditions = new();
    [ProtoMember(7)]
    public List<FoxRecruitmentRewardRecord> RecruitmentRewards = new();
    [ProtoMember(8)]
    public List<FoxPackCairnRecord> Cairns = new();
    [ProtoMember(9)]
    public List<FoxBedRecord> Beds = new();
    [ProtoMember(10)]
    public long NextCairnSequence = 1;
    [ProtoMember(11)]
    public List<FoxPackProgressRecord> PackProgress = new();
    [ProtoMember(12)]
    public List<FoxExpeditionSummaryPacket> LastExpeditionReports = new();
    [ProtoMember(13)]
    public List<FoxPackAmenityRecord> Amenities = new();
    [ProtoMember(14)]
    public List<FoxWorkCartRecord> WorkCarts = new();
    [ProtoMember(15)]
    public long NextExpeditionSequence = 1;
    [ProtoMember(16)]
    public List<FoxExpeditionSummaryPacket> ExpeditionReportHistory = new();
    [ProtoMember(17)]
    public List<string> PermanentlyDeletedFoxIds = new();
    [ProtoMember(18)]
    public List<FoxScavengeSiteRecord> ScavengeSites = new();
    [ProtoMember(19)]
    public long NextScavengeSiteSequence = 1;
}

[ProtoContract]
public sealed class FoxPackProgressRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public List<string> UnlockedExpeditionTypes = new();
    [ProtoMember(3)]
    public bool PatrolPrepared;
    [ProtoMember(4)]
    public bool CargoUnloadingActive;
    [ProtoMember(5)]
    public List<string> UnlockedPackTalents = new();
    [ProtoMember(6)]
    public int PermanentRangeRank;
    [ProtoMember(7)]
    public int PermanentExpeditionCapacityRank;
}

[ProtoContract]
public sealed class FoxPackCairnRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public int X;
    [ProtoMember(3)]
    public int Y;
    [ProtoMember(4)]
    public int Z;
    [ProtoMember(5)]
    public int Dimension;
    [ProtoMember(6)]
    public long PlacedSequence;
}

[ProtoContract]
public sealed class FoxBedRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public int X;
    [ProtoMember(3)]
    public int Y;
    [ProtoMember(4)]
    public int Z;
    [ProtoMember(5)]
    public int Dimension;
    [ProtoMember(6)]
    public string FoxId = string.Empty;
    [ProtoMember(7)]
    public string BlockCode = string.Empty;
    [ProtoMember(8)]
    public string BrambleOwnerUid = string.Empty;
}

[ProtoContract]
public sealed class FoxWorkCartRecord
{
    [ProtoMember(1)] public string OwnerUid = string.Empty;
    [ProtoMember(2)] public int X;
    [ProtoMember(3)] public int Y;
    [ProtoMember(4)] public int Z;
    [ProtoMember(5)] public int Dimension;
    [ProtoMember(6)] public List<string> AssignedFoxIds = new();
    [ProtoMember(7)] public bool LoggingEnabled;
    [ProtoMember(8)] public string Kind = "generic";
}

[ProtoContract]
public sealed class FoxPackAmenityRecord
{
    [ProtoMember(1)] public string OwnerUid = string.Empty;
    [ProtoMember(2)] public int X;
    [ProtoMember(3)] public int Y;
    [ProtoMember(4)] public int Z;
    [ProtoMember(5)] public int Dimension;
    [ProtoMember(6)] public string Kind = string.Empty;
    [ProtoMember(7)] public bool StorageRoutingEnabled;
    [ProtoMember(8)] public int StorageRoutingMask;
    [ProtoMember(9)] public bool StorageAdvancedMode;
    [ProtoMember(10)] public List<string> StorageAdvancedIncludedItemCodes = new();
    [ProtoMember(11)] public List<string> StorageAdvancedExcludedItemCodes = new();
}

[ProtoContract]
public sealed class FoxPackLootRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public List<byte[]> Items = new();
}

[ProtoContract]
public sealed class FoxExpeditionRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public string Type = string.Empty;
    [ProtoMember(3)]
    public long StartedUtcMs;
    [ProtoMember(4)]
    public long CompletesUtcMs;
    [ProtoMember(5)]
    public List<string> SelectedFoxIds = new();
    [ProtoMember(6)]
    public float ExpeditionStrength;
    [ProtoMember(7)]
    public float TargetStrength;
    [ProtoMember(8)]
    public bool RunningLate;
    [ProtoMember(9)]
    public float PerkStrengthBonus;
    [ProtoMember(10)]
    public float PerkSpeedBonus;
    [ProtoMember(11)]
    public float PerkRiskReduction;
    [ProtoMember(12)]
    public float RecruitmentChanceBonus;
    [ProtoMember(13)]
    public float PerkInjuryRiskReduction;
    [ProtoMember(14)]
    public string TargetFoxId = string.Empty;
    [ProtoMember(15)]
    public bool Prepared;
    [ProtoMember(16)]
    public float PreparationRiskReduction;
    [ProtoMember(17)]
    public float PatrolRiskReduction;
    [ProtoMember(18)]
    public double StartedTotalHours;
    [ProtoMember(19)]
    public double CompletesTotalHours;
    [ProtoMember(20)]
    public float BaseDurationHours;
    [ProtoMember(21)]
    public float LateDurationHours;
    [ProtoMember(22)]
    public List<FoxExpeditionMemberSummaryPacket> MemberSnapshots = new();
    [ProtoMember(23)]
    public float PlannedDurationHours;
    [ProtoMember(24)]
    public float PerkRewardBonus;
    [ProtoMember(25)]
    public float PerkMiaRiskReduction;
    [ProtoMember(26)]
    public float PerkCargoRiskReduction;
    [ProtoMember(27)]
    public long ExpeditionId;
    [ProtoMember(28)]
    public ExpeditionStoryState? Story;
    [ProtoMember(29)]
    public bool DebugForceCompletion;
    [ProtoMember(30)]
    public long ScavengeSiteId;
    [ProtoMember(31)]
    public string ScavengeFocus = string.Empty;
    [ProtoMember(32)]
    public string ScoutDuration = string.Empty;
    [ProtoMember(33)]
    public bool BlockedAtDoor;
    [ProtoMember(34)] public int ScoutInformationBonus;
    [ProtoMember(35)] public bool FavorDifficultScavengeSites;
    [ProtoMember(36)] public bool QuietScavengeParty;
    [ProtoMember(37)] public bool LargeScavengeParty;
    [ProtoMember(38)] public CompanionSpeciesExpeditionBonus? SpeciesBonus;
}

internal sealed class ExpeditionPerkSnapshot
{
    public float StrengthBonus;
    public float SpeedBonus;
    public float RiskReduction;
    public float InjuryRiskReduction;
    public float RecruitmentChanceBonus;
    public float RewardBonus;
    public float MiaRiskReduction;
    public float CargoRiskReduction;
    public int ScoutInformationBonus;
    public bool FavorDifficultScavengeSites;
    public bool QuietScavengeParty;
    public bool LargeScavengeParty;
}

[ProtoContract]
public sealed class FoxRecruitmentRewardRecord
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public bool Arctic;
    [ProtoMember(3)]
    public bool Female;
    [ProtoMember(4)]
    public string SpeciesId = string.Empty;
    [ProtoMember(5)]
    public string WildEntityCode = string.Empty;
}

[ProtoContract]
public sealed class FoxPackMemberPacket
{
    [ProtoMember(1)]
    public string FoxId = string.Empty;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(3)]
    public string Name = string.Empty;
    [ProtoMember(4)]
    public string Status = string.Empty;
    [ProtoMember(5)]
    public float CurrentHealth;
    [ProtoMember(6)]
    public float MaxHealth;
    [ProtoMember(7)]
    public string Personality = string.Empty;
    [ProtoMember(8)]
    public string Mood = string.Empty;
    [ProtoMember(9)]
    public int RequestsGenerated;
    [ProtoMember(10)]
    public int RequestsCompleted;
    [ProtoMember(11)]
    public int Points;
    [ProtoMember(12)]
    public string ActiveRequest = string.Empty;
    [ProtoMember(13)]
    public string LastCompleted = string.Empty;
    [ProtoMember(14)]
    public bool HasLastKnownPosition;
    [ProtoMember(15)]
    public int LastKnownX;
    [ProtoMember(16)]
    public int LastKnownY;
    [ProtoMember(17)]
    public int LastKnownZ;
    [ProtoMember(18)]
    public float ExpeditionStrengthBonus;
    [ProtoMember(19)]
    public float ExpeditionSpeedBonus;
    [ProtoMember(20)]
    public float RecruitmentChanceBonus;
    [ProtoMember(21)]
    public float ExpeditionInjuryRiskReduction;
    [ProtoMember(22)]
    public int LifetimePoints;
    [ProtoMember(23)]
    public float ExpeditionCoreStrength;
    [ProtoMember(24)]
    public float ExpeditionConditionStrength;
    [ProtoMember(25)]
    public float ExpeditionHealthStrength;
    [ProtoMember(26)]
    public float ExpeditionMovementStrength;
    [ProtoMember(27)]
    public string CarriedItem = string.Empty;
    [ProtoMember(28)]
    public bool WaitingForCartCargo;
    [ProtoMember(29)]
    public string SpeciesId = string.Empty;
    [ProtoMember(30)]
    public string SpeciesDisplayName = string.Empty;
    [ProtoMember(31)]
    public bool IsJuvenile;
    [ProtoMember(32)]
    public bool PregnancyActive;
    [ProtoMember(33)]
    public float ExpeditionStrengthFactor = 1f;
    [ProtoMember(34)]
    public bool EntityLoaded;
    [ProtoMember(35)]
    public bool RescueRecoverable;
    [ProtoMember(36)]
    public int Level = CompanionProgressionRules.StartingLevel;
    [ProtoMember(37)]
    public long CurrentLevelExperience;
    [ProtoMember(38)]
    public long RequiredLevelExperience = 40L;
    [ProtoMember(39)]
    public long LifetimeExperience;
    [ProtoMember(40)]
    public bool BackpackEquipped;
    [ProtoMember(41)]
    public string BackpackName = string.Empty;
    [ProtoMember(42)]
    public int BackpackOccupiedSlots;
    [ProtoMember(43)]
    public int BackpackTotalSlots;
    [ProtoMember(44)]
    public bool BackpackDeliveryActive;
    [ProtoMember(45)]
    public string BackpackDeliveryPhase = string.Empty;
    [ProtoMember(46)]
    public float BackpackReturnRemainingSeconds;
    [ProtoMember(47)]
    public string BackpackLastDeliverySummary = string.Empty;
    [ProtoMember(48)]
    public bool BackpackReturnScheduled;
    [ProtoMember(49)]
    public long EntityId;
    [ProtoMember(50)]
    public bool AtCart;
    [ProtoMember(51)]
    public string AppearanceCode = string.Empty;
    [ProtoMember(52)] public int SpeciesTraining;
}

[ProtoContract]
public sealed class FoxPackStatePacket
{
    [ProtoMember(1)]
    public int PackSize;
    [ProtoMember(2)]
    public int PackPoints;
    [ProtoMember(3)]
    public List<FoxPackMemberPacket> Members = new();
    [ProtoMember(4)]
    public string Message = string.Empty;
    [ProtoMember(5)]
    public List<FoxPackLootItemPacket> LootItems = new();
    [ProtoMember(6)]
    public string ExpeditionType = string.Empty;
    [ProtoMember(7)]
    public float ExpeditionRemainingSeconds;
    [ProtoMember(8)]
    public List<string> ExpeditionSelectedFoxIds = new();
    [ProtoMember(9)]
    public float ExpeditionStrength;
    [ProtoMember(10)]
    public float ExpeditionTargetStrength;
    [ProtoMember(11)]
    public float ExpeditionProgressPercent;
    [ProtoMember(12)]
    public List<FoxPackMemberPacket> ArchivedMembers = new();
    [ProtoMember(13)]
    public bool RecruitmentReady;
    [ProtoMember(14)]
    public string RecruitmentType = string.Empty;
    [ProtoMember(15)]
    public float ExpeditionRecruitmentChanceBonus;
    [ProtoMember(16)]
    public string ExpeditionTargetFoxId = string.Empty;
    [ProtoMember(17)]
    public List<string> UnlockedExpeditionTypes = new();
    [ProtoMember(18)]
    public bool PatrolPrepared;
    [ProtoMember(19)]
    public bool ExpeditionPrepared;
    [ProtoMember(20)]
    public float ExpeditionPreparationRiskReduction;
    [ProtoMember(21)]
    public float ExpeditionPatrolRiskReduction;
    [ProtoMember(22)]
    public double ExpeditionCompletesTotalHours;
    [ProtoMember(23)]
    public float ExpeditionBaseDurationHours;
    [ProtoMember(24)]
    public float ExpeditionLateDurationHours;
    [ProtoMember(25)]
    public FoxExpeditionSummaryPacket? LastExpedition;
    [ProtoMember(26)]
    public bool CargoUnloadingActive;
    [ProtoMember(27)]
    public string RecruitmentSpeciesId = string.Empty;
    [ProtoMember(28)]
    public List<string> UnlockedPackTalents = new();
    [ProtoMember(29)]
    public int PermanentRangeRank;
    [ProtoMember(30)]
    public List<FoxActiveExpeditionPacket> ActiveExpeditions = new();
    [ProtoMember(31)]
    public List<FoxExpeditionSummaryPacket> ExpeditionReports = new();
    [ProtoMember(32)]
    public int PendingRecruitmentCount;
    [ProtoMember(33)]
    public int PermanentExpeditionCapacityRank;
    [ProtoMember(34)]
    public int ExpeditionCapacity = 2;
    [ProtoMember(35)]
    public List<FoxScavengeSitePacket> ScavengeSites = new();
}

[ProtoContract]
public sealed class FoxActiveExpeditionPacket
{
    [ProtoMember(1)] public long ExpeditionId;
    [ProtoMember(2)] public string Type = string.Empty;
    [ProtoMember(3)] public float RemainingSeconds;
    [ProtoMember(4)] public List<string> SelectedFoxIds = new();
    [ProtoMember(5)] public float Strength;
    [ProtoMember(6)] public float TargetStrength;
    [ProtoMember(7)] public float ProgressPercent;
    [ProtoMember(8)] public float RecruitmentChanceBonus;
    [ProtoMember(9)] public string TargetFoxId = string.Empty;
    [ProtoMember(10)] public bool Prepared;
    [ProtoMember(11)] public float PreparationRiskReduction;
    [ProtoMember(12)] public float PatrolRiskReduction;
    [ProtoMember(13)] public double CompletesTotalHours;
    [ProtoMember(14)] public float BaseDurationHours;
    [ProtoMember(15)] public float LateDurationHours;
    [ProtoMember(16)] public bool RunningLate;
    [ProtoMember(17)] public float RawCompletion;
    [ProtoMember(18)] public float OvercapRewardFactor = 1f;
}

[ProtoContract]
public sealed class FoxExpeditionMemberSummaryPacket
{
    [ProtoMember(1)]
    public string FoxId = string.Empty;
    [ProtoMember(2)]
    public int Number;
    [ProtoMember(3)]
    public string Name = string.Empty;
    [ProtoMember(4)]
    public float BaseStrength;
    [ProtoMember(5)]
    public List<string> Perks = new();
    [ProtoMember(6)]
    public string Outcome = string.Empty;
    [ProtoMember(7)]
    public float ConditionStrength;
    [ProtoMember(8)]
    public float HealthStrength;
    [ProtoMember(9)]
    public float MovementStrength;
    [ProtoMember(10)]
    public float PerkStrength;
    [ProtoMember(11)]
    public float TotalStrength;
    [ProtoMember(12)]
    public string Personality = string.Empty;
    [ProtoMember(13)]
    public string MoodAtDeparture = string.Empty;
    [ProtoMember(14)]
    public string SpeciesId = string.Empty;
}

[ProtoContract]
public sealed class FoxExpeditionSummaryPacket
{
    [ProtoMember(1)]
    public string OwnerUid = string.Empty;
    [ProtoMember(2)]
    public string Type = string.Empty;
    [ProtoMember(3)]
    public string Name = string.Empty;
    [ProtoMember(4)]
    public double StartedTotalHours;
    [ProtoMember(5)]
    public double CompletedTotalHours;
    [ProtoMember(6)]
    public float BaseDurationHours;
    [ProtoMember(7)]
    public bool RanLate;
    [ProtoMember(8)]
    public float LateDurationHours;
    [ProtoMember(9)]
    public bool Prepared;
    [ProtoMember(10)]
    public bool PatrolProtected;
    [ProtoMember(11)]
    public float ExpeditionStrength;
    [ProtoMember(12)]
    public float TargetStrength;
    [ProtoMember(13)]
    public List<FoxExpeditionMemberSummaryPacket> Members = new();
    [ProtoMember(14)]
    public List<FoxPackLootItemPacket> LootItems = new();
    [ProtoMember(15)]
    public string Result = string.Empty;
    [ProtoMember(16)]
    public bool NotificationPending;
    [ProtoMember(17)]
    public float PlannedDurationHours;
    [ProtoMember(18)]
    public long ExpeditionId;
    [ProtoMember(19)]
    public ExpeditionStoryState? Story;
    [ProtoMember(20)]
    public string SceneSiteLabel = string.Empty;
}

[ProtoContract]
public sealed class FoxPackLootItemPacket
{
    [ProtoMember(1)]
    public string Code = string.Empty;
    [ProtoMember(2)]
    public string Name = string.Empty;
    [ProtoMember(3)]
    public int Count;
    [ProtoMember(4)]
    public byte[] StackBytes = Array.Empty<byte>();
}

[ProtoContract]
public sealed class TrainingDummyActionPacket
{
    [ProtoMember(1)]
    public int Action;
    [ProtoMember(2)]
    public long TargetEntityId;
}

[ProtoContract]
public sealed class TrainingDummyStatePacket
{
    [ProtoMember(1)]
    public long TargetEntityId;
    [ProtoMember(2)]
    public float CurrentHealth;
    [ProtoMember(3)]
    public float MaxHealth;
    [ProtoMember(4)]
    public string LastAttacker = string.Empty;
    [ProtoMember(5)]
    public float LastDamage;
    [ProtoMember(6)]
    public int LastDamageTier;
    [ProtoMember(7)]
    public string LastDamageType = string.Empty;
    [ProtoMember(8)]
    public int HitCount;
}

[ProtoContract]
public sealed class CompanionSoundPacket
{
    [ProtoMember(1)]
    public string Cue = string.Empty;
}

[ProtoContract]
public sealed class CompanionSpatialSoundPacket
{
    [ProtoMember(1)]
    public string Sound = string.Empty;
    [ProtoMember(2)]
    public double X;
    [ProtoMember(3)]
    public double Y;
    [ProtoMember(4)]
    public double Z;
    [ProtoMember(5)]
    public int Dimension;
    [ProtoMember(6)]
    public float Pitch = 1f;
    [ProtoMember(7)]
    public float Range = 24f;
    [ProtoMember(8)]
    public float Volume = 1f;
}

[ProtoContract]
public sealed class CompanionThoughtPacket
{
    [ProtoMember(1)]
    public long TargetEntityId;

    [ProtoMember(2)]
    public string Text = string.Empty;

    [ProtoMember(3)]
    public int DurationMs = 5000;

    [ProtoMember(4)]
    public int Priority = 10;

    [ProtoMember(5)]
    public long ExpiresAtUtcMs;

    [ProtoMember(6)]
    public string SemanticGroup = string.Empty;
    [ProtoMember(7)] public double FollowOwnerX;
    [ProtoMember(8)] public double FollowOwnerY;
    [ProtoMember(9)] public double FollowOwnerZ;
    [ProtoMember(10)] public int FollowOwnerDimension;

}

[ProtoContract]
public sealed class CompanionDeveloperOpenPacket
{
    [ProtoMember(1)]
    public long TargetEntityId;
}

[ProtoContract]
public sealed class CompanionRecoveryRequestPacket
{
    public const int Open = 1;
    public const int Recover = 2;
    public const int RecoverAll = 3;
    public const int NuclearRebuild = 4;
    public const int Teleport = 5;
    public const int OpenDeveloper = 6;
    public const int RerollScavengeSites = 7;
    public const int RerollScavengeSitesSurveyed = 8;

    [ProtoMember(1)]
    public int Action;

    [ProtoMember(2)]
    public string FoxId = string.Empty;
}

[ProtoContract]
public sealed class CompanionRecoveryStatePacket
{
    [ProtoMember(1)]
    public List<CompanionRecoveryEntryPacket> Records = new();

    [ProtoMember(2)]
    public string Message = string.Empty;
}

[ProtoContract]
public sealed class CompanionRecoveryEntryPacket
{
    [ProtoMember(1)]
    public string FoxId = string.Empty;

    [ProtoMember(2)]
    public int Number;

    [ProtoMember(3)]
    public string Name = string.Empty;

    [ProtoMember(4)]
    public string SpeciesDisplayName = string.Empty;

    [ProtoMember(5)]
    public string Status = string.Empty;

    [ProtoMember(6)]
    public string RecoveryStatus = string.Empty;

    [ProtoMember(7)]
    public bool EntityLoaded;

    [ProtoMember(8)]
    public bool CanRecover;

    [ProtoMember(9)]
    public string BlockedReason = string.Empty;

    [ProtoMember(10)]
    public string EntityCode = string.Empty;

    [ProtoMember(11)]
    public bool CanNuclearRebuild;
    [ProtoMember(12)]
    public long EntityId;
}
