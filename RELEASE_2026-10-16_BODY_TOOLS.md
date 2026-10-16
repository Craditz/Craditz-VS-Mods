# Body Tools 1.6.19 source publication â€” October 16, 2026

This update is limited to Animalica Body Tools source, supporting assets and release documentation. The selected implementation advances the public 1.6.17 snapshot (`9769f83f`) through the retained private 1.6.18 recovered-head work and the accepted 1.6.19 r3 durability, feedback and startup fixes. Other mods and their releases are unchanged.

## Accepted package identity

| Field | Value |
| --- | --- |
| Installer | `AnimalicaBodyTools_v1.6.19.zip` |
| Size | 3,823,020 bytes |
| SHA256 | `e6c847af80fdbf35f26d0fe5e5f8321c5da3bb340b758ff73b12e3ee0e22c747` |
| Manifest | `animalicabodytools`, version `1.6.19` |
| DLL informational version | `1.6.19-private.r3-startup` |
| DLL SHA256 | `85fdfcbd6f2c9437d5b95e133e79e1b0422d7bbb29c6cccb912cc35fb3d96f93` |
| Qualified game | Vintage Story 1.22.7 |
| Build target | .NET 10; SDK 10.0.303 |

The accepted r3 ZIP is retained byte-for-byte, including its private revision marker and package note. This source publication does not rebuild that DLL, normalize its metadata, or create a replacement binary. This repository keeps downloadable ZIPs separate from source control. No new GitHub binary release is created by this update.

## Source and assets

All 35 runtime C# files match the accepted r3 source after the repository's existing LF normalization. The project preserves version and informational-version metadata; only shared-source and external-game-reference paths are adapted to the public layout. `shared/VintageStoryConfigMigration.cs` is unchanged and matches the accepted source after line-ending normalization. All 1,902 runtime assets correspond to the selected package: binary/model assets retain their bytes, while ordinary JSON text follows the existing Git line-ending policy. No game DLLs, dependency DLLs, private test projects, fixtures, logs, credentials or saves are included.

Build with:

```powershell
dotnet build mods/animalica-body-tools/src/AnimalicaBodyTools/AnimalicaBodyTools.csproj -c Release -p:VintageStoryPath="<Vintage-Story-1.22.7-directory>"
```

A public-layout rebuild is a separate artifact. It is not substituted into the accepted installer and is not represented as byte-identical merely because it compiles. Preserve the manifest's dependency minima and existing contributor and third-party notices.

## Behavior

See [the changelog](mods/animalica-body-tools/CHANGELOG_v1.6.19.md). Destruction is server-authoritative, acts on the original slot/stack identity and synchronizes the changed inventory. Already-zero items are rejected at use; one-to-zero items complete their last valid action and are removed. Starter claws have no exemption. Remaining durability uses the engine's authoritative behavior, including collectible overrides. Fitted/rending coexistence retains the valid fallback. Ordinary non-Body-Tools wearables are outside the destruction fix.

## Verification and limits

- The public-layout project builds against installed Vintage Story 1.22.7 with zero warnings and errors. The private harnesses were rerun against this checkout and passed the counts below. A clean extraction of the retained private source reproduced the exact accepted installer before this publication.
- Automated engine/mod-method checks with fixture services: 154 startup/lifecycle assertions; 333 durability/audio/chopping assertions, including 191 break-sound assertions across 38 cases and 10 chopping-feedback assertions; 33,286 recovered-head assertions across 49 pairs and 2,940 round trips. These cover held/equipped exhaustion, preexisting zero, starter/fitted controls, replacement identity, behavior overrides, fallback, synchronization calls, sound counts and patch teardown/reload. They are not live gameplay tests.
- Separately reported cloud testing passed cold startup and gameplay checks for the accepted r3; Cheese confirmed durability and sound retesting. These are attributed external observations, separate from the local automated checks.
- An actual second remote multiplayer client has not been tested. Automated sound-recipient and synchronization checks do not establish packet receipt or audible playback on that client.

Cold-restart before installing a different build, use the same accepted ZIP on both endpoints, and use disposable worlds for remaining remote-client checks. Private test evidence remains outside the public repository.
