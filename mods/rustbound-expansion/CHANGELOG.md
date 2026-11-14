# 0.11.12 - Trait Restrictions tooltip compatibility

Restore the vanilla tooltip argument contract for Mana Gem, Travel Mark and
Holy Hand Grenade so optional Trait Restrictions patches can bind. Retains
the Party Foul tooltip safety fix. No new dependency, spell or balance changes.

﻿# Private 0.11.12-tooltip-preview.1

Restore the vanilla tooltip argument contract for Mana Gem, Travel Mark and
Holy Hand Grenade so optional Trait Restrictions patches can bind. Retains
the stable 0.11.11 Party Foul background-tooltip fix. Unreleased.

# 0.11.11 - Party Foul tooltip hotfix (local release)

User approved promotion of the exact installed package on 2026-11-01, with
affected-client logs and original-mod-set runtime confirmation still pending.
No rebuild/repack; the package retains its earlier Testing-era changelog.

- Party Foul's formatted mana previews no longer select a target or ray-trace
  blocks. They display the full modified combat cost, regardless of aim.
- Numeric mana queries on background threads or before world-thread setup
  also avoid target lookup. Live main-thread affordability checks and explicit
  cast payments retain the cheaper self/ally mode and full hostile cost.
- Addresses the unsafe join/handbook tooltip path reported with Place on Slabs
  1.2.0. Affected-client logs and in-game confirmation remain pending.
- Spell balance, 71-spell catalogue, dependencies and VS 1.22.0 minimum stay.

# 0.11.10 - Local release

User-approved release of the 71-spell catalogue. Promotes the exact installed
0.11.10 archive without rebuilding or repacking. The package's embedded
Testing-era changelog is retained; this source note records its acceptance.

Since promoted 0.7.4: eleven Hemomancy spells and their cores, Bulwark I-III,
Holy Hand Grenade, Wallop, Cinder Trail and Breadcrumbs, with the subsequent
spell, visual, tooltip and Host navigation refinements below.

## Final conservative goober routes

- Crimson Host plans routes using one-block steps, choosing stairs or detours
  instead of depending on two-block climbs. Native physical two-block
  clambering remains available. Combat and grenade behavior are unchanged.

# 0.11.9 - Goober terrain navigation (Testing candidate)

- Crimson Host uses the game's asynchronous A* pathfinder to navigate around
  obstacles and onto raised targets. Destinations require real footing and
  clear headroom, replacing unsupported fixed positions beside pillars.
- Routes retain vertical steps, retry blocked approaches and update for moving
  targets. Goobers can pursue nearby enemies around cover; hits still require
  line of sight. Native two-block climbing, target distribution and combat stay.
- Grenade behavior and other spell tuning remain unchanged.

# 0.11.8 - Two-block goober climbing (Testing candidate)

- Crimson Host goobers use the native two-block step/climb setting on client
  and server, allowing them to follow over ledges while retaining normal
  collision and gravity. Grenade behavior is unchanged from accepted testing.

# 0.11.7 - Goober tactics and questionable grenade physics (Testing candidate)

- Restores Open Wound's previous 1 damage per travelled block, frequent hits
  and 12-second duration, with the existing 2-block movement budget.
- Hemomancy cores show native-style specialization bullets before the Mod
  footer, including the health-cost reduction. Actual bonuses are unchanged.
- Crimson Host spreads targets, approaches enemies from different sides and
  steers away from its siblings. Slight movement/attack timing variation and a
  forward lunge replace the synchronized pile-up and recoil-like animation.
- Energetic grenade bounces have a 40% chance to gain speed rather than lose
  it. Quiet contacts still settle; the existing fuse/ownership rules remain.
- Creative/spawned grenade copies intentionally remain inert without a valid
  conjuration owner/token. Conjure a grenade with the spell for testing.

# 0.11.6 - Sanguine Hemomancy cores (Testing candidate)

- Basic and advanced Hemomancy cores use Rustbound's native attunement-core
  model and display/hand transforms, with a dark sanguine color.
- Spell behavior, soulbinding, core bonuses and recipes are unchanged.

# 0.11.5 - Blood spell and grenade polish (Testing candidate)

- Missing Hemomancy attunement shows the generic spell-failure notice even
  when the client rejects casting before sending a server interaction.
- Blood Missiles fires three separate shots, 200 ms apart, for one payment.
- Cinder Trail uses neutral full-bright lighting to preserve its orange flames.
- Crimson Host walks with native ground physics, collision, steps and gravity
  instead of hovering. Attacks have a synchronized squash-and-chomp animation.
- Open Wound deals 0.1 base injury damage per travelled block, capped at 0.2
  per second, with one small tick per second over 20 base seconds.
- Blood Scent range strongly scales with buff potency: 64 blocks at baseline,
  192 at +50%, 320 at +100%, capped at 512. Wound severity still affects reach.
- Holy Hand Grenade bounces vary in height and direction, sometimes reversing
  course. Rebounds lose energy and still settle before the existing fuse starts.
- Vintage Story 1.22.0 minimum and Rustbound Magic 4.0.7 dependency retained.

# 0.11.4 - Vintage Story 1.22.x (Testing candidate)

- Lowers the minimum game version from 1.22.7 to 1.22.0 so earlier stable
  1.22 releases are no longer blocked. Rustbound Magic 4.0.7 is still required.
- Gameplay and spell tuning are unchanged from 0.11.3.
- Earlier patch versions still require in-game verification.

# 0.11.3 - Spell feedback (Testing candidate)

- Cinder Trail has saturated orange embers and a denser, larger rising flame
  layer. Still cosmetic fire only: no ignition, terrain changes or extra damage.
- Wallop uses native queued knockback so coyote-time ground drag no longer
  eats horizontal motion. Spell power increases distance; the small hop stays
  fixed. Horizontal impulse retains strong cubic scaling and a finite cap.
- Clot: 0.2 base HP/sec for 30 base seconds, replacing 0.5/sec for 12 seconds.
  Taking damage still cancels it. Cost, cast time and potency rules unchanged.
- Blood Scent: 64-block maximum base reach, up from 24; injury still controls
  actual reach. Open Wound movement budget drops from 4 to 2 blocks/sec.
- Heartburst: 1.5 base damage per HP actually paid, down from 3. Health sacrifice
  and the existing health-cost reductions remain unchanged.
- Crimson Host searches clear nearby positions instead of requiring all three
  original circle positions to be visible. Goobers gain client interpolation
  and a spawn burst. Missing entity types fail safely before health payment.
- Holy Hand Grenade reconciles a valid held grenade returning from an unloaded
  claim before throwing, while rejecting expired/duplicate/in-flight copies.
- Adds server-only cast/spawn/throw diagnostics for the reported Blood Missiles,
  Crimson Host and grenade issues. Player-facing failure text stays generic.
  Blood Missiles' reported failure has not been reproduced; no confirmed fix
  is claimed for that report. Friend/server retests and matching logs needed.

# 0.11.2 - Actual-looking breadcrumbs (Testing candidate)

- Breadcrumbs draws native bread models/textures instead of blue particles.
  Each casting chooses one of the seven normal bread varieties and keeps it
  throughout the trail. Tiny breads have stable random rotations/sizes; about
  one in 48 points is a larger whole loaf for silliness.
- Very faint self-glow (6/255), ordinary terrain occlusion and ground alignment.
  These are private cosmetic meshes: no dropped food, pickup, collision or AI.
- Keeps the thirty-minute base buff, duration bonuses and full trail retention
  until removal. Rendering is limited to 96 nearby breads. Owned GPU resources
  and visual selection clear with the spell. Wallop/Cinder Trail are unchanged.
- In-game visual and multiplayer acceptance remains pending. Delirium deferred.

# 0.11.1 - Breadcrumbs duration (Testing candidate)

- Breadcrumbs now lasts 30 base minutes, with normal duration modifiers.
- Every recorded mote lasts until the spell ends or is removed. Earlier points
  no longer fade after ten minutes or get discarded at 2,048 points.
- Click off the buff or recast to clear the trail sooner. Rendering still uses
  at most 96 nearby motes. Cinder Trail and Wallop are unchanged.

# 0.11.0 - Wallop and trails (Testing candidate)

- Adds three core-free tier-1 General/Neutral spells, bringing the catalogue to 71.
- Wallop: 18 mana, 0.75-second cast, 12-block targeting, 2 base blunt damage
  and 4 additional base damage on one wall impact. Native spell power scales
  damage and cubically scales the horizontal launch; +100% power gives eight
  times the shove. Bounded impulse, ordinary physics, no teleporting targets.
- Cinder Trail: 30 mana, 1-second cast, 20 base seconds. Walking leaves
  six-second ember patches dealing 2 base fire damage per second. Overlapping
  patches from one caster do not multiply damage. No real fire, ignition,
  block changes or terrain damage. Friendly targets and PvP permissions apply.
- Breadcrumbs: 10 mana, 0.5-second cast, 10 base minutes. Caster-only glowing
  motes record walked surfaces, with no dropped items or connecting lines
  across teleports. Click off the buff or recast freely to clear the trail.
- Includes native tier-1 research/recipes, sourced icons and readable buff
  labels. Delirium remains deferred; in-game acceptance is pending.

# 0.9.0 - Bulwark (Testing candidate)

- Adds Bulwark I, II and III: physical damage reduction after armor and
  Stoneskin, with a movement penalty that softens with potency.
- Base protection is 1/2/3 per hit. At +100% bonus potency, it caps at
  1.33/2.67/4. Only one rank applies; weaker casts cannot replace stronger ones.
- Strong worn armor reduces Bulwark's contribution using the armor location
  actually hit. Robes retain full strength; very strong armor retains at least
  40%. Armor still takes its normal durability wear.
- Tier I is core-free General; II/III use basic/advanced Alteration progression.
  Native research, tiered Spellforge recipes, scrolls and readable buff names
  are included. Supports self, allies and owned creatures.
- Does not reduce elemental damage, poison, falls or health costs. Delirium
  remains deferred. All new in-game acceptance is pending.

# 0.8.2 - Blood magic has opinions (Testing candidate)

- Adds twelve randomized death messages for fatal Hemomancy health costs,
  through the normal death announcement and its server visibility setting.
- Adds twelve randomized, caster-private chat notices when health-cost grace
  saves the caster. Each payment sends at most one grace notice.
- All approved lines are included, with the tooltip line's swear removed.
  Ordinary deaths and blood-cost/grace rules are unchanged.

# 0.8.1 - Blood costs can be fatal (Testing candidate)

- Hemomancy health costs can now kill their caster. After core discounts, a
  shortfall of up to 0.5 HP leaves 0.1 HP as grace; larger shortfalls are lethal.
  Ordinary positive remainders are paid normally, and grace never heals you.
- A fatal cast finishes its effects and mana payment before native player death.
  Death cleanup still ends owned buffs/summons, with no Host death refund.
- Crimson Host invests the actual HP spent, including grace, so its 75% refund
  cannot return unpaid health. Heartburst also uses the actual sacrifice.
- Shields, armor and Blood Oath do not reduce or echo health costs. Core bonuses
  and all spell tuning are unchanged. In-game death/respawn testing is pending.

# 0.8.0 - Hemomancy (Testing candidate)

- Adds eleven Hemomancy spells across creation tiers 1, 2 and 3: Bloodletting,
  Transfusion, Clot, Blood Scent, Blood Missiles, Crimson Aegis, Red Harvest,
  Open Wound, Exsanguinate, Heartburst and Crimson Host.
- Adds soulbound basic/advanced blood cores. Tier 1 is core-free; tier 2 needs a
  basic or advanced blood core, and tier 3 needs advanced. Health costs are
  reduced by 10% / 25%; advanced school modifiers match Rustbound's usual values.
- Blood cores are craftable from the corresponding native core plus a clean
  bandage. Ordinary chalk can imprint tier 2/3 Hemomancy ciphers. Tier 1 uses
  ordinary matching-essence research, and each spell has a native-tier synthesis
  recipe. Total catalogue: 64 spells. Existing Blood Oath access is unchanged.
- Health sacrifices bypass shields/armor and do not trigger damage echoes.
  Clot ends on actual damage. Crimson Host refunds 75% of actual invested health
  scaled by each surviving goober's health on dismissal/expiry; no death,
  disconnect or reload refunds. Spell costs/effects are server-authoritative.
- Both school plans are saved; Delirium is not implemented in this candidate.

In-game visuals, balance, dedicated-server behavior and manual acceptance are
pending. See docs/HEMOMANCY_TESTING.md and docs/EDIT_hemomancy_2026-10-18.md.
No release promotion or public upload is authorized.

# 0.7.4 - Heat Item, Hot Hands and heated stones (approved local release)

- Heated stones now disappear after popping against the floor or a wall,
  instead of continuing the native bounce/recovery behavior.
- Cold stones and other thrown items retain their native terrain behavior.
- Fire damage, heat/pop scaling, entity hit handling and Hot Hands are unchanged.
  No area damage or recoverable terrain-impact item is spawned.

2,421 offline assertions pass. User confirms terrain removal works and approves
release on2026-10-18, following Heat Item/Hot Hands reports. The exact installed
ZIP is promoted without rebuilding/repacking; its embedded Testing-era changelog
is retained. See RELEASE_NOTES_0.7.4.txt and docs/RELEASE_0.7.4_2026-10-18.md.
Broader multiplayer/lifecycle scenarios are not separately confirmed.

# 0.7.3 - Hotter rocks hit harder (Testing candidate)

- Heated native stone throws gain +1 fire damage per 100 C, from 100 C upward,
  bounded at the supported 600 C heat limit. A normal 1-damage stone deals
  2 at100 C,4 at300 C,7 at600 C before mitigation, in a single hit.
- Hotter impact pops have more/larger particles, greater spread, a longer
  lifetime and a stronger sound. Effects stay bounded and cosmetic: no AOE,
  nearby damage, terrain destruction or repeated pop on the same projectile.
- Cold stones, native throw/recovery behavior, Heat Item and Hot Hands remain.
  No separate throw spell is added.

2,417 offline assertions pass. Native throws/scaling need in-game testing.

# 0.7.2 - Heated vanilla stone throws (Testing candidate)

- Removes Throw Heated Rock as a spell, including its book/cipher/scroll/research
  and recipe. Heating rocks remains part of Heat Item.
- Throw a loose stone normally while it is at least 100 C hot: its native single
  hit deals Fire damage, preserving the vanilla damage amount and attribution.
- Heated stones make a small fire pop on terrain/entity impact. Cosmetic only:
  no area damage, extra damage hit or terrain destruction. Pops once per throw.
- Keeps native stone flight/gravity, item consumption and recovery behavior.
  Cold stones, other thrown items and spell projectiles are unaffected.
- Hot Hands is unchanged. User confirms it works for holding heated items.

2,403 offline assertions pass. Native throws and multiplayer visuals still need
in-game testing; this revision is not promoted.

# 0.7.1 - Hot Hands and heated stones (Testing candidate)

- Heat Item now uses the standard Vintage Story title bar, shaded window and
  recipe-style item selection grid. Hover to see stack count and temperature.
- Heat Item also heats ordinary loose stones, sharing its heat across the stack,
  capped at 600 C. A lower native melting threshold still takes precedence.
- Hot Hands: tier 1 General/Neutral, 15 mana, 2s cast, 5-minute base duration.
  Cast on an ally player or yourself to prevent forced hot-item drops and their
  contact burn. Other fire/environmental damage remains unchanged.
- Throw Heated Rock: tier 1 General/Neutral, 8 mana, 1s cast, 32-block range.
  Select a loose stone at least 100 C hot. Consumes one and flings a solid rock
  with a fire trail in the direction you cast. Deals 4 base blunt damage plus
  heat-scaled fire damage (5.8 at 600 C, maximum 6 before damage modifiers).
- Both inventory-targeting spells preserve mana and scrolls on cancel/failure.
  Adds native recipe/research, sourced icons and player-facing names for both.

2,410 offline assertions pass. Native UI, hot handling and heated-rock combat
still need in-game testing. No release promotion is included.

# 0.7.0 - Heat Item (Testing candidate)

- Adds only Heat Item: tier 1 General/Neutral, 10 mana, 2-second cast.
- Cast to open SELECT HEATABLE ITEM, a distinct copper/orange spell picker with
  native item icons, temperature previews, counts, pagination and a flame badge.
  Only eligible personal hotbar/backpack items appear; nothing is moved.
- Shares 100 C additive heat across the stack, scaled by native potency, retaining
  fractional heat. Uses each item's native melting threshold and stops 1 C below
  it. Unknown thresholds and already-capped items are excluded.
- Server owns selection, stack identity, inventory, mana, expiry and cap checks.
  Cancellation/failure costs no mana; scrolls are consumed only on success.
- Adds normal native tier-1 Neutral synthesis/research, icon and localized name.
  Hot-item protection and heated-rock spells remain deferred.

2,360 offline assertions pass. GUI rendering, cooling, hot-item handling and
multiplayer synchronization still require in-game testing. This is not promoted.

# 0.6.0 - Beneficial spells on owned creatures (approved local release)

- Automatically recognizes PetAI/taming ownership and vanilla ownable ownership,
  covering Companions/Feral Kinship pets and Draconis dragons without requiring
  those mods. Wild, conflicting-owner and hostile-owner creatures are excluded.
- Extends the shared native area-buff helper, plus addon ally targeting. Native
  Flash Heal, Regeneration and Revive retain their existing creature support.
- Healing Surge heals friendly owned creatures; Dispel cleanses their debuffs
  while retaining enemy buff stripping. Player behavior and spell balance stay.
- Bridges useful NPC combat/movement buffs: Strengthen, Swiftness/Burst,
  Stoneskin, elemental resistance, Spike Shield, Featherfall and Endless Breath.
  Barrier fallback does not absorb hits twice. Player-only controls/UI stay native.
- Restores external pets' native buff stats and remaining durations after save/
  reload without repeating initial heals or replenishing barriers. Expiry,
  removal, replacement, reset and death discard persisted buff state.

2,271 offline assertions pass. Package/install evidence and in-game acceptance
are tracked in docs/EDIT_beneficial-creature-spells_2026-10-18.md. Native spell
audit and applicability limits: docs/CREATURE_SPELL_SUPPORT.md. User reports
strength, speed and hats work on pets, then approves promotion as-is on
2026-10-18. Dragon flight remains unchanged. Broader multiplayer/lifecycle
coverage is not claimed. Exact accepted package promoted without repacking;
its embedded Testing-era changelog is retained. No public upload.

# 0.5.5 - Mischief and display polish (approved local release)

- Replaces addon cast/staff/mana/target/travel/gem and unsupported-scroll failure
  explanations with Rustbound's native localized "Spell failed." notification.
- Failure conditions, costs, cooldowns, silent paths, recipes and gameplay are
  unchanged. Retains all 51 corrected book names and three hidden HUD labels.

Approved for local release on 2026-10-17. Adds the eight mischief spells and
separate cipher from 0.5.0-0.5.3, plus the 0.5.4 naming corrections below.
2,068 offline assertions pass; exact accepted ZIP validated and promoted.
Latest runtime logs show 0.5.4; separate 0.5.5 message inspection and detailed
multiplayer coverage were not provided. No public upload. See RELEASE_NOTES_0.5.5.txt.

# 0.5.4 - Native-style spell book and buff names (Testing candidate)

- Removes the extra "Grimoire" suffix from all 51 spell book names, matching
  Rustbound's spell-title naming convention.
- Adds the native HUD label keys for hidden effects: Winded, Recently Lifegripped
  and Party Foul Recovery. Both player and companion buff lists use these keys.
- Audits all 54 addon spell/effect labels and descriptions for missing entries
  or internal IDs. No spell logic, data, balance, recipes or audio changed.

The user accepts 0.5.3 behavior. This revision changes display text only;
asset/package checks cover the naming change. Final in-game label inspection
pending; no promotion or public upload.

# 0.5.3 - Current-facing leaf and enthusiastic applause (Testing candidate)

- Leaf placement uses the recipient's actual native look vector, including the
  player's level pitch of PI. Leaves follow the current recipient position and
  facing during their fall, so turning/moving no longer leaves them behind.
  The 0.3-block forward offset, sprite, cadence and lifetime remain unchanged.
- Replaces the rejected cartoon duck with OwennewO's CC0 Soft Duck Quack,
  described by its author as a female duck softly quacking. Sources/hashes kept.
- Every container opening can add an overlapping applause voice; removes the
  two-second limiter. Each playback gets server-chosen pitch from 0.88 to 1.12.
- Prior mana/durations, soaking/one-shot weather rumble, rain loop, other spells,
  research/crafting/config and accepted sound waveforms remain unchanged.

2,068 offline assertions pass; current-facing/audio and multiplayer retest
pending. No promotion, game launch or public upload by the agent.

# 0.5.2 - Face leaf and audible nuisances (Testing candidate)

- Conjure Leaf drops 0.3 blocks in front of the recipient's face, following
  their horizontal facing. The accepted sprite and falling animation remain.
- Suspicious Duck now uses Breviceps' CC0 Cartoon - Duck Quack recording from
  Freesound. Source URL/license/hash are retained in audio-sources/sources.json.
- Pocket Weather's rumble plays once per cast. Only native quiet rain loops;
  fully soaked wetness and all durations remain unchanged.
- Fixes silent applause: trim the source and reset timestamps before applying
  fades. Converted recordings now pass decoded peak/RMS checks to reject
  silent or effectively inaudible output. Audio peaks are limited before Vorbis.
- Native container-opening integration is covered by an actual InventoryBase.Open
  regression check. No changes to the other spells, research or server config.

2,057 offline assertions pass. Runtime audio/leaf placement and multiplayer
retest pending; no promotion or public upload.

# 0.5.1 - Mischief sounds and visuals (Testing candidate)

- Spiteblast uses the actual Rustbound Corpse Explosion shader/quad and its
  native medium-explosion sound at normal pitch. No damaging projectile or
  server explosion is created.
- Squeaky Steps uses Craditz's supplied WAV. Unsolicited Applause uses a short
  faded excerpt of eXpl0it3r's CC0 crowd recording from OpenGameArt. Sources and
  hashes are retained; future sounds use supplied/native/online recordings.
- Conjure Leaf uses an original transparent leaf sprite on a fluttering,
  falling client-only quad, with no dropped items.
- Hatastrophe stacks complete hats with individual brims and stable random
  colors shared between observers. The eight-growth collapse remains particles.
- Pocket Weather's cloud is translucent and its quiet native rain loop follows
  the recipient, stopping on expiry/unload/world leave. Soaking is unchanged.
- Dramatic Entrance reveals with the actual native client lightning bolt and
  thunder, without invoking damaging server weather lightning. Smoke and
  spotlight remain. Durations, mana, requirements and acquisition are unchanged.

2,050 offline assertions pass; zero build warnings/errors. New visual/audio and
multiplayer inspection remains pending. No promotion or public upload.

# 0.5.0 - Eight mischief spells (Testing candidate)

- Adds tier-1 General/Neutral Spiteblast, Squeaky Steps, Unsolicited Applause,
  Conjure Leaf, Suspicious Duck, Dramatic Entrance, Hatastrophe and Pocket Weather.
  All cost 8 base mana with a 2-second base cast. Target an entity or self.
- Spiteblast is a large damage-free explosion. The five footstep/audience/leaf/
  duck/weather effects last 60 seconds. Dramatic Entrance hides its recipient
  for 10 seconds before thunder, smoke and a spotlight; it grants no immunity
  or movement/action lock. Hatastrophe lasts 120 seconds, grows on successful
  casts and collapses every eight growths into particle-only paper scraps.
- Pocket Weather maintains actual fully soaked wetness, then leaves it to dry
  normally. A Dry Spell during active Pocket Weather is immediately rained over.
- Shapeless tier-1 Neutral blank cipher + flint makes a Mischief Cipher. Native
  Spellforge charging, meditation and success/failure still apply; successful
  mischief research selects only these eight. Ordinary research excludes them.
  Learned ciphers use native tier-1 Neutral grimoire creation recipes.
- Server ModConfig/rustboundexpansion.json: EnableMemeCipherCrafting defaults
  true. False disables the recipe and vetoes native server crafting/output
  taking; restart required. Existing ciphers/spells remain usable.
- Original synthetic prank audio, original hat/cloud meshes and sourced WoW
  icons. No additional third-party mod dependency or change to the earlier
  43 spells' balance/acquisition.

2,031 offline assertions pass. Runtime, visuals and multiplayer remain pending;
this candidate is not promoted or publicly uploaded. The accepted local release
remains 0.4.1; first public release still awaits Rustbound developer permission.

# 0.4.1 - Spark, Pebble and Mending Circle polish

Accepted by the user for release on 2026-10-17. The exact tested archive is
preserved; its embedded changelog retains the earlier Testing status. This
source changelog and docs/RELEASE_NOTES_0.4.1.txt record the release status.

- Seeking Spark's range increases from 16 to 40 blocks, making long-distance
  tracking easier to observe. Damage, mana cost and cast time are unchanged.
- Ricochet Pebble now throws faster, falls under gravity and ricochets up to
  three times, with a 96-block travel budget and ten-second lifetime limit.
  Fixes an accidental loss of velocity that made reflected pebbles crawl.
  Enemy impacts still stop it; damage, mana cost and cast time are unchanged.
- Mending Circle has a much denser green/gold boundary, an inner ring and rising
  sparks. Visuals refresh between healing pulses; healing and duration are unchanged.

User reports from 0.4.0 prompted these changes. The user subsequently reported
"All of these work now" and authorized release. Detailed multiplayer coverage
was not specified; earlier untested notes describe the state before acceptance.

# 0.4.0 - Fourteen more questionable spells (Testing candidate)

- Adds eight tier-1 General spells: Seeking Spark, Ricochet Pebble, Dry Spell,
  Returning Orb, Cinder Wisps, Conjure Buckshot, Starshot and Furtive Caster.
- Adds six tier-2 spells: Prayer of Mending, Lifegrip, Mending Circle, Party
  Foul, Snapcast and All In. Native school, research and altar requirements apply.
- Cinder Wisps launch at least 0.75 seconds apart. Lifegrip gives its recipient
  ten seconds of immunity across all casters, with no caster cooldown.
- Prayer of Mending shares five charges and its original expiry across jumps.
  Mending Circle splits its healing pool among injured allies each second.
- Hostile Party Foul ends after four seconds or fifteen total prevented damage.
  Ally/self casts add cosmetic effects while damage remains normal. Buckshot's
  harmless 8-9-block impacts also make confetti and an original party-blower sound.
- Starshot charges through staff use and fires on release; spent charge mana
  is not refunded on interruption. It cannot be cast or inscribed as a scroll.
- Casting stances toggle, exclude one another and scale modestly with buff
  potency. Their damage modifiers do not alter healing or buff potency.
- Existing spell balance and travel requirements retain their released values.
  Rustbound Magic remains the sole third-party dependency.

Offline checks and packaging do not establish in-game acceptance. This batch is
a Testing candidate; the promoted 0.3.12 package remains available separately.

# 0.3.12 - Craditz's Crooked Codex, initial release

- Adds 29 Rustbound spells: Resonance, Brittle Sigil, Arc Lightning, Burst of
  Speed, Blood Oath, Banish, Unstable Plague, Lightning Bolt, Armageddon, Living
  Bomb, Conjure Bullet, Conjure Cannonball, Conjure Mana Gem, Blizzard, Starfall,
  Mark, Teleport, Gate Travel, Twin Gates, and ten elemental/rock Sentry spells.
- Adds reusable, nameable Travel Marks, up to four placed runes per block,
  destination-colored rune symbols, and a 20-slot mark-only satchel.
- Includes channeled storms, stone sentry models and a rock-spitting Rock Sentry.
- Uses the selected Craditz branding and fox wizard mod icon.
- Restores native school attunement requirements by disabling the testing bypass.
- Requires Vintage Story 1.22.7 and Rustbound Magic 4.0.7. No other mod dependency.

Initial local release, with runtime/manual testing explicitly waived by the
author. Balance is provisional. The proposed progression rebalance and shared-
school travel have not been implemented; current addon creation tiers remain 2.

# 0.2.0 - Arc Lightning, Burst of Speed and sourced icons

- Added tier 2 Arc Lightning: 10 base lightning damage, five distinct targets, 20% falloff per jump, six-block jumps and line of sight. One entity ID can be hit only once during an entire cast.
- Added tier 2 Burst of Speed: ally/self targeting, +200% base movement and +50% incoming damage for ten base seconds, followed by a 40% movement slow for ten seconds. No recast or stacking during either phase.
- Added native research pools, tier-2 recipes, item variants and internal Winded recovery HUD integration, plus cleanup across reset/death/reconnect.
- Replaced Resonance and Brittle icons and added Arc/Burst/Winded icons using sourced World of Warcraft art at the user's request. Source hashes and attribution retained; close native-icon matches rejected.
- Retained Lightning Bolt, Armageddon, Mana Gem, Living Bomb and mark-item travel in the later idea bin; none is implemented here.

535 offline assertions pass. New runtime/visual/multiplayer acceptance pending; no promotion or public upload.

# 0.1.2 - Brittle Sigil impact pop

- Added one bright violet-white particle burst when a damaging hit consumes the sigil, including a lethal hit. Nearby observers receive the game's particle effect.
- Expiry and reset retain the normal soft fade. Damage and the following marker are unchanged.
- The user confirmed the 0.1.1 following marker works; the impact pop awaits in-game confirmation.

# 0.1.1 - Brittle Sigil target marker

- Added a violet particle orbit around marked players and animals, following their current position until the mark is consumed or ends.
- Marker uses the server-synchronized debuff state; clears on removal, death, unload and world leave. No damage or duration changes.
- User reported 0.1.0 punch damage of 0.5 -> 10.5 -> 0.5. Damage-type matching remains unconfirmed in-game.

Visual runtime and multiplayer acceptance pending. Not promoted or published.

# 0.1.0 - first Testing candidate

- Added tier 2 light Alteration spell Resonance I: native area-buff targeting, fixed +20% spell potency, refreshing timer without stacking.
- Added tier 2 neutral Evocation spell Brittle Sigil I: the next direct damaging hit gains 10 base bonus damage, preserving the triggering damage source's type, element and attribution. Normal damage modifiers are captured when marking; normal health mitigation still applies.
- Added spellforge pool entries, cipher/grimoire/scroll/icon variants, tier 2 Oculus recipes and original rune icons.
- Added registry/recipe rebuild integration and cleanup for expiration, reset, death, logout and reload.
- Rustbound Magic 4.0.7 is the sole third-party mod dependency. No other owned mod integration.

Offline checks only. Runtime, multiplayer and manual acceptance pending. Not promoted or published.
# 0.10.0 - Holy Hand Grenade (Testing candidate)

- Adds a tier-2 Conjuration/Light spell: 400 base mana and a 20-second base cast.
- Conjures one unstackable Holy Hand Grenade. Right-click pulls its pin with a
  metallic clink and a fading cosmetic pin; release, hold to aim, release to throw.
- Heavy bouncing physics, then 0.3 seconds of settling and a random 0-3 second
  delay. Further movement restarts the wait; the pin does not start a hand fuse.
- 30 base physical damage, full strength within 1 block and falling to zero at
  6 blocks. The thrower can be hurt; other targets follow normal combat rules.
  No terrain damage. Uses the requested Worms Hallelujah recording.
- One persistent grenade allowance per caster, including storage, nested bags,
  transfers and thrown grenades. Released by explosion or observed destruction.
- Original gold orb/cross grenade and pin models, native research/recipe/core
  integration, and generic spell-failure notifications. 68 spells total.
- Built/package/install evidence is recorded separately from pending in-game,
  multiplayer, physics, visual/audio and save/reload acceptance.

# 0.10.1 - Grenades expire on creator logout (Testing candidate)

- Logging out silently removes the creator's Holy Hand Grenade and immediately
  frees their conjuration allowance, even if someone else has hidden it.
- Loaded held, stored, nested-bag, dropped and thrown grenades are cleaned up;
  unloaded copies become permanently inert and disappear when loaded or updated.
- Reconnecting or conjuring a replacement cannot revive an old grenade.
  Holder/thrower logout only expires grenades they originally created.
- Server restart also expires previous-session grenades, including after a crash.
- Other items and other creators' grenades remain intact. No balance/art/audio changes.
