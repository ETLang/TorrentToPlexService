using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TorrentToPlexService
{
    internal class Program
    {
        static string _7zPath;
        static string _FinishedTorrentFolder;
        static string _PlexMediaRoot;
        static string _MoviesFolder;
        static string _ShowsFolder;
        static string _MiscVideosFolder;
        static string _PlexLibraryUpdateUrl;
        static string _TempFolderBase;

        static string[] _VideoExtensions = new string[] { ".mp4", ".mpg", ".mpeg", ".mov", ".mkv", ".avi", ".webm" };
        static string[] _ZippedExtensions = new string[] { ".zip", ".7z", ".rar", ".tar.gz" };

        static string[] _SampleFilter = new string[] { @"sample", @"ref" };
        static string[] _ShowFilter = new string[] { @"season", @"s[0-9]+e[0-9]+" };

        static Regex _SampleRegex;
        static Regex _ShowRegex;

        static FileSystemWatcher _Watcher;

        static void Main(string[] args)
        {
            var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            _7zPath = Path.Combine(programFiles, "7-zip", "7z.exe");
            _FinishedTorrentFolder = @"C:\TorrentsCompleted"; // Path.Combine(myDocuments, "TorrentsCompleted");
            _PlexMediaRoot = Path.Combine(myVideos, "PlexMedia");
            _MoviesFolder = Path.Combine(_PlexMediaRoot, "Movies");
            _ShowsFolder = Path.Combine(_PlexMediaRoot, "Shows");
            _MiscVideosFolder = Path.Combine(_PlexMediaRoot, "Other");
            _TempFolderBase = Path.Combine(_PlexMediaRoot, "Temp");


            if(!File.Exists(_7zPath))
            {
                Error("Can't find 7z.exe");
                return;
            }

            if (!Directory.Exists(_FinishedTorrentFolder))
            {
                Error("Can't Find completed torrents folder:\n" + _FinishedTorrentFolder);
                return;
            }

            // find Plex Library updater

            // Set up filter expressions
            _SampleRegex = new Regex($@"(?<![a-z])({string.Join("|", _SampleFilter)})(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _ShowRegex = new Regex($@"(?<![a-z0-9])({string.Join("|", _ShowFilter)})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            _Watcher = new FileSystemWatcher(_FinishedTorrentFolder);

            _Watcher.EnableRaisingEvents = true;
            //_Watcher.Created += (s,e) => OnTorrentCompleted(e.FullPath);
            //_Watcher.Deleted += OnTorrentRemoved;

            bool contentPublished = false;

            foreach (var dir in Directory.GetDirectories(_FinishedTorrentFolder))
                if (ProcessTorrent(dir))
                    contentPublished = true;

            if(contentPublished)
            {
                NotifyPlex();
            }

            while (true)
            {
                var result = _Watcher.WaitForChanged(WatcherChangeTypes.Created);

                OnTorrentCompleted(Path.Combine(_Watcher.Path, result.Name));
            }
        }

        //private static void OnTorrentRemoved(object sender, FileSystemEventArgs e)
        //{
        //    throw new NotImplementedException();
        //}

        private static void OnTorrentCompleted(string path)
        {
            if(Directory.Exists(path))
            {
                Log("Torrent Completed - " + Path.GetFileName(path));
                if(ProcessTorrent(path))
                {
                    NotifyPlex();
                }
            }
            else
            {
                Log("File created in torrent folder:\n    " + path);
            }
        }

        static bool IsSample(string file) => _SampleRegex.IsMatch(file);
        static bool IsShow(string file) => _ShowRegex.IsMatch(file);

        static void NotifyPlex()
        {
        }

        static string CreateTempFolder()
        {
            int i = 0;
            while(Directory.Exists(_TempFolderBase + i.ToString()))
            {
                i++;
            }

            var output = _TempFolderBase + i.ToString();
            Directory.CreateDirectory(output);
            return output;
        }

        static bool ProcessTorrent(string torrentFolder, bool isDefinitelyShow = false)
        {
            Log("Processing Torrent " + Path.GetFileName(torrentFolder));

            bool published = false;

            foreach (var file in Directory.GetFiles(torrentFolder, "*", SearchOption.AllDirectories))
            {
                if (IsSample(file))
                {
                    Log("Skipping Sample File:\n    " + file);
                    continue;
                }

                var ext = Path.GetExtension(file).ToLower();

                var isVideo = _VideoExtensions.Contains(ext);
                var isZip = _ZippedExtensions.Contains(ext);
                var isShow = isDefinitelyShow || IsShow(file);

                if(isVideo)
                {
                    string destination;

                    if(isShow)
                        destination = Path.Combine(_ShowsFolder, Path.GetFileName(file));
                    else
                        destination = Path.Combine(_MoviesFolder, Path.GetFileName(file));

                    if (File.Exists(destination))
                    {
                        Log($"Skipping {(isShow ? "Show" : "Movie")} Already Published:\n    " + file);
                    }
                    else
                    {
                        Log($"Publishing {(isShow ? "Show" : "Movie")}:\n    " + file);

                        Directory.CreateDirectory(_ShowsFolder);
                        Directory.CreateDirectory(_MoviesFolder);

                        File.Copy(file, destination);

                        published = true;
                    }
                }
                
                if(isZip)
                {
                    var tempFolder = CreateTempFolder();

                    var pi = new ProcessStartInfo
                    {
                        //RedirectStandardError = true,
                        //RedirectStandardInput = true,
                        //RedirectStandardOutput = true,
                        FileName = _7zPath,
                        Arguments = $"x \"{file}\" -o\"{tempFolder}\"",
                        UseShellExecute = false
                    };

                    Log("Unzipping:\n    " + file);
                    Log("Destination:\n    " + tempFolder);
                    var unzipProcess = Process.Start(pi);
                    unzipProcess.WaitForExit();

                    if (ProcessTorrent(tempFolder, isShow))
                        published = true;

                    Directory.Delete(tempFolder, true);
                }
            }

            Log("Done Processing Torrent! " + (published ? "New Content Published!" : "No New Content"));
            return published;
        }

        static void Error(string message)
        {
            Console.WriteLine("ERROR: " + message);
        }

        static void Log(string message)
        {
            Console.WriteLine("INFO: " + message);
        }
    }
}
