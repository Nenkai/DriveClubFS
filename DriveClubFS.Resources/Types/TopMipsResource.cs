using DriveClubFS.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Resources.Types;

public class TopMipsResource : ResourceDataBase
{
    public ResourceIdentifier PixelBufferIdentifier { get; set; }
    public byte[] Data { get; set; }
    public override void Read(EvoBinaryStream bs, ResourcePack pack)
    {
        base.Read(bs, pack);

        PixelBufferIdentifier = bs.ReadUInt64();
        uint dataSize = bs.ReadUInt32();
        Data = bs.ReadBytes((int)dataSize);
    }
}
