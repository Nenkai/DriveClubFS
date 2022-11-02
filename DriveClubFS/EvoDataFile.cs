using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Syroot.BinaryData;

namespace DriveClubFS
{
    public class EvoDataFile : IDisposable
    {
        private bool disposedValue;

        public uint Version { get; set; }
        public DateTime TimeStamp { get; set; }
        public int DataIndex { get; set; }
        public uint BufferSize { get; set; }
        public uint[] ChunkOffsets { get; set; }
        public BinaryStream Stream { get; set; }
        public long DataStartOffset { get; set; }

        /// <summary>
        /// For older versions which has their index inside the data file
        /// </summary>
        public EvoIndexFile Index { get; set; }

        public static EvoDataFile Init(short index, string fileName)
        {
            var dataFile = new EvoDataFile();
            dataFile.DataIndex = index;

            var fs = new FileStream(fileName, FileMode.Open);
            var bs = new BinaryStream(fs);
            dataFile.Stream = bs;

            if (bs.ReadInt32() != 0x46544144)
                throw new InvalidDataException("Data corrupted (DATF magic missing/incorrect).");

            dataFile.Version = bs.ReadUInt32();
            if (dataFile.Version != 4300 && dataFile.Version != 3100)
                throw new InvalidDataException("Data corrupted (Version not 4300 (DriveClub 1.28) or 3100 (DriveClub alpha)).");

            dataFile.TimeStamp = DateTime.FromFileTimeUtc(bs.ReadInt64());
            long tocOffset = bs.ReadInt64();

            int chunkCount = 0;
            if (dataFile.Version > 3100)
            {
                chunkCount = bs.ReadInt32();
                dataFile.BufferSize = bs.ReadUInt32();
            }
            else
            {
                dataFile.DataStartOffset = bs.Position;
                bs.Position = tocOffset;
                dataFile.Index = EvoIndexFile.ReadIndex(bs.BaseStream);
                dataFile.BufferSize = dataFile.Index.ReadBufferSize;
            }

            int chnkMagic = bs.ReadInt32();
            if (chnkMagic != 0x4B4E4843 && chnkMagic != 0x43484e4b) // CHNK
                throw new InvalidDataException("Data corrupted (CHNK magic missing/incorrect).");

            if (dataFile.Version <= 3100)
                chunkCount = bs.ReadInt32();

            uint[] chunkSizes = bs.ReadUInt32s(chunkCount);
            dataFile.ChunkOffsets = new uint[chunkCount + 1];

            if (dataFile.Version > 3100)
            {
                if (bs.ReadInt32() != 0x41544144) // DATA
                    throw new InvalidDataException("DATA corrupted (DATA magic missing/incorrect).");

                dataFile.DataStartOffset = bs.Position;
            }

            // Translate chunk sizes to an absolute offset
            // Last extra one will be the end, in such that (next - previous) = chunk size
            uint offset = 0;
            for (var i = 0; i < chunkCount; i++)
            {
                dataFile.ChunkOffsets[i] = offset;
                offset += chunkSizes[i];
            }
            dataFile.ChunkOffsets[^1] = offset; // Ending

            return dataFile;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    Stream.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
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
}
