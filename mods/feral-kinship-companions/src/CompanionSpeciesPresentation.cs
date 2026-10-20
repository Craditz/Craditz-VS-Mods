#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;

namespace FeralKinshipCompanions;
internal static class CompanionSpeciesPresentation
{
    internal static AssetLocation? ExactCode(string? text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Any(char.IsControl)||text.Any(char.IsWhiteSpace))return null;
        try {var code=new AssetLocation(text);return code.Domain.Length>0&&code.Path.Length>0&&code.Valid?code:null;}
        catch(ArgumentException){return null;}
    }
    // The provider records the exact original source code on its source-name
    // behavior. For historical unloaded codes, this is also its documented
    // tame-code construction, not a species rename or recovery alias.
    internal static string SourceCode(EntityProperties? properties,string? savedCode)
    {
        var behaviors=(properties?.Server?.BehaviorsAsJsonObj??Array.Empty<JsonObject>())
            .Concat(properties?.Client?.BehaviorsAsJsonObj??Array.Empty<JsonObject>());
        foreach(var b in behaviors) {
            if(b["code"].AsString()?.EndsWith("sourcename",StringComparison.OrdinalIgnoreCase)!=true)continue;
            var code=ExactCode(b["sourceEntityCode"].AsString());if(code!=null)return code.ToString();
        }
        var tame=ExactCode(savedCode);
        if(tame?.Domain!="tamablesfotsa"||!tame.Path.StartsWith("tame-",StringComparison.Ordinal))return string.Empty;
        string rest=tame.Path[5..];int split=rest.IndexOf('-');
        if(split<=0||split==rest.Length-1)return string.Empty;
        // Known provider namespaces only: don't guess how a hyphenated or new
        // provider namespace was encoded if its exact metadata is unavailable.
        string domain=rest[..split];
        if(!FotsaDomains.Contains(domain))return string.Empty;
        return new AssetLocation(domain,rest[(split+1)..]).ToString();
    }
    private static readonly HashSet<string> FotsaDomains=new(StringComparer.Ordinal) {
        "bovinae","caninae","capreolinae","casuariidae","cervinae","chelonioidea","dinornithidae","elephantidae",
        "felinae","iniidae","machairodontinae","manidae","meiolaniidae","pantherinae","rhinocerotidae",
        "sirenia","spheniscidae","thylacinidae","viverridae","vombatidae"
    };
    private static string? Localize(string key)
    {
        if(string.IsNullOrEmpty(Lang.CurrentLocale))return null;
        try {string? value=Lang.GetUnformatted(key);return string.IsNullOrWhiteSpace(value)||value==key?null:value;}
        catch(KeyNotFoundException){return null;} // Languages not initialized yet.
    }
    internal static string Display(string? speciesId,string? entityCode,string? sourceCode=null,System.Func<string,string?>? translate=null)
    {
        translate??=Localize;
        var code=ExactCode(entityCode);var source=ExactCode(sourceCode??string.Empty);
        if(source==null)source=ExactCode(SourceCode(null,entityCode));
        // Provider source-name metadata avoids both a personal nametag and the
        // broad "FOTSA Tamable" compatibility profile.
        var names=code?.Domain is "tamablesfotsa" or "tamablescritters"?new[]{source,code}:new[]{code,source};
        foreach(var name in names) {
            if(name==null)continue;
            string? translated=translate(name.Domain+":item-creature-"+name.Path);
            if(!string.IsNullOrWhiteSpace(translated))return translated;
        }
        if(CompanionSpeciesCatalog.TryGetById(speciesId??string.Empty,out var profile)
            && profile.Id is not ("tamables-fotsa" or "tamables-critters")
            && (code==null||profile.MatchesTameEntityCode(code)))
            return translate("feralkinshipcompanions:species-"+profile.Id)??profile.DisplayName;
        if(code==null)return translate("feralkinshipcompanions:species-unavailable")??"Unavailable";
        return string.Format(translate("feralkinshipcompanions:species-name-unavailable")??"Name unavailable ({0})",code);
    }
    internal static string SocialLabel(FoxSocialStatePacket state)
    {
        string label=Localize(state.SpeciesFromSavedRecord?"feralkinshipcompanions:species-saved-label":"feralkinshipcompanions:species-label")
            ??(state.SpeciesFromSavedRecord?"Species: {0} (saved)":"Species: {0}");
        return string.Format(label,Display(state.SpeciesId,state.AppearanceCode,state.SpeciesSourceCode));
    }
}
