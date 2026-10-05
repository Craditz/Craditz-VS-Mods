#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace FeralKinshipCompanions;

/// <summary>
/// Fictional expedition leads. These never claim to identify a worldgen structure.
/// The site and its clue deck are rolled once and persist across later scouts.
/// </summary>
internal static class FoxScavengeSites
{
    public const int MaximumRememberedSites = 3;
    public const string Useful = "useful";
    public const string Furniture = "furniture";
    public const string Mixed = "mixed";
    public const string Walls = "walls";
    public const string ShortScout = "short";
    public const string MediumScout = "medium";
    public const string LongScout = "long";
    public const double RivalPartyAbortChance = 0.015;

    internal sealed record Kind(string Id, string Name, string FirstClue, int Weight, float IdentifyChance, string LootFamily);

    private static readonly Kind[] Kinds =
    {
        new("house", "House", "A small building with the remains of living quarters.", 18, 0.95f, "home"),
        new("farm", "Farmstead", "Several outbuildings stand beside an overgrown yard.", 15, 0.85f, "farm"),
        new("cabin", "Hunter's cabin", "A modest timber shelter lies beyond an old trail.", 9, 0.85f, "home"),
        new("barn", "Barn or stable", "A long building has broad doors and old stalls.", 8, 0.85f, "farm"),
        new("workshop", "Workshop", "Heavy benches and scattered fittings fill the interior.", 10, 0.58f, "workshop"),
        new("inn", "Roadside inn", "Many small rooms surround a larger common room.", 6, 0.62f, "home"),
        new("store", "Store or trader's house", "Shelving and a broad front entrance remain.", 7, 0.46f, "store"),
        new("bakery", "Bakery or brewery", "An old hearth dominates the rear room.", 5, 0.50f, "workshop"),
        new("mill", "Mill", "A large mechanism once occupied the center.", 5, 0.52f, "workshop"),
        new("warehouse", "Warehouse or granary", "A broad hall holds rows of empty supports.", 6, 0.34f, "store"),
        new("school", "Schoolhouse", "The rooms contain rows of low, matching fittings.", 4, 0.22f, "records"),
        new("firehouse", "Firehouse or watch station", "Large doors open onto an unusually clear yard.", 3, 0.18f, "workshop"),
        new("office", "Office or records hall", "Many small rooms hold shelves and writing surfaces.", 4, 0.20f, "records"),
        new("clinic", "Clinic or apothecary", "Narrow beds and storage cabinets remain.", 3, 0.28f, "records"),
        new("crypt", "Crypt", "Stone chambers continue below a weathered entrance.", 4, 0.18f, "crypt"),
        new("chapel", "Chapel", "A tall central room is surrounded by smaller alcoves.", 4, 0.36f, "crypt"),
        new("watchpost", "Watchpost", "A compact stone building overlooks an old route.", 4, 0.55f, "fort"),
        new("manor", "Manor", "A sprawling house sits within a ruined enclosure.", 2, 0.40f, "home"),
        new("mine", "Mine complex", "Sheds and a descending passage share one enclosure.", 2, 0.40f, "mine"),
        new("quarry", "Quarry works", "Cut stone and abandoned equipment cover a broad cut.", 2, 0.45f, "mine"),
        new("university", "University", "Several wings connect around a large court.", 1, 0.15f, "records"),
        new("fort", "Fortress outwork", "Thick walls surround rooms of uncertain purpose.", 1, 0.27f, "fort"),
        new("library", "Underground library", "Rows of alcoves continue well below the surface.", 1, 0.12f, "records"),
        new("unknowable", "", "The scouts found an entrance but could not name the place.", 1, 0f, "unknown"),

        // Extensions to the established families. IDs are stable save keys.
        new("townhouse", "Townhouse", "Several floors of living quarters face an old street.", 3, 0.80f, "home"),
        new("servants-quarters", "Servants' quarters", "Small rooms repeat behind a larger house.", 2, 0.42f, "home"),
        new("estate-cottage", "Estate cottage", "A tidy cottage stands beyond the main grounds.", 2, 0.72f, "home"),
        new("gatekeeper-lodge", "Gatekeeper's lodge", "A little dwelling adjoins a ruined entrance.", 2, 0.54f, "home"),
        new("orchard", "Orchard", "Old rows of fruit trees surround a storage shed.", 3, 0.83f, "farm"),
        new("apiary", "Apiary", "Several empty hive stands remain beside a small shelter.", 2, 0.62f, "farm"),
        new("vineyard", "Vineyard", "Terraced rows and a press shelter cover the slope.", 2, 0.55f, "farm"),
        new("shepherd-station", "Shepherd's station", "A low shelter and old pens overlook pasture.", 2, 0.57f, "farm"),
        new("dairy", "Dairy", "A cool workroom still holds shelves and crocks.", 2, 0.48f, "farm"),
        new("cooperage", "Cooperage", "Barrel staves and workbenches fill a narrow building.", 2, 0.43f, "workshop"),
        new("glassworks", "Glassworks", "A broad furnace stands among broken glass.", 2, 0.38f, "workshop"),
        new("clockmaker", "Clockmaker's workshop", "Tiny fittings cover benches beneath a tall window.", 1, 0.25f, "workshop"),
        new("instrument-maker", "Instrument maker", "Fine tools remain in a room of fitted shelves.", 1, 0.24f, "workshop"),
        new("carpenter", "Carpenter's workshop", "Sawhorses and benches fill a timber building.", 2, 0.56f, "workshop"),
        new("mason", "Mason's workshop", "Cut stone and measuring marks surround a workbench.", 2, 0.49f, "workshop"),
        new("cloth-merchant", "Cloth merchant", "Empty rails and folded fabric fill a shop.", 2, 0.47f, "store"),
        new("hardware-merchant", "Hardware merchant", "Bins of fittings line the remaining shelves.", 2, 0.43f, "store"),
        new("chandlery", "Chandlery", "Wax and old lamp fittings mark a small shop.", 2, 0.38f, "store"),
        new("pawnshop", "Pawnshop", "A barred counter faces closely packed shelves.", 1, 0.35f, "store"),
        new("market-warehouse", "Market warehouse", "Many loading bays face a long roofed hall.", 2, 0.38f, "store"),
        new("courthouse-archive", "Courthouse archive", "Ranks of shelves fill a stone annex.", 2, 0.21f, "records"),
        new("map-room", "Map room", "Long tables and broad cabinets stand under a high roof.", 1, 0.18f, "records"),
        new("observatory", "Observatory", "An upper chamber opens toward the sky.", 1, 0.28f, "records"),
        new("survey-office", "Survey office", "Measuring marks and writing desks fill the rooms.", 2, 0.24f, "records"),
        new("museum-collection", "Museum collection", "Display cases fill a hall of uncertain purpose.", 1, 0.12f, "records"),
        new("mausoleum", "Mausoleum", "A decorated stone entrance leads into burial chambers.", 2, 0.29f, "crypt"),
        new("gravekeeper-house", "Gravekeeper's house", "A little dwelling stands beside old graves.", 2, 0.55f, "crypt"),
        new("monastery-crypt", "Monastery crypt", "Stone cells descend beneath a large cloister.", 1, 0.12f, "crypt"),
        new("supply-depot", "Supply depot", "Storehouses sit behind a defensive wall.", 2, 0.42f, "fort"),
        new("signal-tower", "Signal/watch tower", "A tall lookout rises over a deserted road.", 2, 0.45f, "fort"),
        new("prospecting-camp", "Prospecting camp", "Small tents and tool sheds surround shallow cuts.", 3, 0.56f, "mine"),
        new("ore-washing", "Ore washing works", "Channels and sorting tables cross a streambed.", 2, 0.35f, "mine"),
        new("assay-hut", "Assay hut", "A small furnace stands beside trays of stone samples.", 2, 0.31f, "mine"),
        new("abandoned-adit", "Abandoned adit", "A braced tunnel disappears beneath the hillside.", 2, 0.47f, "mine"),

        // New families. One family is chosen before its location, so expansion
        // does not dilute the chance of finding an unknowable site.
        new("smithy", "Smithy", "A forge and heavy workbench remain inside.", 4, 0.68f, "smithing"),
        new("foundry", "Foundry", "Large hearths stand among old casting channels.", 2, 0.48f, "smithing"),
        new("armorer-shop", "Armorer's shop", "Metal plates and fitting benches line the walls.", 2, 0.36f, "smithing"),
        new("bell-foundry", "Bell foundry", "A deep casting pit lies below the furnace.", 1, 0.20f, "smithing"),
        new("metalworks", "Metalworks", "Several workshops share one soot-darkened hall.", 2, 0.34f, "smithing"),
        new("weaver-house", "Weaver's house", "Lengths of thread hang across a workroom.", 3, 0.64f, "textile"),
        new("tailor-shop", "Tailor's workshop", "Cutting tables and clothing racks remain.", 3, 0.56f, "textile"),
        new("dyer-yard", "Dyer's yard", "Stained vats stand around a roofless court.", 2, 0.39f, "textile"),
        new("fullery", "Fullery", "Water channels lead through a cloth-processing hall.", 1, 0.19f, "textile"),
        new("ropewalk", "Ropewalk", "A very long narrow shed follows the old road.", 2, 0.30f, "textile"),
        new("tannery", "Tannery", "Empty hide frames surround stained vats.", 3, 0.60f, "leather"),
        new("saddler-shop", "Saddler's shop", "Straps and leather patterns cover the benches.", 2, 0.42f, "leather"),
        new("cobbler-shop", "Cobbling workshop", "Shoe forms fill a cramped workroom.", 2, 0.43f, "leather"),
        new("hide-yard", "Hide yard", "Drying frames stand in an overgrown yard.", 2, 0.57f, "leather"),
        new("pottery", "Pottery", "A small kiln stands among shelves of vessels.", 3, 0.65f, "ceramics"),
        new("brickworks", "Brickworks", "Stacks of fired brick surround a broad kiln.", 3, 0.63f, "ceramics"),
        new("tile-kiln", "Tile kiln", "Decorated fired pieces lie near a narrow furnace.", 2, 0.40f, "ceramics"),
        new("kiln-yard", "Kiln yard", "Several kilns and clay pits share an open yard.", 2, 0.50f, "ceramics"),
        new("smokehouse", "Smokehouse", "Hooks hang above an old smoking hearth.", 3, 0.62f, "provisions"),
        new("butcher-shop", "Butcher's shop", "Heavy tables and hooks fill a tiled room.", 2, 0.55f, "provisions"),
        new("cheesehouse", "Cheesehouse", "Cool shelves and empty crocks remain.", 2, 0.35f, "provisions"),
        new("curing-cellar", "Curing cellar", "Rows of jars descend into a cool cellar.", 2, 0.36f, "provisions"),
        new("cider-press", "Cider press", "A press stands beside barrels and old fruit bins.", 2, 0.48f, "provisions"),
        new("laboratory", "Laboratory", "Glassware and fitted benches fill several rooms.", 3, 0.38f, "laboratory"),
        new("assay-office", "Assay office", "Scales and ore trays lie beneath a locked counter.", 2, 0.25f, "laboratory"),
        new("specimen-room", "Specimen room", "Glass tanks and numbered shelves line the walls.", 2, 0.16f, "laboratory"),
        new("dissection-room", "Dissection room", "Narrow tables stand among medical cabinets.", 2, 0.19f, "laboratory"),
        new("experimental-workshop", "Experimental workshop", "Unfamiliar machinery crowds a large workroom.", 1, 0.13f, "laboratory"),
        new("barracks", "Barracks", "Rows of sleeping places fill a walled building.", 3, 0.52f, "military"),
        new("armory", "Armory", "Weapon racks line a guarded storeroom.", 2, 0.43f, "military"),
        new("gatehouse", "Gatehouse", "A heavy passage runs beneath defensive rooms.", 3, 0.61f, "military"),
        new("arsenal-store", "Arsenal store", "Sealed shelves stand behind thick doors.", 1, 0.27f, "military"),
        new("siege-workshop", "Siege workshop", "Massive fittings remain under a high roof.", 1, 0.18f, "military"),
        new("courthouse", "Courthouse", "A central chamber faces rows of desks.", 2, 0.49f, "civic"),
        new("guildhall", "Guildhall", "A meeting room adjoins several craft chambers.", 2, 0.30f, "civic"),
        new("gaol", "Gaol", "Locked cells run behind a guarded entrance.", 2, 0.45f, "civic"),
        new("tax-office", "Tax office", "Ledgers and lockboxes fill a small office.", 2, 0.32f, "civic"),
        new("tollhouse", "Tollhouse", "A booth and barrier stand beside an old road.", 3, 0.67f, "civic"),
        new("coach-house", "Coach house", "Broad doors open onto bays for old vehicles.", 2, 0.53f, "transport"),
        new("caravan-depot", "Caravan depot", "Loading sheds surround a central yard.", 2, 0.43f, "transport"),
        new("ferry-landing", "Ferry landing", "Ropes and a roofed store stand beside the water.", 2, 0.55f, "transport"),
        new("bridgekeeper-house", "Bridgekeeper's house", "A small house overlooks an old crossing.", 2, 0.47f, "transport"),
        new("road-station", "Road station", "A low building sits at a junction of worn tracks.", 3, 0.56f, "transport"),
        new("survey-camp", "Survey camp", "Measuring stakes surround a temporary shelter.", 2, 0.35f, "fieldcamp"),
        new("excavation-camp", "Excavation camp", "Sifting tables stand beside a fresh-looking cut.", 2, 0.30f, "fieldcamp"),
        new("hunting-camp", "Hunting camp", "Traps and a fire ring mark an old campsite.", 3, 0.63f, "fieldcamp"),
        new("road-crew-camp", "Road crew camp", "Tools and stone piles line a rough shelter.", 2, 0.45f, "fieldcamp"),
        new("expedition-camp", "Expedition camp", "Packed gear surrounds an abandoned camp.", 2, 0.26f, "fieldcamp"),
        new("masons-yard", "Mason's yard", "Cut stone blocks fill an open work yard.", 3, 0.61f, "construction"),
        new("builders-depot", "Builder's depot", "Stacks of material surround a storehouse.", 3, 0.53f, "construction"),
        new("lumber-yard", "Lumber yard", "Seasoned logs lie beside a covered saw pit.", 3, 0.70f, "construction"),
        new("scaffold-yard", "Scaffold yard", "Tall frames and rope piles surround a work shed.", 2, 0.40f, "construction"),
        new("logging-camp", "Logging camp", "Fresh-cut stumps surround an old shelter.", 3, 0.67f, "forestry"),
        new("charcoal-camp", "Charcoal burner's camp", "Charcoal pits and a small hut mark the site.", 2, 0.54f, "forestry"),
        new("sawmill", "Sawmill", "An old cutting mechanism stands by stacked timber.", 2, 0.48f, "forestry"),
        new("woodcutters-yard", "Woodcutter's yard", "Split logs and tools fill an open yard.", 3, 0.67f, "forestry"),
        new("limeworks", "Limeworks", "Pale dust coats kilns and storage bins.", 2, 0.36f, "chemical"),
        new("soap-boiler", "Soap boiler", "Ash and greasy vats mark a little works.", 2, 0.31f, "chemical"),
        new("dye-works", "Dye works", "Stained channels lead between several vats.", 2, 0.37f, "chemical"),
        new("tallow-works", "Tallow works", "Candle molds and greasy pots fill a shed.", 2, 0.29f, "chemical"),
        new("saltworks", "Saltworks", "Broad pans stand beside crusted drying beds.", 2, 0.53f, "chemical"),
        new("cistern", "Cistern", "A stone reservoir sits below a narrow entrance.", 2, 0.28f, "publicworks"),
        new("pump-house", "Pump house", "Rusted pipes lead into an old machine room.", 2, 0.27f, "publicworks"),
        new("bathhouse", "Bathhouse", "Basins surround several small tiled rooms.", 2, 0.43f, "publicworks"),
        new("drain-works", "Drain works", "A covered channel leads beneath the street.", 2, 0.20f, "publicworks"),
        new("wellhouse", "Wellhouse", "A roof and lifting gear shelter a deep well.", 3, 0.60f, "publicworks"),
        new("tavern", "Tavern", "A crowded common room adjoins a cool cellar.", 3, 0.59f, "hospitality"),
        new("boarding-house", "Boarding house", "Small furnished rooms open onto a shared hall.", 2, 0.46f, "hospitality"),
        new("feast-hall", "Feast hall", "Long tables fill a large decorated room.", 2, 0.29f, "hospitality"),
        new("coaching-inn", "Coaching inn", "Guest rooms surround a broad vehicle yard.", 2, 0.39f, "hospitality"),
        new("monastery", "Monastery", "A cloister connects sleeping and writing rooms.", 2, 0.30f, "religious"),
        new("shrine-house", "Shrine house", "Small offerings surround a central shrine.", 2, 0.46f, "religious"),
        new("pilgrim-hospice", "Pilgrim hospice", "Simple beds fill a hall beside a chapel.", 2, 0.39f, "religious"),
        new("ossuary", "Ossuary", "Neatly arranged bones fill stone alcoves.", 1, 0.20f, "religious"),
        new("quarantine-house", "Quarantine house", "Sealed rooms stand around a medical station.", 2, 0.31f, "disaster"),
        new("burned-quarter", "Burned quarter", "Fire-blackened walls surround several empty plots.", 2, 0.44f, "disaster"),
        new("flood-shelter", "Flood shelter", "Raised storage rooms stand above old water marks.", 2, 0.34f, "disaster"),
        new("relief-station", "Abandoned relief station", "Empty cots and crates fill a broad shelter.", 2, 0.29f, "disaster"),
        new("collector-house", "Collector's house", "Display cases crowd a private home.", 2, 0.35f, "oddity"),
        new("taxidermist-shop", "Taxidermist's shop", "Bones and hide frames line a workroom.", 2, 0.28f, "oddity"),
        new("menagerie-store", "Menagerie store", "Cages and feed containers fill a storehouse.", 2, 0.22f, "oddity"),
        new("curiosity-cabinet", "Curiosity cabinet", "Small display cases fill a windowless room.", 1, 0.12f, "oddity")
    };

    // These are family chances, not per-location weights. The old family's
    // relative frequency survives even though it now has more site names.
    private static readonly (string Id, int Weight)[] FamilyWeights =
    {
        ("home", 35), ("farm", 23), ("workshop", 23), ("store", 13),
        ("records", 13), ("crypt", 8), ("fort", 5), ("mine", 4),
        ("smithing", 4), ("textile", 4), ("leather", 4), ("ceramics", 4),
        ("provisions", 4), ("laboratory", 4), ("military", 4), ("civic", 4),
        ("transport", 4), ("fieldcamp", 4), ("construction", 4),
        ("forestry", 4), ("chemical", 4), ("publicworks", 4),
        ("hospitality", 4), ("religious", 4), ("disaster", 4), ("oddity", 4)
    };

    internal static IReadOnlyCollection<string> KindIds => Kinds.Select(kind => kind.Id).ToArray();
    internal static IReadOnlyCollection<string> HardToFindKindIds => Kinds
        .Where(kind => kind.Weight <= 2).Select(kind => kind.Id).ToArray();
    internal static IReadOnlyCollection<string> LootFamilyIds => FamilyWeights.Select(entry => entry.Id).ToArray();

    private static readonly string[] StrangeFindings =
    {
        "The passage slopes farther down than they could follow.",
        "There is a large open center; they could not see the bottom.",
        "Doorways face the center from several different heights.",
        "The lowest visible stairs end at a wall.",
        "Rooms lie beneath rooms, but no obvious way connects them.",
        "The inner walls use stone unlike anything around the entrance.",
        "Some blocks fit together without visible mortar.",
        "A window frame faces solid earth.",
        "The floor is smooth, though the walls are rough.",
        "Heavy fittings cover a wall with no doorway.",
        "Several empty chairs face the same bare wall.",
        "Shelves are numbered, but nothing remains on them.",
        "There are locks on both sides of a door.",
        "A rope disappears into a narrow crack and will not move.",
        "Scratches mark the stone well above their heads.",
        "A draft comes from a wall they could not find a gap in.",
        "It grows warmer farther down.",
        "They heard water, but found nothing wet.",
        "Their footsteps returned as more than one echo.",
        "A faint knocking stopped whenever they stood still.",
        "An old path leads to the entrance and ends there.",
        "The nearby trees all lean away from it.",
        "Tracks approach the opening, but the scouts found none leaving.",
        "Dust covers everything except a narrow strip of floor.",
        "Places for furniture remain, but none of the furniture does.",
        "A stairwell descends around a space too dark to measure.",
        "The outer stones are weathered; those inside look untouched.",
        "Small bells hang where no wind reaches them.",
        "The air smells of rain although the rooms are dry.",
        "A line of identical doors opens onto empty cells.",
        "The roof is intact, yet soil fills one entire room.",
        "The scouts found a warm handrail and no source of heat.",
        "A narrow bridge crosses the interior above darkness.",
        "Nothing grows in the ring of earth around the entrance.",
        "The walls carry markings that stop abruptly at the same height.",
        "An iron chain vanishes beneath the floor.",
        "A sealed door has hinges on the wrong side.",
        "One chamber has a ceiling too high for their light to reach.",
        "They found the remains of a camp at the threshold, facing outward.",
        "The ground gives a faint vibration where the corridor turns."
    };

    public static bool IsFocus(string focus) => focus is Useful or Furniture or Mixed or Walls;
    public static bool IsDuration(string duration) => duration is ShortScout or MediumScout or LongScout;
    public static int ScoutInformation(string duration) => duration switch
    {
        LongScout => 3,
        MediumScout => 2,
        _ => 1
    };
    public static float ScoutHours(string duration) => duration switch
    {
        LongScout => 18f,
        MediumScout => 10f,
        _ => 5f
    };

    public static FoxScavengeSiteRecord Create(Random random, string ownerUid, long siteId,
        bool favorDifficultSites = false)
    {
        // Keep the original unknowable frequency near one in 125. It is never
        // identified, no matter how many scouts return to it.
        Kind kind;
        if (random.Next(125) == 0)
        {
            kind = Kinds.First(candidate => candidate.Id == "unknowable");
        }
        else
        {
            int familySelection = random.Next(FamilyWeights.Sum(entry => entry.Weight));
            string family = FamilyWeights[0].Id;
            foreach ((string id, int weight) in FamilyWeights)
            {
                familySelection -= weight;
                if (familySelection < 0) { family = id; break; }
            }
            Kind[] candidates = Kinds.Where(candidate => candidate.LootFamily == family).ToArray();
            int Weight(Kind candidate) => candidate.Weight
                * (favorDifficultSites && candidate.Weight <= 2 ? 3 : 1);
            int selection = random.Next(candidates.Sum(Weight));
            kind = candidates[0];
            foreach (Kind candidate in candidates)
            {
                selection -= Weight(candidate);
                if (selection < 0) { kind = candidate; break; }
            }
        }

        int size = random.Next(1, 4);
        if (favorDifficultSites) size = Math.Max(size, random.Next(1, 4));
        int visits = kind.Id == "unknowable"
            ? random.Next(9, 13)
            : Math.Clamp(1 + size + random.Next(0, 3), 2, 6);
        string layout = random.Next(4) switch
        {
            0 => "fragile",
            1 => "heavy-door",
            2 => "narrow",
            _ => "open"
        };
        string danger = random.Next(17) switch
        {
            0 => "locusts",
            1 or 2 => "ringing",
            3 or 4 => "occupied",
            5 or 6 => "traps",
            7 or 8 => "flooded",
            9 or 10 => "predator-tracks",
            11 or 12 => "knocking",
            13 or 14 => "pale-lights",
            _ => "quiet"
        };
        List<string> clues = kind.Id == "unknowable"
            ? StrangeFindings.OrderBy(_ => random.Next()).Take(12).ToList()
            : new List<string> { kind.FirstClue };
        return new FoxScavengeSiteRecord
        {
            SiteId = siteId,
            OwnerUid = ownerUid,
            KindId = kind.Id,
            LootFamily = kind.LootFamily,
            DisplayName = kind.Name,
            Clues = clues,
            TotalVisits = visits,
            RemainingVisits = visits,
            Size = size,
            Age = random.Next(1, 4),
            Integrity = random.Next(1, 4),
            Danger = favorDifficultSites
                ? Math.Max(random.Next(1, 4), random.Next(1, 4))
                : random.Next(1, 4),
            PreviouslyLooted = random.NextDouble() < 0.25,
            LayoutRule = layout,
            DangerRule = danger,
            LayoutThreshold = random.Next(2, 6),
            DangerThreshold = random.Next(3, 7)
        };
    }

    public static void ApplyScout(FoxScavengeSiteRecord site, string duration, Random random,
        int informationBonus = 0)
    {
        if (HasLearnedAllFromOutside(site)) return;
        int information = ScoutInformation(duration) + Math.Clamp(informationBonus, 0, 1);
        site.ScoutCount++;
        site.Knowledge = Math.Min(12, site.Knowledge + information);
        site.RevealedClues = Math.Min(site.Clues.Count, site.RevealedClues + information);
        if (site.Identified || site.KindId == "unknowable") return;
        Kind? kind = Kinds.FirstOrDefault(candidate => candidate.Id == site.KindId);
        float chance = (kind?.IdentifyChance ?? 0.2f) * (0.50f + 0.28f * information)
            + 0.06f * Math.Min(4, site.ScoutCount - 1);
        site.Identified = random.NextDouble() < Math.Clamp(chance, 0f, 0.98f);
    }

    public static void ExpandLegacyUnknowableVisits(FoxScavengeSiteRecord site)
    {
        // New unknowable sites start at nine visits. Old saves could only
        // contain two through six, so this also reopens an exhausted lead.
        if (site.KindId != "unknowable" || site.TotalVisits is < 2 or > 6) return;
        site.RemainingVisits = Math.Clamp(site.RemainingVisits, 0, site.TotalVisits) + 7;
        site.TotalVisits += 7;
    }

    public static void RevealAllFromOutside(FoxScavengeSiteRecord site)
    {
        site.Knowledge = Math.Max(site.Knowledge, 4);
        site.RevealedClues = site.Clues.Count;
        site.Identified = site.KindId != "unknowable";
    }

    public static bool HasLearnedAllFromOutside(FoxScavengeSiteRecord site) =>
        site.Knowledge >= 4
        && site.RevealedClues >= site.Clues.Count
        && (site.Identified || site.KindId == "unknowable");

    public static string ProspectHint(FoxScavengeSiteRecord site)
    {
        if (site.RemainingVisits <= 0) return "The site appears picked clean.";
        if (site.SearchesStarted == 0)
            return site.PreviouslyLooted
                ? "Others have been here, but worthwhile salvage may remain."
                : "The site still looks promising.";
        float remainingShare = (float)site.RemainingVisits / Math.Max(1, site.TotalVisits);
        if (remainingShare > 0.6f) return "Plenty of ground still looks worth searching.";
        if (remainingShare > 0.3f) return "Some useful pockets may remain.";
        return "Only a little promising ground seems left.";
    }

    public static string LayoutHint(FoxScavengeSiteRecord site) => site.LayoutRule switch
    {
        "fragile" => "The floor is unsteady; fewer companions may be safer.",
        "heavy-door" => "A heavy rusted door may take more companions to force.",
        "narrow" => "The passages are tight; a smaller party may move more safely.",
        _ => "The approach is open enough for a larger party."
    };

    public static string DangerHint(FoxScavengeSiteRecord site) => site.DangerRule switch
    {
        "locusts" => "Locusts cover the lower rooms; more companions may be needed.",
        "ringing" => site.ThreatCleared
            ? "The ringing has stopped."
            : "Something stirs within, and it sounds like ringing; fewer may pass unnoticed.",
        "occupied" => "The site shows signs of a hostile group; more companions may help.",
        "traps" => "They found signs of old traps; numbers may not help, but preparation might.",
        "flooded" => "Lower rooms are flooded; hauling salvage out may be difficult.",
        "predator-tracks" => "Large predator tracks cross the route home; the party may need a detour.",
        "knocking" => "Knocking answers from behind the walls; the scouts found no source.",
        "pale-lights" => "Pale lights moved between rooms whenever the scouts looked away.",
        _ => "The scouts found no clear sign of an occupant."
    };

    public static float RoutineSalvageMultiplier(FoxScavengeSiteRecord site) =>
        site.DangerRule == "flooded" ? 0.80f : 1f;

    public static string DescribeLateReturn(FoxScavengeSiteRecord site, int itemCount, Random random)
    {
        List<string> reasons = new()
        {
            "Heavy rain caught them on the way home. They kept the salvage covered and waited it out.",
            "They made a long detour around a bear guarding the return path.",
            "They circled around a persistent wolf instead of risking the cargo.",
            "A washed-out crossing forced them to find another way back."
        };
        if (site.DangerRule == "predator-tracks")
            reasons.Add("Fresh predator tracks led them to take a longer, safer route home.");
        if (itemCount >= 8)
            reasons.Add("The haul was too heavy to move at a normal pace; they stopped often to rest.");
        return reasons[random.Next(reasons.Count)];
    }

    public static string DisplayLabel(FoxScavengeSiteRecord site)
    {
        if (site.KindId == "unknowable") return "Unknowable site";
        if (site.Identified) return site.DisplayName;
        return site.KindId switch
        {
            "crypt" or "library" or "mine" => "Unidentified underground place",
            "farm" or "barn" or "quarry" => "Unidentified outbuildings",
            _ => "Unidentified building"
        };
    }

    internal static (string Image, string Marker) SceneArtForLabel(string? label)
    {
        if (string.Equals(label, "Unknowable site", StringComparison.OrdinalIgnoreCase))
            return ("unknowable", string.Empty);
        if (string.IsNullOrWhiteSpace(label)
            || label.StartsWith("Unidentified", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase))
            return ("unidentified", string.Empty);
        Kind? kind = Kinds.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, label, StringComparison.OrdinalIgnoreCase));
        return kind == null ? ("unidentified", string.Empty) : (kind.LootFamily, kind.Id);
    }
}

[ProtoContract]
public sealed class FoxScavengeSiteRecord
{
    [ProtoMember(1)] public long SiteId;
    [ProtoMember(2)] public string OwnerUid = string.Empty;
    [ProtoMember(3)] public string KindId = string.Empty;
    [ProtoMember(4)] public string LootFamily = string.Empty;
    [ProtoMember(5)] public string DisplayName = string.Empty;
    [ProtoMember(6)] public List<string> Clues = new();
    [ProtoMember(7)] public int RevealedClues;
    [ProtoMember(8)] public int ScoutCount;
    [ProtoMember(9)] public int Knowledge;
    [ProtoMember(10)] public bool Identified;
    [ProtoMember(11)] public int TotalVisits;
    [ProtoMember(12)] public int RemainingVisits;
    [ProtoMember(13)] public int Size;
    [ProtoMember(14)] public int Age;
    [ProtoMember(15)] public int Integrity;
    [ProtoMember(16)] public int Danger;
    [ProtoMember(17)] public bool PreviouslyLooted;
    [ProtoMember(18)] public string LayoutRule = string.Empty;
    [ProtoMember(19)] public string DangerRule = string.Empty;
    [ProtoMember(20)] public int LayoutThreshold;
    [ProtoMember(21)] public int DangerThreshold;
    [ProtoMember(22)] public bool ThreatCleared;
    [ProtoMember(23)] public int SearchesStarted;
}

[ProtoContract]
public sealed class FoxScavengeSitePacket
{
    [ProtoMember(1)] public long SiteId;
    [ProtoMember(2)] public string Label = string.Empty;
    [ProtoMember(3)] public List<string> Clues = new();
    [ProtoMember(4)] public string LayoutHint = string.Empty;
    [ProtoMember(5)] public string DangerHint = string.Empty;
    [ProtoMember(6)] public string ConditionHint = string.Empty;
    [ProtoMember(7)] public bool Barren;
    [ProtoMember(8)] public bool Busy;
    [ProtoMember(9)] public bool OutsideSurveyComplete;
    [ProtoMember(10)] public int SearchesStarted;
    [ProtoMember(11)] public string ProspectHint = string.Empty;
}
