param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$shapeDir = Join-Path $Root 'assets\animalicabodytools\shapes\item\tool'
$toolDir = Join-Path $Root 'assets\animalicabodytools\itemtypes\tool'
$wearableDir = Join-Path $Root 'assets\animalicabodytools\itemtypes\wearable'

$materials = [ordered]@{
    copper = 'game:block/metal/ingot/copper'
    bronze = 'game:block/metal/ingot/tinbronze'
    bismuthbronze = 'game:block/metal/ingot/bismuthbronze'
    blackbronze = 'game:block/metal/ingot/blackbronze'
    iron = 'game:block/metal/ingot/iron'
    meteoriciron = 'game:block/metal/ingot/meteoriciron'
    steel = 'game:block/metal/ingot/steel'
    # Optional integrations.  Their item/recipe definitions are disabled by
    # default and enabled by a dependency-guarded patch generated below.
    titanium = 'game:block/metal/ingot/titanium'
    wootsteel = 'game:block/metal/ingot/wootsteel'
    uraniumsteel = 'game:block/metal/ingot/uraniumsteel'
    meteoricsteel = 'game:block/metal/ingot/meteoricsteel'
    blacksteel = 'game:block/metal/ingot/blacksteel'
    redsteel = 'game:block/metal/ingot/redsteel'
    bluesteel = 'game:block/metal/ingot/bluesteel'
    aluminum = 'game:block/metal/ingot/aluminum'
    aluminumbronze = 'game:block/metal/ingot/aluminumbronze'
    mythril = 'game:block/metal/ingot/mythril'
    adamantite = 'game:block/metal/ingot/adamantite'
    toolsteel = 'improved_metallurgy:block/metal/ingot/toolsteel'
    hadfieldsteel = 'improved_metallurgy:block/metal/ingot/hadfieldsteel'
    inconel = 'improved_metallurgy:block/metal/ingot/inconel'
    highpalladium = 'metalsofmyths:block/metal/ingot/highpalladium'
    tin = 'game:block/metal/ingot/tin'
    bismuth = 'game:block/metal/ingot/bismuth'
    lead = 'game:block/metal/ingot/lead'
    zinc = 'game:block/metal/ingot/zinc'
    nickel = 'game:block/metal/ingot/nickel'
    molybdochalkos = 'game:block/metal/ingot/molybdochalkos'
    brass = 'game:block/metal/ingot/brass'
    electrum = 'game:block/metal/ingot/electrum'
    cupronickel = 'game:block/metal/ingot/cupronickel'
    leadsolder = 'game:block/metal/ingot/leadsolder'
    silversolder = 'game:block/metal/ingot/silversolder'
    chromium = 'game:block/metal/ingot/chromium'
    stainlesssteel = 'game:block/metal/ingot/stainlesssteel'
    platinum = 'game:block/metal/ingot/platinum'
    uranium = 'game:block/metal/ingot/uranium'
    lockalloy = 'game:block/metal/ingot/lockalloy'
    purplegold = 'game:block/metal/ingot/purplegold'
    bluegold = 'game:block/metal/ingot/bluegold'
    rosegold = 'game:block/metal/ingot/rosegold'
    whitegold = 'game:block/metal/ingot/whitegold'
    blackgold = 'game:block/metal/ingot/blackgold'
}

$families = @(
    'clawtips',
    'fangcaps',
    'tunnelingclaws',
    'rendingclaws',
    'harvestingclaws',
    'prospectingclaws'
)
$equippedFamilies = $families | Where-Object { $_ -ne 'fangcaps' }
$transformNames = @('guiTransform', 'groundTransform', 'tpHandTransform', 'fpHandTransform')

function New-MaterialShape {
    param(
        [string]$Source,
        [string]$Destination,
        [string]$Texture,
        [string]$TextureKey,
        [switch]$ReplaceIronAccent
    )

    $text = Get-Content -Raw -LiteralPath $Source
    $shape = $text | ConvertFrom-Json
    $sourceTexture = $shape.textures.PSObject.Properties |
        Where-Object { $_.Value -eq 'game:block/metal/ingot/copper' } |
        Select-Object -First 1
    if ($null -eq $sourceTexture) {
        throw "Copper source texture was not found in '$Source'."
    }

    $sourceKey = $sourceTexture.Name
    $text = $text.Replace(
        '"' + $sourceKey + '": "game:block/metal/ingot/copper"',
        '"' + $TextureKey + '": "' + $Texture + '"'
    )
    $text = $text.Replace('#' + $sourceKey, '#' + $TextureKey)

    if ($ReplaceIronAccent) {
        $text = $text.Replace('#iron', '#' + $TextureKey)
    }

    [IO.File]::WriteAllText($Destination, $text, [Text.UTF8Encoding]::new($false))
}

function Sync-ItemPresentation {
    param(
        [string]$Path,
        [string]$TemplatePath,
        [string]$ShapeBase
    )

    $item = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    $template = Get-Content -Raw -LiteralPath $TemplatePath | ConvertFrom-Json
    $item.shape.base = $ShapeBase

    foreach ($name in $transformNames) {
        $item.PSObject.Properties.Remove($name)
        $templateProperty = $template.PSObject.Properties[$name]
        if ($null -ne $templateProperty) {
            $item | Add-Member -NotePropertyName $name -NotePropertyValue $templateProperty.Value
        }
    }

    $json = $item | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

foreach ($family in $families) {
    $inventoryTemplate = Join-Path $shapeDir "bodytools-$family-copper.json"
    $toolTemplate = Join-Path $toolDir "$family-copper.json"
    $wearableTemplate = Join-Path $wearableDir "$family-copper-wearable.json"

    foreach ($material in $materials.Keys) {
        $inventoryShape = Join-Path $shapeDir "bodytools-$family-$material.json"
        $textureKey = "$family-$material"
        New-MaterialShape -Source $inventoryTemplate -Destination $inventoryShape -Texture $materials[$material] -TextureKey $textureKey

        $toolItem = Join-Path $toolDir "$family-$material.json"
        if (Test-Path -LiteralPath $toolItem) {
            Sync-ItemPresentation -Path $toolItem -TemplatePath $toolTemplate -ShapeBase "item/tool/bodytools-$family-$material"
        }

        $wearableItem = Join-Path $wearableDir "$family-$material-wearable.json"
        if (Test-Path -LiteralPath $wearableItem) {
            $wearableShapeBase = "item/tool/bodytools-$family-$material"
            if ($family -in $equippedFamilies) {
                $equippedTemplate = Join-Path $shapeDir "equipped-$family-copper.json"
                $equippedShape = Join-Path $shapeDir "equipped-$family-$material.json"
                New-MaterialShape -Source $equippedTemplate -Destination $equippedShape -Texture $materials[$material] -TextureKey $textureKey
                $wearableShapeBase = "item/tool/equipped-$family-$material"
            }

            Sync-ItemPresentation -Path $wearableItem -TemplatePath $wearableTemplate -ShapeBase $wearableShapeBase
        }
    }
}

$flintTexture = 'game:block/stone/flint'
$flintFamily = 'rendingclaws'
$flintInventoryShape = Join-Path $shapeDir 'bodytools-rendingclaws-flint.json'
$flintEquippedShape = Join-Path $shapeDir 'equipped-rendingclaws-flint.json'
New-MaterialShape -Source (Join-Path $shapeDir 'bodytools-rendingclaws-copper.json') -Destination $flintInventoryShape -Texture $flintTexture -TextureKey 'rendingclaws-flint' -ReplaceIronAccent
New-MaterialShape -Source (Join-Path $shapeDir 'equipped-rendingclaws-copper.json') -Destination $flintEquippedShape -Texture $flintTexture -TextureKey 'rendingclaws-flint' -ReplaceIronAccent
Sync-ItemPresentation -Path (Join-Path $toolDir 'rendingclaws-flint.json') -TemplatePath (Join-Path $toolDir 'rendingclaws-copper.json') -ShapeBase 'item/tool/bodytools-rendingclaws-flint'
Sync-ItemPresentation -Path (Join-Path $wearableDir 'rendingclaws-flint-wearable.json') -TemplatePath (Join-Path $wearableDir 'rendingclaws-copper-wearable.json') -ShapeBase 'item/tool/equipped-rendingclaws-flint'

Write-Output 'Material tool models and item presentations synchronized.'
