#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FeralKinshipCompanions;

public sealed partial class FeralKinshipCompanionSystem
{
    private static readonly string[] FurnitureRandomizers =
    {
        "game:stackrandomizer-clutter-furniture",
        "game:stackrandomizer-clutter-intact",
        "game:stackrandomizer-clutter-scribe",
        "game:stackrandomizer-clutter-workshop-basic",
        "game:stackrandomizer-clutter-science"
    };

    private void AbandonScavengeSite(IServerPlayer owner, long siteId)
    {
        if (packRepository == null) return;
        if (!packRepository.TryAbandonScavengeSite(owner.PlayerUID, siteId))
        {
            SendPackState(owner, "Site could not be abandoned while a party is there, or it no longer exists.");
            return;
        }
        packRepository.Save();
        SendPackState(owner, "The pack has abandoned that lead.");
    }

    private List<FoxScavengeSitePacket> BuildScavengeSitePackets(string ownerUid)
    {
        if (packRepository == null) return new List<FoxScavengeSitePacket>();
        return packRepository.GetScavengeSites(ownerUid)
            .Select(site => new FoxScavengeSitePacket
            {
                SiteId = site.SiteId,
                Label = FoxScavengeSites.DisplayLabel(site),
                Clues = site.Clues.Take(site.RevealedClues).ToList(),
                LayoutHint = site.Knowledge >= 2 ? FoxScavengeSites.LayoutHint(site) : string.Empty,
                DangerHint = site.Knowledge >= 3 ? FoxScavengeSites.DangerHint(site) : string.Empty,
                ConditionHint = site.Knowledge >= 4
                    ? $"{(site.Age == 3 ? "Ancient" : site.Age == 1 ? "More recent" : "Old")} site; "
                      + $"{(site.Integrity == 1 ? "fragile" : site.Integrity == 3 ? "sound" : "weathered")} construction; "
                      + (site.PreviouslyLooted ? "signs of earlier looting." : "little sign of earlier looting.")
                    : string.Empty,
                Barren = site.RemainingVisits <= 0,
                Busy = packRepository.IsScavengeSiteBusy(ownerUid, site.SiteId),
                OutsideSurveyComplete = FoxScavengeSites.HasLearnedAllFromOutside(site),
                SearchesStarted = Math.Max(0, site.SearchesStarted),
                ProspectHint = FoxScavengeSites.ProspectHint(site)
            })
            .ToList();
    }

    private ExpeditionResolution ResolveScavengeScout(FoxExpeditionRecord expedition)
    {
        if (serverApi == null || packRepository == null)
            return new ExpeditionResolution("The scouts could not complete their report.");

        string report;
        if (expedition.ScavengeSiteId > 0)
        {
            if (!packRepository.TryGetScavengeSite(
                    expedition.OwnerUid, expedition.ScavengeSiteId, out FoxScavengeSiteRecord? site)
                || site == null)
            {
                report = "The scouts returned, but that lead had already been abandoned.";
            }
            else
            {
                FoxScavengeSites.ApplyScout(site, expedition.ScoutDuration, serverApi.World.Rand,
                    expedition.ScoutInformationBonus);
                report = $"Scouts revisited {FoxScavengeSites.DisplayLabel(site)}. "
                    + string.Join(" ", site.Clues.Take(site.RevealedClues).TakeLast(3));
                if (site.Knowledge >= 2) report += " " + FoxScavengeSites.LayoutHint(site);
                if (site.Knowledge >= 3) report += " " + FoxScavengeSites.DangerHint(site);
                if (FoxScavengeSites.HasLearnedAllFromOutside(site))
                    report += " The scouts think they have found everything they can from outside this site.";
            }
        }
        else if (packRepository.GetScavengeSites(expedition.OwnerUid).Count
                 >= FoxScavengeSites.MaximumRememberedSites)
        {
            report = "The scouts returned without adding a lead; the pack already remembers three sites.";
        }
        else
        {
            float chance = expedition.ScoutDuration switch
            {
                FoxScavengeSites.LongScout => 0.95f,
                FoxScavengeSites.MediumScout => 0.78f,
                _ => 0.55f
            };
            if (serverApi.World.Rand.NextDouble() >= chance
                || !packRepository.TryCreateScavengeSite(
                    expedition.OwnerUid, serverApi.World.Rand, out FoxScavengeSiteRecord? site,
                    expedition.FavorDifficultScavengeSites)
                || site == null)
            {
                report = "The scouts returned with traces of old roads, but no place worth searching.";
            }
            else
            {
                FoxScavengeSites.ApplyScout(site, expedition.ScoutDuration, serverApi.World.Rand,
                    expedition.ScoutInformationBonus);
                report = $"The scouts discovered {FoxScavengeSites.DisplayLabel(site)}. "
                    + string.Join(" ", site.Clues.Take(site.RevealedClues));
                if (site.Knowledge >= 2) report += " " + FoxScavengeSites.LayoutHint(site);
                if (site.Knowledge >= 3) report += " " + FoxScavengeSites.DangerHint(site);
                if (FoxScavengeSites.HasLearnedAllFromOutside(site))
                    report += " The scouts think they have found everything they can from outside this site.";
            }
        }
        QueueExpeditionReturns(expedition.OwnerUid,
            expedition.SelectedFoxIds.Select(id => (id, "Returned healthy")), "expedition.return_normal");
        return new ExpeditionResolution(report);
    }

    private ExpeditionResolution ResolveScavengeSite(FoxExpeditionRecord expedition)
    {
        if (serverApi == null || packRepository == null
            || !packRepository.TryGetScavengeSite(
                expedition.OwnerUid, expedition.ScavengeSiteId, out FoxScavengeSiteRecord? site)
            || site == null)
        {
            QueueExpeditionReturns(expedition.OwnerUid,
                expedition.SelectedFoxIds.Select(id => (id, "Returned healthy")), "expedition.return_normal");
            return new ExpeditionResolution("The scavenge party returned, but its site record was lost.");
        }

        if (expedition.BlockedAtDoor)
        {
            QueueExpeditionReturns(expedition.OwnerUid,
                expedition.SelectedFoxIds.Select(id => (id, "Returned healthy")),
                expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal");
            return new ExpeditionResolution(
                $"The party reached {FoxScavengeSites.DisplayLabel(site)}, but could not force its heavy door. "
                + "The reserved site visit was refunded."
                + ScavengeLateNote(expedition, site, 0));
        }

        Random random = serverApi.World.Rand;
        if (random.NextDouble() < FoxScavengeSites.RivalPartyAbortChance)
        {
            QueueExpeditionReturns(expedition.OwnerUid,
                expedition.SelectedFoxIds.Select(id => (id, "Returned healthy")),
                expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal");
            return new ExpeditionResolution(
                $"The party saw another group enter {FoxScavengeSites.DisplayLabel(site)} first "
                + "and withdrew without searching. The visit was spent."
                + (site.RemainingVisits <= 0 ? " The site appears barren." : string.Empty)
                + ScavengeLateNote(expedition, site, 0));
        }

        Dictionary<string, string> outcomes = new(StringComparer.Ordinal);
        string? avoidable = RollScavengeAvoidableIncident(site, expedition, random);
        string? unavoidable = RollScavengeUnavoidableIncident(site, random);
        // Outfoxed avoids the encounter itself; injury-oriented instincts act on casualties below.
        if (avoidable != null && expedition.SpeciesBonus?.SynergySpecies == "fox"
            && CompanionSpeciesTraits.Mitigate(expedition.SpeciesBonus, avoidable, "encounter", random).Length == 0)
            avoidable = null;
        string incidentReport = string.Empty;
        int itemCount = 0;
        bool endedEarly = false;

        if (avoidable == "ringing")
        {
            bool killed = random.NextDouble() < Math.Clamp(
                0.18 + expedition.SelectedFoxIds.Count * 0.055
                    + expedition.ExpeditionStrength / Math.Max(1f, expedition.TargetStrength) * 0.12,
                0.15, 0.82);
            AddScavengeCasualty(expedition, outcomes, random, true, "ringing", expedition.SpeciesBonus?.SynergySpecies != "fox");
            if (killed)
            {
                site.ThreatCleared = true;
                itemCount = AddGreatEnemyLoot(expedition.OwnerUid, random);
                incidentReport = "The party drew the attention of a great ringing enemy and killed it. "
                    + "They brought back what they could take from the body. The ringing has stopped.";
            }
            else
            {
                incidentReport = "The party drew the attention of a great ringing enemy and fled, "
                    + "abandoning the search. The ringing remains.";
            }
            endedEarly = true;
        }
        else if (avoidable != null)
        {
            AddScavengeCasualty(expedition, outcomes, random, false, avoidable, expedition.SpeciesBonus?.SynergySpecies != "fox");
            incidentReport = avoidable switch
            {
                "locusts" => "The party was caught by a mass of locusts.",
                "fragile" => "Too many companions crossed an unstable floor, and part of it gave way.",
                "narrow" => "The crowded party was trapped in a narrow passage.",
                "occupied" => "The party ran into hostile occupants.",
                "traps" => "The party triggered an old trap.",
                _ => "The party encountered a preventable hazard."
            };
            endedEarly = random.NextDouble() < 0.40;
        }

        if (unavoidable != null)
        {
            string? casualtyOutcome = AddScavengeCasualty(expedition, outcomes, random, false,
                unavoidable.Contains("hole", StringComparison.Ordinal) ? "dangerous-descent" : "hidden-trap");
            string incidentText = unavoidable == "A companion became separated in the interior."
                ? casualtyOutcome switch
                {
                    "MIA" => "A companion became separated in the interior and did not return.",
                    "Mortally wounded" => "A companion became separated in the interior and was found, then carried home.",
                    "Returned injured" => "A companion became separated in the interior but was found and returned hurt.",
                    _ => "The party briefly lost sight of one another inside, then regrouped."
                }
                : unavoidable;
            incidentReport += (incidentReport.Length > 0 ? " " : string.Empty) + incidentText;
        }

        if (!endedEarly)
        {
            itemCount += AddSpeciesExpeditionLoot(expedition.OwnerUid, expedition.Type,
                expedition.SpeciesBonus, expedition.MemberSnapshots, 1f, site);
            int searchNumber = Math.Max(1, site.SearchesStarted);
            float progress = site.TotalVisits <= 1 ? 1f
                : (searchNumber - 1f) / (site.TotalVisits - 1f);
            float yield = (1f - 0.75f * Math.Clamp(progress, 0f, 1f))
                * (site.PreviouslyLooted ? 0.75f : 1f)
                * (site.Integrity == 1 ? 0.85f : 1f)
                * FoxScavengeSites.RoutineSalvageMultiplier(site);
            int rolls = Math.Max(1, (int)Math.Round((3 + site.Size * 2) * yield));
            if (expedition.LargeScavengeParty && expedition.SelectedFoxIds.Count >= 6)
                rolls += GameMath.RoundRandom(random, rolls * 0.20f);
            for (int i = 0; i < rolls; i++)
            {
                string focus = expedition.ScavengeFocus == FoxScavengeSites.Mixed
                    ? (random.Next(2) == 0 ? FoxScavengeSites.Useful : FoxScavengeSites.Furniture)
                    : expedition.ScavengeFocus;
                itemCount += AddScavengeFocusLoot(expedition.OwnerUid, site, focus, random);
            }
            if (site.KindId == "smithy"
                && random.NextDouble() < FoxScavengeSalvageCatalog.SmithyAnvilChancePerSearch)
            {
                int anvil = AddScavengeDirectLoot(expedition.OwnerUid, "b", "anvil-copper", random);
                if (anvil > 0)
                {
                    itemCount += anvil;
                    incidentReport += (incidentReport.Length > 0 ? " " : string.Empty)
                        + "Beneath the forge rubble, they found an intact copper anvil.";
                }
            }
            float breakthroughChance = 0.10f + 0.30f * Math.Clamp(progress, 0f, 1f)
                + 0.02f * (site.Age - 2);
            if (random.NextDouble() < breakthroughChance)
            {
                itemCount += AddScavengeBreakthrough(expedition.OwnerUid, site, random);
                incidentReport += (incidentReport.Length > 0 ? " " : string.Empty)
                    + "The party also uncovered an unexpected find.";
            }
            if (site.DangerRule == "flooded")
                incidentReport += (incidentReport.Length > 0 ? " " : string.Empty)
                    + "Flooded rooms limited the routine salvage they could carry out.";
        }

        QueueExpeditionReturns(expedition.OwnerUid,
            expedition.SelectedFoxIds.Select(id =>
                (id, outcomes.TryGetValue(id, out string? status) ? status : "Returned healthy")),
            expedition.RunningLate ? "expedition.return_late" : "expedition.return_normal");
        string description = endedEarly
            ? $"The search at {FoxScavengeSites.DisplayLabel(site)} ended early."
            : $"The party searched {FoxScavengeSites.DisplayLabel(site)} and returned with {itemCount} item"
                + (itemCount == 1 ? "." : "s.");
        if (site.RemainingVisits <= 0) description += " The site appears barren.";
        return new ExpeditionResolution(description
            + (incidentReport.Length > 0 ? " " + incidentReport : "")
            + ScavengeLateNote(expedition, site, itemCount));
    }

    private string ScavengeLateNote(FoxExpeditionRecord expedition, FoxScavengeSiteRecord site,
        int itemCount) => expedition.RunningLate && serverApi != null
            ? " " + FoxScavengeSites.DescribeLateReturn(site, itemCount, serverApi.World.Rand)
            : string.Empty;

    private static string? RollScavengeAvoidableIncident(
        FoxScavengeSiteRecord site, FoxExpeditionRecord expedition, Random random)
    {
        int partySize = expedition.SelectedFoxIds.Count;
        double risk = Math.Clamp(1d - expedition.PreparationRiskReduction
            - expedition.PatrolRiskReduction - expedition.PerkRiskReduction, 0.40d, 1d);
        if (expedition.QuietScavengeParty && partySize <= 3) risk *= 0.75d;
        if (site.LayoutRule == "fragile" && partySize > site.LayoutThreshold
            && random.NextDouble() < (site.Integrity == 1 ? 0.55 : 0.35) * risk) return "fragile";
        if (site.LayoutRule == "narrow" && partySize > site.LayoutThreshold
            && random.NextDouble() < 0.30 * risk) return "narrow";
        if (site.DangerRule == "ringing" && !site.ThreatCleared
            && partySize > site.DangerThreshold
            && random.NextDouble() < 0.65 * risk) return "ringing";
        if (site.DangerRule == "locusts" && partySize < site.DangerThreshold
            && random.NextDouble() < 0.55 * risk) return "locusts";
        if (site.DangerRule == "occupied" && partySize < site.DangerThreshold
            && random.NextDouble() < 0.45 * risk) return "occupied";
        if (site.DangerRule == "traps"
            && random.NextDouble() < 0.20 * risk) return "traps";
        return null;
    }

    private static string? RollScavengeUnavoidableIncident(
        FoxScavengeSiteRecord site, Random random)
    {
        // Site danger may differ; party size, focus, and amount of scouting
        // never affect whether this branch happens.
        if (random.NextDouble() >= 0.035 + 0.025 * site.Danger) return null;
        return random.Next(3) switch
        {
            0 => "One companion fell into a concealed hole.",
            1 => "A companion became separated in the interior.",
            _ => "A hidden trap was triggered without warning."
        };
    }

    private static string? AddScavengeCasualty(
        FoxExpeditionRecord expedition, Dictionary<string, string> outcomes,
        Random random, bool greatEnemy, string cause = "hidden-trap", bool allowSynergy = true)
    {
        List<string> eligible = expedition.SelectedFoxIds
            .Where(id => !outcomes.ContainsKey(id)).ToList();
        if (eligible.Count == 0) return null;
        string foxId = eligible[random.Next(eligible.Count)];
        double roll = random.NextDouble();
        string consequence = greatEnemy
            ? roll < 0.40 ? "injury" : roll < 0.75 ? "mortal" : "mia"
            : roll < 0.68 ? "injury" : roll < 0.88 ? "mortal" : "mia";
        consequence = CompanionSpeciesTraits.Mitigate(expedition.SpeciesBonus, cause, consequence, random, allowSynergy);
        if (consequence.Length == 0) return null;
        outcomes[foxId] = consequence == "injury" ? "Returned injured"
            : consequence == "mortal" ? "Mortally wounded" : "MIA";
        return outcomes[foxId];
    }

    private int AddGreatEnemyLoot(string ownerUid, Random random)
    {
        if (serverApi == null) return 0;
        int count = 0;
        count += AddExpeditionItem(ownerUid,
            serverApi.World.GetItem(new AssetLocation("game:gear-rusty")),
            Math.Max(0, GameMath.RoundRandom(random, 2.5f + random.NextSingle() * 5f)));
        count += AddExpeditionItem(ownerUid,
            serverApi.World.GetItem(new AssetLocation("game:flaxfibers")), 2);
        count += AddExpeditionItem(ownerUid,
            serverApi.World.GetItem(new AssetLocation("game:gear-temporal")),
            Math.Max(0, GameMath.RoundRandom(random, 0.6f + random.NextSingle() * 0.8f)));
        if (random.NextDouble() < 0.40)
            count += AddResolvedRandomizerLoot(ownerUid, "game:stackrandomizer-alljonas");
        return count;
    }

    private int AddScavengeFocusLoot(
        string ownerUid, FoxScavengeSiteRecord site, string focus, Random random)
    {
        if (serverApi == null || packRepository == null) return 0;
        if (focus is FoxScavengeSites.Useful or FoxScavengeSites.Furniture
            && random.NextDouble() < FoxScavengeSalvageCatalog.RoutineSignatureChance)
        {
            int tier = FoxScavengeSalvageCatalog.RollRoutineTier(random);
            int signature = AddScavengeSignatureLoot(ownerUid, site, focus, tier, random);
            if (signature > 0) return signature;
        }
        if (focus == FoxScavengeSites.Useful)
        {
            if (random.NextDouble() < 0.18)
            {
                string[] vessels = site.LootFamily switch
                {
                    "farm" => new[] { "game:lootvessel-farming", "game:lootvessel-seed" },
                    "mine" => new[] { "game:lootvessel-ore", "game:lootvessel-tool" },
                    "workshop" or "smithing" or "military" => new[] { "game:lootvessel-tool", "game:lootvessel-ore" },
                    "provisions" or "hospitality" => new[] { "game:lootvessel-food" },
                    _ => Array.Empty<string>()
                };
                Block? vessel = vessels.Length > 0 ? PickAvailableLootBlock(vessels) : null;
                if (vessel != null)
                {
                    ItemStack[] drops = vessel.GetDrops(serverApi.World, new BlockPos(0, 0, 0), null);
                    int count = 0;
                    foreach (ItemStack drop in drops ?? Array.Empty<ItemStack>())
                    {
                        if (drop?.Collectible == null || drop.StackSize <= 0
                            || IsForbiddenScavengeLoot(drop)) continue;
                        packRepository.AddLoot(ownerUid, drop);
                        count += drop.StackSize;
                    }
                    if (count > 0) return count;
                }
            }
            string[] pools = UsefulFallbackPools(site.LootFamily);
            return AddScavengeResolvedRandomizerLoot(ownerUid,
                pools[random.Next(pools.Length)]);
        }
        if (focus == FoxScavengeSites.Furniture)
        {
            if (random.NextDouble() < 0.68)
            {
                string[] pools = FurnitureFallbackPools(site.LootFamily);
                string pool = pools[random.Next(pools.Length)];
                int count = AddScavengeResolvedRandomizerLoot(ownerUid, pool);
                if (count > 0) return count;
            }
            Block[] candidates = serverApi.World.Blocks
                .Where(block => block?.Code != null
                    && (block.Code.Path.StartsWith("chair-", StringComparison.Ordinal)
                        || block.Code.Path.StartsWith("table-", StringComparison.Ordinal)
                        || block.Code.Path.StartsWith("bookshelf-", StringComparison.Ordinal)))
                .ToArray();
            if (candidates.Length == 0) return 0;
            packRepository.AddLoot(ownerUid, new ItemStack(candidates[random.Next(candidates.Length)]));
            return 1;
        }
        if (focus == FoxScavengeSites.Walls)
        {
            if (random.NextDouble() < 0.18)
            {
                string metal = random.NextDouble() < 0.72 ? "copper" : "tinbronze";
                int fittings = AddScavengeDirectLoot(ownerUid, "i",
                    $"metalnailsandstrips-{metal}", random);
                if (fittings > 0) return fittings;
            }
            if (random.NextDouble() < 0.80)
            {
                int count = AddScavengeResolvedRandomizerLoot(ownerUid,
                    "game:stackrandomizer-materials-building");
                if (count > 0) return count;
            }
            Block[] candidates = serverApi.World.Blocks
                .Where(block => block?.Code != null
                    && (block.Code.Path.Contains("window", StringComparison.Ordinal)
                        || block.Code.Path.StartsWith("fence-", StringComparison.Ordinal)
                        || block.Code.Path.StartsWith("cobblestone-", StringComparison.Ordinal)))
                .ToArray();
            if (candidates.Length == 0) return 0;
            int amount = random.Next(2, 6);
            packRepository.AddLoot(ownerUid,
                new ItemStack(candidates[random.Next(candidates.Length)], amount));
            return amount;
        }
        return 0;
    }

    private int AddScavengeBreakthrough(
        string ownerUid, FoxScavengeSiteRecord site, Random random)
    {
        // The search focus is deliberately absent. Site identity may affect
        // which find is plausible, but useful/furniture/walls never does.
        if (site.KindId == "unknowable")
            return GenerateAncientFind(ownerUid);
        if (random.NextDouble() < FoxScavengeSalvageCatalog.BreakthroughSiteFindChance)
        {
            int signature = AddScavengeSignatureLoot(ownerUid, site, "any", 2, random);
            if (signature > 0) return signature;
        }
        if (random.NextDouble() < 0.55)
            return GenerateAncientFind(ownerUid);
        string[] pools =
        {
            "game:stackrandomizer-lantern",
            "game:stackrandomizer-painting",
            "game:stackrandomizer-tuningcylinder",
            "game:stackrandomizer-alljonas"
        };
        return AddScavengeResolvedRandomizerLoot(ownerUid, pools[random.Next(pools.Length)]);
    }

    private int AddScavengeSignatureLoot(string ownerUid, FoxScavengeSiteRecord site,
        string focus, int tier, Random random)
    {
        string filter = focus == FoxScavengeSites.Furniture ? "f"
            : focus == FoxScavengeSites.Useful ? "u" : "any";
        FoxScavengeSalvageCatalog.Choice? choice = FoxScavengeSalvageCatalog.Choose(
            site.KindId, site.LootFamily, filter, tier, random);
        if (choice == null) return 0;
        int count = choice.Type == "p"
            ? AddScavengeResolvedRandomizerLoot(ownerUid, $"game:{choice.Code}")
            : AddScavengeDirectLoot(ownerUid, choice.Type, choice.Code, random);
        return count > 0 ? count : AddScavengeFamilyFallback(ownerUid, site, filter, tier, random);
    }

    private int AddScavengeFamilyFallback(string ownerUid, FoxScavengeSiteRecord site,
        string focus, int tier, Random random)
    {
        FoxScavengeSalvageCatalog.Choice? choice = FoxScavengeSalvageCatalog.Choose(
            string.Empty, site.LootFamily, focus, tier, random);
        return choice == null ? 0 : choice.Type == "p"
            ? AddScavengeResolvedRandomizerLoot(ownerUid, $"game:{choice.Code}")
            : AddScavengeDirectLoot(ownerUid, choice.Type, choice.Code, random);
    }

    private int AddScavengeDirectLoot(string ownerUid, string type, string code, Random random)
    {
        if (serverApi == null || packRepository == null) return 0;
        ItemStack? stack = type switch
        {
            "i" => serverApi.World.GetItem(new AssetLocation($"game:{code}")) is Item item
                ? new ItemStack(item) : null,
            "b" => serverApi.World.GetBlock(new AssetLocation($"game:{code}")) is Block block
                ? new ItemStack(block) : null,
            "c" => serverApi.World.GetBlock(new AssetLocation("game:clutter")) is Block clutter
                ? new ItemStack(clutter) : null,
            _ => null
        };
        if (stack == null || IsForbiddenScavengeLoot(stack)) return 0;
        if (type == "c")
        {
            stack.Attributes.SetString("type", code);
            stack.Attributes.SetBool("collected", true);
        }
        if (code == "paper-parchment") stack.StackSize = random.Next(2, 6);
        else if (code.StartsWith("metalnailsandstrips-", StringComparison.Ordinal))
            stack.StackSize = random.Next(2, 5);
        else if (code is "flaxfibers" or "flaxtwine" or "stick" or "stone-chalk"
            or "stone-limestone" or "charcoal" or "clay-blue" or "clay-fire")
            stack.StackSize = random.Next(2, 5);
        packRepository.AddLoot(ownerUid, stack);
        return stack.StackSize;
    }

    private int AddScavengeResolvedRandomizerLoot(string ownerUid, string code)
    {
        if (packRepository == null) return 0;
        ItemStack? stack = ResolveVanillaRandomizer(code);
        if (stack == null || stack.StackSize <= 0 || IsForbiddenScavengeLoot(stack)) return 0;
        packRepository.AddLoot(ownerUid, stack);
        return stack.StackSize;
    }

    private static bool IsForbiddenScavengeLoot(ItemStack stack) =>
        stack.Collectible?.Code?.Path.StartsWith("leather-sturdy-", StringComparison.Ordinal) == true;

    private static string[] UsefulFallbackPools(string family) => family switch
    {
        "records" or "civic" or "religious" => new[] { "game:stackrandomizer-library", "game:stackrandomizer-lore-villager" },
        "mine" or "construction" or "publicworks" => new[] { "game:stackrandomizer-materials-mining", "game:stackrandomizer-resource" },
        "smithing" or "workshop" or "forestry" => new[] { "game:stackrandomizer-coppertool", "game:stackrandomizer-resource" },
        "military" or "fort" => new[] { "game:stackrandomizer-ruinedweapon", "game:stackrandomizer-armor" },
        "farm" or "fieldcamp" => new[] { "game:stackrandomizer-seed", "game:stackrandomizer-resource" },
        "textile" or "leather" or "store" => new[] { "game:stackrandomizer-cloth-lowstatus", "game:stackrandomizer-resource" },
        "laboratory" or "oddity" => new[] { "game:stackrandomizer-resource", "game:stackrandomizer-gear" },
        "chemical" or "ceramics" => new[] { "game:stackrandomizer-resource", "game:stackrandomizer-materials-building" },
        _ => new[] { "game:stackrandomizer-kitchen", "game:stackrandomizer-resource" }
    };

    private static string[] FurnitureFallbackPools(string family) => family switch
    {
        "records" or "civic" or "religious" => new[] { "game:stackrandomizer-clutter-scribe", "game:stackrandomizer-clutter-science" },
        "laboratory" or "chemical" => new[] { "game:stackrandomizer-clutter-lab", "game:stackrandomizer-clutter-science" },
        "smithing" or "workshop" or "construction" or "forestry" => new[] { "game:stackrandomizer-clutter-workshop-basic", "game:stackrandomizer-clutter-workshop-advanced" },
        "fort" or "military" => new[] { "game:stackrandomizer-clutter-ruined-weapons", "game:stackrandomizer-clutter-chains-and-fences" },
        "mine" or "fieldcamp" => new[] { "game:stackrandomizer-mines", "game:stackrandomizer-clutter-workshop-basic" },
        "textile" or "leather" => new[] { "game:stackrandomizer-clutter-ruined-toys-clothing", "game:stackrandomizer-clutter-furniture" },
        "oddity" => new[] { "game:stackrandomizer-clutter-displaycase", "game:stackrandomizer-clutter-science" },
        _ => FurnitureRandomizers
    };
}
