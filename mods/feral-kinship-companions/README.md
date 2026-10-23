# Feral Kinship Companions 0.5.92

Matching production source and assets for the accepted 0.5.92 update. See CHANGELOG_v0.5.92.txt and RELEASE_NOTES.md for changes from public ModDB 0.5.91, qualification and rollback limits. GitHub source synchronization does not establish a ModDB release.

Build with .NET SDK 10 and your own Vintage Story 1.22.7 installation:
`dotnet build -c Release -p:VintageStoryPath="A:/Vintagestory" -p:IncludeSourceRevisionInInformationalVersion=false`

The repository-relative shared inputs are included. Engine/provider binaries and standalone private tests are excluded. Builds use the neutral reproducible source path /_/feralkinship-source. On Linux, the optional navigation diagnostic task label is the source basename rather than a Windows-style build path. This changes diagnostic text, not routing algorithms or saved data.

Use a Pack Ledger on ordinary containers for current pack storage. Legacy crate identities remain hidden compatibility warning containers so existing contents can be transferred safely. No timed deletion is introduced. Ledger schema remains 57; this update does not downgrade private schema 58/59 testing saves.

Existing licenses, sound credits and third-party notices are retained.
