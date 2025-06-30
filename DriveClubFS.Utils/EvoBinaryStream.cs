using Syroot.BinaryData;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Entities;

public class EvoBinaryStream : BinaryStream
{
    public EvoBinaryStream(Stream baseStream, 
        ByteConverter converter = null, 
        Encoding encoding = null, 
        BooleanCoding booleanCoding = BooleanCoding.Byte, 
        DateTimeCoding dateTimeCoding = DateTimeCoding.NetTicks, 
        StringCoding stringCoding = StringCoding.VariableByteCount, 
        bool leaveOpen = false) 
        : base(baseStream, converter, encoding, booleanCoding, dateTimeCoding, stringCoding, leaveOpen)
    {
    }

    public void ReadStreamHeader()
    {
        byte[] magic = ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual("EVOS"u8))
            throw new InvalidDataException("StreamHeader magic did not match 'EVOS'");

        byte[] endian = ReadBytes(4);
        if (endian.AsSpan().SequenceEqual("LITL"u8))
            ByteConverter = ByteConverter.Little;
        else if (magic.AsSpan().SequenceEqual("BIG "u8))
            ByteConverter = ByteConverter.Big;
        else
            throw new InvalidDataException("StreamHeader endian is invalid");
    }
}
