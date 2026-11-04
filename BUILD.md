# building things with the stolen computer

the fox has left the wiring accessible. bring your own game libraries.

## supplies

- Git LFS; run `git lfs pull` if assets look like short pointer files
- .NET SDK 10
- your own compatible Vintage Story installation
- the third-party libraries required by the selected project, kept outside this repository

## ordinary code projects

```powershell
dotnet build <project.csproj> -c Release -p:VintageStoryPath="<game-directory>" -p:PlayerModelLibPath="<external-PlayerModelLib.dll>" -p:PetAIPath="<external-PetAI.dll>"
```

only pass the dependency properties the project needs. manifest minima describe that release’s requirements; they are not a recommendation to combine every mod with whichever dependency is newest.



## Toolsmith has two sets of teeth

build `AnimalicaToolsmith.csproj` with `ToolsmithPath` and `PlayerModelLibPath`, then `AnimalicaToolsmith.Lto.csproj` with `LtoBlackSmithPlusPath` and `PlayerModelLibPath`. both use `VintageStoryPath`. finally build `AnimalicaToolsmith.Loader.csproj`; it embeds those two owned backend DLLs. package the loader, not the loose backends. no third-party provider DLL goes into the ZIP. choose exactly one provider when playing.

Emotes links Core’s `LoadedPlayerIndex.cs`. Companions links the Kinship race resolver and the shared configuration migration source. these are source links; the mods still compile into separate assemblies.

## putting a package together

keep `modinfo.json` at the ZIP root, beside the compiled mod DLL, `assets/` and the icon where present. content-only packs need no mod DLL. use forward slashes and unique safe archive paths. retained accepted ZIPs are downloadable release assets, not build outputs from this checkout.

selected release assets are included. rebuilding the original models from every historical donor and generator is a separate task; those inputs are not all in this den. third-party art and sounds retain their own terms and credits.

## the fox’s check sheet

the November 4 public-layout build results are in [the collection record](RELEASE_2026-11-04.md). successful compilation checks these project paths and references. it does not establish binary identity, full packaging behaviour or new gameplay acceptance. the accepted installers are preserved without rebuilding. historical release notes describe their own dates and test scope.

game libraries, external mods, private fixtures, saves, logs and credentials stay outside source control. keep download ZIPs as release assets. the repository’s model JSON, textures and sounds use Git LFS; check the real bytes as well as the pointer labels.
