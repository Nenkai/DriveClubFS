using DriveClubFS.Entities;
using DriveClubFS.Resources.Types;

using Syroot.BinaryData;
using Syroot.BinaryData.Memory;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Resources;

public class ResourcePack : IDisposable
{
    private EvoBinaryStream _stream;

    // m_Root
    public ResourceIdentifier RootIdentifier { get; set; }
    public byte[] NameStringPool { get; set; }
    public byte[] SourceAssetStringPool { get; set; }
    public Dictionary<ResourceIdentifier, ResourceInfo> ResourceInfos { get; set; } = [];
    public List<UnkStruct3> Unk3 { get; set; } = [];
    public List<string> RequiredResourcePacksMaybe { get; set; } = [];

    public List<ResourcePack> TopMipsPacks { get; set; } = [];

    public static ResourcePack Open(string file)
    {
        var fs = File.OpenRead(file);
        return OpenStream(fs);
    }

    public static ResourcePack OpenStream(Stream stream)
    {
        var resource = new ResourcePack();
        resource.Read(stream);
        return resource;
    }

    private void Read(Stream stream)
    {
        var bs = new EvoBinaryStream(stream);
        _stream = bs;

        bs.ReadStreamHeader();
        string headerMagic = bs.ReadString(StringCoding.Int32CharCount);
        if (headerMagic != "Resource PacK file")
            throw new InvalidDataException("Not a resource pack. 'Resource PacK file' not found.");

        uint tocSize = bs.ReadUInt32();
        uint tocOffset = bs.ReadUInt32();
        bs.Position = tocOffset;
        ReadResourceInfoBlock(bs);
    }

    private void ReadResourceInfoBlock(EvoBinaryStream bs)
    {
        bs.ReadStreamHeader();

        Resource resourceInfo = Resource.Read(bs);
        if (resourceInfo.Identifier.Type != ResourceTypeId.RTUID_RESOURCE_INFO_BLOCK)
            throw new InvalidDataException($"Not a valid resource pack. Main identifier is not {nameof(ResourceTypeId.RTUID_RESOURCE_INFO_BLOCK)}.");

        if (bs.ReadUInt32() != 0x11111111)
            throw new InvalidDataException($"Not a valid resource pack. Resource Info Block did not start with 0x11111111 marker.");

        uint count = bs.ReadUInt32();
        uint aliasCount = bs.ReadUInt32(); // count of every identifier incl dependencies? all merged?
        uint dependancyCount = bs.ReadUInt32();
        uint nameStringPoolSize = bs.ReadUInt32();
        uint sourceAssetStringPoolSize = bs.ReadUInt32();

        uint unk3Count = 0;
        uint requiredResourcePacksCountMaybe = 0;
        if (resourceInfo.Version >= 0x40002)
        {
            unk3Count = bs.ReadUInt32();
            requiredResourcePacksCountMaybe = bs.ReadUInt32();
        }

        RootIdentifier = bs.ReadUInt64();
        NameStringPool = bs.ReadBytes((int)nameStringPoolSize);
        SourceAssetStringPool = bs.ReadBytes((int)sourceAssetStringPoolSize);
        ResourceInfos.EnsureCapacity((int)count);
        for (int i = 0; i < count; i++)
        {
            var info = ResourceInfo.Read(bs, this);
            ResourceInfos.Add(info.ResourceId, info);
        }

        for (int i = 0; i < unk3Count; i++)
        {
            ResourceIdentifier identifier = bs.ReadUInt64();
            uint nameOffset = bs.ReadUInt32();

            SpanReader sr = new SpanReader(NameStringPool);
            sr.Position = (int)nameOffset;
            string resourceName = sr.ReadString0();
            Unk3.Add(new UnkStruct3(identifier, resourceName));
        }

        for (int i = 0; i < requiredResourcePacksCountMaybe; i++)
        {
            uint nameOffset = bs.ReadUInt32();

            SpanReader sr = new SpanReader(NameStringPool);
            sr.Position = (int)nameOffset;
            string resourceName = sr.ReadString0();
            RequiredResourcePacksMaybe.Add(resourceName);
        }

        Console.WriteLine($"[-] Root: {RootIdentifier}");
        Console.WriteLine($"[-] Resources: {ResourceInfos.Count}");
    }

    public T GetResourceData<T>(ResourceIdentifier identifier) where T : ResourceDataBase
    {
        if (identifier.Type == ResourceTypeId.RTUID_TOP_MIPS)
        {
            foreach (var pack in TopMipsPacks)
            {
                if (pack.ResourceInfos.ContainsKey(identifier))
                    return pack.GetResourceData<T>(identifier);
            }
        }

        if (!ResourceInfos.TryGetValue(identifier, out ResourceInfo? info))
            throw new KeyNotFoundException($"Resource with identifier {identifier} not found in resource pack");

        ResourceDataBase resourceData = info.ResourceId.Type switch
        {
            ResourceTypeId.RTUID_PIXEL_BUFFER => new PixelBufferResource(),
            ResourceTypeId.RTUID_BIN => new BinaryResource(),
            ResourceTypeId.RTUID_XML => new XmlResource(),
            ResourceTypeId.RTUID_TOP_MIPS => new TopMipsResource(),
            _ => throw new NotSupportedException($"Resource data type {info.ResourceId.Type} is not yet supported"),
        };

        _stream.Position = info.Offset;
        resourceData.Read(_stream, this);
        return (T)resourceData;
    }

    public void Dispose()
    {
        ((IDisposable)_stream).Dispose();
    }

    public record UnkStruct3(ResourceIdentifier identifier, string resourceName);
}
