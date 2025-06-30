using DriveClubFS.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Resources;

public class ResourceDataBase
{
    public Resource Resource { get; set; }

    public virtual void Read(EvoBinaryStream bs, ResourcePack pack)
    {
        bs.ReadStreamHeader();
        Resource = Resource.Read(bs);
    }
}
