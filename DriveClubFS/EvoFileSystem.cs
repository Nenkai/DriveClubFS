using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using K4os.Compression.LZ4;
using Syroot.BinaryData;
using System.Buffers.Binary;
using System.IO;
using System.Buffers;

namespace DriveClubFS
{
    public class EvoFileSystem : IDisposable
    {
        private bool disposedValue;

        public EvoIndexFile IndexFile { get; set; }
        public string InputDirectory { get; set; }

        public EvoDataFile[] DataFiles { get; set; }

        public void Init(string directory)
        {
            Console.WriteLine($"[:] Loading Evo File System from directory: {directory}");
            InputDirectory = directory;

            if (!File.Exists(InputDirectory + "/game.ndx"))
                throw new FileNotFoundException("game.ndx does not exist in the folder.");

            IndexFile = EvoIndexFile.ReadIndex(InputDirectory + "/game.ndx");
            Console.WriteLine($"[:] File System entries: {IndexFile.Entries.Count}");

            DataFiles = new EvoDataFile[IndexFile.DataFileCount];
            Console.WriteLine($"[:] Data Files registered: {IndexFile.DataFileCount}");

            Console.WriteLine($"[/] File System loaded.");
        }

        public void ListFiles()
        {
            using var sw = new StreamWriter("files.txt");
            foreach (var file in IndexFile.Entries.OrderBy(e => e.FileName))
                sw.WriteLine($"{file.FileName} ({file.Size} bytes, Dat Index: {file.DatIndexAndOffset & 0xFFFF}, Offset: 0x{(file.DatIndexAndOffset >> 16):X8})");

            Console.WriteLine($"[/] Printed {IndexFile.Entries.Count} entries.");
        }

        public void ExtractFile(string fileName, string outputDirectory = "", bool verifyChecksum = true)
        {
            var index = IndexFile.FindFile(fileName);
            if (index is null)
            {
                Console.WriteLine($"[x] Cound not extract '{fileName}' - file not found");
                return;
            }

            using EvoFileReader reader = GetFileReader(fileName);
            if (reader is null)
            {
                Console.WriteLine($"[x] Cound not extract '{fileName}' - dat issue?");
                return;
            }

            reader.ExtractToFile(outputDirectory, index, verifyChecksum);
            Console.WriteLine($"[/] Extracted: {index.FileName}");
        }

        public void ExtractAll(string outputDirectory, bool verifyChecksum = true)
        {
            int i = 0;
            int count = IndexFile.Entries.Count;

            foreach (var file in IndexFile.Entries.OrderBy(e => e.FileName))
            {
                using EvoFileReader reader = GetFileReader(file.FileName);
                if (reader is null)
                {
                    Console.WriteLine($"[x] Cound not process '{file.FileName}' - Data File 'game{file.DatIndexAndOffset & 0xFF:D3}.dat' missing or errored");
                    continue;
                }

                Console.WriteLine($"[:] ({i + 1}/{count}) Extracting: {file.FileName}");

                reader.ExtractToFile(outputDirectory, file, verifyChecksum);
                reader.Dispose();

                i++;
            }

            Console.WriteLine("[/] Fully extracted!");
        }

        public EvoFileReader GetFileReader(string file)
        {
            EvoIndexFileEntry entry = IndexFile.FindFile(file, 0);
            if (entry is null)
                return null;

            var datFile = GetDatFile((short)(entry.DatIndexAndOffset & 0xFFFF));
            if (datFile is null)
                return null;

            return new EvoFileReader(IndexFile, entry, datFile, file);
        }


        private EvoDataFile GetDatFile(short index)
        {
            if (DataFiles[index] is not null)
                return DataFiles[index];

            string datName = InputDirectory + $"/game{index:D3}.dat";
            if (!File.Exists(datName))
                return null;

            var dataFile = EvoDataFile.Init(index, datName);

            DataFiles[index] = dataFile;
            return dataFile;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (DataFiles != null)
                    {
                        foreach (var d in DataFiles)
                            d?.Dispose();
                    }
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
}
