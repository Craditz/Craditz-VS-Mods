# Animalica Body Tools

Production source and supporting assets for **1.6.19**, matching the accepted r3 implementation tested against Vintage Story **1.22.7**. See [the changelog](CHANGELOG_v1.6.19.md) and [release record](../../RELEASE_2026-10-16_BODY_TOOLS.md).

The selected installer is `AnimalicaBodyTools_v1.6.19.zip`, SHA256 `e6c847af80fdbf35f26d0fe5e5f8321c5da3bb340b758ff73b12e3ee0e22c747`. Its manifest is 1.6.19 and its DLL deliberately retains informational version `1.6.19-private.r3-startup`. The accepted binary was not rebuilt or relabelled for this source publication. Cold-restart the game when replacing another build, and install the same package on client and server.

Install Git LFS before cloning to download model and binary assets. Runtime C# is unchanged apart from Git line-ending normalization; project paths use this repository's shared source and external-reference properties. JSON text also follows the existing Git LF policy. Private fixtures, logs and test evidence are not part of this repository.

Game libraries and separately installed dependencies stay external. Third-party assets retain their original credits, licenses and compatibility conditions; the project license covers only rights held or authorized by the relevant licensor.

See [BUILD.md](../../BUILD.md), the [project license](../../LICENSE) and [third-party notices](../../THIRD_PARTY_NOTICES.md). A fresh source build is a separate artifact; compilation alone does not establish release-binary identity or gameplay acceptance.
