using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syroot.BinaryData;
using Syroot.BinaryData.Memory;

using DriveClubFS.Entities;

namespace DriveClubFS.Resources;

public class ResourceInfo
{
    public ResourceIdentifier ResourceId { get; set; }
    public uint Size { get; set; }
    public uint Offset { get; set; }
    public List<ResourceIdentifier> Dependancies { get; set; } = [];
    public List<string> Names { get; set; } = [];
    public List<string> SourceAssetPaths { get; set; } = [];

    public static ResourceInfo Read(EvoBinaryStream bs, ResourcePack pack)
    {
        var res = new ResourceInfo();
        res.ReadInternal(bs, pack);
        return res;
    }

    private void ReadInternal(EvoBinaryStream bs, ResourcePack pack)
    {
        ResourceId = bs.ReadUInt64();
        Size = bs.ReadUInt32();
        Offset = bs.ReadUInt32();

        ushort numDependancies = bs.ReadUInt16();
        for (int i = 0; i < numDependancies; i++)
        {
            ResourceIdentifier dependancy = bs.ReadUInt64();
            Dependancies.Add(dependancy);
        }

        ushort numNames = bs.ReadUInt16();
        for (int i = 0; i < numNames; i++)
        {
            uint nameOffset = bs.ReadUInt32();

            SpanReader sr = new SpanReader(pack.NameStringPool);
            sr.Position = (int)nameOffset;
            string name = sr.ReadString0();
            Names.Add(name);
        }
        
        ushort numSourceAssetPaths = bs.ReadUInt16();
        for (int i = 0; i < numSourceAssetPaths; i++)
        {
            uint nameOffset = bs.ReadUInt32();

            SpanReader sr = new SpanReader(pack.SourceAssetStringPool);
            sr.Position = (int)nameOffset;

            string name = sr.ReadString0();
            SourceAssetPaths.Add(name);
        }
    }

    public override string ToString()
    {
        return $"{(Names.Count > 0 ? Names[0] : "<no name>")} ({ResourceId.Uid:X8} - {ResourceId.Type})";
    }
}