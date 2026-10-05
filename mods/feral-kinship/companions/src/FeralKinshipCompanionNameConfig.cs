#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;

namespace FeralKinshipCompanions;

internal sealed class CompanionNamePool
{
    public string[] Male { get; set; } = Array.Empty<string>();
    public string[] Female { get; set; } = Array.Empty<string>();
    public string[] Unisex { get; set; } = Array.Empty<string>();

    public string[] GetEligible(string gender)
    {
        IEnumerable<string> names = gender switch
        {
            "male" => Male.Concat(Unisex),
            "female" => Female.Concat(Unisex),
            _ => Unisex.Concat(Male).Concat(Female)
        };

        return names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IEnumerable<string> AllNames()
    {
        return Male.Concat(Female).Concat(Unisex);
    }
}

internal sealed class CompanionNameConfig
{
    public CompanionNamePool General { get; set; } = new();
    public CompanionNamePool Timid { get; set; } = new();
    public CompanionNamePool Bold { get; set; } = new();
    public CompanionNamePool Curious { get; set; } = new();
    public CompanionNamePool Affectionate { get; set; } = new();
    public CompanionNamePool Independent { get; set; } = new();
    public CompanionNamePool Playful { get; set; } = new();
    public CompanionNamePool Restless { get; set; } = new();
    public CompanionNamePool Homebody { get; set; } = new();
    public CompanionNamePool Social { get; set; } = new();
    public CompanionNamePool Solitary { get; set; } = new();
    public CompanionNamePool Protective { get; set; } = new();
    public CompanionNamePool Territorial { get; set; } = new();
    public CompanionNamePool Greedy { get; set; } = new();
    public CompanionNamePool Demanding { get; set; } = new();
    public CompanionNamePool Stubborn { get; set; } = new();
    public CompanionNamePool Skittish { get; set; } = new();

    public string[] GetEligiblePool(string personality, string gender)
    {
        return GetPool(personality).GetEligible(gender);
    }

    public string[] GetGeneralEligiblePool(string gender)
    {
        return General.GetEligible(gender);
    }

    public string[] GetAllEligibleNames(string gender)
    {
        return GetPools()
            .SelectMany(pool => pool.GetEligible(gender))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string[] GetAllNames()
    {
        return GetPools()
            .SelectMany(pool => pool.AllNames())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public CompanionNamePool GetPool(string personality)
    {
        return personality.Trim().ToLowerInvariant() switch
        {
            "timid" => Timid,
            "bold" => Bold,
            "curious" => Curious,
            "affectionate" => Affectionate,
            "independent" => Independent,
            "playful" => Playful,
            "restless" => Restless,
            "homebody" => Homebody,
            "social" => Social,
            "solitary" => Solitary,
            "protective" => Protective,
            "territorial" => Territorial,
            "greedy" => Greedy,
            "demanding" => Demanding,
            "stubborn" => Stubborn,
            "skittish" => Skittish,
            _ => General
        };
    }

    public void Normalize()
    {
        foreach (CompanionNamePool pool in GetPools())
        {
            pool.Male = NormalizeNames(pool.Male);
            pool.Female = NormalizeNames(pool.Female);
            pool.Unisex = NormalizeNames(pool.Unisex);
        }
    }

    public string ToCommentedText()
    {
        StringBuilder text = new();
        text.AppendLine("# Feral Kinship companion name pools");
        text.AppendLine("#");
        text.AppendLine("# Add, remove, or reorder names freely. Changes are read when the server starts.");
        text.AppendLine("# Comments begin with #. Leave a list empty when that category should contribute no names.");
        text.AppendLine("#");
        text.AppendLine("# Suggestions use the companion's personality about 70% of the time and General about 30%.");
        text.AppendLine("# Male companions use Male + Unisex names; female companions use Female + Unisex names.");
        text.AppendLine("# If gender is unavailable, Unisex names are preferred and the other lists remain a fallback.");
        text.AppendLine("# Names may overlap between categories and genders.");
        text.AppendLine();

        AppendSection(text, "General", "The fallback pool used for every personality.", General);
        AppendSection(text, "Timid", "Gentle, cautious, and easily startled.", Timid);
        AppendSection(text, "Bold", "Confident, fearless, and forward.", Bold);
        AppendSection(text, "Curious", "Inquisitive, observant, and eager to investigate.", Curious);
        AppendSection(text, "Affectionate", "Warm, loyal, and fond of closeness.", Affectionate);
        AppendSection(text, "Independent", "Self-directed, capable, and comfortable alone.", Independent);
        AppendSection(text, "Playful", "Energetic, mischievous, and full of fun.", Playful);
        AppendSection(text, "Restless", "Fidgety, excitable, and always ready to move.", Restless);
        AppendSection(text, "Homebody", "Content, settled, and happiest close to home.", Homebody);
        AppendSection(text, "Social", "Friendly, outgoing, and drawn to company.", Social);
        AppendSection(text, "Solitary", "Quiet, private, and comfortable keeping distance.", Solitary);
        AppendSection(text, "Protective", "Watchful, loyal, and quick to defend.", Protective);
        AppendSection(text, "Territorial", "Possessive, alert, and keenly aware of boundaries.", Territorial);
        AppendSection(text, "Greedy", "Hungry for treats, treasure, and one more helping.", Greedy);
        AppendSection(text, "Demanding", "Certain of what they want and unafraid to ask.", Demanding);
        AppendSection(text, "Stubborn", "Determined, persistent, and difficult to sway.", Stubborn);
        AppendSection(text, "Skittish", "Nervous, wary, and quick to flinch.", Skittish);
        return text.ToString();
    }

    public static CompanionNameConfig CreateDefault()
    {
        return new CompanionNameConfig
        {
            General = Pool("Orin, Martin, Mark, Harrison, Halden, Harlan, Casio, Eli, Regonoth, Xanarch, Craditz, Valek, Milo, Slate, Rend, Frost", "Alyssa, Ember, Mara, Maris, Nyx, Nettle, Chime, Poppy, Honey, Willow, Tansy, Maple", "Ash, Wren, Cetus, Stone, Tallow, Bristle, Lark, Rook, Morrow, Flint, Shadow, Veil, Harrow, Quill, Fen, Brindle, Sable, Silva, Mox, Serrin, Vale, Sayer, Sorell, Morningstar, Cortex, Core, Pink, Rowan, Juniper, Mica, Thistle, Rune"),
            Timid = Pool("Eli, Milo, Pippin", "Maris, Chime, Poppy", "Wren, Bristle, Mox, Quill, Lark, Vale, Fen, Mallow, Fern, Wisp, Moth"),
            Bold = Pool("Regonoth, Harrison, Harlan, Casio, Craditz, Frost", "Ember, Shadow, Nyx", "Stone, Harrow, Veil, Rook, Flint, Brindle, Cetus, Thorn"),
            Curious = Pool("Mark, Orin, Xanarch, Craditz, Milo, Kit", "Nyx, Alyssa, Chime", "Cortex, Serrin, Quill, Wren, Pip, Cricket, Puck, Rune, Mica"),
            Affectionate = Pool("Milo, Pippin, Eli", "Ember, Mara, Poppy, Honey, Nettle", "Ash, Lark, Cetus, Wren, Mox, Sunny, Clover, Willow, Pip"),
            Independent = Pool("Halden, Orin, Slate, Nox", "Shadow, Silva, Nyx", "Fen, Sable, Tallow, Quill, Morrow, Veil, Ash, Moss, Bracken, Nook"),
            Playful = Pool("Craditz, Milo, Kit, Pippin", "Nyx, Chime, Poppy", "Bristle, Lark, Mox, Pip, Puck, Tumble, Cricket, Rune"),
            Restless = Pool("Mark, Craditz, Milo, Kit", "Nyx, Chime", "Fen, Bristle, Rook, Morrow, Quill, Pip, Cricket, Tumble, Puck, Rune"),
            Homebody = Pool("Slate, Nox", "Silva, Nettle", "Sable, Shadow, Tallow, Vale, Lark, Nook, Moss, Bracken, Rune, Rowan"),
            Social = Pool("Casio, Harrison, Milo, Pippin", "Ember, Mara, Alyssa, Nyx, Poppy", "Lark, Ash, Cetus, Bristle, Wren, Sunny, Pip, Clover, Mox"),
            Solitary = Pool("Halden, Orin, Slate, Nox", "Shadow, Silva", "Fen, Sable, Tallow, Quill, Morrow, Veil, Nook, Bracken, Moss"),
            Protective = Pool("Regonoth, Harrison, Valek, Harlan, Frost, Rend", "Ember, Shadow, Mara, Nyx", "Cetus, Stone, Rook, Flint, Harrow, Ash, Lark, Brindle, Thorn"),
            Territorial = Pool("Regonoth, Kessler, Harlan, Halden, Nox", "Shadow, Nyx", "Harrow, Veil, Rook, Flint, Sable, Tallow, Bracken, Thorn, Stone"),
            Greedy = Pool("Casio", "Honey", "Pink, Copper, Pickle, Biscuit, Truffle, Mica, Pebble, Clover"),
            Demanding = Pool("Halden, Kessler, Harlan, Regonoth", "Alyssa", "Pink, Sorell, Morningstar, Vale, Sayer, Tallow, Sable, Stone, Harrow, Flint, Copper, Pickle, Truffle"),
            Stubborn = Pool("Halden, Harlan, Frost, Rend, Nox", "Silva, Nyx", "Tallow, Sable, Stone, Flint, Lark, Wren, Harrow, Rook, Brindle, Thorn, Copper"),
            Skittish = Pool("Eli, Pippin, Kit", "Maris, Chime, Poppy", "Wren, Bristle, Mox, Quill, Lark, Vale, Mallow, Fern, Wisp, Moth")
        };
    }

    public static CompanionNameConfig ParseCommentedConfig(string text)
    {
        CompanionNameConfig config = CreateDefault();
        string sectionName = string.Empty;

        foreach (string rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                sectionName = line.Substring(1, line.Length - 2).Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0 || string.IsNullOrWhiteSpace(sectionName))
            {
                continue;
            }

            string key = line.Substring(0, equals).Trim();
            string value = line.Substring(equals + 1).Trim();
            SetList(config.GetPoolBySection(sectionName), key, value);
        }

        config.Normalize();
        return config;
    }

    private static CompanionNamePool Pool(string male, string female, string unisex)
    {
        return new CompanionNamePool
        {
            Male = SplitNames(male),
            Female = SplitNames(female),
            Unisex = SplitNames(unisex)
        };
    }

    private static string[] SplitNames(string value)
    {
        return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static string[] NormalizeNames(IEnumerable<string>? names)
    {
        return (names ?? Array.Empty<string>())
            .Select(name => name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0 && name.Length <= 32 && name.All(character => !char.IsControl(character)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<CompanionNamePool> GetPools()
    {
        yield return General;
        yield return Timid;
        yield return Bold;
        yield return Curious;
        yield return Affectionate;
        yield return Independent;
        yield return Playful;
        yield return Restless;
        yield return Homebody;
        yield return Social;
        yield return Solitary;
        yield return Protective;
        yield return Territorial;
        yield return Greedy;
        yield return Demanding;
        yield return Stubborn;
        yield return Skittish;
    }

    private CompanionNamePool? GetPoolBySection(string section)
    {
        return section.Trim().ToLowerInvariant() switch
        {
            "general" => General,
            "timid" => Timid,
            "bold" => Bold,
            "curious" => Curious,
            "affectionate" => Affectionate,
            "independent" => Independent,
            "playful" => Playful,
            "restless" => Restless,
            "homebody" => Homebody,
            "social" => Social,
            "solitary" => Solitary,
            "protective" => Protective,
            "territorial" => Territorial,
            "greedy" => Greedy,
            "demanding" => Demanding,
            "stubborn" => Stubborn,
            "skittish" => Skittish,
            _ => null
        };
    }

    private static void SetList(CompanionNamePool? pool, string key, string value)
    {
        if (pool == null)
        {
            return;
        }

        string[] names = SplitNames(value);
        switch (key.Trim().ToLowerInvariant())
        {
            case "male":
                pool.Male = names;
                break;
            case "female":
                pool.Female = names;
                break;
            case "unisex":
                pool.Unisex = names;
                break;
        }
    }

    private static void AppendSection(StringBuilder text, string name, string description, CompanionNamePool pool)
    {
        text.AppendLine($"# {description}");
        text.AppendLine($"[{name}]");
        text.AppendLine($"Male = {string.Join(", ", pool.Male)}");
        text.AppendLine($"Female = {string.Join(", ", pool.Female)}");
        text.AppendLine($"Unisex = {string.Join(", ", pool.Unisex)}");
        text.AppendLine();
    }
}

internal sealed class LegacyCompanionNameConfig
{
    public string[] General { get; set; } = Array.Empty<string>();
    public string[] Timid { get; set; } = Array.Empty<string>();
    public string[] Bold { get; set; } = Array.Empty<string>();
    public string[] Curious { get; set; } = Array.Empty<string>();
    public string[] Affectionate { get; set; } = Array.Empty<string>();
    public string[] Independent { get; set; } = Array.Empty<string>();
    public string[] Playful { get; set; } = Array.Empty<string>();
    public string[] Restless { get; set; } = Array.Empty<string>();
    public string[] Homebody { get; set; } = Array.Empty<string>();
    public string[] Social { get; set; } = Array.Empty<string>();
    public string[] Solitary { get; set; } = Array.Empty<string>();
    public string[] Protective { get; set; } = Array.Empty<string>();
    public string[] Territorial { get; set; } = Array.Empty<string>();
    public string[] Greedy { get; set; } = Array.Empty<string>();
    public string[] Demanding { get; set; } = Array.Empty<string>();
    public string[] Stubborn { get; set; } = Array.Empty<string>();
    public string[] Skittish { get; set; } = Array.Empty<string>();

    public CompanionNameConfig ToConfig()
    {
        CompanionNameConfig config = CompanionNameConfig.CreateDefault();
        foreach ((string section, string[] names) in Sections())
        {
            CompanionNamePool pool = config.GetPool(section);
            pool.Male = Array.Empty<string>();
            pool.Female = Array.Empty<string>();
            pool.Unisex = names ?? Array.Empty<string>();
        }

        config.Normalize();
        return config;
    }

    private IEnumerable<(string Section, string[] Names)> Sections()
    {
        yield return ("General", General);
        yield return ("Timid", Timid);
        yield return ("Bold", Bold);
        yield return ("Curious", Curious);
        yield return ("Affectionate", Affectionate);
        yield return ("Independent", Independent);
        yield return ("Playful", Playful);
        yield return ("Restless", Restless);
        yield return ("Homebody", Homebody);
        yield return ("Social", Social);
        yield return ("Solitary", Solitary);
        yield return ("Protective", Protective);
        yield return ("Territorial", Territorial);
        yield return ("Greedy", Greedy);
        yield return ("Demanding", Demanding);
        yield return ("Stubborn", Stubborn);
        yield return ("Skittish", Skittish);
    }
}

public sealed partial class FeralKinshipCompanionSystem
{
    private const string CompanionNameConfigFileName = "FeralKinshipCompanionNames.cfg";
    private const string LegacyCompanionNameConfigFileName = "FeralKinshipCompanionNames.json";
    private CompanionNameConfig companionNameConfig = CompanionNameConfig.CreateDefault();

    private void LoadCompanionNameConfig()
    {
        string configDirectory = serverApi!.GetOrCreateDataPath("ModConfig");
        string configPath = Path.Combine(configDirectory, CompanionNameConfigFileName);

        try
        {
            if (File.Exists(configPath))
            {
                string existingText = File.ReadAllText(configPath);
                companionNameConfig = CompanionNameConfig.ParseCommentedConfig(existingText);
                string updatedText = EnsureMissingCommentedEntries(
                    existingText,
                    companionNameConfig.ToCommentedText());
                if (!string.Equals(existingText, updatedText, StringComparison.Ordinal))
                {
                    File.WriteAllText(configPath, updatedText, new UTF8Encoding(false));
                }
            }
            else
            {
                string legacyPath = Path.Combine(configDirectory, LegacyCompanionNameConfigFileName);
                if (File.Exists(legacyPath))
                {
                    LegacyCompanionNameConfig? legacy = serverApi.LoadModConfig<LegacyCompanionNameConfig>(LegacyCompanionNameConfigFileName);
                    companionNameConfig = legacy != null && !LooksLikeOldBuiltInConfig(legacy)
                        ? legacy.ToConfig()
                        : CompanionNameConfig.CreateDefault();
                }
                else
                {
                    companionNameConfig = CompanionNameConfig.CreateDefault();
                }

                companionNameConfig.Normalize();
                File.WriteAllText(configPath, companionNameConfig.ToCommentedText(), new UTF8Encoding(false));
            }

            companionNameConfig.Normalize();
            serverApi.Logger.Notification(
                "[FeralKinshipCompanions] Loaded companion name pools from {0}.",
                configPath);
        }
        catch (Exception exception)
        {
            companionNameConfig = CompanionNameConfig.CreateDefault();
            serverApi.Logger.Warning(
                "[FeralKinshipCompanions] Could not read {0}; using built-in names. {1}",
                configPath,
                exception.Message);
        }
    }

    private static bool LooksLikeOldBuiltInConfig(LegacyCompanionNameConfig config)
    {
        string[] oldGeneral =
        {
            "Ash", "Bramble", "Clover", "Juniper", "Maple", "Mica", "Pebble", "Pine",
            "Rowan", "Tansy", "Thistle", "Willow", "Morrow", "Orin", "Chime", "Nettle",
            "Maris", "Eli", "Vale", "Casio", "Valek", "Rune", "Slate", "Wren"
        };

        return config.General.SequenceEqual(oldGeneral, StringComparer.OrdinalIgnoreCase);
    }

    private static string EnsureMissingCommentedEntries(string existingText, string completeText)
    {
        string newline = existingText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> current = existingText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        List<string> complete = completeText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        List<(string Name, int Start, int End)> sections = FindSections(complete);
        bool changed = false;

        foreach ((string name, int start, int end) in sections)
        {
            int currentStart = FindSection(current, name, 0);
            if (currentStart < 0)
            {
                if (current.Count > 0 && !string.IsNullOrWhiteSpace(current[^1]))
                {
                    current.Add(string.Empty);
                }

                for (int index = start; index < end; index++)
                {
                    current.Add(complete[index]);
                }

                changed = true;
                continue;
            }

            int currentEnd = FindNextSection(current, currentStart + 1);
            List<string> defaultKeys = complete
                .GetRange(start + 1, end - start - 1)
                .Where(line => TryGetConfigKey(line, out _))
                .ToList();

            foreach (string defaultKeyLine in defaultKeys)
            {
                TryGetConfigKey(defaultKeyLine, out string key);
                bool exists = current
                    .GetRange(currentStart + 1, currentEnd - currentStart - 1)
                    .Any(line => TryGetConfigKey(line, out string existingKey)
                        && string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    current.Insert(currentEnd, defaultKeyLine);
                    currentEnd++;
                    changed = true;
                }
            }
        }

        return changed ? string.Join(newline, current) : existingText;
    }

    private static List<(string Name, int Start, int End)> FindSections(List<string> lines)
    {
        List<(string Name, int Start, int End)> sections = new();
        for (int index = 0; index < lines.Count; index++)
        {
            if (!TryGetSectionName(lines[index], out string name))
            {
                continue;
            }

            int end = index + 1;
            while (end < lines.Count && !TryGetSectionName(lines[end], out _))
            {
                end++;
            }

            sections.Add((name, index, end));
        }

        return sections;
    }

    private static int FindSection(List<string> lines, string name, int start)
    {
        for (int index = start; index < lines.Count; index++)
        {
            if (TryGetSectionName(lines[index], out string currentName)
                && string.Equals(currentName, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindNextSection(List<string> lines, int start)
    {
        for (int index = start; index < lines.Count; index++)
        {
            if (TryGetSectionName(lines[index], out _))
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static bool TryGetSectionName(string line, out string name)
    {
        string trimmed = line.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
        {
            name = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return name.Length > 0;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryGetConfigKey(string line, out string key)
    {
        string trimmed = line.Trim();
        int equals = trimmed.IndexOf('=');
        if (equals > 0 && !trimmed.StartsWith("#", StringComparison.Ordinal)
            && !trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            key = trimmed.Substring(0, equals).Trim();
            return key.Length > 0;
        }

        key = string.Empty;
        return false;
    }
}
