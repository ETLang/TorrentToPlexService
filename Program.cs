using Microsoft.SqlServer.Server;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Policy;
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
        static string _TorrentDropFolder;
        static string _TorrentTrackerFolder;

        static string[] _VideoExtensions = new string[] { ".mp4", ".mpg", ".mpeg", ".mov", ".mkv", ".avi", ".webm" };
        static string[] _ZippedExtensions = new string[] { ".zip", ".7z", ".rar", ".tar.gz" };

        static string[] _SampleFilter = new string[] { @"sample", @"ref" };
        static string[] _ShowFilter = new string[] { @"season", @"s[0-9]+e[0-9]+" };

        static Regex _SampleRegex;
        static Regex _ShowRegex;

        static FileSystemWatcher _Watcher;
        static FileSystemWatcher _TorrentDropWatcher;

        static void Main(string[] args)
        {
            var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var myVideos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            _7zPath = Path.Combine(programFiles, "7-zip", "7z.exe");
            _FinishedTorrentFolder = Path.Combine(myDocuments, "TorrentsCompleted");
            _PlexMediaRoot = Path.Combine(myVideos, "PlexMedia");
            _MoviesFolder = Path.Combine(_PlexMediaRoot, "Movies");
            _ShowsFolder = Path.Combine(_PlexMediaRoot, "Shows");
            _MiscVideosFolder = Path.Combine(_PlexMediaRoot, "Other");
            _TempFolderBase = Path.Combine(_PlexMediaRoot, "Temp");
            _TorrentDropFolder = Path.Combine(myDocuments, "TorrentDrop");
            _TorrentTrackerFolder = Path.Combine(myDocuments, "TorrentFiles");

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

            Directory.CreateDirectory(_ShowsFolder);
            Directory.CreateDirectory(_MoviesFolder);

            // find Plex Library updater

            // Set up filter expressions
            _SampleRegex = new Regex($@"(?<![a-z])({string.Join("|", _SampleFilter)})(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _ShowRegex = new Regex($@"(?<![a-z0-9])({string.Join("|", _ShowFilter)})(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            _Watcher = new FileSystemWatcher(_FinishedTorrentFolder);
            _Watcher.EnableRaisingEvents = true;

            _TorrentDropWatcher = new FileSystemWatcher(_TorrentDropFolder);
            _TorrentDropWatcher.Created += (s,e) => ProcessTorrentFile(e.FullPath);
            _TorrentDropWatcher.EnableRaisingEvents = true;

            bool contentPublished = false;

            foreach (var torrentFile in Directory.GetFiles(_TorrentDropFolder, "*.torrent"))
                ProcessTorrentFile(torrentFile);

            foreach (var dir in Directory.GetDirectories(_FinishedTorrentFolder))
                if (ProcessTorrentFolder(dir))
                    contentPublished = true;

            foreach (var file in Directory.GetFiles(_FinishedTorrentFolder))
                if (ProcessTorrentContentFile(file))
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

        private static void ProcessTorrentFile(string path)
        {
            var ext = Path.GetExtension(path).ToLower();

            if(ext == ".torrent")
            {
                var destPath = Path.Combine(_TorrentTrackerFolder, Path.GetFileName(path));

                Log("New Torrent File Dropped:\n    " + path);
                if(File.Exists(destPath))
                {
                    Log("Torrent File Already Processed:\n    " + path);
                    return;
                }

                File.Move(path, destPath);

                Process.Start(destPath);
            }
        }

        //private static void OnTorrentRemoved(object sender, FileSystemEventArgs e)
        //{
        //    throw new NotImplementedException();
        //}

        private static void OnTorrentCompleted(string path)
        {
            Log("Torrent Completed - " + Path.GetFileName(path));
            if(Directory.Exists(path))
            {
                if(ProcessTorrentFolder(path))
                {
                    NotifyPlex();
                }
            }
            else
            {
                if(ProcessTorrentContentFile(path))
                {
                    NotifyPlex();
                }
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

        static bool ProcessTorrentFolder(string torrentFolder, bool isDefinitelyShow = false)
        {
            Log("Processing Torrent " + Path.GetFileName(torrentFolder));

            bool published = false;

            foreach (var file in Directory.GetFiles(torrentFolder, "*", SearchOption.AllDirectories))
            {
                if (ProcessTorrentContentFile(file, isDefinitelyShow))
                    published = true;
            }

            Log("Done Processing Torrent! " + (published ? "New Content Published!" : "No New Content"));
            return published;
        }

        static bool ProcessTorrentContentFile(string file, bool isDefinitelyShow = false)
        {
            bool published = false;

            if (IsSample(file))
            {
                Log("Skipping Sample File:\n    " + file);
                return false;
            }

            var ext = Path.GetExtension(file).ToLower();

            var isVideo = _VideoExtensions.Contains(ext);
            var isZip = _ZippedExtensions.Contains(ext);
            var isShow = isDefinitelyShow || IsShow(file);

            if (isVideo)
            {
                string destination;

                if (isShow)
                    destination = Path.Combine(_ShowsFolder, Path.GetFileName(file));
                else
                    destination = Path.Combine(_MoviesFolder, Path.GetFileName(file));

                var meta = destination + ".meta";

                if (File.Exists(meta))
                {
                    Log($"Skipping {(isShow ? "Show" : "Movie")} Already Published:\n    " + file);
                }
                else
                {
                    Log($"Publishing {(isShow ? "Show" : "Movie")}:\n    " + file);

                    File.Copy(file, destination, true);
                    File.WriteAllText(meta, ".");

                    published = true;
                }
            }

            if (isZip)
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

                if (ProcessTorrentFolder(tempFolder, isShow))
                    published = true;

                Directory.Delete(tempFolder, true);
            }

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
