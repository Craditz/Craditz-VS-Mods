using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace AnimalicaBodyTools.Systems;

internal sealed class MaterialToolBinding
{
    public string Family = "";
    public string Material = "";
    public string Domain = "";
    public string Code = "";
    public string WeaponFamily = "falx";
}

/// <summary>Read patched provider items, not inherited steel template values.</summary>
internal static class MaterialToolStatSystem
{
    internal static int Durability(int value) => Math.Max(1, (int)Math.Round(value * 1.25, MidpointRounding.AwayFromZero));
    internal static float FangDamage(float value) => Math.Max(0.5f, value - (value >= 5 ? 1f : 0.75f));

    internal static Item? Resolve(System.Func<AssetLocation, Item?> get, string domain, string material, string family)
    {
        string[] prefixes = family switch
        {
            "knife" => ["knife-generic", "knife"],
            "axe" => ["axe-felling", "axe"],
            "falx" => ["blade-falx", "blade"],
            _ => [family]
        };
        foreach (string prefix in prefixes)
        {
            Item? item = get(new AssetLocation(domain, prefix + "-" + material));
            if (item != null) return item;
        }
        return null;
    }

    internal static bool Convert(MaterialToolBinding binding, System.Func<AssetLocation, Item?> get, Item target, out string error)
    {
        string family = binding.Family switch
        {
            "clawtips" => "shovel", "fangcaps" => binding.WeaponFamily, "cultivatingclaws" => "hoe",
            "harvestingclaws" => "scythe", "prospectingclaws" => "prospectingpick",
            "rendingclaws" => "axe", "tunnelingclaws" => "pickaxe", _ => ""
        };
        Item? source = Resolve(get, binding.Domain, binding.Material, family);
        Item? knife = binding.Family == "clawtips" ? Resolve(get, binding.Domain, binding.Material, "knife") : null;
        if (source == null || (binding.Family == "clawtips" && knife == null))
        {
            error = $"{binding.Domain}:{family}-{binding.Material}" + (binding.Family == "clawtips" ? " and matching knife" : "");
            return false;
        }

        target.Durability = Durability(source.Durability);
        target.ToolTier = source.ToolTier;
        target.AttackPower = binding.Family == "fangcaps" ? FangDamage(source.AttackPower) : (knife ?? source).AttackPower;
        var speeds = new Dictionary<EnumBlockMaterial, float>();
        float factor = binding.Family == "harvestingclaws" ? 1f : 0.9f;
        if (source.MiningSpeed != null)
            foreach (var pair in source.MiningSpeed)
                speeds[pair.Key] = (float)Math.Round(pair.Value * (double)factor, 2, MidpointRounding.AwayFromZero);
        if (knife?.MiningSpeed != null)
            foreach (var pair in knife.MiningSpeed)
                if (pair.Key is EnumBlockMaterial.Plant or EnumBlockMaterial.Leaves)
                    speeds[pair.Key] = (float)Math.Round(pair.Value * 0.9, 2, MidpointRounding.AwayFromZero);
        target.MiningSpeed = speeds;
        error = "";
        return true;
    }

    public static void Apply(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(new AssetLocation("animalicabodytools:config/material-tool-bindings.json"));
        if (asset == null) return;
        var bindings = asset.ToObject<MaterialToolBinding[]>();
        int applied = 0;
        foreach (var binding in bindings)
        {
            Item? tool = api.World.GetItem(new AssetLocation("animalicabodytools", binding.Code));
            if (tool == null) continue; // Provider guard left this variant disabled.
            if (!Convert(binding, api.World.GetItem, tool, out string missing))
            {
                api.Logger.Error("[animalicabodytools] Cannot balance {0}: missing source {1}", binding.Code, missing);
                continue;
            }
            Item? wear = api.World.GetItem(new AssetLocation("animalicabodytools", binding.Code + "-wearable"));
            if (wear != null)
            {
                wear.Durability = tool.Durability;
                wear.AttackPower = tool.AttackPower;
                wear.ToolTier = tool.ToolTier;
                wear.MiningSpeed = new Dictionary<EnumBlockMaterial, float>(tool.MiningSpeed);
            }
            applied++;
        }
        api.Logger.Notification("[animalicabodytools] Applied provider tool balance to {0} material variants.", applied);
    }
}
