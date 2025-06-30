using DriveClubFS.Entities;

using Syroot.BinaryData;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace DriveClubFS.Resources;

public class Resource
{
    public ResourceIdentifier Identifier { get; set; }
    public uint Version { get; set; }

    public static Resource Read(EvoBinaryStream bs)
    {
        return new Resource
        {
            Identifier = bs.ReadUInt64(),
            Version = bs.ReadUInt32(),
        };
    }
}