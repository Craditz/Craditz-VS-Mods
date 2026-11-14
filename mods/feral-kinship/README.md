# Feral Kinship 0.5.8

Adult bears accept vanilla raw fish fillets with 25% taming progress and the
normal three-hour care cooldown. Accepted held-fish taming synchronizes inventory
and prevents delayed player eating when Pet Profile closes. Existing diets and
normal eating when no treat is accepted remain. Requires Vintage Story 1.22.x;
PetAI is installed separately.

Build with .NET 10 and external game/PetAI libraries:

```powershell
dotnet build FeralKinship.csproj -c Release -p:VintageStoryPath="<game>" -p:PetAIPath="<PetAI.dll>"
```

See RELEASE_FERAL_KINSHIP_0.5.8.md in the repository root for exact installer
hashes and native testing limits. No game/dependency binaries are distributed.
