# Craditz's Crooked Codex 0.11.12

Independent Rustbound Magic addon with 71 spells. Requires Vintage Story 1.22.x
and separately installed Rustbound Magic 4.0.7. Internal modid: rustboundexpansion.

This microfix restores the vanilla tooltip argument names on Mana Gem, Travel
Mark and Holy Hand Grenade, allowing optional Trait Restrictions patches to bind.
The Party Foul tooltip safety fix remains. No new dependency or balance change.

Build with .NET 10, your own game libraries and an external Rustbound DLL:

```powershell
dotnet build RustboundExpansion.csproj -c Release -p:GameDir="<game>" -p:RustboundDll="<external-rustboundmagic.dll>"
```

Credits and separate third-party rights are retained in CREDITS.md and the
repository THIRD_PARTY_NOTICES.md. No game or dependency binaries are included.
Public installers and source packages have separate hashes; a rebuild is a
separate artifact. See the dated repository release record for test limits.
