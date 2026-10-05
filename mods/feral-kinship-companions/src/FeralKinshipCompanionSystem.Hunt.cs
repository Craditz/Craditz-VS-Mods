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
    private sealed record HuntCorpseSource(string Species, string CorpseCode, string AnimalCode);
    private sealed record HuntCorpseChoice(HuntCorpseSource Source, Item Corpse,
        BlockDropItemStack[] Drops, float MeatEquivalent);

    private static readonly HuntCorpseSource[] SmallHuntSources =
    {
        new("hare", "butchering:deadhare-male-arctic-1-dead", "game:hare-arctic-adult-male"),
        new("hare", "butchering:deadhare-female-european-1-dead", "game:hare-european-adult-female"),
        new("raccoon", "butchering:deadraccoon-male-common-1-dead", "game:raccoon-common-adult-male"),
        new("raccoon", "butchering:deadraccoon-female-common-1-dead", "game:raccoon-common-adult-female"),
        new("chicken", "butchering:deadchicken-male-1-dead", "game:chicken-rooster"),
        new("chicken", "butchering:deadchicken-female-1-dead", "game:chicken-hen")
    };

    private static readonly HuntCorpseSource[] PrimeHuntSources =
    {
        new("deer", "butchering:deaddeer-male-adultlarge-1-dead", "game:deer-whitetail-adult-male"),
        new("goat", "butchering:deadgoat-male-adult-1-dead", "game:goat-nubian-adult-male")
    };

    private bool TryGenerateHuntBundle(
        string ownerUid, IReadOnlyList<FoxExpeditionMemberSummaryPacket>? party, out int count)
    {
        count = 0;
        if (serverApi == null || packRepository == null) return false;

        string? excludedSpecies = null;
        if (party is { Count: > 0 }
            && party.All(member => !string.IsNullOrWhiteSpace(member.SpeciesId)
                && string.Equals(member.SpeciesId, party[0].SpeciesId,
                    StringComparison.OrdinalIgnoreCase)))
            excludedSpecies = party[0].SpeciesId;

        List<HuntCorpseChoice> primes = PrimeHuntSources
            .Where(source => !string.Equals(source.Species, excludedSpecies,
                StringComparison.OrdinalIgnoreCase))
            .Select(ResolveHuntCorpse).Where(choice => choice != null)
            .Select(choice => choice!).ToList();
        List<HuntCorpseChoice> small = SmallHuntSources
            .Where(source => !string.Equals(source.Species, excludedSpecies,
                StringComparison.OrdinalIgnoreCase))
            .Select(ResolveHuntCorpse).Where(choice => choice != null)
            .Select(choice => choice!).ToList();
        if (primes.Count == 0 || small.Count == 0) return false;

        Random random = serverApi.World.Rand;
        HuntCorpseChoice prime = primes[random.Next(primes.Count)];
        List<HuntCorpseChoice> bundle = new() { prime };
        float expectedMeat = prime.MeatEquivalent;
        int target = random.Next(12, 25);
        HashSet<string> usedSmallSpecies = new(StringComparer.OrdinalIgnoreCase);
        while (bundle.Count < 9 && (bundle.Count < 4 || expectedMeat < target))
        {
            List<HuntCorpseChoice> choices = small
                .Where(choice => !usedSmallSpecies.Contains(choice.Source.Species))
                .ToList();
            if (choices.Count == 0)
            {
                usedSmallSpecies.Clear();
                choices = small;
            }
            HuntCorpseChoice picked = choices[random.Next(choices.Count)];
            if (bundle.Count >= 4 && expectedMeat + picked.MeatEquivalent > 25f) break;
            bundle.Add(picked);
            usedSmallSpecies.Add(picked.Source.Species);
            expectedMeat += picked.MeatEquivalent;
        }

        foreach (HuntCorpseChoice choice in bundle)
        {
            ItemStack corpse = new(choice.Corpse);
            // Butchering normally copies these values when a real animal dies.
            // Use the loaded entity behavior so asset patches and other mods' edits
            // to the harvest table carry through to the returned corpse.
            corpse.Attributes.SetFloat("animalWeight", 1f);
            corpse.Attributes.SetString("AnimalDrops", JsonConvert.SerializeObject(choice.Drops));
            packRepository.AddLoot(ownerUid, corpse);
            count++;
        }
        return true;
    }

    private HuntCorpseChoice? ResolveHuntCorpse(HuntCorpseSource source)
    {
        if (serverApi == null) return null;
        Item? corpse = ResolveExpeditionLootItem(source.CorpseCode);
        EntityProperties? animal = serverApi.World.GetEntityType(new AssetLocation(source.AnimalCode));
        if (corpse == null || animal?.Server?.BehaviorsAsJsonObj == null) return null;
        foreach (var behavior in animal.Server.BehaviorsAsJsonObj)
        {
            if (behavior == null || !string.Equals(behavior["code"].AsString(), "harvestable", StringComparison.Ordinal))
                continue;
            BlockDropItemStack[]? drops = behavior["drops"].AsObject<BlockDropItemStack[]>(null);
            if (drops == null || drops.Length == 0) return null;
            float meat = drops.Sum(drop =>
            {
                string path = drop.Code?.Path ?? string.Empty;
                float equivalent = path switch
                {
                    "redmeat-raw" => 1f,
                    "bushmeat-raw" or "poultry-raw" => 0.75f,
                    _ => 0f
                };
                return equivalent * Math.Max(0f, drop.Quantity?.avg ?? 0f);
            });
            return meat > 0f ? new HuntCorpseChoice(source, corpse, drops, meat) : null;
        }
        return null;
    }

    private int AddRawHuntMeat(string ownerUid, float quantityFactor)
    {
        if (serverApi == null) return 0;
        bool redMeat = serverApi.World.Rand.NextDouble() < 0.5;
        Item? item = serverApi.World.GetItem(new AssetLocation(redMeat
            ? "game:redmeat-raw" : "game:bushmeat-raw"));
        int amount = redMeat ? serverApi.World.Rand.Next(12, 25)
            : serverApi.World.Rand.Next(18, 31);
        return AddExpeditionItem(ownerUid, item,
            Math.Max(1, (int)Math.Round(amount * quantityFactor)));
    }
}
