#nullable enable
using System;

namespace FeralKinshipCompanions;

// Read only the protobuf schema header, without interpreting journal payloads.
// Missing, malformed or duplicated schema authority is rejected read-only
// before payload interpretation. No journal fields are decoded by this reader.
internal static class RetainedLedgerSchema
{
    internal static int? ReadVersion(byte[]? data)
    {
        if(data==null)return null;
        int index=0;
        int? found=null;
        bool Varint(out ulong value) {
            value=0;
            for(int shift=0;shift<64;shift+=7) {
                if(index>=data.Length)return false;
                byte b=data[index++];
                if(shift==63 && b>1)return false;
                value|=(ulong)(b&127)<<shift;
                if((b&128)==0)return true;
            }
            return false;
        }
        while(index<data.Length) {
            if(!Varint(out ulong key)||key==0)return null;
            if(key==8) {
                if(found.HasValue)return int.MaxValue; // Ambiguous duplicate authority, before payload decoding.
                if(!Varint(out ulong version))return null;
                found=version>int.MaxValue?int.MaxValue:(int)version;continue;
            }
            ulong length;
            switch(key&7) {
                case 0: if(!Varint(out _))return null;continue;
                case 1: length=8;break;
                case 2: if(!Varint(out length))return null;break;
                case 5: length=4;break;
                default: return null;
            }
            if(length>(ulong)(data.Length-index))return null;
            index+=(int)length;
        }
        return found;
    }
}
