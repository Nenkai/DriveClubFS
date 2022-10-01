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

        public uint HashA { get; set; }
        public uint HashB { get; set; }
        public int CompressionFormat { get; set; }
        public int BufferSizeMaybe { get; set; }
        public int Unk5 { get; set; }
        public int Unk6 { get; set; }
        public int DataFileCount { get; set; }
        
        public static EvoIndexFile ReadIndex(string fileName)
        {
            var indexFile = new EvoIndexFile();
            using var fs = new FileStream(fileName, FileMode.Open);
            using var bs = new BinaryStream(fs);

            int magic = bs.ReadInt32();
            if (magic != 0x4E544144 && magic != 0x58544144)
                throw new InvalidDataException("Unexpected magic. Did not match DATX.");

            int version = bs.ReadInt32();
            if ((version & 0xF000) != 4096)
                throw new InvalidDataException("Unexpected version. Did not match 4300.");

            bs.ReadInt64();
            bs.ReadInt64();
            indexFile.HashA = bs.ReadUInt32();
            indexFile.HashB = bs.ReadUInt32();
            indexFile.CompressionFormat = bs.ReadInt32();
            indexFile.BufferSizeMaybe = bs.ReadInt32();
            bs.ReadInt32();
            bs.ReadInt32();

            indexFile.DataFileCount = bs.ReadInt32();
            bs.ReadInt32();
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
                ulong datIndexAndHash = bs.ReadUInt64();
                int fileSize = bs.ReadInt32();
                uint fileNameHash = bs.ReadUInt32();

                byte[] data = new byte[0x10];
                bs.Read(data);

                indexFile.Entries.Add(new EvoIndexFileEntry()
                {
                    Size = fileSize,
                    DatIndexAndOffset = datIndexAndHash,
                    FileNameHash = fileNameHash,
                    FileMD5Checksum = data,
                });
            }

            if (bs.ReadInt32() != 0x12345678)
                throw new InvalidDataException("Data corrupted. 0x12345678 after index entries not found.");

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

            if (entry is not null)
            {
                long weirdHash = (t - (long)(Entries[mid].DatIndexAndOffset >> 16));
                if (weirdHash < 1)
                    weirdHash = ((long)(Entries[mid].DatIndexAndOffset >> 16) - t);

                for (var i = mid + 1; i < Entries.Count; i++)
                {
                    if (Entries[i].FileNameHash != target)
                        break;

                    long currentHash = (t - (long)(Entries[i].DatIndexAndOffset >> 16));
                    if (currentHash < 1)
                        currentHash = ((long)(Entries[i].DatIndexAndOffset >> 16) - t);

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
            Span<byte> outputStr = new byte[0x80];

            if (dictSize - 1 > 0)
            {
                int i = 0;
                Span<byte> outputEnd = outputBuffer.Slice(dictSize - 1);
                byte currentOffset;

                do
                {
                    while (true)
                    {
                        Span<byte> currentPtr = compData;
                        if (i > 0)
                        {
                            --i;
                            currentPtr = outputStr.Slice(i);
                        }
                        else
                        {
                            compData = compData[1..];
                            i = 0;
                        }

                        currentOffset = currentPtr[0];
                        if ((currentOffset & 0x80) == 0)
                            break;

                        outputStr[i] = dict[currentOffset];
                        outputStr[i + 1] = dict[currentOffset - 0x80];
                        i += 2;
                    }

                    outputBuffer[0] = currentOffset;
                    outputBuffer = outputBuffer[1..];
                } while (currentOffset != 0 && outputBuffer != outputEnd);
            }

            outputBuffer[0] = 0;
        }
    }
}
