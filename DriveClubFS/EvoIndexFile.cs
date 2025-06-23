using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Syroot.BinaryData;
using System.Buffers.Binary;

namespace DriveClubFS;

public class EvoIndexFile
{
    public List<string> Files = [];
    public List<EvoIndexFileEntry> Entries = [];

    public byte[] CompressDictionary;
    public byte[] CompressedFileNames;

    public uint Version { get; set; }
    public DateTime TimeStamp { get; set; }
    public ulong TotalDataSize { get; set; }
    public uint HashA { get; set; }
    public uint HashB { get; set; }
    public EvoCompressionType CompressionFormat { get; set; }
    public uint ReadBufferSize { get; set; }
    public int Unk5 { get; set; }
    public int Unk6 { get; set; }
    public int DataFileCount { get; set; }
    
    public static EvoIndexFile ReadIndex(Stream stream)
    {
        var bs = new BinaryStream(stream);
        var indexFile = new EvoIndexFile();

        int magic = bs.ReadInt32();
        if (magic != BinaryPrimitives.ReadUInt32LittleEndian("DATN"u8) && 
            magic != BinaryPrimitives.ReadUInt32LittleEndian("DATX"u8))
            throw new InvalidDataException("Unexpected magic. Did not match DATN or DATX.");

        indexFile.Version = bs.ReadUInt32();
        if (indexFile.Version != 4300 && indexFile.Version != 3100)
            throw new InvalidDataException("Unexpected version. Did not match 4300 (Driveclub) or 3100 (Driveclub Alpha).");

        // 1.00 - CUSA00093 - 19/08/2014 15:25:39
        indexFile.TimeStamp = DateTime.FromFileTimeUtc(bs.ReadInt64());

        indexFile.TotalDataSize = bs.ReadUInt64();
        indexFile.HashA = bs.ReadUInt32();
        indexFile.HashB = bs.ReadUInt32();
        indexFile.CompressionFormat = (EvoCompressionType)bs.ReadInt32();
        indexFile.ReadBufferSize = bs.ReadUInt32();

        if (indexFile.Version > 3100)
        {
            uint unk1 = bs.ReadUInt32();
            uint unk2 = bs.ReadUInt32();
            indexFile.DataFileCount = bs.ReadInt32();
            uint unk4 = bs.ReadUInt32();
        }

        // The following checks are actual checks in the game
        if ((int)indexFile.CompressionFormat > 7)
            throw new InvalidDataException($"Invalid compression format. (current: {indexFile.CompressionFormat}");

        if (indexFile.ReadBufferSize >= 0x40000)
            throw new InvalidDataException($"ReadBufferSize cannot be more than 0x40000. (current: 0x{indexFile.ReadBufferSize:X}");

        int nEntries = bs.ReadInt32();
        byte dictSize = bs.Read1Byte();

        if (nEntries >= 1000000)
            throw new InvalidDataException($"Number of entries cannot be more than 1000000. (current: {nEntries}");

        // Not an actual game check, but driveclub allocates 0x80 of space twice in the index manager
        if (dictSize > 0x80)
            throw new InvalidDataException($"Dict size cannot be more than 0x80. (current: 0x{dictSize:X}");

        // Game reads these separately, but we can read them in one go
        //byte[] buf1 = bs.ReadBytes(dictSize);
        //byte[] buf2 = bs.ReadBytes(dictSize);
        indexFile.CompressDictionary = new byte[dictSize * 2];
        bs.ReadExactly(indexFile.CompressDictionary);

        int fileNamesCompressedLength = bs.ReadInt32();
        indexFile.CompressedFileNames = bs.ReadBytes(fileNamesCompressedLength);

        if (bs.ReadInt32() != 0x12345678)
            throw new InvalidDataException("Data corrupted. 0x12345678 marker after compressed file names not found.");

        for (var i = 0; i < nEntries; i++)
        {
            ushort datIndex;
            ulong fileOffset;
            if (indexFile.Version > 3100)
            {
                // Some driveclub version changed data index from a byte to a ushort
                // Which is annoying. Because the version number was left as is

                // TODO: If a poor soul is reading this wanting to support any version between 1.00 an 1.28, the date may need to be changed
                // to the date of the index's timestamp which ACTUALLY changed the data index to be a ushort.

                ulong datIndexAndOffset = bs.ReadUInt64();
                if (indexFile.TimeStamp > new DateTime(2014, 08, 19).AddDays(1)) // 19/08/2014 15:25:39 - 1.00
                {
                    datIndex = (ushort)(datIndexAndOffset & 0xFFFF);
                    fileOffset = datIndexAndOffset >> 16;
                }
                else
                {
                    datIndex = (ushort)(datIndexAndOffset & 0xFF);
                    fileOffset = datIndexAndOffset >> 8;
                }
            }
            else
            {
                datIndex = 0; // Does not exist
                fileOffset = bs.ReadUInt64();
            }

            int fileSize = bs.ReadInt32();
            uint fileNameHash = bs.ReadUInt32();

            byte[] md5Hash = null;
            if (indexFile.Version > 3100)
            {
                md5Hash = new byte[0x10];
                bs.ReadExactly(md5Hash);
            }

            indexFile.Entries.Add(new EvoIndexFileEntry()
            {
                Size = fileSize,
                DatIndex = datIndex,
                FileOffset = fileOffset,
                FileNameHash = fileNameHash,
                FileMD5Checksum = md5Hash,
            });
        }

        if (indexFile.Version > 3100)
        {
            // So, 1.00 has a file offset (uint) here
            // 1.28 checks against a 0x12345678 marker instead
            // Sucks when they change the file system without noteworthy header/version changes (from what I can see)
            // The check *we* do here should be fine and shouldn't need to be touched

            uint currentPos = (uint)bs.Position;
            uint checkTag = bs.ReadUInt32();
            if (checkTag != currentPos && checkTag != 0x12345678)
               throw new InvalidDataException("Data corrupted. Check marker did not match current offset, or 0x12345678.");
        }

        Span<byte> compSpan = indexFile.CompressedFileNames.AsSpan();
        for (var i = 0; i < nEntries; i++)
        {
            byte[] buffer = new byte[0x100];
            ExtractString(buffer, dictSize, indexFile.CompressDictionary, ref compSpan);
            string entryName = Encoding.UTF8.GetString(buffer).TrimEnd('\0');

            indexFile.Files.Add(entryName);
            indexFile.Entries[i].FileName = entryName;
        }
            
        return indexFile;
    }

    public static EvoIndexFile ReadIndex(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Open);
        return ReadIndex(fs);
    }

    public EvoIndexFileEntry FindFile(string fileName, long a2 = 0)
    {
        uint target = Hash(fileName, HashA, HashB);

        int max = Entries.Count;
        int min = 0;

        EvoIndexFileEntry entry = null;
        int mid = 0;
        while (min <= max)
        {
            mid = (min + max) / 2;
            if (Entries[mid].FileNameHash == target)
            {
                entry = Entries[mid];
                break;
            }
            else if (Entries[mid].FileNameHash < target)
            {
                min = mid + 1;
            }
            else
            {
                max = mid - 1;
            }
        }

        if (entry is null)
        {
            long weirdHash = (a2 - (long)(Entries[mid].FileOffset));
            if (weirdHash < 1)
                weirdHash = ((long)(Entries[mid].FileOffset) - a2);

            for (var i = mid + 1; i < Entries.Count; i++)
            {
                if (Entries[i].FileNameHash != target)
                    break;

                long currentHash = (a2 - (long)(Entries[i].FileOffset));
                if (currentHash < 1)
                    currentHash = ((long)(Entries[i].FileOffset) - a2);

                if (weirdHash > currentHash)
                    entry = Entries[i];
                if (weirdHash > currentHash)
                    weirdHash = currentHash;
            }
        }

        return entry;
    }

    static uint Hash(string str, uint hashA, uint hashB)
    {
        uint result = 0;
        for (var i = 0; i < str.Length; i++)
        {
            char c = char.ToUpper(str[i]);
            if (c == '\\')
                c = '/';

            hashA = hashA * hashB % 0x7FFFFFFE;
            result = ((result * hashA) + c) % 0x7FFFFFFFu;
        }

        return result;
    }

    static void ExtractString(Span<byte> outputBuffer, int dictSize, Span<byte> dict, ref Span<byte> compData)
    {
        Span<byte> currentDict = new byte[0x88];

        int i = 0;
        byte data = 0xFF;
        int strOffset = 0;

        while (data != 0 || strOffset < dictSize - 1)
        {
            while (true)
            {
                Span<byte> currentPtr = compData;
                if (i > 0)
                {
                    --i;
                    currentPtr = currentDict[i..];
                }
                else
                {
                    compData = compData[1..];
                    i = 0;
                }

                data = currentPtr[0];
                if ((data & 0x80) == 0)
                    break;

                currentDict[i] = dict[data];
                currentDict[i + 1] = dict[(byte)(data - 0x80)];
                i += 2;
            }

            outputBuffer[strOffset++] = data;
            if (data == 0)
                return;
        }
    }
}
