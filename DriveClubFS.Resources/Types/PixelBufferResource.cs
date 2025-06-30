using DriveClubFS.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DriveClubFS.Resources.Types;

public class PixelBufferResource : ResourceDataBase
{
    public ulong UnkId { get; set; }
    public ushort WriteWidth { get; set; }
    public ushort WriteHeight { get; set; }
    public ushort Depth { get; set; }
    public ushort PixelFormat { get; set; }
    public byte MipLevels { get; set; }
    public byte Type { get; set; }
    public byte ServerUsage { get; set; }
    public byte Unk { get; set; }
    public byte StartMip { get; set; }
    public ResourceIdentifier TopMipsIdentifier { get; set; }
    public byte Unk3 { get; set; }
    public byte[] Data { get; set; }

    public override void Read(EvoBinaryStream bs, ResourcePack pack)
    {
        base.Read(bs, pack);

        if (Resource.Version == 0x20000)
            UnkId = bs.ReadUInt64();

        WriteWidth = bs.ReadUInt16();
        WriteHeight = bs.ReadUInt16();
        Depth = bs.ReadUInt16();
        PixelFormat = bs.ReadUInt16();
        MipLevels = bs.Read1Byte();
        Type= bs.Read1Byte();
        ServerUsage = bs.Read1Byte();

        if (Resource.Version >= 0x20003)
        {
            Unk = bs.Read1Byte();
            StartMip = bs.Read1Byte();

            if (Resource.Version >= 0x20005)
                Unk3 = bs.Read1Byte();

            TopMipsIdentifier = bs.ReadUInt64();
        }

        uint dataSize = bs.ReadUInt32();
        Data = new byte[dataSize];
        bs.ReadExactly(Data);
    }
}

/// <summary>
/// PS3
/// </summary>
public enum PixelFormatOld
{
    PF_UNDEFINED_PIXEL_FORMAT = 0x0,
    PF_RGBA_32_I = 1,
    PF_RGBA_64_F = 2,
    PF_RGBA_128_F = 3,
    PF_Z_16 = 4,
    PF_Z_24 = 5,
    PF_Z_24_S_8 = 6,
    PF_Z_32 = 7,
    PF_STENCIL = 8,
    PF_DXT1 = 10,
    PF_DXT3 = 10,
    PF_DXT5 = 11,
    PF_LUM_8 = 12, // 1 byte luminance
    PF_RG_32_F = 13,
    PF_RGBA_16_I = 14,
    PF_L8A8 = 15,
    PF_A16 = 16,
    PF_L16A16 = 17,
    PF_R_32_F = 18,
}

/// <summary>
/// PS4
/// </summary>
public enum PixelFormat /*evo::renderer::PixelFormat*/
{
    // Driveclub Proto
    UNDEFINED_PIXEL_FORMAT = 0x0,
    RGBA_32_I_LIN = 1,

    /// <summary>
    /// R8G8B8A8 UNORM SRGB
    /// </summary>
    RGBA_32_I_SRGB = 2,
    RGBA_64_F = 3,
    RGBA_128_F = 4,
    Z_16 = 5,
    Z_32 = 6,
    Z32_S8 = 7,
    STENCIL = 8,
    DXT1_LIN = 9,
    DXT1_SRGB = 10,
    DXT3_LIN = 11,
    DXT3_SRGB = 12,
    DXT5_LIN = 13,
    DXT5_SRGB = 14,
    A8_UNORM = 15,
    RG_16_F = 16,
    RG_32_F = 17,
    RGBA_16_I = 18,
    R8G8 = 19,
    A16 = 20,
    L16A16 = 21,
    R_16_F = 22,
    R_32_F = 23,
    R11G11B10_F = 24,
    R9G9B9E5 = 25,
    R10G10B10A2_UNORM = 26,
    R8_UNORM = 27,
    BGRA_32_I_SRGB = 28,
    BC4_UNORM = 29,
    BC4_SNORM = 30,
    BC5_UNORM = 31,
    BC5_SNORM = 32,
    BC6H_UF = 33,
    BC6H_SF = 34,
    BC7_UNORM = 35,
    BC7_SRGB = 36,
    RGBA_64_UNORM = 37,
    RGBA_32_SNORM = 38,

    // Driveclub retail
    UNK_39 = 39,
    UNK_40 = 40,
    UNK_41 = 41,

    NUM_PIXEL_FORMATS = 39,
}
