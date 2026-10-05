#nullable enable

using System;
using System.Collections.Generic;

namespace FeralKinshipCompanions;

/// <summary>Companion voice and non-statistical personality flavor.</summary>
internal static class FoxPersonalityText
{
    private static readonly Dictionary<string, string> summaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["timid"] = "Cautious and soft-spoken; safety matters more than pride.",
        ["bold"] = "Direct, eager, and usually the first nose into trouble.",
        ["curious"] = "Forever investigating odd places, creatures, and machines.",
        ["affectionate"] = "Happiest close to familiar people and packmates.",
        ["independent"] = "Values company, but only on their own terms.",
        ["playful"] = "Turns ordinary pack life into games whenever possible.",
        ["restless"] = "Needs movement, novelty, and room to roam.",
        ["homebody"] = "Fond of warm dens, known paths, and familiar comforts.",
        ["social"] = "Actively seeks out the bustle of the whole pack.",
        ["solitary"] = "Prefers quiet corners and unhurried time alone.",
        ["protective"] = "Keeps one ear turned toward danger and the pack.",
        ["territorial"] = "Treats the pack's home ground as a personal responsibility.",
        ["greedy"] = "Food-motivated, opportunistic, and shameless about both.",
        ["demanding"] = "Knows exactly what they want and expects follow-through.",
        ["stubborn"] = "Slow to be persuaded and slower still to abandon a decision.",
        ["skittish"] = "Quick to startle, quicker to seek a safer vantage point."
    };

    private static readonly Dictionary<string, string> actions = new(StringComparer.OrdinalIgnoreCase)
    {
        [FoxRequestType.Predator] = "drive that danger away", [FoxRequestType.StayClose] = "stay close for a while",
        [FoxRequestType.StayAway] = "give me a little space", [FoxRequestType.Inside] = "take me somewhere enclosed",
        [FoxRequestType.NearOwnerStill] = "sit quietly with me", [FoxRequestType.TravelDistance] = "take a proper walk with me",
        [FoxRequestType.WalkDistance] = "take a proper walk with me", [FoxRequestType.HigherGround] = "find us higher ground",
        [FoxRequestType.NearWater] = "find some water", [FoxRequestType.NearLight] = "find a well-lit place",
        [FoxRequestType.NearHeat] = "find somewhere warm", [FoxRequestType.Shelter] = "get me out of this weather",
        [FoxRequestType.OutsideClear] = "take me outside while the weather is fair",
        [FoxRequestType.NearTamedAnimal] = "visit another tame animal", [FoxRequestType.LargeTree] = "show me a truly large tree",
        [FoxRequestType.CropField] = "walk with me by the crops", [FoxRequestType.Trader] = "take me to meet a trader",
        [FoxRequestType.MechanicalDevice] = "show me a working machine", [FoxRequestType.AnotherAnimal] = "find another animal to watch",
        [FoxRequestType.RegularFood] = "bring me something ordinary to eat", [FoxRequestType.LuxuryFood] = "bring me something especially good",
        [FoxRequestType.NonFoodConsumed] = "let me inspect that odd thing", [FoxRequestType.NonFoodNearby] = "show me something unusual",
        [FoxRequestType.HealingItem] = "show me something used for healing", [FoxRequestType.PlaceableNearby] = "place that nearby for me",
        [FoxRequestType.OutsideUntilMorning] = "let me remain outside until morning",
        [FoxRequestType.InsideUntilMorning] = "let me remain safely inside until morning"
    };

    public static string Summary(string personality) => summaries.TryGetValue(personality ?? string.Empty, out string? text)
        ? text : "Still deciding what kind of companion to become.";

    public static string Request(string request, string personality)
    {
        string normalized = FoxRequestCatalog.NormalizeId(request);
        string action = actions.TryGetValue(normalized, out string? specific) ? specific : FoxRequestCatalog.GetActionText(normalized);
        if (action == "None") return action;

        string key = (personality ?? string.Empty).ToLowerInvariant() + ":" + normalized;
        string? bespoke = key switch
        {
            "timid:predator" => "It's still out there. Please make sure it can't come closer.",
            "skittish:predator" => "Something dangerous is nearby. I want to be anywhere else.",
            "bold:predator" => "There it is. Let's send it running.",
            "protective:predator" => "That thing is too close to the pack. Drive it off.",
            "territorial:predator" => "A predator crossed onto our ground. Remove it.",
            "homebody:inside" => "I want walls around me and a roof overhead for a while.",
            "homebody:nearheat" => "Could we find the warmest corner of home?",
            "homebody:shelter" => "This weather has made its point. Take me inside.",
            "restless:traveldistance" => "We've worn a groove standing here. Come walk until the world feels larger.",
            "playful:traveldistance" => "Race you to somewhere that isn't here!",
            "curious:mechanicaldevice" => "I heard a machine working. I need to see what all those moving parts are doing.",
            "curious:trader" => "Traders smell like a hundred distant places. Can we visit one?",
            "greedy:cropfield" => "The crops should be inspected. Closely. For quality.",
            "greedy:trader" => "Traders carry food from places I've never robbed—visited. Let's go.",
            "social:neartamedanimal" => "Someone else lives nearby. We should make a proper visit.",
            "affectionate:nearownerstill" => "Stay here with me. No task, no hurry—just stay.",
            "solitary:stayaway" => "I need enough room to hear my own thoughts.",
            "independent:higherground" => "Find me a high place. I want to choose what I watch.",
            "demanding:nearheat" => "Find somewhere warm. Warm, not merely less cold.",
            "stubborn:stayclose" => "You are staying close until I decide this is finished.",
            _ => null
        };
        if (bespoke != null) return bespoke;

        return (personality ?? string.Empty).ToLowerInvariant() switch
        {
            "timid" => $"If it isn't too much trouble... could we {action}?",
            "bold" => $"Let's {action}. No sense waiting.",
            "curious" => $"I keep wondering what we'd find if we {action}.",
            "affectionate" => $"Would you {action} with me? I'd like the company.",
            "independent" => $"I'd like to {action}. I can manage the rest.",
            "playful" => $"Come on—let's {action}! It might be fun.",
            "restless" => $"I've been still long enough. Let's {action}.",
            "homebody" => $"Could we {action}, then head back to familiar ground?",
            "social" => $"Let's {action}. Perhaps someone else will come along.",
            "solitary" => $"Please {action}. Quietly, if we can.",
            "protective" => $"For the pack's sake, we should {action}.",
            "territorial" => $"Something about our ground feels wrong. We should {action}.",
            "greedy" => $"If we {action}, I expect first choice of any snacks.",
            "demanding" => $"{Cap(action)}. Properly, and don't cut it short.",
            "stubborn" => $"I've decided: we are going to {action}.",
            "skittish" => $"I don't like standing here. Please, let's {action}.",
            _ => $"{Cap(action)}."
        };
    }

    public static string Thought(string personality, string mood)
    {
        string m = (mood ?? string.Empty).Replace(" (f)", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (m == "sleepy") return personality.Equals("stubborn", StringComparison.OrdinalIgnoreCase) ? "Has decided that wakefulness is negotiable." : "Keeps glancing toward a comfortable place to curl up.";
        if (m is "alarmed" or "anxious") return personality.Equals("bold", StringComparison.OrdinalIgnoreCase) ? "Looks worried, and annoyed about looking worried." : "Every sudden sound earns an immediate look.";
        if (m == "playful") return personality.Equals("solitary", StringComparison.OrdinalIgnoreCase) ? "Might tolerate one very brief game." : "Is plainly looking for someone to chase.";
        if (m is "resting" or "rested" or "content") return "Looks settled into the rhythm of pack life.";
        if (m is "alert" or "rallied") return "Is watching the edges of the pack's ground.";
        if (m is "curious" or "restless") return "Their attention keeps wandering toward something new.";
        return (personality ?? string.Empty).ToLowerInvariant() switch
        {
            "greedy" => "Appears to be conducting a private inventory of nearby food.", "demanding" => "Is waiting with the confidence of someone expecting service.",
            "stubborn" => "Looks thoroughly committed to whatever decision came last.", "affectionate" => "Leans subtly toward familiar company.",
            "social" => "Keeps checking where the rest of the pack has gone.", "solitary" => "Has found a small pocket of quiet and approves of it.",
            "protective" => "Keeps one ear trained on distant trouble.", "territorial" => "Surveys the surroundings as if inspecting their own work.",
            "playful" => "Looks one good idea away from starting a game.", "curious" => "Is studying something that probably does not need studying.",
            _ => "Seems comfortable enough to simply be here."
        };
    }

    private static string Cap(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
}
