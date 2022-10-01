using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Syroot.BinaryData;

namespace DriveClubFS
{
    public class EvoFileSystemBuffer
    {
        public int CurrentChunkIndex { get; set; } = -1;
        public int DataIndex { get; set; }
        public int CompressionFormat { get; set; } = -1;
        public byte[] Buffer { get; set; }

        public static EvoFileSystemBuffer Create(EvoDataFile dataFile, int dataIndex, int chunkIndex)
        {
            var buf = new EvoFileSystemBuffer();
            buf.DataIndex = dataIndex;
            buf.CurrentChunkIndex = -1;
            buf.Buffer = new byte[dataFile.BufferSize];
            return buf;
        }
    }
}
