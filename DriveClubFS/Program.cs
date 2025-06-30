using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using CommandLine;
using CommandLine.Text;

using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

using DriveClubFS.Resources;
using DriveClubFS.Resources.Types;
using DriveClubFS.Utils;

namespace DriveClubFS;

public class Program
{
    public const string Version = "1.1.0";
    static void Main(string[] args)
    {
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine($"- DriveClubFS {Version} by Nenkai");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("- https://github.com/Nenkai");
        Console.WriteLine("- https://twitter.com/Nenkaai");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("");
        
        var p = Parser.Default.ParseArguments<UnpackAllVerbs, UnpackFileVerbs, ListFilesVerbs, ExtractRpkVerbs, ExtractRpksVerbs>(args);

        p.WithParsed<UnpackAllVerbs>(UnpackAll)
         .WithParsed<UnpackFileVerbs>(UnpackFile)
         .WithParsed<ListFilesVerbs>(ListFiles)
         .WithParsed<ExtractRpkVerbs>(ExtractRpk)
         .WithParsed<ExtractRpksVerbs>(ExtractRpks)
         .WithNotParsed(HandleNotParsedArgs);
    }

    public static void UnpackAll(UnpackAllVerbs options)
    {
        using var evoFs = new EvoFileSystem();
        evoFs.Init(options.InputPath);

        if (options.SkipChecksumVerify)
            Console.WriteLine("[!] Will not verify MD5 checksums!");

        if (string.IsNullOrEmpty(options.OutputPath))
            options.OutputPath = Path.Combine(Path.GetFullPath(options.InputPath)!, $"extracted");

        evoFs.ExtractAll(options.OutputPath ?? "", !options.SkipChecksumVerify);
    }

    public static void UnpackFile(UnpackFileVerbs options)
    {
        using var evoFs = new EvoFileSystem();
        evoFs.Init(options.InputPath);

        if (string.IsNullOrEmpty(options.OutputPath))
            options.OutputPath = Path.Combine(Path.GetFullPath(options.InputPath)!, $"extracted");

        evoFs.ExtractFile(options.FileToExtract, options.OutputPath);
    }

    public static void ListFiles(ListFilesVerbs options)
    {
        using var evoFs = new EvoFileSystem();
        evoFs.Init(options.InputPath);
        evoFs.ListFiles();
    }

    public static void ExtractRpk(ExtractRpkVerbs verbs)
    {
        if (!File.Exists(verbs.InputPath))
        {
            Console.WriteLine("[x] ERROR: File does not exist.");
            return;
        }

        ExtractRpkInternal(verbs.InputPath);
    }

    public static void ExtractRpks(ExtractRpksVerbs verbs)
    {
        if (!Directory.Exists(verbs.InputPath))
        {
            Console.WriteLine("[x] ERROR: Folder does not exist.");
            return;
        }

        foreach (var rpkFilePath in Directory.GetFiles(verbs.InputPath, "*.rpk", verbs.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
        {
            ExtractRpkInternal(rpkFilePath);
        }
    }

    private static void ExtractRpkInternal(string rpkFilePath)
    {
        string dirName = Path.GetDirectoryName(rpkFilePath)!;
        string fileName = Path.GetFileNameWithoutExtension(rpkFilePath);

        Console.WriteLine($"[-] Loading {rpkFilePath}...");
        var resourcePack = ResourcePack.Open(rpkFilePath);
        Console.WriteLine($"[/] Loaded {rpkFilePath}.");

        string topMipsPath = Path.Combine(dirName, fileName + "_top_mips.rpk");
        bool gotTopMips = false;
        if (File.Exists(topMipsPath))
        {
            Console.WriteLine($"[-] Loading top mips pack...");
            var mipPack = ResourcePack.Open(topMipsPath);
            resourcePack.TopMipsPacks.Add(mipPack);
            Console.WriteLine($"[/] Loaded top mips.");
            gotTopMips = true;
        }

        if (!gotTopMips)
        {
            topMipsPath = Path.Combine(dirName, fileName + "_topmips.rpk"); // tracks
            if (File.Exists(topMipsPath))
            {
                Console.WriteLine($"[-] Loading top mips pack...");
                var mipPack = ResourcePack.Open(topMipsPath);
                resourcePack.TopMipsPacks.Add(mipPack);
                Console.WriteLine($"[/] Loaded top mips.");
                gotTopMips = true;
            }
        }

        if (!gotTopMips)
        {
            for (int i = 0; i < fileName.Length; i++)
            {
                if (fileName[i] == '_')
                {
                    topMipsPath = Path.Combine(dirName, fileName.Substring(0, i) + "_common_topmips.rpk"); // tracks
                    if (File.Exists(topMipsPath))
                    {
                        Console.WriteLine($"[-] Loading top mips pack {topMipsPath}...");
                        var mipPack = ResourcePack.Open(topMipsPath);
                        resourcePack.TopMipsPacks.Add(mipPack);
                        Console.WriteLine($"[/] Loaded top mips.");
                    }
                }
            }
        }

        foreach (var info in resourcePack.ResourceInfos)
        {
            string outputDir = Path.Combine(dirName, $"{fileName}_rpk_extracted");

            switch (info.Key.Type)
            {
                case ResourceTypeId.RTUID_PIXEL_BUFFER:
                    {
                        ProcessTexture(resourcePack, info, outputDir);
                        break;
                    }

                case ResourceTypeId.RTUID_BIN:
                    {
                        Console.WriteLine($"Extracting BIN resource - {info.Value.Names[0]}");
                        BinaryResource binResource = resourcePack.GetResourceData<BinaryResource>(info.Key);
                        Directory.CreateDirectory(outputDir);

                        if (binResource.Data.Length != 0)
                            File.WriteAllBytes(Path.Combine(outputDir, info.Value.Names[0]), binResource.Data);
                        break;
                    }

                case ResourceTypeId.RTUID_XML:
                    {
                        Console.WriteLine($"Extracting XML resource - {info.Value.Names[0]}");
                        XmlResource xmlResource = resourcePack.GetResourceData<XmlResource>(info.Key);
                        Directory.CreateDirectory(outputDir);

                        File.WriteAllBytes(Path.Combine(outputDir, info.Value.Names[0]), xmlResource.Data);
                        break;
                    }
            }
        }
    }

    private static void ProcessTexture(ResourcePack resourcePack, KeyValuePair<ResourceIdentifier, ResourceInfo> info, string outputDir)
    {
        PixelBufferResource pixelBuffer = resourcePack.GetResourceData<PixelBufferResource>(info.Key);
        Directory.CreateDirectory(outputDir);

        if (pixelBuffer.Resource.Version >= 0x20003)
        {
            PixelFormat format = (PixelFormat)pixelBuffer.PixelFormat;

            int width = pixelBuffer.WriteWidth;
            int height = pixelBuffer.WriteHeight;

            byte[] inputData;
            if (pixelBuffer.StartMip != 0)
            {
                Console.WriteLine($"[-] Processing PIXEL_BUFFER {info.Value.Names[0]} ({format}, {pixelBuffer.WriteWidth}x{pixelBuffer.WriteHeight}) from TOP_MIPS");

                try
                {
                    TopMipsResource topMips = resourcePack.GetResourceData<TopMipsResource>(pixelBuffer.TopMipsIdentifier);
                    inputData = topMips.Data;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[X] Could not find top mips with identifier {pixelBuffer.TopMipsIdentifier}! Skipping.");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"[-] Processing PIXEL_BUFFER {info.Value.Names[0]}  ({format}, {pixelBuffer.WriteWidth}x{pixelBuffer.WriteHeight})");
                inputData = pixelBuffer.Data;
            }

            /*
            for (int i = 0; i < pixelBuffer.StartMip; i++)
            {
                width /= 2;
                height /= 2;
            }
            */

            DXGI_FORMAT dxgiFormat = PixelFormatToDxgiFormat(format);
            int pixelsPerBlock = (int)DxgiUtils.PixelsPerBlock(dxgiFormat);
            int bpp = (int)DxgiUtils.BitsPerPixel(dxgiFormat);
            int blockSize = pixelsPerBlock == 1 ? bpp / 8 : bpp * 2;

            byte[] output = new byte[(width * height * bpp) / 8];
            MemoryOwner<byte> ddsHeader = MemoryOwner<byte>.Allocate(0x100 + output.Length, AllocationMode.Clear);

            PS4Swizzler.Swizzle(inputData, output, width, height, pixelsPerBlock, blockSize);

            var flags = DDSHeaderFlags.TEXTURE | DDSHeaderFlags.LINEARSIZE;
            if (pixelBuffer.MipLevels > 1)
                flags |= DDSHeaderFlags.MIPMAP;

            var dds = new DdsHeader()
            {
                Flags = flags,
                Width = width,
                Height = height,
                FormatFlags = DDSPixelFormatFlags.DDPF_FOURCC,
                LastMipmapLevel = 1,
                FourCCName = "DX10",
                PitchOrLinearSize = (int)0,
                DxgiFormat = PixelFormatToDxgiFormat(format),
                ImageData = output,
            };

            dds.Write(ddsHeader.AsStream());
            File.WriteAllBytes(Path.Combine(outputDir, Path.GetFileNameWithoutExtension(info.Value.Names[0]) + ".dds"), ddsHeader.Span);
        }
        else
        {
            PixelFormatOld pixelFormat = (PixelFormatOld)pixelBuffer.PixelFormat;
            Console.WriteLine($"[x] Skipping PIXEL_BUFFER {info.Value.Names[0]}, PS3 not currently supported");
        }
    }

    private static DXGI_FORMAT PixelFormatToDxgiFormat(PixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            PixelFormat.RGBA_32_I_LIN => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,
            PixelFormat.RGBA_32_I_SRGB => DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
            PixelFormat.RGBA_64_F => DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT,
            PixelFormat.RGBA_128_F => DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT,

            PixelFormat.DXT1_LIN => DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM,
            PixelFormat.DXT1_SRGB => DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB,
            PixelFormat.DXT3_LIN => DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM,
            PixelFormat.DXT3_SRGB => DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB,
            PixelFormat.DXT5_LIN => DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM,
            PixelFormat.DXT5_SRGB => DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM,
            PixelFormat.A8_UNORM => DXGI_FORMAT.DXGI_FORMAT_A8_UNORM,

            PixelFormat.R_32_F => DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT,
            PixelFormat.BC4_UNORM => DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM,
            PixelFormat.BC4_SNORM => DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM,
            PixelFormat.BC5_UNORM => DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM,
            PixelFormat.BC5_SNORM => DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM,
            PixelFormat.BC6H_UF => DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16,
            PixelFormat.BC6H_SF => DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16,
            PixelFormat.BC7_UNORM => DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM,
            PixelFormat.BC7_SRGB => DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB,
            _ => throw new NotSupportedException($"Pixel format {pixelFormat} not supported")
        };
    }

    public static void HandleNotParsedArgs(IEnumerable<Error> errors)
    {
        ;
    }

    [Verb("unpack-all", HelpText = "Unpacks all files from the Drive Club filesystem.")]
    public class UnpackAllVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
        public required string InputPath { get; set; }

        [Option('o', "output", HelpText = "Output folder. If not provided, defaults to an 'extracted' folder inside of the specified input folder.")]
        public string? OutputPath { get; set; }

        [Option("skip-verifying-checksum", HelpText = "Skip verifying MD5 checksum. Speeds up process for slow CPUs. Not recommended.")]
        public bool SkipChecksumVerify { get; set; }

    }

    [Verb("unpack-file", HelpText = "Unpacks a specific file from the Drive Club filesystem.")]
    public class UnpackFileVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
        public required string InputPath { get; set; }

        [Option('f', "file", Required = true, HelpText = "File to extract. Example: crowd/crowd.rpk")]
        public required string FileToExtract { get; set; }

        [Option('o', "output", HelpText = "Output folder. If not provided, defaults to an 'extracted' folder inside of the specified input folder")]
        public string? OutputPath { get; set; }
    }

    [Verb("listfiles", HelpText = "Lists files in the file system. Output will be 'files.txt'.")]
    public class ListFilesVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
        public required string InputPath { get; set; }
    }


    [Verb("extract-rpk", HelpText = "Tries to extract all supported components out of a resource pack (.rpk) file.\n" +
        "Currently, only textures, binary resources and xml resources are supported.")]
    public class ExtractRpkVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Resource Pack (.rpk) file path.")]
        public required string InputPath { get; set; }
    }

    [Verb("extract-rpks", HelpText = "Tries to extract all supported components out of resource packs (.rpk) in the specified folder.\n" +
        "Currently, only textures, binary resources and xml resources are supported.")]
    public class ExtractRpksVerbs
    {
        [Option('i', "input", Required = true, HelpText = "Folder containing .rpk files.")]
        public required string InputPath { get; set; }

        [Option('r', HelpText = "Whether to extract recursively.")]
        public bool Recursive { get; set; }
    }

    // Useful signatures (1.28, generated with IDA/IDA Fusion Plugin) :
    // Stream::Close - 55 48 89 E5 53 50 48 89 FB 48 8B 3B 48 85 FF 74 ? 48 8B 07 FF 50 ? 48 C7 03
    // Stream::GetPosition - 48 8D 05 ? ? ? ? 80 38 ? 74 ? 48 8B 3F 48 8B 07 FF 60 ? 31 C0 C3 0F 1F 84 00 ? ? ? ? 48 8D 05
    // Stream::IsOpen - 48 83 3F ? 0F 95 C0 C3 0F 1F 84 00 ? ? ? ? 83 C4
    // Stream::ReadBytes - 55 48 89 E5 41 57 41 56 41 55 41 54 53 50 48 89 D3 49 89 F4 49 89 FE 41 B7
    // Stream::Seek - 55 48 89 E5 48 8D 05 ? ? ? ? 80 38 ? 74 ? 48 8B 3F 48 8B 07 FF 50 ? EB ? 31 C0 5D C3 90 55 48 89 E5 41 57
    // UnpackZlib - 55 48 89 E5 41 57 41 56 41 54 53 48 81 E4 ? ? ? ? 48 81 EC ? ? ? ? 49 89 F6 4C 8B 25
    // ChunkFile::ChunkFileCreate - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 F6 49 89 FC 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? 41 C7 44 24
    // CreateAccessorBuffer(FileInfoMaybe* a1, uint dataIndex, uint chunkIndex) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 41 89 D4 41 89 F7 49 89 FE
    // CreateStreamFromFileName - 55 48 89 E5 41 56 53 49 89 F6 48 89 FB E8 ? ? ? ? 48 8B 05
    // DatFile::Init(DatFile *datFile, Stream *p_stream, IndexInfo *index, int datIndex) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 41 89 CE 49 89 D4 49 89 F7 49 89 FD
    // DatRead::StartOpen(Stream* p_stream, FileDeviceWrapper *a2, __int64 p_FileName, unsigned int datIndex) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 41 89 CE 49 89 D5
    // EvoDataProcessor *__fastcall EvoDatFS::OpenFile(FileInfoMaybe *a1, unsigned __int8 *name) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 50 49 89 F6 48 89 FB 48 8B 7B
    // void EvoDataProcessor::Destroy(EvoDataProcessor *a1) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 50 48 89 FB 48 8D 05 ? ? ? ? 48 83 C0 ? 48 89 03 4C 8B 7B
    // EvoDataProcessor::EvoDataProcessor(EvoDataProcessor* this, FileInfoMaybe* FileInfoMaybe_1, IndexEntry* IndexEntryByName, DatFile* DatFile_1, char* fileName) - 55 48 89 E5 41 57 41 56 41 54 53 4D 89 C6 48 89 FB
    // EvoDataProcessor::GetFileName(EvoDataProcessor *a1) - 48 83 7F ? ? 48 8D 47 ? 72 ? 48 8B 00 C3 90 55 48 89 E5 41 57 41 56 53 48 83 EC? 49 89 F6 48 89 FB 4C 8B 3D ? ? ? ? 49 8B 07 48 89 45 ? C7 45 ? ? ? ? ? 48 8D 05 ? ? ? ? 48 89 45 ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? 48 8D 75 ? 48 8D 55 ? 48 8D 4D ? 4C 8D 45 ? 4C 8D 4D ? 4C 89 F7 E8 ? ? ? ? 48 89 C7 E8 ? ? ? ? 48 83 C3? 48 89 DF 48 89 C6 E8 ? ? ? ? 48 89 C7 4C 89 F6 E8 ? ? ? ? 49 8B 07 48 3B 45 ? 75 ? 48 83 C4? 5B 41 5E 41 5F 5D C3 E8 ? ? ? ? 90 66 0F 1F 84 00 ? ? ? ? 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC? 48 89 F3 48 89 9D ? ? ? ? 49 89 FE 4C 89 B5? ? ? ? 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? C7 45 ? ? ? ? ? 48 8D 05 ? ? ? ? 48 89 45 ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? 48 8D 75 ? 48 8D 55 ? 48 8D 4D ? 4C 8D 45 ? 4C 8D 4D ? 48 89 DF E8 ? ? ? ? 48 89 C7 E8 ? ? ? ? 49 89 C4 4D 8B 6E ? 4D 8B 76 ? 4C 89 EB 66 66 66 66 2E 0F 1F 84 00 ? ? ? ? 49 39 DE 74 ? 4C 8B 3B 49 8D 7F ? 49 83 7F ? ? 72 ? 48 8B 3F 4C 89 E6 E8 ? ? ? ? 48 83 C3? 85 C0 75 ? 4D 85 FF 75 ? 48 8B 9D ? ? ? ? 48 8B 03 48 89 DF 48 8B B5 ? ? ? ? FF 50 ? 4C 8B 6B? 4C 8B 73 ? 66 0F 1F 44 00 ? BB? ? ? ? 4D 39 EE 74 ? 49 8B 5D ? 48 8D 7B? 48 83 7B? ? 72 ? 48 8B 3F 4C 89 E6 E8 ? ? ? ? 49 83 C5? 85 C0 75 ? 48 89 DF 48 8B B5 ? ? ? ? E8? ? ? ? 48 8B 05 ? ? ? ? 48 8B 00 48 3B 45 ? 75 ? 48 83 C4? 5B 41 5C 41 5D 41 5E 41 5F 5D C3 E8 ? ? ? ? 90 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC? 49 89 FE 4C 89 B5? ? ? ? 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? C7 45 ? ? ? ? ? 48 8D 05 ? ? ? ? 48 89 45 ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? C7 45 ? ? ? ? ? 48 8D 45 ? 48 8D 55 ? 48 8D 4D ? 4C 8D 45 ? 4C 8D 4D ? 48 89 F7 48 89 C6 E8 ? ? ? ? 48 89 C7 E8 ? ? ? ? 49 89 C5 49 8B 5E ? 4D 8B 66 ? 0F 1F 80 ? ? ? ? 49 39 DC 0F 84 ? ? ? ? 4C 8B 3B 4D 8B 77 ? 49 8D 7F ? 49 83 FE? 72 ? 48 8B 3F 4C 89 EE E8 ? ? ? ? 48 83 C3? 85 C0 75 ? 4D 85 FF 4C 8B AD ? ? ? ? 74 ? 49 83 FE? 72 ? 49 8B 77 ? 48 8D 05 ? ? ? ? 48 8B 38 48 8B 07 FF 50 ? 49 C7 47 ? ? ? ? ? 49 C7 47 ? ? ? ? ? 41 C6 47 ? ? 48 8D 05 ? ? ? ? 48 8B 38 48 8B 07 4C 89 FE FF 50 ? 4D 8B 65 ? 49 29 DC 48 8D 7B? 48 89 DE 4C 89 E2 E8 ? ? ? ? 49 83 45 ? ? 48 8B 05 ? ? ? ? 48 8B 00 48 3B 45 ? 75 ? 48 83 C4? 5B 41 5C 41 5D 41 5E 41 5F 5D C3 E8 ? ? ? ? 90 0F 1F 44 00 ? 55 48 89 E5 41 57 41 56 41 54
    // EvoDataProcessor::GetOffset(EvoDataProcessor *a1) - 48 8B 47 ? C3 66 66 2E 0F 1F 84 00 ? ? ? ? 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 4C 8B 35
    // __int64 __fastcall EvoDataProcessor::GetSize(EvoDataProcessor *a1) - 48 8B 47 ? 8B 40 ? C3 0F 1F 84 00 ? ? ? ? 48 8B 7F ? 48 83 C7 ? E9 ? ? ? ? 0F 1F 00 48 83 7F
    // char __fastcall EvoDataProcessor::ReadSize(EvoDataProcessor *a1, __int64 output, unsigned __int64 sizeToRead) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 50 49 89 D6 49 89 F4 49 89 FF 49 8B 47 ? 49 8B 4F
    // char __fastcall EvoDataProcessor::Seek(EvoDataProcessor *a1, __int64 offset, bool relativeToCurrentPos) - 83 FA ? 75 ? 48 03 77 ? 48 89 77 ? B0 ? C3 48 8B 47
    // ExtractString(unsigned __int8 *output, int dictLen, char *fullDict, unsigned __int8 *fullData) - 55 48 89 E5 48 81 EC ? ? ? ? 4C 8B 05 ? ? ? ? 49 8B 00 48 89 45 ? FF CE
    // IndexInfo *__fastcall FileDevice::InitIndex(FileDeviceWrapper *device, int *fileName) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 48 89 F0 48 89 F9
    // Stream *__fastcall FileDevice::OpenDat(Stream *stream, FileDeviceWrapper *fileDevice, __int64 fileName_1) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 D5 49 89 F4 49 89 FE
    // DatFile *__fastcall FileDevice::OpenReadDat(FileDeviceWrapper* this, __int64 p_FileName, unsigned int datIndex, IndexInfo* index) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 49 89 CF 41 89 D4
    // GetIndexEntryByName - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 D5 49 89 F4 48 89 BD ? ? ? ? 4C 8B 0D
    // HandleLZ4 - 55 48 89 E5 41 57 41 56 41 55 41 54 53 50 41 89 CE 49 89 D4 41 89 F5
    // IndexInfo::Read(IndexInfo* index, Stream* stream) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 49 89 F6 48 89 FB 4C 8B 25 ? ? ? ? 49 8B 04 24 48 89 45 ? C6 03
    //   ^ 1.00 -> 55 48 89 E5 41 57 41 56 41 54 53 48 83 EC ? 49 89 F6 48 89 FB 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? C6 03

    // ProcessLZ(unsigned __int8 *input, char *output, int firstVal) - 55 48 89 E5 48 83 EC ? 89 D0
    // void __fastcall ReadNextChunk(EvoDataProcessor *a1, unsigned int chunkIndex) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 41 89 F4 48 89 FB 48 8B 05
    // UnpackZlib - 55 48 89 E5 41 57 41 56 41 54 53 48 81 E4 ? ? ? ? 48 81 EC ? ? ? ? 49 89 F6 4C 8B 25

    // Allocate - 48 89 F8 48 8B 0E 48 8B 49 ? 31 D2 48 89 F7 48 89 C6 FF E1 66 66 66 2E 0F 1F 84 00 ? ? ? ? 89 C6
    // strCopySafe - 55 48 89 E5 41 57 41 56 53 50 49 89 CE 49 89 F7 48 89 FB 49 8D 46

    // AssignStream -  48 8B 06 48 89 07 8A 46

    // ResourcePackage::Dtor(ResourcePackage *a1) - 55 48 89 E5 53 50 48 8D 05 ? ? ? ? 48 83 C0 ? 48 89 07 80 7F
    // void __fastcall ResourcePackage::InitHeader(ResourcePackage *a1, __int64 a2) - 55 48 89 E5 41 56 53 48 83 EC ? 48 89 FB 4C 8B 35 ? ? ? ? 49 8B 06 48 89 45 ? C7 43 ? ? ? ? ? C7 43
    // char __fastcall ResourcePackage::ReadBytes(__int64 a1, __int64 a2, unsigned int a3) - 48 83 C7 ? 89 D2
    // __int64 __fastcall ResourcePackage::ReadInt(ResourcePackage *a1, char *a2) - 55 48 89 E5 41 56 53 48 83 EC ? 48 89 F3 4C 8B 35 ? ? ? ? 49 8B 06 48 89 45 ? 80 7F ? ? 48 8B 07 48 8B 40 ? 74 ? 48 8D 75 ? BA ? ? ? ? FF D0 8A 45 ? 88 03 8A 45 ? 88 43 ? 8A 45 ? 88 43 ? 8A 45 ? 88 43 ? EB
    // void __fastcall ResourcePackage::ReadStringLen4(ResourcePackage *a1) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 FF 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? 41 80 7F
    // ResourceSystem::Dtor - 55 48 89 E5 41 57 41 56 41 54 53 48 83 EC ? 4D 89 C6 49 89 F7 48 89 FB 4C 8B 25 ? ? ? ? 49 8B 04 24 48 89 45 ? 48 8B 03
    // __int64 *__fastcall ResourceSystem::OpenRpk(__int64 *a1, ResourceSystem *this, __int64 *fileName, int type) - 5 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 D4 49 89 F6 49 89 FF 48 8B 1D ? ? ? ? 48 8B 03 48 89 45 ? 83 F9
    // _int64 *__fastcall RpkHandle(__int64 *rdi0, ResourceSystem *rsi0, __int64 *fileName) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 49 89 D7 49 89 F4 49 89 FE 48 8B 1D ? ? ? ? 48 8B 03 48 89 45 ? 4C 89 E7
    // void __fastcall SetupCompression(FileInfoMaybe *this, __int64 *a2, __int64 a3) - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 49 89 D4 49 89 F6 49 89 FF 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? 41 C7 47
    // evo::resourcesystem::ResourceInfoBlock::Write - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 81 EC ? ? ? ? 48 89 B5 ? ? ? ? 49 89 FE 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? 48 8D BD ? ? ? ? E8
    // evo::resourcesystem::ResourceSystem::ResourceSystem - 55 48 89 E5 41 57 41 56 41 55 41 54 53 48 83 EC ? 48 89 75 ? 49 89 FF 48 8B 05 ? ? ? ? 48 8B 00 48 89 45 ? 48 8D 05
    // evo::resourcesystem::ResourceType::ResourceType - 48 89 F8 48 8D 0D ? ? ? ? 48 83 C1 ? 48 89 08 48 89 70
    // evo::streams::operator::<< (int) - 55 48 89 E5 41 56 53 48 83 EC ? 89 F0 48 89 FB 4C 8B 35 ? ? ? ? 49 8B 0E 48 89 4D ? 89 45

    /*
     * 
    struct __attribute__((aligned(8))) DatFile
    {
    _BYTE IsOpen;
    _QWORD TimeStamp;
    _QWORD qword10;
    _QWORD qword18;
    unsigned int ChunkCount;
    unsigned int BufferSize;
    _DWORD DatIndex;
    int gap2C;
    Stream *Stream;
    __int64 field_38;
    _QWORD DataStartPos;
    int *aChunkLocations;
    };

    struct __attribute__((aligned(8))) IndexEntry
    {
      unsigned __int64 DatIndexAndOffset;
      int Size;
      _DWORD Hash;
      int field_10;
      int field_14;
      int field_18;
      int field_1C;
    };

    struct __attribute__((packed)) __attribute__((aligned(1))) Stream
    {
      Stream_Vtalbe *vt;
      char field_8;
    };
    
    struct Stream_Vtalbe
    {
      __int64 field_0;
      __int64 field_8;
      __int64 field_10;
      __int64 Read;
      __int64 field_20;
      __int64 field_28;
      __int64 GetStreamLength;
      __int64 GetPosition;
    };
    
    struct FileDevice
    {
      FileDeviceVTable *vt;
      __int64 field_8;
    };
    
    struct FileDeviceVTable
    {
      __int64 field_0;
      __int64 field_8;
      __int64 field_10;
      __int64 field_18;
      __int64 field_20;
      __int64 field_28;
      __int64 field_30;
      __int64 field_38;
      __int64 field_40;
      __int64 field_48;
      __int64 field_50;
      __int64 field_58;
      __int64 field_60;
      __int64 field_68;
      __int64 field_70;
      __int64 OpenFile;
    };

    struct FileDeviceWrapper
    {
      FileDevice *impl;
    };

    struct EvoDataProcessor
    {
      __int64 vtable;
      FileInfoMaybe *FileInfoMaybe;
      IndexEntry *IndexInfo;
      DatFile *DatFile;
      __int64 Position;
      __int64 FileName;
      _BYTE byte30;
      char field_31;
      char field_32;
      char field_33;
      __attribute__((packed)) __attribute__((aligned(1))) __int64 field_34;
      char field_3C;
      __int64 n0xF;
      __int64 n0x10;
      UnkAccessorRelated *InputBuffer;
      UnkAccessorRelated *OutputBuffer;
    };

    struct UnkAccessorRelated
    {
      _DWORD CompressionFormat;
      _DWORD DatIndex;
      _DWORD CurrentChunkIndex;
      _BYTE byteC;
      char *Buffer;
      volatile signed __int64 unk0x18;
    };

    struct __attribute__((packed)) __attribute__((aligned(1))) ResourcePackage
    {
      ResourcePackage_vtable *vtable;
      _DWORD BigEndian;
      bool IsBigEndian;
      _BYTE gapD[3];
      _DWORD dword10;
      _DWORD dword14;
      Stream *Stream;
      _BYTE gap20[8];
      _BYTE CloseStreamOnDispose;
    };

    struct ResourcePackage_vtable
    {
      __int64 field_0;
      __int64 field_8;
      __int64 ReadBytes;
      __int64 field_18;
    };

    Rest not included as they don't have recognized named structs
    For RPKs, better off using evo games that have debug symbols
    */
}
