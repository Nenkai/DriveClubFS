using DriveClubFS.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Resources.Types;

public class XmlResource : ResourceDataBase
{
    public byte[] Data { get; set; }
    public override void Read(EvoBinaryStream bs, ResourcePack pack)
    {
        base.Read(bs, pack);

        uint binSize = bs.ReadUInt32();
        Data = bs.ReadBytes((int)binSize);
    }
}
