# Feral Kinship Companions 0.5.91

Owner-authorized stable user Restore update. See [release notes](RELEASE_NOTES.md)
for changes, source catch-up from public 0.5.84, qualification and rollback limits.
103 controlled groups passed; affected-server gameplay remains unverified.
The tested installer DLL/assets are retained; source builds are separate artifacts.
The DLL informational version remains 0.5.91-userrestore-preview.1 for traceability.

Build with .NET SDK 10 and your own Vintage Story 1.22.7 installation:
`dotnet build -c Release -p:VintageStoryPath="A:/Vintagestory"`
Both repository-relative linked shared C# inputs are included. Game/provider DLLs
and private test fixtures are not included. Ledger schema 57 is unchanged.
