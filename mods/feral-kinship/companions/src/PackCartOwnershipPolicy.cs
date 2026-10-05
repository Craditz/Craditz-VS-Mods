#nullable enable

using System;

namespace FeralKinshipCompanions;

public enum PackCartOwnershipAuthority
{
    None,
    BlockTag,
    LegacyRepository
}

public readonly record struct PackCartOwnershipResolution(
    string OwnerUid,
    PackCartOwnershipAuthority Authority,
    bool WriteBlockTag,
    bool WriteRepository)
{
    public bool HasOwner => !string.IsNullOrWhiteSpace(OwnerUid);
}

/// <summary>
/// Keeps Pack Cart ownership recovery deterministic. A placed block's explicit
/// tag is authoritative. The old repository may initialize that tag only for a
/// legacy cart that does not have one yet. With neither source, the cart stays
/// unowned until it is broken and placed again.
/// </summary>
public static class PackCartOwnershipPolicy
{
    public static PackCartOwnershipResolution Resolve(
        string? taggedOwnerUid,
        string? repositoryOwnerUid)
    {
        string taggedOwner = Normalize(taggedOwnerUid);
        string repositoryOwner = Normalize(repositoryOwnerUid);

        if (!string.IsNullOrEmpty(taggedOwner))
        {
            return new PackCartOwnershipResolution(
                taggedOwner,
                PackCartOwnershipAuthority.BlockTag,
                WriteBlockTag: false,
                WriteRepository: !string.Equals(
                    taggedOwner,
                    repositoryOwner,
                    StringComparison.Ordinal));
        }

        if (!string.IsNullOrEmpty(repositoryOwner))
        {
            return new PackCartOwnershipResolution(
                repositoryOwner,
                PackCartOwnershipAuthority.LegacyRepository,
                WriteBlockTag: true,
                WriteRepository: false);
        }

        return new PackCartOwnershipResolution(
            string.Empty,
            PackCartOwnershipAuthority.None,
            WriteBlockTag: false,
            WriteRepository: false);
    }

    private static string Normalize(string? ownerUid) => ownerUid?.Trim() ?? string.Empty;
}
