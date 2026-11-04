using System;
using System.Collections.Generic;
using System.Linq;

namespace Druidry;

public sealed class DruidFormDefinition
{
    public DruidFormDefinition(
        string code,
        string modelCode,
        string name,
        string title,
        string lore,
        string gifts,
        string burden,
        string tokenOffering,
        string packName,
        string requiredModId,
        DruidGlyph glyph)
    {
        Code = code;
        ModelCode = modelCode;
        Name = name;
        Title = title;
        Lore = lore;
        Gifts = gifts;
        Burden = burden;
        TokenOffering = tokenOffering;
        PackName = packName;
        RequiredModId = requiredModId;
        Glyph = glyph;
    }

    public string Code { get; }
    public string ModelCode { get; }
    public string Name { get; }
    public string Title { get; }
    public string Lore { get; }
    public string Gifts { get; }
    public string Burden { get; }
    public string TokenOffering { get; }
    public string PackName { get; }
    public string RequiredModId { get; }
    public DruidGlyph Glyph { get; }
    public string TokenCode => "druidry:spirit-token-" + Code;
}

public enum DruidGlyph
{
    Paw,
    Tail,
    Fang,
    Claw,
    Antler,
    Horn,
    Tusk,
    Wool,
    Ear,
    Mask,
    Feather,
    Fish
}

public static class DruidForms
{
    public static readonly IReadOnlyList<DruidFormDefinition> All = new[]
    {
        new DruidFormDefinition(
            "wolf", "feralwolf", "Wolf", "The Packbound",
            "A patient hunter that reads distance, weakness, and the shape of a trail.",
            "Predator build, keen movement, carnivore physiology.",
            "Ravenous metabolism; meat is not optional.",
            "Raw red meat bound with resin and bone.",
            "Animalica Canids", "animalicafoxwolfhyena", DruidGlyph.Paw),
        new DruidFormDefinition(
            "fox", "feralfox", "Fox", "The Ember-Tail",
            "A small forest runner that survives by choosing the clever path first.",
            "Forest-runner agility and a flexible meat-leaning diet.",
            "A small body trades force and reach for speed.",
            "Poultry, resin, and a carved bone.",
            "Animalica Canids", "animalicafoxwolfhyena", DruidGlyph.Tail),
        new DruidFormDefinition(
            "hyena", "spottedhyena", "Hyena", "The Bone-Laugh",
            "A relentless endurance hunter whose strength is refusing to yield.",
            "Robust predator build and carnivore physiology.",
            "Ravenous metabolism and little patience for plants.",
            "Fat, resin, and a weathered bone.",
            "Animalica Canids", "animalicafoxwolfhyena", DruidGlyph.Fang),
        new DruidFormDefinition(
            "bear", "brownbear", "Bear", "The Old Strength",
            "A mountain of fur, appetite, and calm force until the moment calm ends.",
            "Heavy predator strength with omnivorous resilience.",
            "Great power carries a great appetite.",
            "Honeycomb, fat, resin, and bone.",
            "Animalica Bears", "animalicabears", DruidGlyph.Claw),
        new DruidFormDefinition(
            "deer", "deer-moose", "Deer / Moose", "The Crowned Path",
            "A towering grazer spirit that crosses forest and marsh without haste.",
            "Large-grazer durability and herbivore efficiency.",
            "Bulk and antlers make tight spaces awkward.",
            "A large raw hide, flax fiber, resin, and bone.",
            "Animalica Hooved Animals", "animalicahooved", DruidGlyph.Antler),
        new DruidFormDefinition(
            "gazelle", "gazelle-thomson", "Gazelle", "The Open Horizon",
            "The grassland wind made flesh: alert, graceful, and difficult to catch.",
            "Fleet-grazer speed and herbivore efficiency.",
            "Lightness offers little protection in a stand-up fight.",
            "Flax grain, fiber, resin, and bone.",
            "Animalica Hooved Animals", "animalicahooved", DruidGlyph.Horn),
        new DruidFormDefinition(
            "goat", "goat-mountain", "Mountain Goat", "The High Step",
            "A sure-footed climber that treats cliffs as roads and heights as shelter.",
            "Mountain-grazer mobility and herbivore efficiency.",
            "Built for rough ground rather than raw combat power.",
            "A turnip, flax fiber, resin, and bone.",
            "Animalica Hooved Animals", "animalicahooved", DruidGlyph.Horn),
        new DruidFormDefinition(
            "pig", "pig-eurasian-adult", "Wild Boar", "The Root-Breaker",
            "A stubborn woodland forager with the shoulders to turn panic into a charge.",
            "Boar build and broad omnivore diet.",
            "The sturdy body is hungry and not especially subtle.",
            "A carrot, fat, resin, and bone.",
            "Animalica Hooved Animals", "animalicahooved", DruidGlyph.Tusk),
        new DruidFormDefinition(
            "sheep", "sheep-bighorn", "Bighorn Sheep", "The Stone Ram",
            "A sure and steady grazer whose answer to danger is often forehead-first.",
            "Sturdy-grazer resilience and herbivore efficiency.",
            "Less agile than the fleet forms and poorly suited to predation.",
            "Flax fiber, grain, resin, and bone.",
            "Animalica Hooved Animals", "animalicahooved", DruidGlyph.Wool),
        new DruidFormDefinition(
            "hare", "hare-male", "Hare", "The Quick Heart",
            "A tiny pulse of speed, caution, and impossible changes of direction.",
            "Small-agile movement and herbivore efficiency.",
            "Fragile and easy to corner if speed fails.",
            "A carrot, flax fiber, resin, and tiny bone.",
            "Animalica Small & Odd", "animalicasmallodd", DruidGlyph.Ear),
        new DruidFormDefinition(
            "raccoon", "feralraccoon", "Raccoon", "The Clever Hands",
            "A nimble scavenger that can turn another creature's refuse into opportunity.",
            "Nimble scavenger physiology and a forgiving diet.",
            "A small frame cannot absorb much punishment.",
            "A turnip, fat, resin, and bone.",
            "Animalica Small & Odd", "animalicasmallodd", DruidGlyph.Mask),
        new DruidFormDefinition(
            "chicken", "chicken-hen", "Chicken", "The Hearth Feather",
            "A light-footed forager spirit: watchful, social, and unexpectedly bold.",
            "Light-forager movement with an omnivore diet.",
            "Extremely fragile when caught.",
            "Feathers, flax grain, resin, and tiny bone.",
            "Animalica Small & Odd", "animalicasmallodd", DruidGlyph.Feather),
        new DruidFormDefinition(
            "fish", "feralfish", "Fish", "The River Memory",
            "A current-borne shape that knows water as home and land as catastrophe.",
            "True aquatic movement and carnivore physiology.",
            "Stranded on land; choose the moment of transformation carefully.",
            "Raw fish, resin, and tiny bone.",
            "Animalica Small & Odd", "animalicasmallodd", DruidGlyph.Fish)
    };

    private static readonly Dictionary<string, DruidFormDefinition> ByCode =
        All.ToDictionary(form => form.Code, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(string? code, out DruidFormDefinition form)
    {
        if (!string.IsNullOrWhiteSpace(code) && ByCode.TryGetValue(code, out DruidFormDefinition? found))
        {
            form = found;
            return true;
        }

        form = null!;
        return false;
    }

    public static bool LooksLikeAnimalicaModel(string? modelCode)
    {
        if (string.IsNullOrWhiteSpace(modelCode)) return false;
        string code = modelCode.ToLowerInvariant();
        int namespaceSeparator = code.LastIndexOf(':');
        if (namespaceSeparator >= 0) code = code[(namespaceSeparator + 1)..];
        return code.StartsWith("feral", StringComparison.Ordinal)
            || code.StartsWith("deer-", StringComparison.Ordinal)
            || code.StartsWith("gazelle-", StringComparison.Ordinal)
            || code.StartsWith("goat-", StringComparison.Ordinal)
            || code.StartsWith("pig-", StringComparison.Ordinal)
            || code.StartsWith("sheep-", StringComparison.Ordinal)
            || code.StartsWith("hare-", StringComparison.Ordinal)
            || code.StartsWith("chicken-", StringComparison.Ordinal)
            || code is "blackbear" or "brownbear" or "polarbear" or "giantpanda" or "sunbear" or "spottedhyena";
    }
}
