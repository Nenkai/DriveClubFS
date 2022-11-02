using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Syroot.BinaryData;

namespace DriveClubFS
{
    public class EvoIndexFile
    {
        public List<string> Files = new List<string>();
        public List<EvoIndexFileEntry> Entries = new();

        public byte[] CompressDictionary;
        public byte[] CompressedFileNames;

        public uint Version { get; set; }
        public DateTime TimeStamp { get; set; }
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
            if (magic != 0x4E544144 && magic != 0x58544144)
                throw new InvalidDataException("Unexpected magic. Did not match DATX.");

            indexFile.Version = bs.ReadUInt32();
            if (indexFile.Version != 4300 && indexFile.Version != 3100)
                throw new InvalidDataException("Unexpected version. Did not match 4300 (1.28 Driveclub) or 3100 (Driveclub Alpha).");

            indexFile.TimeStamp = DateTime.FromFileTimeUtc(bs.ReadInt64());
            bs.ReadInt64(); // Self ptr/offset
            indexFile.HashA = bs.ReadUInt32();
            indexFile.HashB = bs.ReadUInt32();
            indexFile.CompressionFormat = (EvoCompressionType)bs.ReadInt32();
            indexFile.ReadBufferSize = bs.ReadUInt32();

            if (indexFile.Version > 3100)
            {
                bs.ReadInt32();
                bs.ReadInt32();
                indexFile.DataFileCount = bs.ReadInt32();
                bs.ReadInt32();
            }

            int nEntries = bs.ReadInt32();
            byte dictSize = bs.Read1Byte();

            indexFile.CompressDictionary = new byte[dictSize * 2];
            bs.Read(indexFile.CompressDictionary);

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
                    ulong datIndexAndOffset = bs.ReadUInt64();
                    datIndex = (ushort)(datIndexAndOffset & 0xFFFF);
                    fileOffset = datIndexAndOffset >> 16;
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
                    bs.Read(md5Hash);
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
                if (bs.ReadInt32() != 0x12345678)
                    throw new InvalidDataException("Data corrupted. 0x12345678 after index entries not found.");
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

        public EvoIndexFileEntry FindFile(string fileName, long t = 0)
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
                long weirdHash = (t - (long)(Entries[mid].FileOffset));
                if (weirdHash < 1)
                    weirdHash = ((long)(Entries[mid].FileOffset) - t);

                for (var i = mid + 1; i < Entries.Count; i++)
                {
                    if (Entries[i].FileNameHash != target)
                        break;

                    long currentHash = (t - (long)(Entries[i].FileOffset));
                    if (currentHash < 1)
                        currentHash = ((long)(Entries[i].FileOffset) - t);

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
                        currentPtr = currentDict.Slice(i);
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
}
