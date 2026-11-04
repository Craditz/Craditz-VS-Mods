# Animalica: Toolsmith

the fox has arranged the parts on the floor. here is how they go together.

Version 1.1.0 — Vintage Story 1.22.X.

An optional addon for Animalica Body Tools. Install either Toolsmith 1.2.19+
or LTO BlackSmith Plus 3.0.1+, together with the chosen provider's dependencies,
on both client and server. Use exactly one provider. It uses original metal
tool heads, an animal fitting and the chosen provider's bindings. Fang caps, weapons and
natural fitted claws keep their existing behavior.

## Make a fitting

Choose a seat, brace and optional lining. All 60 combinations have their own
Handbook recipe and item. The cutter is retained during crafting.

| Component | Choices | Ingredients |
| --- | --- | --- |
| Seat | Rough wood / bone | One firewood / one bone |
| Seat | Shaped wood / precision wood | One support beam / one support beam and sandpaper |
| Brace | Rawhide / leather / sturdy leather | Two matching Toolsmith fabric strips |
| Lining | Unlined / twine / cloth / leather / sturdy leather | None / one twine / one cloth or linen / one matching leather |

There is no general twine requirement. Only the twine-lined fitting uses twine.
Seats and braces reuse the corresponding Toolsmith handle profiles; linings
combine with them. The finished tooltip shows the resulting bonuses.

## Assemble and equip

1. Hold an original supported metal head and put the fitting in your offhand.
   Hold right-click to make a parts bundle.
2. Hold the bundle and put a Toolsmith binding in your offhand. Hold right-click
   again to finish the tool. Rope, fabric strips and metal nails-and-strips use
   their native Toolsmith material properties.
3. Equip the finished Body Tool and use it with an empty active hand.

The native Toolsmith workbench also accepts the head, fitting and binding.
Place each in its matching socket with a brief right-click, then use a hammer
on the head to assemble it. Holding right-click can repeatedly place and remove
a component.
Sneak-right-click an unfinished bundle to recover its two components.

| Original head | Body Tool |
| --- | --- |
| Knife blade or shovel head | Claw Tips |
| Axe head | Rending Claws |
| Pickaxe head | Tunneling Claws |
| Prospecting pick head | Prospecting Claws |
| Scythe head | Harvesting Claws |
| Hoe head | Cultivating Claws |

Bindings affect durability and recovery according to Toolsmith's rules. A steel
binding alone does not increase mining speed. Fitting materials, head properties
and sharpness determine the relevant speed bonuses.

## Maintain and refit

Use Toolsmith's sharpening and dismantling interactions. Hold Shift-right-click
on a grindstone, or the workbench vise, to dismantle a finished tool. Ordinary
Toolsmith tools can be dismantled this way under Default animal restrictions,
so a recovered original head can be fitted for animal use. Ordinary attacks and
general use remain subject to those restrictions. Aim away from interactive
stations when pairing a recovered head with a fitting in your hands.

Component wear and head quality belong to the parts and carry through refitting;
a refit is not a repair.
Damaged bindings may be consumed when dismantling under Toolsmith's rules.

Existing manufactured Body Tools can be paired with a new fitting and finished
with a new binding. They keep their remaining condition. Their old construction
does not provide an extra handle or binding to recover. Untouched old tools keep
their previous behavior until explicitly refitted.

Keep a matching world/mod backup before removing the addon. Removing its ZIP
does not convert saved fittings or bundles into ordinary Toolsmith components.

## LTO BlackSmith Plus provider

LTO replaces standalone Toolsmith; do not install both together. LTO 3.0.1 needs
Immersive Ore Crush 2.2.5, LTO Library Plus 1.0.0 and VS ImGui 1.2.7.

The same 60 fitting combinations are available. Under LTO the first brace tier
uses its plant-fiber binding instead of Toolsmith rawhide strips. Leather, sturdy
leather and sandpaper recipes use LTO items. Fittings compose LTO's native handle
and binding profiles; their bonuses follow LTO rather than copied Toolsmith values.
Twine and cloth lining both use LTO's native flax-twine profile; LTO has no
separate cloth profile.

LTO also supports original flint axe and hoe heads for the existing flint Rending
and Cultivating Claws. Other stone heads have no matching manufactured Body Tool
and cannot consume an animal fitting. Ordinary handles retain LTO's normal tools.
Existing supported provisional bundles with an incorrect ordinary target are
corrected when you start finishing them; their component data is retained.

Hold an original supported head with the fitting in the offhand for LTO's native
five-second manual assembly, then finish the provisional tool with an LTO binding.
The LTO workbench accepts the fitting in its handle socket. Its hammer interaction
uses a timed hold. Sharpening uses LTO's whetstone or grindstone. Dismantling uses
Shift-right-click on the LTO repair workbench vise. A grindstone sharpens; it is
not LTO's dismantling station. Native quality, upgrades, sharpness and component
condition carry into the finished tool and recovered parts.

Changing providers on an existing save is unsupported. Saved
modular tools, bundles and worn parts are not migrated. Test the providers in
separate worlds and keep a matching world and mod backup. Old untouched Body Tools can still be
explicitly refitted; already modular tools from the other provider are excluded.
