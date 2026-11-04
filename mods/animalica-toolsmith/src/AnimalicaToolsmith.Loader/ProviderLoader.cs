using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;

namespace AnimalicaToolsmith.Bootstrap;

public static class ProviderSelection
{
    public static string Select(bool toolsmith,bool lto)
    {
        if(toolsmith==lto)throw new InvalidOperationException(toolsmith
            ? "Animalica: Toolsmith requires exactly one provider. LTO BlackSmith Plus replaces Toolsmith; remove the standalone Toolsmith mod when using LTO."
            : "Animalica: Toolsmith requires either Toolsmith 1.2.19+ OR LTO BlackSmith Plus 3.0.1+, with that provider's dependencies.");
        return toolsmith?"toolsmith":"ltobsplus";
    }
}

public sealed class ProviderLoader : ModSystem
{
    internal static readonly Dictionary<EnumAppSide,ModSystem> Backends=new();
    internal static readonly Dictionary<EnumAppSide,string> Providers=new();
    public override double ExecuteOrder()=>.05;
    public override void StartPre(ICoreAPI api)
    {
        string selected=ProviderSelection.Select(api.ModLoader.IsModEnabled("toolsmith"),api.ModLoader.IsModEnabled("ltobsplus"));
        string minimum=selected=="toolsmith"?"1.2.19":"3.0.1";
        string? version=api.ModLoader.GetMod(selected)?.Info?.Version;
        if(version!=null && !GameVersion.IsAtLeastVersion(version,minimum))
            throw new InvalidOperationException($"Animalica: Toolsmith requires {selected} {minimum} or newer; installed {version}.");
        using var stream=typeof(ProviderLoader).Assembly.GetManifestResourceStream("providers."+selected)
            ?? throw new InvalidOperationException("Missing packaged integration module: "+selected);
        string assemblyName=selected=="toolsmith"?"AnimalicaToolsmith":"AnimalicaToolsmith.Lto";
        var assembly=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a=>a.GetName().Name==assemblyName)
            ?? AssemblyLoadContext.Default.LoadFromStream(stream);
        var backend=(ModSystem)Activator.CreateInstance(assembly.GetType("AnimalicaToolsmith.IntegrationSystem",true)!)!;
        Backends[api.Side]=backend;Providers[api.Side]=selected;
        backend.StartPre(api);
        api.Logger.Notification("[animalicatoolsmith] Selected provider {0}; integration assembly {1}. Only this provider module is loaded.",selected,assemblyName);
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        if(Providers[api.Side]!="ltobsplus")return;
        // Before native item/recipe loaders: inactive-provider resources must not
        // leave unresolved Handbook ingredients or force standalone Toolsmith.
        foreach(var pair in api.Assets.AllAssets.Where(p=>p.Key.Domain=="animalicatoolsmith").ToArray())
        {
            string path=pair.Key.Path;
            if(!path.EndsWith(".json") || !(path.StartsWith("recipes/grid/fittings/") || path.StartsWith("lang/")))continue;
            var asset=api.Assets.Get(pair.Key); // AllAssets also contains lazy, unloaded entries.
            string json=asset.ToText()
                .Replace("toolsmith:fabricstrips-rawhide","ltobsplus:vegetalfiberbinding",StringComparison.Ordinal)
                .Replace("toolsmith:fabricstrips-leather","ltobsplus:leatherstrips",StringComparison.Ordinal)
                .Replace("toolsmith:fabricstrips-sturdy","ltobsplus:sturdyleatherstrips",StringComparison.Ordinal)
                .Replace("toolsmith:sandpaper","ltobsplus:sandpaper",StringComparison.Ordinal)
                .Replace("Rawhide","Plant fiber",StringComparison.Ordinal)
                .Replace("a Toolsmith binding","an LTO binding",StringComparison.Ordinal);
            asset.Data=Encoding.UTF8.GetBytes(json);
        }
    }
}

/// <summary>Keep integration finalization after native provider catalogs, independently of early asset adaptation.</summary>
public sealed class ProviderHost : ModSystem
{
    private ModSystem? backend;
    private EnumAppSide side;
    public override double ExecuteOrder()=>1.1;
    public override void Start(ICoreAPI api)
    {side=api.Side;backend=ProviderLoader.Backends[side];backend.Start(api);}
    public override void StartClientSide(ICoreClientAPI api)=>backend!.StartClientSide(api);
    public override void StartServerSide(ICoreServerAPI api)=>backend!.StartServerSide(api);
    public override void AssetsLoaded(ICoreAPI api)=>backend!.AssetsLoaded(api);
    public override void AssetsFinalize(ICoreAPI api)=>backend!.AssetsFinalize(api);
    public override void Dispose()
    {backend?.Dispose();backend=null;ProviderLoader.Backends.Remove(side);ProviderLoader.Providers.Remove(side);}
}
