param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$materials = [ordered]@{
    titanium = @{ modid='titaniumtools'; texture='game:block/metal/ingot/titanium'; headNamespace='titaniumtools' }
    obdoltoolstitanium = @{ modid='obdoltools'; texture='game:block/metal/ingot/titanium'; headNamespace='game'; headMaterial='titanium' }
    wootsteel = @{ modid='wootsteel'; texture='game:block/metal/ingot/wootsteel'; headNamespace='game' }
    uraniumsteel = @{ modid='uraniumexpanded'; texture='game:block/metal/ingot/uraniumsteel'; headNamespace='game' }
    ferrousuranium = @{ modid='uraniumexpanded'; texture='game:block/metal/ingot/ferrousuranium'; headNamespace='game' }
    meteoricsteel = @{ modid='meteoricsteel'; texture='game:block/metal/ingot/meteoricsteel'; headNamespace='game' }
    blacksteel = @{ modid='canadditionalmetals'; texture='game:block/metal/ingot/blacksteel'; headNamespace='canadditionalmetals'; toolDomain='game' }
    redsteel = @{ modid='canadditionalmetals'; texture='game:block/metal/ingot/redsteel'; headNamespace='canadditionalmetals'; toolDomain='game' }
    bluesteel = @{ modid='canadditionalmetals'; texture='game:block/metal/ingot/bluesteel'; headNamespace='canadditionalmetals'; toolDomain='game' }
    aluminum = @{ modid='sillyaluminum'; texture='game:block/metal/ingot/aluminum'; headNamespace='game' }
    aluminumbronze = @{ modid='sillyaluminum'; texture='game:block/metal/ingot/aluminumbronze'; headNamespace='game' }
    realloyaluminumbronze = @{ modid='realloy'; texture='game:block/metal/ingot/aluminumbronze'; headNamespace='game'; headMaterial='aluminumbronze' }
    mythril = @{ modid='fm'; texture='game:block/metal/ingot/mythril'; headNamespace='fm' }
    adamantite = @{ modid='fm'; texture='game:block/metal/ingot/adamantite'; headNamespace='fm' }
    toolsteel = @{ modid='improvedmetallurgy'; texture='improved_metallurgy:block/metal/ingot/toolsteel'; headNamespace='improved_metallurgy' }
    hadfieldsteel = @{ modid='improvedmetallurgy'; texture='improved_metallurgy:block/metal/ingot/hadfieldsteel'; headNamespace='improved_metallurgy' }
    inconel = @{ modid='improvedmetallurgy'; texture='improved_metallurgy:block/metal/ingot/inconel'; headNamespace='improved_metallurgy' }
    highpalladium = @{ modid='metalsofmyths'; extraModids=@('improvedmetallurgy'); texture='metalsofmyths:block/metal/ingot/highpalladium'; headNamespace='metalsofmyths' }
    metalsofmythstitanium = @{ modid='metalsofmyths'; extraModids=@('improvedmetallurgy'); texture='game:block/metal/ingot/titanium'; headNamespace='metalsofmyths'; headMaterial='titanium' }
    metalsofmythsmithril = @{ modid='metalsofmyths'; extraModids=@('improvedmetallurgy'); texture='game:block/metal/ingot/mithril'; headNamespace='metalsofmyths'; headMaterial='mithril' }
    tin = @{ modid='obdoltools'; texture='game:block/metal/ingot/tin'; headNamespace='game' }
    bismuth = @{ modid='obdoltools'; texture='game:block/metal/ingot/bismuth'; headNamespace='game' }
    lead = @{ modid='obdoltools'; texture='game:block/metal/ingot/lead'; headNamespace='game' }
    zinc = @{ modid='obdoltools'; texture='game:block/metal/ingot/zinc'; headNamespace='game' }
    nickel = @{ modid='obdoltools'; texture='game:block/metal/ingot/nickel'; headNamespace='game' }
    molybdochalkos = @{ modid='obdoltools'; texture='game:block/metal/ingot/molybdochalkos'; headNamespace='game' }
    brass = @{ modid='obdoltools'; texture='game:block/metal/ingot/brass'; headNamespace='game' }
    electrum = @{ modid='obdoltools'; texture='game:block/metal/ingot/electrum'; headNamespace='game' }
    cupronickel = @{ modid='obdoltools'; texture='game:block/metal/ingot/cupronickel'; headNamespace='game' }
    leadsolder = @{ modid='obdoltools'; texture='game:block/metal/ingot/leadsolder'; headNamespace='game' }
    silversolder = @{ modid='obdoltools'; texture='game:block/metal/ingot/silversolder'; headNamespace='game' }
    chromium = @{ modid='obdoltools'; texture='game:block/metal/ingot/chromium'; headNamespace='game' }
    stainlesssteel = @{ modid='obdoltools'; texture='game:block/metal/ingot/stainlesssteel'; headNamespace='game' }
    platinum = @{ modid='obdoltools'; texture='game:block/metal/ingot/platinum'; headNamespace='game' }
    uranium = @{ modid='obdoltools'; texture='game:block/metal/ingot/uranium'; headNamespace='game' }
    lockalloy = @{ modid='realloy'; texture='game:block/metal/ingot/lockalloy'; headNamespace='game' }
    purplegold = @{ modid='realloy'; texture='game:block/metal/ingot/purplegold'; headNamespace='game' }
    bluegold = @{ modid='realloy'; texture='game:block/metal/ingot/bluegold'; headNamespace='game' }
    rosegold = @{ modid='realloy'; texture='game:block/metal/ingot/rosegold'; headNamespace='game' }
    whitegold = @{ modid='realloy'; texture='game:block/metal/ingot/whitegold'; headNamespace='game' }
    blackgold = @{ modid='realloy'; texture='game:block/metal/ingot/blackgold'; headNamespace='game' }
}
# These pinned providers use ingot textures, not generated fox-art PNGs.
# Keep this item-default correction separate from shape-domain mapping policy.
$verifiedItemTextureFallbacks = @{
    aluminum = 'game:block/metal/ingot/aluminum'
    aluminumbronze = 'game:block/metal/ingot/aluminumbronze'
    titanium = 'game:block/metal/ingot/titanium'
    meteoricsteel = 'game:block/metal/ingot/meteoricsteel'
}
$families = [ordered]@{ clawtips=@('metal-clawtips','knifeblade'); cultivatingclaws=@('metal-cultivating','hoehead'); fangcaps=@('metal-fangcaps','bladehead-falx'); harvestingclaws=@('metal-harvesting','scythehead'); prospectingclaws=@('metal-prospecting','prospectingpickhead'); rendingclaws=@('metal-rending','axehead'); tunnelingclaws=@('metal-tunneling','pickaxehead') }
$sourceStats = @{
    aluminum = @{ clawtips=@(270,2.5,2); cultivatingclaws=@(180,1.125,2); harvestingclaws=@(405,1.125,2); prospectingclaws=@(135,1.125,2); rendingclaws=@(225,1.8,2); tunnelingclaws=@(270,1.125,2) }
    aluminumbronze = @{ clawtips=@(725,3.875,4); cultivatingclaws=@(415,2.4,4); harvestingclaws=@(1125,1.125,4); prospectingclaws=@(500,2.425,4); rendingclaws=@(750,3.75,4); tunnelingclaws=@(775,2.5,4) }
    mythril = @{ clawtips=@(3000,4.6,6); cultivatingclaws=@(1850,2.8,6); fangcaps=@(2625,6,6); harvestingclaws=@(4000,1.125,6); prospectingclaws=@(2136,2.8,6); rendingclaws=@(2000,4.5,6); tunnelingclaws=@(3000,2.8,6) }
    adamantite = @{ clawtips=@(3800,4.8,7); cultivatingclaws=@(2150,3.2,7); fangcaps=@(3225,7,7); harvestingclaws=@(4800,1.125,7); prospectingclaws=@(2950,3,7); rendingclaws=@(2600,5,7); tunnelingclaws=@(3800,3.2,7) }
    toolsteel = @{ clawtips=@(2800,5,5); cultivatingclaws=@(1500,2.75,5); fangcaps=@(2500,6,5); harvestingclaws=@(3800,1.125,5); prospectingclaws=@(1950,2.75,6); rendingclaws=@(2200,5.5,6); tunnelingclaws=@(3500,2.75,6) }
    hadfieldsteel = @{ clawtips=@(3400,4.5,5); cultivatingclaws=@(1800,2.5,5); fangcaps=@(2750,5.5,5); harvestingclaws=@(4200,1.125,5); prospectingclaws=@(2250,2.5,6); rendingclaws=@(2600,5,6); tunnelingclaws=@(4500,2.75,6) }
    inconel = @{ clawtips=@(4000,5,6); cultivatingclaws=@(2200,2.5,5); fangcaps=@(3000,6,6); harvestingclaws=@(4800,1.125,5); prospectingclaws=@(2500,2.75,7); rendingclaws=@(3000,5.5,6); tunnelingclaws=@(6000,2.75,7) }
    highpalladium = @{ clawtips=@(6000,7,8); cultivatingclaws=@(4000,5,8); fangcaps=@(5000,7,8); harvestingclaws=@(7000,1.125,8); prospectingclaws=@(4000,4,10); rendingclaws=@(5000,7,10); tunnelingclaws=@(8000,3.5,10) }
}
$statsPath = Join-Path $Root 'docs\more-vanilla-material-stats.json'
if (Test-Path $statsPath) {
    $statsData = Get-Content -Raw $statsPath | ConvertFrom-Json
    foreach ($material in $statsData.materials.psobject.Properties.Name) {
        $sourceStats[$material] = @{}
        foreach ($family in $statsData.materials.$material.psobject.Properties.Name) {
            $sourceStats[$material][$family] = @($statsData.materials.$material.$family | ForEach-Object { $_ })
        }
    }
}
$uraniumStatsPath = Join-Path $Root 'docs\uraniumexpanded-material-stats.json'
if (Test-Path $uraniumStatsPath) {
    $statsData = Get-Content -Raw $uraniumStatsPath | ConvertFrom-Json
    foreach ($material in $statsData.materials.psobject.Properties.Name) { $sourceStats[$material] = @{}; foreach ($family in $statsData.materials.$material.psobject.Properties.Name) { $sourceStats[$material][$family] = @($statsData.materials.$material.$family | ForEach-Object { $_ }) } }
}
$originalOptionalStatsPath = Join-Path $Root 'docs\original-optional-material-stats.json'
if (Test-Path $originalOptionalStatsPath) {
    $statsData = Get-Content -Raw $originalOptionalStatsPath | ConvertFrom-Json
    foreach ($material in $statsData.materials.psobject.Properties.Name) {
        $sourceStats[$material] = @{}
        foreach ($family in $statsData.materials.$material.psobject.Properties.Name) { $sourceStats[$material][$family] = @($statsData.materials.$material.$family | ForEach-Object { $_ }) }
    }
}
$mymStatsPath = Join-Path $Root 'docs\metalsofmyths-material-stats.json'
if (Test-Path $mymStatsPath) {
    $statsData = Get-Content -Raw $mymStatsPath | ConvertFrom-Json
    foreach ($material in $statsData.materials.psobject.Properties.Name) {
        $sourceStats[$material] = @{}
        foreach ($family in $statsData.materials.$material.psobject.Properties.Name) { $sourceStats[$material][$family] = @($statsData.materials.$material.$family | ForEach-Object { $_ }) }
    }
}
$realloyStatsPath = Join-Path $Root 'docs\realloy-material-stats.json'
if (Test-Path $realloyStatsPath) {
    $statsData = Get-Content -Raw $realloyStatsPath | ConvertFrom-Json
    foreach ($material in $statsData.materials.psobject.Properties.Name) {
        $sourceStats[$material] = @{}
        foreach ($family in $statsData.materials.$material.psobject.Properties.Name) {
            $sourceStats[$material][$family] = @($statsData.materials.$material.$family | ForEach-Object { $_ })
        }
    }
}
# Audited provider falx values and actual registered head codes.
$falxSources = Get-Content -Raw (Join-Path $Root 'docs/falx-source-stats.json') | ConvertFrom-Json
$auditedFangMaterials = @($falxSources.psobject.Properties.Name)
foreach ($material in $auditedFangMaterials) {
    $sourceStats[$material]['fangcaps'] = @($falxSources.$material.stats)
}
$itemDir = Join-Path $Root 'assets\animalicabodytools\itemtypes\tool'
$wearDir = Join-Path $Root 'assets\animalicabodytools\itemtypes\wearable'
$shapeDir = Join-Path $Root 'assets\animalicabodytools\shapes\item\tool'
$recipeRoot = Join-Path $Root 'assets\animalicabodytools\recipes\grid\bodytools'
$patchDir = Join-Path $Root 'assets\animalicabodytools\patches\material-compat'
New-Item -ItemType Directory -Force $patchDir | Out-Null
$obsoleteNamePatch = Join-Path $patchDir 'name-conflicts.json'
if (Test-Path $obsoleteNamePatch) { Remove-Item -LiteralPath $obsoleteNamePatch -Force }

# Keep the normal item names material-first.  Provider qualifiers are added by
# a guarded language patch only when two sources expose the same material, so
# a provider installed by itself still presents the familiar name.
$materialDisplayNames = @{
    titanium='Titanium'; obdoltoolstitanium='Titanium'; wootsteel='Wootsteel'; uraniumsteel='Uranium Steel'; ferrousuranium='Ferrous Uranium'; meteoricsteel='Meteoric Steel'; blacksteel='Black Steel'; redsteel='Red Steel'; bluesteel='Blue Steel'; aluminum='Aluminum'; aluminumbronze='Aluminum Bronze'; realloyaluminumbronze='Aluminum Bronze'; mythril='Mythril'; adamantite='Adamantite'; toolsteel='Tool Steel'; hadfieldsteel='Hadfield Steel'; inconel='Inconel'; highpalladium='High Palladium'; metalsofmythstitanium='Titanium'; metalsofmythsmithril='Mithril'; tin='Tin'; bismuth='Bismuth'; lead='Lead'; zinc='Zinc'; nickel='Nickel'; molybdochalkos='Molybdochalkos'; brass='Brass'; electrum='Electrum'; cupronickel='Cupronickel'; leadsolder='Lead Solder'; silversolder='Silver Solder'; chromium='Chromium'; stainlesssteel='Stainless Steel'; platinum='Platinum'; uranium='Uranium'; lockalloy='Lockalloy'; purplegold='Purple Gold'; bluegold='Blue Gold'; rosegold='Rose Gold'; whitegold='White Gold'; blackgold='Black Gold'
}
$familyDisplayNames = @{ clawtips='Claw Tips'; cultivatingclaws='Cultivating Claws'; fangcaps='Fang Caps'; harvestingclaws='Harvesting Claws'; prospectingclaws='Prospecting Claws'; rendingclaws='Rending Claws'; tunnelingclaws='Tunneling Claws' }
$languagePath = Join-Path $Root 'assets\animalicabodytools\lang\en.json'
if (Test-Path $languagePath) {
    $languageText = Get-Content -Raw $languagePath
    foreach ($material in $materials.Keys) {
        $displayMaterial = $materialDisplayNames[$material]
        if (-not $displayMaterial) { continue }
        foreach ($family in $families.Keys) {
            $display = "$displayMaterial $($familyDisplayNames[$family])"
            foreach ($key in @(
                "animalicabodytools:item-$family-$material",
                "item-animalicabodytools-$family-$material",
                "animalicabodytools:item-$family-$material-wearable",
                "item-animalicabodytools-$family-$material-wearable")) {
                $pattern = '("' + [regex]::Escape($key) + '"\s*:\s*")[^"]*(")'
                $languageText = [regex]::Replace($languageText, $pattern, { param($match) $match.Groups[1].Value + $display + $match.Groups[2].Value })
            }
        }
    }
    [IO.File]::WriteAllText($languagePath, (($languageText -replace '(\r?\n)+$', '') + "`r`n"), [Text.UTF8Encoding]::new($false))
}

$statBindings = @()
foreach ($material in $materials.Keys) {
    $patches = @()
    $requiredMods = @($materials[$material].modid) + @($materials[$material].extraModids) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    foreach ($family in $families.Keys) {
        if ($family -eq 'fangcaps' -and $material -notin $auditedFangMaterials -and $material -notin @('mythril','adamantite','toolsteel','hadfieldsteel','inconel','highpalladium','tin','bismuth','lead','zinc','nickel','molybdochalkos','brass','electrum','cupronickel','leadsolder','silversolder','chromium','stainlesssteel','platinum','uranium','realloyaluminumbronze','ferrousuranium','obdoltoolstitanium','metalsofmythstitanium','metalsofmythsmithril')) { continue }
        if ($sourceStats.ContainsKey($material) -and -not $sourceStats[$material].ContainsKey($family)) { continue }
        $sourceMaterial = if ($materials[$material].headMaterial) { $materials[$material].headMaterial } else { $material }
        $statBindings += [ordered]@{ family=$family; material=$sourceMaterial; domain=$(if ($materials[$material].toolDomain) {$materials[$material].toolDomain} else {$materials[$material].headNamespace}); code="$family-$material"; weaponFamily=$(if ($material -in @('metalsofmythsmithril','highpalladium')) {'sword'} else {'falx'}) }
        $item = Get-Content -Raw (Join-Path $itemDir "$family-steel.json") | ConvertFrom-Json
        $item.code = "$family-$material"; $item | Add-Member enabled $false
        if ($sourceStats.ContainsKey($material) -and $sourceStats[$material].ContainsKey($family)) { $item.durability=$sourceStats[$material][$family][0]; $item.attackPower=$sourceStats[$material][$family][1]; $item.toolTier=$sourceStats[$material][$family][2] }
        $item.shape.base = $item.shape.base.Replace('-steel', "-$material")
        if ($item.texture -is [pscustomobject]) {
            $item.texture.base = if ($verifiedItemTextureFallbacks.ContainsKey($material)) { $verifiedItemTextureFallbacks[$material] } else { $item.texture.base.Replace('-steel', "-$material") }
        }
        $item | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $itemDir "$family-$material.json")
        $wear = Get-Content -Raw (Join-Path $wearDir "$family-steel-wearable.json") | ConvertFrom-Json
        $wear.code = "$family-$material-wearable"; $wear | Add-Member enabled $false
        if ($sourceStats.ContainsKey($material) -and $sourceStats[$material].ContainsKey($family)) {
            $wear.durability=$sourceStats[$material][$family][0]; $wear.attackPower=$sourceStats[$material][$family][1]; $wear.toolTier=$sourceStats[$material][$family][2]
        }
        $wear.shape.base = $wear.shape.base.Replace('-steel', "-$material")
        if ($wear.texture -is [pscustomobject]) {
            $wear.texture.base = if ($verifiedItemTextureFallbacks.ContainsKey($material)) { $verifiedItemTextureFallbacks[$material] } else { $wear.texture.base.Replace('-steel', "-$material") }
        }
        if ($wear.behaviors) {
            foreach ($behavior in @($wear.behaviors)) {
                if ($behavior.properties -and $behavior.properties.shape) { $behavior.properties.shape.base = $behavior.properties.shape.base.Replace("$family-steel", "$family-$material") }
            }
        }
        if ($wear.attributes) {
            if ($wear.attributes.animalicaWearableBodyToolId) { $wear.attributes.animalicaWearableBodyToolId = $wear.attributes.animalicaWearableBodyToolId.Replace("$family-steel", "$family-$material") }
            if ($wear.attributes.animalicaVirtualToolItemCode) { $wear.attributes.animalicaVirtualToolItemCode = $wear.attributes.animalicaVirtualToolItemCode.Replace(":$family-steel", ":$family-$material") }
        }
        $wear | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 (Join-Path $wearDir "$family-$material-wearable.json")
        foreach ($shapeKind in @('bodytools','equipped')) {
            $shapePath = Join-Path $shapeDir "$shapeKind-$family-steel.json"
            if (Test-Path $shapePath) {
                (Get-Content -Raw $shapePath).Replace("$shapeKind-$family-steel", "$shapeKind-$family-$material").Replace('game:block/metal/ingot/steel', $materials[$material].texture) |
                    Set-Content -Encoding utf8 (Join-Path $shapeDir "$shapeKind-$family-$material.json")
            }
        }
        $headMaterial = if ($materials[$material].headMaterial) { $materials[$material].headMaterial } else { $material }
        $head = "$($materials[$material].headNamespace):$($families[$family][1])-$headMaterial"
        if ($family -eq 'fangcaps' -and $material -in $auditedFangMaterials) {
            $head = $falxSources.$material.head
        }
        if ($family -eq 'fangcaps' -and $material -in @('metalsofmythsmithril','highpalladium','electrum')) {
            $head = "animalicabodytools:bladehead-falx-$material"
        }
        # Preserve every established binding option, including rawhide.
        $objects = @(foreach ($template in @('steel.json', 'steel-hide.json')) {
            $recipe = Get-Content -Raw (Join-Path (Join-Path $recipeRoot $families[$family][0]) $template)
            $recipe = $recipe.Replace("game:$($families[$family][1])-steel", $head).Replace("animalicabodytools:$family-steel-wearable", "animalicabodytools:$family-$material-wearable")
            $recipe | ConvertFrom-Json
        })
        foreach ($obj in $objects) { $obj | Add-Member enabled $false }
        ConvertTo-Json -InputObject $objects -Depth 100 | Set-Content -Encoding utf8 (Join-Path (Join-Path $recipeRoot $families[$family][0]) "$material.json")
        $guards = @($requiredMods | ForEach-Object { @{modid=$_} })
        $recipeFile = "animalicabodytools:recipes/grid/bodytools/$($families[$family][0])/$material.json"
        $recipePatches = if ($objects -is [array]) {
            @($objects | ForEach-Object -Begin { $index = 0 } -Process { [ordered]@{op='replace';path="/$index/enabled";value=$true;file=$recipeFile;dependsOn=$guards}; $index++ })
        } else {
            @([ordered]@{op='replace';path='/enabled';value=$true;file=$recipeFile;dependsOn=$guards})
        }
        $guard = @([ordered]@{op='replace';path='/enabled';value=$true;file="animalicabodytools:itemtypes/tool/$family-$material.json";dependsOn=$guards},[ordered]@{op='replace';path='/enabled';value=$true;file="animalicabodytools:itemtypes/wearable/$family-$material-wearable.json";dependsOn=$guards}) + $recipePatches
        $patches += $guard
    }
    $patches | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $patchDir "$material.json")
}

# Ensure newly introduced fang variants also have item and wearable names.
$language = Get-Content -Raw $languagePath | ConvertFrom-Json
foreach ($material in $auditedFangMaterials) {
    foreach ($suffix in @('', '-wearable')) {
        foreach ($prefix in @('animalicabodytools:item-', 'item-animalicabodytools-')) {
            $language | Add-Member -Force -NotePropertyName "${prefix}fangcaps-${material}${suffix}" -NotePropertyValue "$($materialDisplayNames[$material]) Fang Caps"
        }
    }
}
$language | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 $languagePath

# ReAlloy patches vanilla lockalloy heads, but its gold-alloy falxes are
# steel heads plated with the alloy. Preserve that cost in owned heads,
# just as for aluminum and electrum; no realloy: head IDs are registered.
foreach ($material in @('purplegold','bluegold','rosegold','whitegold','blackgold')) {
    $head = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/falx/itemtypes/toolhead/bladehead-falx-aluminum.json') | ConvertFrom-Json
    $head.code = "bladehead-falx-$material"
    $head | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Root "assets/animalicabodytools/itemtypes/toolhead/bladehead-falx-$material.json")
    $recipe = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/falx/recipes/grid/bodytools/aluminum-falx-head.json') | ConvertFrom-Json
    $recipe.ingredients.P.code = "game:metalplate-$material"
    $recipe.output.code = "animalicabodytools:bladehead-falx-$material"
    $recipe | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Root "assets/animalicabodytools/recipes/grid/bodytools/$material-falx-head.json")
    $patches = @("itemtypes/toolhead/bladehead-falx-$material.json", "recipes/grid/bodytools/$material-falx-head.json") | ForEach-Object {
        [ordered]@{op='replace';path='/enabled';value=$true;file="animalicabodytools:$_";dependsOn=@(@{modid='realloy'})}
    }
    $patches | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 (Join-Path $patchDir "$material-falx-head.json")
    $language | Add-Member -Force -NotePropertyName "item-bladehead-falx-$material" -NotePropertyValue "$($materialDisplayNames[$material]) Falx Head"
}
$language | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 $languagePath

# Silly Aluminum supplies a steel-based finished falx but no aluminum head.
# Keep the same steel-head + aluminum-plate cost in an owned compatibility item.
foreach ($relative in @('itemtypes/toolhead/bladehead-falx-aluminum.json', 'recipes/grid/bodytools/aluminum-falx-head.json')) {
    $destination = Join-Path $Root "assets/animalicabodytools/$relative"
    New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "templates/falx/$relative") $destination -Force
}
$headPatches = @('itemtypes/toolhead/bladehead-falx-aluminum.json', 'recipes/grid/bodytools/aluminum-falx-head.json') | ForEach-Object {
    [ordered]@{ op='replace'; path='/enabled'; value=$true; file="animalicabodytools:$_"; dependsOn=@(@{modid='sillyaluminum'}) }
}
$headPatches | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 (Join-Path $patchDir 'aluminum-falx-head.json')
$language | Add-Member -Force -NotePropertyName 'item-bladehead-falx-aluminum' -NotePropertyValue 'Aluminum Falx Head'
$language | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 $languagePath

$configDir = Join-Path $Root 'assets/animalicabodytools/config'
New-Item -ItemType Directory -Force $configDir | Out-Null
ConvertTo-Json -InputObject @($statBindings) -Depth 10 | Set-Content -Encoding utf8 (Join-Path $configDir 'material-tool-bindings.json')

# Metals of Myths provides swords for these metals, but no falx heads.
# Supply forgeable falx heads with the vanilla falx smithing pattern.
foreach ($material in @('metalsofmythsmithril','highpalladium')) {
    $sourceMaterial = if ($material -eq 'metalsofmythsmithril') {'mithril'} else {'highpalladium'}
    $head = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/falx/itemtypes/toolhead/bladehead-falx-aluminum.json') | ConvertFrom-Json
    $head.code = "bladehead-falx-$material"
    $head.textures | Add-Member -Force -NotePropertyName 'steel' -NotePropertyValue @{base=$materials[$material].texture}
    $head | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Root "assets/animalicabodytools/itemtypes/toolhead/bladehead-falx-$material.json")
    $recipe = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/base-heads/recipe-blade-falx.json') | ConvertFrom-Json
    $recipe.ingredient = @{type='item';code="game:ingot-$sourceMaterial"}
    $recipe.output = @{type='item';code="animalicabodytools:bladehead-falx-$material"}
    $recipe | Add-Member -Force -NotePropertyName 'enabled' -NotePropertyValue $false
    $recipe.code = "animalicafalx-$material"
    $dir = Join-Path $Root 'assets/animalicabodytools/recipes/smithing'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $recipe | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $dir "blade-falx-$material.json")
    $patches = @("itemtypes/toolhead/bladehead-falx-$material.json", "recipes/smithing/blade-falx-$material.json") | ForEach-Object {
        [ordered]@{op='replace';path='/enabled';value=$true;file="animalicabodytools:$_";dependsOn=@(@{modid='metalsofmyths'},@{modid='improvedmetallurgy'})}
    }
    $patches | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 (Join-Path $patchDir "$material-falx-head.json")
    $language | Add-Member -Force -NotePropertyName "item-bladehead-falx-$material" -NotePropertyValue "$($materialDisplayNames[$material]) Falx Head"
}
$language | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 $languagePath

# More Vanilla Tools also plates a steel falx for electrum instead of registering a head.
$head = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/falx/itemtypes/toolhead/bladehead-falx-aluminum.json') | ConvertFrom-Json
$head.code = 'bladehead-falx-electrum'
$head | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Root 'assets/animalicabodytools/itemtypes/toolhead/bladehead-falx-electrum.json')
$recipe = Get-Content -Raw (Join-Path $PSScriptRoot 'templates/falx/recipes/grid/bodytools/aluminum-falx-head.json') | ConvertFrom-Json
$recipe.ingredients.P.code = 'game:metalplate-electrum'
$recipe.output.code = 'animalicabodytools:bladehead-falx-electrum'
$recipe | ConvertTo-Json -Depth 30 | Set-Content -Encoding utf8 (Join-Path $Root 'assets/animalicabodytools/recipes/grid/bodytools/electrum-falx-head.json')
$patches = @('itemtypes/toolhead/bladehead-falx-electrum.json', 'recipes/grid/bodytools/electrum-falx-head.json') | ForEach-Object {
    [ordered]@{op='replace';path='/enabled';value=$true;file="animalicabodytools:$_";dependsOn=@(@{modid='obdoltools'})}
}
$patches | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 (Join-Path $patchDir 'electrum-falx-head.json')
$language | Add-Member -Force -NotePropertyName 'item-bladehead-falx-electrum' -NotePropertyValue 'Electrum Falx Head'
$language | ConvertTo-Json -Depth 100 | Set-Content -Encoding utf8 $languagePath
