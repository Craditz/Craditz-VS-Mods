#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static int GetSpeciesTraining(Entity? entity, string species) => entity == null
        || CompanionSpeciesTraits.Get(species) == null ? 0
        : Math.Clamp(GetFoxPerkRank(entity, CompanionSpeciesTraits.TrainingId(species)), 0, 1)
            + Math.Clamp(GetFoxPerkRank(entity, CompanionSpeciesTraits.MasteryId(species)), 0, 1);

    private CompanionSpeciesExpeditionBonus SnapshotSpeciesTraits(string route,
        List<FoxExpeditionMemberSummaryPacket> members)
    {
        var inputs = new List<(string Species, float Strength, int Training)>();
        foreach (var member in members)
        {
            Entity? entity = null;
            if (packRepository?.TryGetRecord(member.FoxId, out var record) == true && record != null)
                entity = serverApi?.World.GetEntityById(record.EntityId);
            int training = GetSpeciesTraining(entity, member.SpeciesId);
            inputs.Add((member.SpeciesId, member.TotalStrength, training));
            float extra = CompanionSpeciesTraits.StrengthBonus(member.SpeciesId, route, member.TotalStrength, training);
            member.PerkStrength += extra;
            member.TotalStrength += extra;
            var trait = CompanionSpeciesTraits.Get(member.SpeciesId);
            if (trait != null) member.Perks.Add($"{trait.Name} ({CompanionSpeciesTraits.Affinity(training):P0} affinity)");
        }
        return CompanionSpeciesTraits.Build(route, inputs);
    }

    private int AddSpeciesCatch(string ownerUid, bool prime,
        IReadOnlyList<FoxExpeditionMemberSummaryPacket>? party)
    {
        if (serverApi == null || packRepository == null) return 0;
        string excluded = party is { Count: > 0 } && party.All(m => m.SpeciesId == party[0].SpeciesId)
            ? party[0].SpeciesId : string.Empty;
        var choices = (prime ? PrimeHuntSources : SmallHuntSources)
            .Where(source => source.Species != excluded).Select(ResolveHuntCorpse)
            .Where(choice => choice != null).Select(choice => choice!).ToArray();
        if (choices.Length > 0)
        {
            var choice = choices[serverApi.World.Rand.Next(choices.Length)];
            ItemStack corpse = new(choice.Corpse);
            corpse.Attributes.SetFloat("animalWeight", 1f);
            corpse.Attributes.SetString("AnimalDrops", JsonConvert.SerializeObject(choice.Drops));
            packRepository.AddLoot(ownerUid, corpse);
            return 1;
        }
        // Raw fallback uses the same path as hunts without a loaded corpse provider.
        return AddExpeditionItem(ownerUid, serverApi.World.GetItem(new AssetLocation(
            prime ? "game:redmeat-raw" : "game:bushmeat-raw")), prime ? 6 : 2);
    }

    private int AddSpeciesFind(string ownerUid, string[] codes, int quantity) =>
        AddExpeditionItem(ownerUid, PickAvailableLootItem(codes), quantity);

    private int AddRootingFind(string ownerUid, int quantity)
    {
        if (serverApi == null || packRepository == null) return 0;
        int kind = serverApi.World.Rand.Next(4);
        if (kind == 0)
        {
            Block? mushroom = PickAvailableLootBlock(new[] {
                "game:mushroom-fieldmushroom-normal", "game:mushroom-chanterelle-normal" });
            if (mushroom != null)
            {
                packRepository.AddLoot(ownerUid, new ItemStack(mushroom, quantity));
                return quantity;
            }
        }
        if (kind == 1) return AddSpeciesFind(ownerUid, ScavengeLootCodes, quantity);
        return AddSpeciesFind(ownerUid, SpeciesRootCodes, quantity);
    }

    private static readonly string[] SpeciesEdibleCodes = {
        "game:fruit-blueberry", "game:fruit-redcurrant", "game:fruit-blackcurrant",
        "game:vegetable-carrot", "game:vegetable-turnip", "game:vegetable-parsnip" };
    private static readonly string[] SpeciesRootCodes = {
        "game:vegetable-carrot", "game:vegetable-turnip", "game:vegetable-parsnip", "game:vegetable-onion" };
    private static readonly string[] SpeciesSeedCodes = {
        "game:seeds-spelt", "game:seeds-rye", "game:seeds-flax", "game:grain-spelt", "game:grain-rye" };

    private int AddSpeciesExpeditionLoot(string ownerUid, string route,
        CompanionSpeciesExpeditionBonus? bonus, IReadOnlyList<FoxExpeditionMemberSummaryPacket>? party,
        float completion, FoxScavengeSiteRecord? site = null)
    {
        if (bonus == null || serverApi == null) return 0;
        Random random = serverApi.World.Rand;
        int count = 0;
        void Add(string name, Func<int> generate)
        {
            int found = generate();
            count += found;
            if (found > 0) bonus.Notes.Add($"{name} brought back an extra find.");
        }
        bool Roll(float chance) => chance > 0f && random.NextDouble() < chance;
        bool hunt = CompanionSpeciesTraits.IsHunt(route);
        bool forage = CompanionSpeciesTraits.IsForage(route);
        bool scavenge = CompanionSpeciesTraits.IsScavenge(route);
        if (hunt && Roll(bonus.SmallCatch)) Add("Cunning Tracker", () => AddSpeciesCatch(ownerUid, false, party));
        if (scavenge && Roll(bonus.UsefulFind)) Add("Nimble Paws", () => site != null
            ? AddScavengeFocusLoot(ownerUid, site, FoxScavengeSites.Useful, random)
            : AddSpeciesFind(ownerUid, ScavengeLootCodes, 3));
        if (forage && Roll(bonus.RootFind)) Add("Keen Snout", () => AddRootingFind(ownerUid, 3));
        if (forage && Roll(bonus.SeedFind)) Add("Sharp Eyes", () => AddSpeciesFind(ownerUid, SpeciesSeedCodes, 3));
        if ((hunt || scavenge) && Roll(bonus.OrdinaryFind)) Add("Nothing Wasted", () => hunt
            ? AddSpeciesCatch(ownerUid, false, party) : AddSpeciesFind(ownerUid, ScavengeLootCodes, 3));
        string reward = CompanionSpeciesTraits.RollSynergyReward(bonus, route, completion, random);
        Func<int>? generate = reward switch
        {
            "prime" => () => AddSpeciesCatch(ownerUid, true, party),
            "small" => () => AddSpeciesCatch(ownerUid, false, party),
            "useful" => () => site != null ? AddScavengeFocusLoot(ownerUid, site, FoxScavengeSites.Useful, random)
                : AddSpeciesFind(ownerUid, ScavengeLootCodes, 3),
            "ordinary" => () => AddSpeciesFind(ownerUid, ScavengeLootCodes, 3),
            "edible" => () => AddSpeciesFind(ownerUid, SpeciesEdibleCodes, 4),
            "roots" => () => AddRootingFind(ownerUid, 4),
            "seeds" => () => AddSpeciesFind(ownerUid, SpeciesSeedCodes, 4),
            _ => null
        };
        if (generate != null) Add(CompanionSpeciesTraits.Get(bonus.SynergySpecies)!.Synergy, generate);
        return count;
    }
}
