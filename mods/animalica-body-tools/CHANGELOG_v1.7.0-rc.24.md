# Animalica Body Tools 1.7.0-rc.24 preview

Turns out a copper ballista could spend all day staring at a perfectly good flint arrow if Combat Overhaul was installed. Fixed that.

- The scout, medium, and ballista now load Combat Overhaul's replacement arrows. Hold the arrow you want, hold right-click until the latch clicks, then middle-click to fire. The ballista still needs nine seconds standing still.
- All 14 arrow materials keep their usual Body Tools damage and recovery chances. Combat Overhaul keeps its own arrow storage and projectile behaviors. The compatibility patch is bundled and Combat Overhaul is optional.

**Tested on Vintage Story 1.22.7 with Combat Overhaul Fork 0.18.9.** Two Docker clients watched a copper ballista load and fire bone, flint, and copper arrows. Each shot spent one arrow. Dedicated-server checks covered all 14 materials on all three crossbows, with the fork installed and with it absent. The original upstream Combat Overhaul build still needs a playtest.

Replace the old Body Tools ZIP on the server and each client, then restart. If you're coming straight from 1.6.23, the previous preview notes cover the rest of the ranged gear. [Full ranged changelog and controls](https://github.com/Craditz/Craditz-VS-Mods/releases/tag/body-tools-v1.7.0-rc.23)
