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

namespace DriveClubFS;

public class EvoFileSystem : IDisposable
{
    private bool disposedValue;

    public EvoIndexFile IndexFile { get; set; }
    public string InputDirectory { get; set; }

    public EvoDataFile[] DataFiles { get; set; }

    /// <summary>
    /// For older versions without a split index file
    /// </summary>
    public EvoDataFile MainDataFile { get; set; }

    public void Init(string directory)
    {
        Console.WriteLine($"[:] Loading Evo File System from directory: {directory}");
        InputDirectory = directory;

        if (File.Exists(InputDirectory + "/game.ndx"))
        {
            Console.WriteLine("[!] Loading Evo File System from game.idx file");
            IndexFile = EvoIndexFile.ReadIndex(InputDirectory + "/game.ndx");
            DataFiles = new EvoDataFile[IndexFile.DataFileCount];

            Console.WriteLine($"[:] Version: {IndexFile.Version}");
            Console.WriteLine($"[:] Data Files registered: {IndexFile.DataFileCount}");
        }
        else if (File.Exists(InputDirectory + "/game.dat"))
        {
            Console.WriteLine("[!] game.idx file not found, reading from game.dat");

            MainDataFile = EvoDataFile.Init(0, InputDirectory + "/game.dat");
            IndexFile = MainDataFile.Index;
        }
        else
            throw new FileNotFoundException("Could not detect a valid evo file system in this directory.");

        Console.WriteLine($"[:] Version: {IndexFile.Version}");
        Console.WriteLine($"[:] File System entries: {IndexFile.Entries.Count}");
        Console.WriteLine($"[/] File System loaded.");
    }

    public void ListFiles()
    {
        using var sw = new StreamWriter("files.txt");
        foreach (var file in IndexFile.Entries.OrderBy(e => e.FileName))
            sw.WriteLine($"{file.FileName} ({file.Size} bytes, Dat Index: {file.DatIndex}, Offset: 0x{(file.FileOffset):X8})");

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

        using EvoFileReader? reader = GetFileReader(fileName);
        if (reader is null)
        {
            Console.WriteLine($"[x] Cound not extract '{fileName}' - dat issue?");
            return;
        }

        if (verifyChecksum && IndexFile.Version <= 3100)
            verifyChecksum = false; // No checksum available

        reader.ExtractToFile(outputDirectory, index, verifyChecksum);
        Console.WriteLine($"[/] Extracted: {index.FileName}");
    }

    public void ExtractAll(string outputDirectory, bool verifyChecksum = true)
    {
        int i = 0;
        int count = IndexFile.Entries.Count;

        foreach (var file in IndexFile.Entries)
        {
            using EvoFileReader? reader = GetFileReader(file.FileName);
            if (reader is null)
            {
                Console.WriteLine($"[x] Cound not process '{file.FileName}' - Data File 'game{file.DatIndex & 0xFF:D3}.dat' missing or errored");
                continue;
            }

            Console.WriteLine($"[:] ({i + 1}/{count}) Extracting: {file.FileName}");

            if (verifyChecksum && IndexFile.Version <= 3100)
                verifyChecksum = false; // No checksum available

            reader.ExtractToFile(outputDirectory, file, verifyChecksum);
            reader.Dispose();

            i++;
        }

        Console.WriteLine("[/] Fully extracted!");
    }

    public EvoFileReader? GetFileReader(string file)
    {
        EvoIndexFileEntry entry = IndexFile.FindFile(file, 0);
        if (entry is null)
            return null;

        if (IndexFile.Version > 3100)
        {
            var datFile = GetDatFile((short)entry.DatIndex);
            if (datFile is null)
                return null;

            return new EvoFileReader(IndexFile, entry, datFile, file);
        }
        else
        {
            return new EvoFileReader(IndexFile, entry, MainDataFile, file);
        }
    }


    private EvoDataFile? GetDatFile(short index)
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
