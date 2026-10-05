#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace FeralKinshipCompanions;

/// <summary>
/// Loaded-game salvage choices. Pool entries resolve the currently patched
/// stack randomizer; direct entries are checked against the world registry.
/// The compact rows are kind/family, common, uncommon, rare. Choices are
/// focus/kind/code, separated by + where a tier has alternatives.
/// </summary>
internal static class FoxScavengeSalvageCatalog
{
    internal const double RoutineSignatureChance = 0.36;
    internal const double RoutineUncommonChance = 0.24;
    internal const double RoutineRareChance = 0.018;
    internal const double BreakthroughSiteFindChance = 0.28;
    internal const double SmithyAnvilChancePerSearch = 0.0025;

    internal sealed record Profile(string Common, string Uncommon, string Rare)
    {
        public string ForTier(int tier) => tier switch { 2 => Rare, 1 => Uncommon, _ => Common };
    }

    internal sealed record Choice(string Focus, string Type, string Code);

    private static readonly Dictionary<string, Profile> Families = ParseProfiles(FamilyRows);
    private static readonly Dictionary<string, Profile> Sites = ParseProfiles(SiteRows);

    internal static IReadOnlyCollection<string> SiteIds => Sites.Keys;
    internal static IReadOnlyCollection<string> FamilyIds => Families.Keys;
    internal static IEnumerable<Choice> AllChoices => Sites.Values.Concat(Families.Values)
        .SelectMany(profile => new[] { profile.Common, profile.Uncommon, profile.Rare })
        .SelectMany(tier => tier.Split('+', StringSplitOptions.RemoveEmptyEntries))
        .Select(raw => ParseChoice(raw) ?? throw new InvalidOperationException($"Invalid choice: {raw}"));

    internal static int RollRoutineTier(Random random)
    {
        double roll = random.NextDouble();
        return roll < RoutineRareChance ? 2
            : roll < RoutineRareChance + RoutineUncommonChance ? 1 : 0;
    }

    internal static Choice? Choose(string siteId, string familyId, string focus,
        int tier, Random random)
    {
        Sites.TryGetValue(siteId, out Profile? site);
        Families.TryGetValue(familyId, out Profile? family);
        // Location signatures dominate, but the shared family keeps repeated
        // searches from collapsing to one item.
        Profile? first = random.NextDouble() < 0.68 ? site : family;
        Profile? second = first == site ? family : site;
        return ChooseFrom(first, focus, tier, random)
            ?? ChooseFrom(second, focus, tier, random);
    }

    private static Choice? ChooseFrom(Profile? profile, string focus, int tier, Random random)
    {
        if (profile == null) return null;
        string[] options = profile.ForTier(tier).Split('+', StringSplitOptions.RemoveEmptyEntries);
        Choice[] eligible = options.Select(ParseChoice)
            .Where(choice => choice != null && (focus == "any" || choice.Focus == focus))
            .Cast<Choice>().ToArray();
        return eligible.Length == 0 ? null : eligible[random.Next(eligible.Length)];
    }

    private static Choice? ParseChoice(string raw)
    {
        string[] parts = raw.Split('/', 3);
        return parts.Length == 3 ? new Choice(parts[0], parts[1], parts[2]) : null;
    }

    private static Dictionary<string, Profile> ParseProfiles(string rows)
    {
        Dictionary<string, Profile> result = new(StringComparer.Ordinal);
        foreach (string raw in rows.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] columns = line.Split('|');
            if (columns.Length != 4 || !result.TryAdd(columns[0],
                    new Profile(columns[1], columns[2], columns[3])))
                throw new InvalidOperationException($"Invalid Scavenge salvage row: {line}");
        }
        return result;
    }

    // Tokens use u=useful, f=furniture; i=item, b=block, p=patched
    // randomizer pool, c=the vanilla clutter block with its type attribute.
    private const string FamilyRows = """
home|u/i/rot+f/p/stackrandomizer-clutter-ruined-toys-clothing|u/p/stackrandomizer-cloth-lowstatus+f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-accessory-mediumstatus+f/p/stackrandomizer-clutter-intact
farm|u/i/flaxfibers+f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-seed+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-coppertool+f/b/fruitpress-ns
workshop|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-coppertool+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-ingot+f/p/stackrandomizer-clutter-workshop-advanced
store|u/i/paper-parchment+f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-cloth-mediumstatus+f/p/stackrandomizer-clutter-displaycase|u/p/stackrandomizer-accessory-highstatus+f/p/stackrandomizer-clutter-intact
records|u/i/paper-parchment+f/p/stackrandomizer-clutter-scribe|u/i/inkandquill+f/p/stackrandomizer-clutter-science|u/p/stackrandomizer-lore-research+f/p/stackrandomizer-clutter-intact
crypt|u/i/bone+f/c/coffin-rot-lid|u/p/stackrandomizer-accessory-lowstatus+f/c/gravestone-2|u/p/stackrandomizer-lazaret+f/p/stackrandomizer-clutter-intact
fort|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-ruined-weapons|u/p/stackrandomizer-ruinedweapon+f/p/stackrandomizer-clutter-chains-and-fences|u/p/stackrandomizer-armor+f/p/stackrandomizer-clutter-workshop-advanced
mine|u/i/stone-chalk+f/c/rubble-wood1|u/p/stackrandomizer-materials-mining+f/p/stackrandomizer-mines|u/p/stackrandomizer-ore+f/p/stackrandomizer-clutter-workshop-advanced
smithing|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-workshop-basic|u/i/ingot-copper+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-ingot+f/p/stackrandomizer-clutter-workshop-advanced
textile|u/i/flaxfibers+f/p/stackrandomizer-clutter-ruined-toys-clothing|u/i/cloth-plain+f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-cloth-highstatus+f/p/stackrandomizer-clutter-intact
leather|u/i/hide-raw-small+f/p/stackrandomizer-clutter-ruined-toys-clothing|u/i/hide-prepared-medium+f/p/stackrandomizer-clutter-furniture|u/i/leather-normal-plain+f/p/stackrandomizer-clutter-intact
ceramics|u/i/clay-blue+f/b/bowl-blue-fired|u/i/clay-fire+f/b/crock-blue-fired|u/p/stackrandomizer-materials-building+f/b/jug-gray-fired
provisions|u/i/rot+f/b/bowl-blue-fired|u/i/salt+f/b/crock-blue-fired|u/i/honeycomb+f/b/woodbucket
laboratory|u/i/paper-parchment+f/p/stackrandomizer-clutter-lab|u/i/ore-sulfur+f/p/stackrandomizer-clutter-science|u/p/stackrandomizer-alljonas+f/p/stackrandomizer-clutter-workshop-advanced
military|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-ruined-weapons|u/p/stackrandomizer-copperweapon+f/p/stackrandomizer-clutter-chains-and-fences|u/p/stackrandomizer-armor+f/p/stackrandomizer-clutter-workshop-advanced
civic|u/i/paper-parchment+f/p/stackrandomizer-clutter-scribe|u/i/inkandquill+f/p/stackrandomizer-clutter-furniture|u/i/gear-rusty+f/p/stackrandomizer-clutter-displaycase
transport|u/i/rope+f/c/barrel-big|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-resource+f/p/stackrandomizer-clutter-furniture
fieldcamp|u/i/stick+f/c/rubble-wood1|u/i/rope+f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-lantern+f/p/stackrandomizer-clutter-science
construction|u/i/stone-chalk+f/b/planks-aged-ud|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-workshop-basic|u/i/metalnailsandstrips-tinbronze+f/p/stackrandomizer-clutter-workshop-advanced
forestry|u/i/stick+f/b/log-placed-aged-ud|u/i/charcoal+f/b/planks-aged-ud|u/p/stackrandomizer-coppertool+f/p/stackrandomizer-clutter-workshop-basic
chemical|u/i/quicklime+f/p/stackrandomizer-clutter-lab|u/i/beeswax+f/p/stackrandomizer-clutter-science|u/i/ore-lapislazuli+f/p/stackrandomizer-clutter-intact
publicworks|u/i/stone-chalk+f/p/stackrandomizer-clutter-chains-and-fences|u/i/metalnailsandstrips-copper+f/p/stackrandomizer-clutter-lab|u/p/stackrandomizer-ingot+f/p/stackrandomizer-clutter-workshop-advanced
hospitality|u/i/rot+f/b/bowl-blue-fired|u/i/gear-rusty+f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-accessory-mediumstatus+f/p/stackrandomizer-painting
religious|u/i/paper-parchment+f/p/stackrandomizer-clutter-scribe|u/i/candle+f/p/stackrandomizer-library|u/p/stackrandomizer-lore-villager+f/p/stackrandomizer-clutter-intact
disaster|u/i/rot+f/c/rubble-wood1|u/i/bandage-clean+f/p/stackrandomizer-clutter-lab|u/p/stackrandomizer-resource+f/p/stackrandomizer-clutter-intact
oddity|u/i/bone+f/p/stackrandomizer-clutter-displaycase|u/i/hide-raw-small+f/p/stackrandomizer-clutter-science|u/p/stackrandomizer-alljonas+f/p/stackrandomizer-clutter-intact
""";

    private const string SiteRows = """
house|f/c/candlestub-single|u/i/paper-parchment|f/p/stackrandomizer-clutter-furniture
farm|u/i/seeds-carrot|f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-coppertool
cabin|u/i/flint|u/i/rope|u/p/stackrandomizer-copperweapon
barn|u/i/flaxfibers|f/b/woodbucket|u/i/flaxtwine
workshop|u/i/metalnailsandstrips-copper|f/p/stackrandomizer-clutter-workshop-basic|u/p/stackrandomizer-ingot
inn|f/b/bowl-blue-fired|f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-lantern
store|u/i/paper-parchment|f/p/stackrandomizer-clutter-displaycase|u/i/gear-rusty
bakery|f/b/bowl-blue-fired|f/b/crock-blue-fired|f/b/quern-granite
mill|u/i/grain-spelt|f/c/grinding-stone|u/i/metalnailsandstrips-tinbronze
warehouse|u/i/rope|f/c/barrel-big|u/p/stackrandomizer-cloth-mediumstatus
school|u/i/paper-parchment|u/i/inkandquill|u/p/stackrandomizer-lore-villager
firehouse|f/b/woodbucket|f/c/bell-parts|u/p/stackrandomizer-lantern
office|u/i/paper-parchment|f/p/stackrandomizer-clutter-scribe|u/p/stackrandomizer-lore-diaries
clinic|u/i/paper-parchment|f/c/pile-medical|u/i/bandage-clean
crypt|u/i/bone|f/c/coffin-rot-lid|u/p/stackrandomizer-lazaret
chapel|f/c/candlestub-single|f/p/stackrandomizer-clutter-scribe|u/p/stackrandomizer-lore-villager
watchpost|u/i/flint|u/p/stackrandomizer-ruinedweapon|u/p/stackrandomizer-armor
manor|f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-cloth-mediumstatus|f/p/stackrandomizer-painting
mine|u/i/stone-chalk|f/p/stackrandomizer-mines|u/p/stackrandomizer-ore
quarry|u/i/stone-limestone|f/b/stonebricks-granite|u/i/metalnailsandstrips-tinbronze
university|u/i/paper-parchment|f/p/stackrandomizer-clutter-science|u/p/stackrandomizer-lore-research+u/i/gear-temporal
fort|u/i/metalnailsandstrips-copper|f/p/stackrandomizer-clutter-ruined-weapons|u/p/stackrandomizer-armor
library|u/i/book-rotten-gray|f/p/stackrandomizer-library|u/p/stackrandomizer-lore-jonas
unknowable|f/c/rubble-wood1|f/p/stackrandomizer-clutter-workshop-advanced|u/p/stackrandomizer-alljonas
townhouse|u/i/paper-parchment|f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-accessory-mediumstatus
servants-quarters|u/i/flaxfibers|u/p/stackrandomizer-cloth-lowstatus|u/i/gear-rusty
estate-cottage|f/c/candlestub-single|f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-cloth-mediumstatus
gatekeeper-lodge|u/i/rope|u/i/metalnailsandstrips-copper|u/p/stackrandomizer-lantern
orchard|u/i/stick|u/i/seeds-carrot|f/b/fruitpress-ns
apiary|u/i/beeswax|u/i/honeycomb|u/i/beenade-closed
vineyard|u/i/stick|f/c/barrel-big|u/p/stackrandomizer-resource
shepherd-station|u/i/flaxfibers|u/i/rope|u/p/stackrandomizer-cloth-mediumstatus
dairy|f/b/crock-blue-fired|f/b/woodbucket|u/i/salt
cooperage|u/i/stick|u/i/metalnailsandstrips-copper|f/b/barrel
glassworks|u/i/ore-quartz|f/p/stackrandomizer-clutter-lab|f/c/tank-glass1
clockmaker|u/i/metalnailsandstrips-copper|f/c/pile-precisiontools|u/p/stackrandomizer-alljonas
instrument-maker|u/i/metalnailsandstrips-copper|f/c/pile-drafting-instrument|f/p/stackrandomizer-clutter-science
carpenter|u/i/stick|f/c/pile-woodworkingtools|u/p/stackrandomizer-coppertool
mason|u/i/stone-limestone|f/c/grinding-stone|f/b/stonebricks-chalk
cloth-merchant|u/i/flaxfibers|u/p/stackrandomizer-cloth-mediumstatus|u/p/stackrandomizer-cloth-highstatus
hardware-merchant|u/i/metalnailsandstrips-copper|u/p/stackrandomizer-coppertool|u/i/metalnailsandstrips-tinbronze
chandlery|f/c/candlestub-single|u/i/beeswax|u/p/stackrandomizer-lantern
pawnshop|u/i/gear-rusty|u/p/stackrandomizer-accessory-mediumstatus|u/p/stackrandomizer-accessory-highstatus
market-warehouse|u/i/rope|f/c/barrel-big|u/p/stackrandomizer-cloth-mediumstatus
courthouse-archive|u/i/paper-parchment|u/i/inkandquill|u/p/stackrandomizer-lore-diaries
map-room|u/i/paper-parchment|f/c/pile-drafting-instrument|f/c/globe1
observatory|u/i/paper-parchment|f/c/astrolabe|f/c/armillary-pristine
survey-office|u/i/paper-parchment|f/c/alidade|f/p/stackrandomizer-clutter-science
museum-collection|u/i/paper-parchment|f/p/stackrandomizer-clutter-displaycase|f/p/stackrandomizer-clutter-intact
mausoleum|u/i/bone|f/c/gravestone-2|u/p/stackrandomizer-lazaret
gravekeeper-house|u/i/paper-parchment|f/c/coffin-rot-lid|u/p/stackrandomizer-accessory-mediumstatus
monastery-crypt|u/i/bone|f/p/stackrandomizer-library|u/p/stackrandomizer-lore-villager
supply-depot|u/i/metalnailsandstrips-copper|u/p/stackrandomizer-ruinedweapon|u/p/stackrandomizer-armor
signal-tower|u/i/rope|f/c/lantern/ground1|u/p/stackrandomizer-lantern
prospecting-camp|u/i/stone-chalk|u/p/stackrandomizer-materials-mining|u/p/stackrandomizer-ore
ore-washing|u/i/stone-limestone|u/p/stackrandomizer-materials-mining|u/p/stackrandomizer-ore
assay-hut|u/i/ore-quartz|f/c/pile-precisiontools|u/p/stackrandomizer-ingot
abandoned-adit|u/i/stone-chalk|f/p/stackrandomizer-mines|u/p/stackrandomizer-ore
smithy|u/i/metalnailsandstrips-copper|f/c/anvil-broken1|u/i/ingot-copper
foundry|u/i/charcoal|f/b/crucible-blue-fired|u/p/stackrandomizer-ingot
armorer-shop|u/i/metalnailsandstrips-tinbronze|f/p/stackrandomizer-clutter-ruined-weapons|u/p/stackrandomizer-armor
bell-foundry|u/i/metalnailsandstrips-tinbronze|f/c/bell-parts|u/i/ingot-brass
metalworks|u/i/metalnailsandstrips-copper|f/p/stackrandomizer-clutter-workshop-basic|f/p/stackrandomizer-clutter-workshop-advanced
weaver-house|u/i/flaxfibers|u/i/flaxtwine|u/p/stackrandomizer-cloth-mediumstatus
tailor-shop|u/i/flaxfibers|u/p/stackrandomizer-cloth-lowstatus|u/p/stackrandomizer-cloth-highstatus
dyer-yard|u/i/clay-blue|u/i/ore-lapislazuli|u/p/stackrandomizer-cloth-mediumstatus
fullery|u/i/flaxfibers|f/c/washboard1|u/p/stackrandomizer-cloth-mediumstatus
ropewalk|u/i/flaxfibers|u/i/flaxtwine|u/i/rope
tannery|u/i/hide-raw-small|u/i/hide-prepared-medium|u/i/leather-normal-plain
saddler-shop|u/i/hide-raw-small|u/i/rope|u/i/leather-normal-plain
cobbler-shop|u/i/hide-raw-small|u/p/stackrandomizer-cloth-lowstatus|u/i/leather-normal-plain
hide-yard|u/i/hide-raw-small|u/i/hide-salted-medium|u/i/leather-normal-plain
pottery|u/i/clay-blue|f/b/crock-blue-fired|f/b/jug-gray-fired
brickworks|u/i/clay-fire|f/b/claybricks-good-fire|u/p/stackrandomizer-materials-building
tile-kiln|u/i/clay-blue|f/b/claybricks-good-fire|f/b/stonebricks-chalk
kiln-yard|u/i/clay-fire|f/b/crucible-blue-fired|f/b/jug-gray-fired
smokehouse|u/i/rot|u/i/salt|f/b/crock-blue-fired
butcher-shop|u/i/bone|u/i/salt|u/p/stackrandomizer-coppertool
cheesehouse|f/b/crock-blue-fired|u/i/salt|f/b/woodbucket
curing-cellar|u/i/rot|f/b/crock-blue-fired|u/i/salt
cider-press|u/i/stick|f/c/barrel-big|f/b/fruitpress-ns
laboratory|u/i/paper-parchment|f/p/stackrandomizer-clutter-lab|f/p/stackrandomizer-clutter-science
assay-office|u/i/ore-quartz|f/c/pile-precisiontools|u/p/stackrandomizer-ingot
specimen-room|u/i/paper-parchment|f/c/tank-glass1|f/p/stackrandomizer-clutter-lab
dissection-room|u/i/bone|f/c/pile-medical|u/i/bandage-clean
experimental-workshop|u/i/metalnailsandstrips-tinbronze|f/p/stackrandomizer-clutter-workshop-advanced|u/p/stackrandomizer-alljonas
barracks|u/i/flaxfibers|u/p/stackrandomizer-ruinedweapon|u/p/stackrandomizer-armor
armory|u/i/metalnailsandstrips-copper|f/p/stackrandomizer-clutter-ruined-weapons|u/p/stackrandomizer-armor
gatehouse|u/i/rope|f/p/stackrandomizer-clutter-chains-and-fences|u/p/stackrandomizer-copperweapon
arsenal-store|u/i/metalnailsandstrips-tinbronze|u/p/stackrandomizer-ruinedweapon|u/p/stackrandomizer-armor
siege-workshop|u/i/rope|f/c/pulley|f/p/stackrandomizer-clutter-workshop-advanced
courthouse|u/i/paper-parchment|u/i/inkandquill|u/p/stackrandomizer-lore-diaries
guildhall|u/i/paper-parchment|f/p/stackrandomizer-clutter-workshop-basic|u/i/gear-rusty
gaol|u/i/paper-parchment|f/p/stackrandomizer-clutter-chains-and-fences|u/p/stackrandomizer-lazaret
tax-office|u/i/paper-parchment|u/i/gear-rusty|u/p/stackrandomizer-accessory-mediumstatus
tollhouse|u/i/paper-parchment|u/i/rope|u/i/gear-rusty
coach-house|u/i/metalnailsandstrips-copper|f/c/pulley|u/p/stackrandomizer-coppertool
caravan-depot|u/i/rope|f/c/barrel-big|u/p/stackrandomizer-cloth-mediumstatus
ferry-landing|u/i/rope|f/c/block-tackle|u/p/stackrandomizer-resource
bridgekeeper-house|u/i/metalnailsandstrips-copper|f/c/pulley|u/p/stackrandomizer-coppertool
road-station|u/i/rope|f/c/lantern/ground1|u/p/stackrandomizer-lantern
survey-camp|u/i/paper-parchment|f/c/alidade|f/p/stackrandomizer-clutter-science
excavation-camp|u/i/stone-chalk|f/p/stackrandomizer-mines|u/p/stackrandomizer-ore
hunting-camp|u/i/flint|u/i/rope|u/p/stackrandomizer-copperweapon
road-crew-camp|u/i/stone-limestone|u/i/metalnailsandstrips-copper|u/p/stackrandomizer-coppertool
expedition-camp|u/i/paper-parchment|f/c/lantern/ground1|u/p/stackrandomizer-lantern
masons-yard|u/i/stone-limestone|f/b/stonebricks-granite|u/i/metalnailsandstrips-tinbronze
builders-depot|u/i/metalnailsandstrips-copper|f/b/planks-aged-ud|u/p/stackrandomizer-materials-building
lumber-yard|u/i/stick|f/b/log-placed-aged-ud|f/b/planks-aged-ud
scaffold-yard|u/i/rope|u/i/metalnailsandstrips-copper|u/i/metalnailsandstrips-tinbronze
logging-camp|u/i/stick|f/b/log-placed-aged-ud|u/p/stackrandomizer-coppertool
charcoal-camp|u/i/charcoal|u/i/charcoal|u/p/stackrandomizer-fuel
sawmill|u/i/stick|f/c/pile-woodworkingtools|u/p/stackrandomizer-coppertool
woodcutters-yard|u/i/stick|f/b/planks-aged-ud|u/p/stackrandomizer-coppertool
limeworks|u/i/stone-limestone|u/i/quicklime|u/i/quicklime
soap-boiler|u/i/beeswax|f/c/bucket1|u/i/beeswax
dye-works|u/i/ore-lapislazuli|f/c/bucket2|u/i/ore-lapislazuli
tallow-works|u/i/beeswax|f/c/candlestub-single|u/i/candle
saltworks|u/i/stone-halite|u/i/salt|u/i/salt
cistern|u/i/stone-limestone|f/b/woodbucket|f/p/stackrandomizer-clutter-chains-and-fences
pump-house|u/i/metalnailsandstrips-copper|f/p/stackrandomizer-clutter-workshop-advanced|u/p/stackrandomizer-alljonas
bathhouse|f/c/bucket1|f/p/stackrandomizer-clutter-furniture|f/p/stackrandomizer-clutter-intact
drain-works|u/i/stone-chalk|f/p/stackrandomizer-clutter-chains-and-fences|u/p/stackrandomizer-ingot
wellhouse|u/i/rope|f/c/pulley|f/c/block-tackle
tavern|f/b/bowl-blue-fired|f/c/barrel-big|u/p/stackrandomizer-accessory-mediumstatus
boarding-house|u/i/flaxfibers|f/p/stackrandomizer-clutter-furniture|u/p/stackrandomizer-cloth-mediumstatus
feast-hall|f/b/bowl-blue-fired|f/p/stackrandomizer-clutter-furniture|f/p/stackrandomizer-painting
coaching-inn|u/i/rope|f/c/lantern/ground1|u/p/stackrandomizer-lantern
monastery|u/i/paper-parchment|f/p/stackrandomizer-library|u/p/stackrandomizer-lore-villager
shrine-house|f/c/candlestub-single|u/i/candle|u/p/stackrandomizer-lazaret
pilgrim-hospice|u/i/paper-parchment|u/i/bandage-clean|u/p/stackrandomizer-cloth-mediumstatus
ossuary|u/i/bone|f/c/coffin-rot-lid|u/p/stackrandomizer-lazaret
quarantine-house|u/i/rot|f/c/pile-medical|u/i/bandage-clean
burned-quarter|u/i/charcoal|f/c/rubble-wood1|u/p/stackrandomizer-materials-building
flood-shelter|u/i/rope|f/c/barrel-big|u/p/stackrandomizer-resource
relief-station|u/i/rot|u/i/bandage-clean|u/p/stackrandomizer-coppertool
collector-house|u/i/paper-parchment|f/p/stackrandomizer-clutter-displaycase|f/p/stackrandomizer-clutter-science
taxidermist-shop|u/i/bone|u/i/hide-prepared-medium|u/i/leather-normal-plain
menagerie-store|u/i/rot|f/b/cage-wooden-north|u/i/beenade-closed
curiosity-cabinet|u/i/paper-parchment|f/p/stackrandomizer-clutter-displaycase|u/p/stackrandomizer-alljonas
""";
}
