using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using K4os.Compression.LZ4;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Syroot.BinaryData;
using System.Buffers.Binary;
using System.Buffers;
using System.Security.Cryptography;

namespace DriveClubFS;

public class EvoFileReader : IDisposable
{
    private EvoIndexFile _indexFile { get; set; }
    private EvoIndexFileEntry _indexEntry { get; set; }
    public EvoDataFile DataFile { get; set; }
    public string FileName { get; set; }

    private EvoFileSystemBuffer InputBuffer { get; set; }
    private EvoFileSystemBuffer OutputBuffer { get; set; }

    private long Position { get; set; }

    private MD5 _md5;
    private bool disposedValue;

    public EvoFileReader(EvoIndexFile indexFile, EvoIndexFileEntry indexEntry, EvoDataFile datFile, string fileName)
    {
        _md5 = MD5.Create();

        _indexFile = indexFile;
        _indexEntry = indexEntry;
        DataFile = datFile;
        FileName = fileName;

        int chunkIndex = (int)(indexEntry.FileOffset / datFile.BufferSize);
        var outBuffer = EvoFileSystemBuffer.Create(datFile, datFile.DataIndex, chunkIndex);
        if (outBuffer.CurrentChunkIndex == chunkIndex && outBuffer.CompressionFormat == 0)
        {
            OutputBuffer = outBuffer;
            InputBuffer = EvoFileSystemBuffer.Create(datFile, datFile.DataIndex, chunkIndex + 1);
        }
        else
        {
            InputBuffer = outBuffer;
            OutputBuffer = EvoFileSystemBuffer.Create(datFile, datFile.DataIndex, -1);
        }
    }


    public bool Read(Span<byte> output, int length)
    {
        long currentPosition = Position + (long)_indexEntry.FileOffset;
        int currentChunkIndex = (int)(currentPosition / DataFile.BufferSize);

        if (currentChunkIndex != OutputBuffer.CurrentChunkIndex)
            FeedNextChunk(currentChunkIndex);

        if (length > 0)
        {
            long chunkOffset = (currentPosition % DataFile.BufferSize);
            int toRead = length;

            Span<byte> chkPtr = default;
            while (toRead > 0)
            {
                int chunkLeft = (int)(DataFile.BufferSize - chunkOffset);
                chkPtr = OutputBuffer.Buffer.AsSpan((int)chunkOffset);
                if (toRead <= chunkLeft)
                    break;

                MemCpy(output, chkPtr, (int)(DataFile.BufferSize - chunkOffset));
                output = output[chunkLeft..];

                currentChunkIndex++;
                FeedNextChunk(currentChunkIndex);
                chunkOffset = 0;

                toRead -= chunkLeft;
                if (toRead == 0)
                {
                    Position += length;
                    return true;
                }
            }

            MemCpy(output, chkPtr, toRead);
        }

        Position += length;
        return true;

    }

    public void ExtractToFile(string outputDirectory, EvoIndexFileEntry file, bool verifyChecksum = true)
    {
        string outputPath = Path.Combine(outputDirectory, file.FileName);
        string outputFullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath));

        using var fs = new FileStream(outputFullPath, FileMode.Create);

        int remBytes = file.Size;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(0x10000);
        while (remBytes > 0)
        {
            int toRead = Math.Min(remBytes, (int)DataFile.BufferSize);
            Read(buffer, toRead);
            fs.Write(buffer, 0, toRead);

            remBytes -= toRead;

            if (verifyChecksum)
            {
                if (remBytes > 0)
                    _md5.TransformBlock(buffer, 0, toRead, buffer, 0);
                else
                    _md5.TransformFinalBlock(buffer, 0, toRead);
            }
        }

        if (verifyChecksum && !_md5.Hash.AsSpan().SequenceEqual(file.FileMD5Checksum))
            throw new CryptographicException($"MD5 hash for extracted file '{file.FileName}' did not match.");
    }

    public EvoIndexFileEntry GetIndexEntry()
    {
        return _indexEntry;
    }

    public long GetPosition()
    {
        return Position;
    }

    public int GetLength()
    {
        return _indexEntry.Size;
    }

    public bool Seek(long offset, bool relativeToCurrentPos)
    {
        if (relativeToCurrentPos)
            offset += Position;

        Position = offset;
        return true;
    }

    private void FeedNextChunk(int chunkIndex)
    {
        long relativeChunkLocation = DataFile.ChunkOffsets[chunkIndex];
        long relativeNextChunkLocation = DataFile.ChunkOffsets[chunkIndex + 1];
        long absoluteChunkOffset = DataFile.DataStartOffset + relativeChunkLocation;

        int currentChunkSize = (int)(relativeNextChunkLocation - relativeChunkLocation);

        if (InputBuffer.CurrentChunkIndex != chunkIndex)
        {
            if (DataFile.Stream.Position != absoluteChunkOffset)
                DataFile.Stream.Position = absoluteChunkOffset;

            DataFile.Stream.ReadExactly(InputBuffer.Buffer, 0, currentChunkSize);

            InputBuffer.CurrentChunkIndex = chunkIndex;
        }

        if (_indexFile.CompressionFormat != 0)
        {
            if (currentChunkSize == DataFile.BufferSize)
            {
                // Direct copy
                InputBuffer.Buffer.AsSpan().CopyTo(OutputBuffer.Buffer);
            }
            else
            {
                // Decompress
                ProcessCompressedChunk(_indexFile.CompressionFormat, InputBuffer.Buffer, OutputBuffer.Buffer, currentChunkSize);
            }
        }
    }

    private int ProcessCompressedChunk(EvoCompressionType compressionType, byte[] input, byte[] output, int length)
    {
        switch (compressionType)
        {
            case EvoCompressionType.NoPack:
                return HandleNoPack(input, output, length);

            case EvoCompressionType.Zlib:
                return HandleZlib(input, output, length);

            case EvoCompressionType.LZ4:
            case EvoCompressionType.LZ4HC:
                return HandleLZ4(input, output, length);

            default:
                throw new NotSupportedException($"Unsupported compression type '{compressionType}'");
        }
    }

    private int HandleNoPack(byte[] input, byte[] output, int length)
    {
        MemCpy(input, output, length);
        return length;
    }

    private int HandleZlib(byte[] input, byte[] output, int length)
    {
        if (input[0] == 0x55)
            throw new NotImplementedException();

        var deflater = new Inflater(noHeader: false);
        deflater.SetInput(input);
        deflater.Inflate(output);

        return length;
    }

    private int HandleLZ4(byte[] input, byte[] output, int length)
    {
        if (input[0] == 0x55)
            throw new NotImplementedException();

        int decompressedSize = BinaryPrimitives.ReadInt32LittleEndian(input);
        if (LZ4Codec.Decode(input.AsSpan(4, length - 4), output.AsSpan()) == decompressedSize)
            return decompressedSize;

        return 0;
    }

    private static void MemCpy(Span<byte> output, Span<byte> input, int length)
    {
        input.Slice(0, length).CopyTo(output);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _md5.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
