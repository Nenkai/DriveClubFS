using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Buffers;

using CommandLine;
using CommandLine.Text;
namespace DriveClubFS
{
    public class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("DriveClubFS 0.1 - by Nenkai#9075");

            var p = Parser.Default.ParseArguments<UnpackAllVerbs, UnpackFileVerbs, ListFilesVerbs>(args);

            p.WithParsed<UnpackAllVerbs>(UnpackAll)
             .WithParsed<UnpackFileVerbs>(UnpackFile)
             .WithParsed<ListFilesVerbs>(ListFiles)
             .WithNotParsed(HandleNotParsedArgs);

        }

        public static void UnpackAll(UnpackAllVerbs options)
        {
            using var evoFs = new EvoFileSystem();
            evoFs.Init(options.InputPath);

            if (options.SkipChecksumVerify)
                Console.WriteLine("[!] Will not verify MD5 checksums!");

            evoFs.ExtractAll(options.OutputPath ?? "", !options.SkipChecksumVerify);
        }

        public static void UnpackFile(UnpackFileVerbs options)
        {
            using var evoFs = new EvoFileSystem();
            evoFs.Init(options.InputPath);
            evoFs.ExtractFile(options.FileToExtract, options.OutputPath ?? "");
        }

        public static void ListFiles(ListFilesVerbs options)
        {
            using var evoFs = new EvoFileSystem();
            evoFs.Init(options.InputPath);
            evoFs.ListFiles();
        }

        public static void HandleNotParsedArgs(IEnumerable<Error> errors)
        {
            ;
        }

        [Verb("unpackall", HelpText = "Unpacks all files from the Drive Club filesystem.")]
        public class UnpackAllVerbs
        {
            [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
            public string InputPath { get; set; }

            [Option('o', "output", HelpText = "Output folder.")]
            public string OutputPath { get; set; }

            [Option("skip-verifying-checksum", HelpText = "Skip verifying MD5 checksum. Speeds up process for slow CPUs. Not recommended.")]
            public bool SkipChecksumVerify { get; set; }

        }

        [Verb("unpackfile", HelpText = "Unpacks a specific file from the Drive Club filesystem.")]
        public class UnpackFileVerbs
        {
            [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
            public string InputPath { get; set; }

            [Option('f', "file", Required = true, HelpText = "File to extract. Example: crowd/crowd.rpk")]
            public string FileToExtract { get; set; }

            [Option('o', "output", HelpText = "Output folder.")]
            public string OutputPath { get; set; }
        }

        [Verb("listfiles", HelpText = "Lists files in the file system. Output will be 'files.txt'.")]
        public class ListFilesVerbs
        {
            [Option('i', "input", Required = true, HelpText = "Input folder where game.ndx is located.")]
            public string InputPath { get; set; }
        }
    }
}
