using System;
using System.Buffers.Binary;
using System.IO;

namespace CustomMusicRadio;

// Container/header validation and server duration only. Actual Vorbis decoding remains
// the game's client decoder. Supports sequential chained streams, not multiplexed Ogg.
public static class OggVorbisInfo
{
    private static readonly uint[] CrcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint crc = i << 24;
            for (int bit = 0; bit < 8; bit++) crc = (crc << 1) ^ ((crc & 0x80000000) != 0 ? 0x04c11db7u : 0);
            table[i] = crc;
        }
        return table;
    }
    public static double Duration(ReadOnlySpan<byte> bytes)
    {
        int offset = 0, headers = 0, packetLength = 0;
        byte[] prefix = new byte[30];
        uint serial = 0, sequence = 0, sampleRate = 0;
        long lastGranule = 0;
        bool active = false;
        double duration = 0;
        while (offset < bytes.Length)
        {
            var page = bytes.Slice(offset);
            if (page.Length < 27 || !page.Slice(0, 4).SequenceEqual("OggS"u8) || page[4] != 0)
                throw new InvalidDataException("Invalid or truncated Ogg page. Only Ogg Vorbis .ogg files are supported.");
            int flags = page[5], segments = page[26], bodyLength = 0;
            if ((flags & ~7) != 0 || page.Length < 27 + segments) throw new InvalidDataException("Invalid Ogg page header.");
            for (int i = 0; i < segments; i++) bodyLength += page[27 + i];
            int size = 27 + segments + bodyLength;
            if (page.Length < size) throw new InvalidDataException("Truncated Ogg page body.");
            page = page.Slice(0, size);
            uint crc = 0;
            for (int i = 0; i < size; i++) crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ (uint)(i is >= 22 and < 26 ? 0 : page[i])];
            if (crc != BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(22, 4))) throw new InvalidDataException("Ogg checksum failed; file is corrupt.");
            uint pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(14, 4));
            uint pageSequence = BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(18, 4));
            if ((flags & 2) != 0)
            {
                if (active || pageSequence != 0) throw new InvalidDataException("Overlapping or malformed Ogg streams are unsupported.");
                active = true; serial = pageSerial; sequence = 0; headers = 0; sampleRate = 0; lastGranule = 0; packetLength = 0;
            }
            if (!active || pageSerial != serial || pageSequence != sequence++ || ((flags & 1) != 0) != (packetLength > 0))
                throw new InvalidDataException("Missing, reordered or invalid Ogg pages.");
            int body = 27 + segments;
            for (int i = 0; i < segments; i++)
            {
                int length = page[27 + i];
                if (headers < 3 && packetLength < prefix.Length)
                    page.Slice(body, Math.Min(length, prefix.Length - packetLength)).CopyTo(prefix.AsSpan(packetLength));
                packetLength += length; body += length;
                if (length == 255) continue;
                if (headers < 3)
                {
                    int type = headers * 2 + 1;
                    if (packetLength < 7 || prefix[0] != type || !prefix.AsSpan(1, 6).SequenceEqual("vorbis"u8))
                        throw new InvalidDataException("Unsupported audio: only Ogg Vorbis is supported (no Opus, MP3 or WAV).");
                    if (headers == 0)
                    {
                        if (packetLength != 30 || BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(7, 4)) != 0
                            || prefix[11] is < 1 or > 2 || (prefix[29] & 1) == 0)
                            throw new InvalidDataException("Only mono/stereo Vorbis version 0 is supported.");
                        sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(12, 4));
                        int small = prefix[28] & 15, large = prefix[28] >> 4;
                        if (sampleRate == 0 || small < 6 || large > 13 || small > large) throw new InvalidDataException("Invalid Vorbis sample rate or block size.");
                    }
                    headers++;
                }
                packetLength = 0;
            }
            long granule = BinaryPrimitives.ReadInt64LittleEndian(page.Slice(6, 8));
            if (granule < -1 || (granule >= 0 && granule < lastGranule)) throw new InvalidDataException("Invalid Ogg playback position.");
            if (granule >= 0) lastGranule = granule;
            if ((flags & 4) != 0)
            {
                if (headers != 3 || packetLength != 0 || granule <= 0 || sampleRate == 0) throw new InvalidDataException("Ogg stream has no complete playable duration.");
                duration += granule / (double)sampleRate;
                active = false;
            }
            offset += size;
        }
        if (active || duration <= 0) throw new InvalidDataException("Ogg stream is empty or missing its end page.");
        return duration;
    }
}
