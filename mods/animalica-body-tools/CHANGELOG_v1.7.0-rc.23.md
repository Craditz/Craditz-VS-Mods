# Animalica Body Tools 1.7.0-rc.23 preview

Same ranged.23 build, with a version number ModDB can read. The DLL and all gameplay assets are identical to the tested preview.

The fox got the keyboard again. We already had claws and fangs for the thing currently chewing on us. This one's for the thing over there.

## Crossbows

These wear in the **outer torso clothing slot** on your back. You can still wear body armor, use your hands, and bite things. There are 29 crossbows across three patterns. Scout and medium start in wood. The ballista starts in copper. The metal you craft with shows up on the model and changes the damage and durability.

- **Scout** is the run-and-gun one. Short range, 0.8-second reload, and you can do the whole thing at a full sprint.
- **Medium** is the everyday one. More damage, 2.2 seconds to load. You can still sprint while loading, but you move at half speed until you're done.
- **Ballista** is the biggun. You move 10% slower with it equipped. Stand still for nine seconds to load it. Take a step halfway through and you get to do those nine seconds again. Once it clicks you can carry it loaded. Firing kicks you back, hits hard, and a regular arrow can punch through two targets, slowing them briefly. Yes, it takes a normal arrow. We're handwaving that one.

**How to use one:** Hold the arrow or bomb bolt you actually want and hold **right-click**. Let go early and you keep the round. When you hear the latch click, it's loaded. **Middle mouse** fires instantly, even with your empty hand selected. You can rebind quick fire. There's no ammo readout for the thing strapped to your back. Take it off or swap it to get the round back. Full inventory? It drops at your feet.

All three use vanilla arrows. A negative arrow modifier comes straight off the crossbow's base damage. Each positive point adds 20% of that base, so it's worth spending the good arrows on the biggun. The limbs and string draw back, the arrow seats, and the mounts are fitted across 43 vanilla animal models. The 25 that would otherwise shoot themselves in the back of the head duck when you fire. They shoot just as fast as everybody else.

## Blowpipe and darts

Two cattail tops and flax twine make the blowpipe. A stick and a feather make four blank darts. Hold the pipe and right-click to shoot. It takes the first dart the way a vanilla bow takes an arrow. In the layout we tested that went earlier hotbar slot, later hotbar slot, offhand, then handbasket. Darts have a **75% chance to break** on impact. If you find one afterward, it still has whatever you put on it.

- **Blank** does 1 base damage. You can just shoot the thing.
- **Trail** uses resin. The animal leaves a green trail for 30 seconds so you can follow it through the brush.
- **Poison** uses a harmful mushroom. Two blanks and a mushroom make two poison darts. The tooltip names the mushroom and tells you the total damage. A mushroom with −4 food health gives you 8 poison damage spread over four ticks.
- **Slowing** uses honeycomb. The target starts 50% slower and gets its speed back gradually over five seconds.
- **Berserk** uses a rusty gear. For ten seconds the animal goes after the nearest eligible NPC, even one it would normally leave alone. If it has no attack it can use, it runs around in a panic instead. Yes, we tested the angry hare.
- **Tranq** uses a temporal gear. For 30 seconds the target deals half damage, and animals with supported AI look for trouble at half their usual range.

## Bolas

Two rocks and some flax twine. Throw them by hand with right-click. The weights stay on the animal and slow it by **60–90% for 5–10 seconds**, depending on material and luck. Surviving bolas drop where the animal is when it gets free or dies. Stone has a 35% chance to break on impact. Metal versions use two matching ingots and wear down one throw at a time. There's no cutting them loose. Wait it out.

## Bomb bolts

One vanilla arrow plus an ore, stone, or scrap bomb makes a bolt with that arrow's material. A direct hit gets the arrow damage plus the explosion when damage is allowed. **Only the ore bomb bolt breaks blocks.** Stone and scrap give you the blast while leaving the terrain intact. Other people's claims are respected. PvP off protects the other player. Your own blast can still kill you. Found that one out on the test range.

**XSkills is optional.** With it installed, crossbows, bomb-bolt direct hits, and blank darts get Archer bonuses and eligible combat XP. Ore bomb bolts use the placed ore bomb's mining XP and bombing perks.

**Server preview for Vintage Story 1.22.7.** Replace your existing Body Tools ZIP on the server and clients. It uses the usual Animalica Core, PlayerModelLib, and Trait Restrictions dependencies. Two Docker clients checked dart order, exactly-one ammo refunds with a full inventory, bomb bolts at a claim edge with PvP off, and disconnecting on either side of the ballista click before a server restart. Next up is trying it in an existing survival world. If something feels off, tell me which animal, which weapon, and what you were holding when it happened.
