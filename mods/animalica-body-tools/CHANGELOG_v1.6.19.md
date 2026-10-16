# Animalica Body Tools 1.6.19

- Body Tools now break at zero durability while held as well as equipped. The server removes and synchronizes the exact exhausted stack, including one already broken when a use is attempted. Healthy replacement stacks are protected, and unrelated stored items are not swept.
- Depleted held claws cannot continue their tool action. Starter and fitted claws follow the same durability rules; starter claws have no exemption. The normal final valid use still works and is charged once.
- Equipped rending claws retain tree chopping and use axe chopping feedback. Final depletion produces one tool-break sound; cleanup of an already-broken stack is silent. Cultivating claws avoid a duplicate hoe break sound.
- Startup and patch lifetime handling supports singleplayer server/client initialization, repeated initialization and session reload without duplicate patches or charges. Cold-restart when replacing an older build.
- Includes the recovered metal tool-head feature from 1.6.18: seven tool families across seven metals can be dismantled into quenchable heads while preserving wear through recrafting. This is not a repair recipe. Keep Body Tools enabled while using recovered worn heads so their saved wear can be applied.

Thanks to **Cheese** for the report and durability/sound retesting.

The accepted package keeps DLL informational version `1.6.19-private.r3-startup`; its manifest remains 1.6.19. See [the release record](../../RELEASE_2026-10-16_BODY_TOOLS.md) for exact identity and verification limits.
