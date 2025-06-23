using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Syroot.BinaryData;

namespace DriveClubFS;

public class EvoIndexFileEntry
{
    public string FileName { get; set; }

    public ushort DatIndex { get; set; }
    public ulong FileOffset { get; set; }
    public int Size { get; set; }
    public uint FileNameHash { get; set; }
    public byte[] FileMD5Checksum { get; set; }

    public override string ToString()
    {
        return $"{FileName} (0x{FileNameHash:X8})";
    }
}
