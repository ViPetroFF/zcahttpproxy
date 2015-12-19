using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Globalization;

using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;


namespace GDiskLoader
{
    public static class ConsoleEx
    {
        public static bool IsOutputRedirected
        {
            get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdout)); }
        }
        public static bool IsInputRedirected
        {
            get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stdin)); }
        }
        public static bool IsErrorRedirected
        {
            get { return FileType.Char != GetFileType(GetStdHandle(StdHandle.Stderr)); }
        }

        // P/Invoke:
        private enum FileType { Unknown, Disk, Char, Pipe };
        private enum StdHandle { Stdin = -10, Stdout = -11, Stderr = -12 };
        [DllImport("kernel32.dll")]
        private static extern FileType GetFileType(IntPtr hdl);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(StdHandle std);
    }

    class GoogleDiskLoader
    {
        private static readonly bool _bIsFileLog = true;

        private ushort _lastProgress = 4;
        private ushort _deltaProgress = 4;
        private Int64 _sizeMax;
        private Int64 _lastSentBytes;
        private DateTime _startUpdate;
        private DateTime _lastUpdate;

        static GoogleDiskLoader()
        {
            _bIsFileLog = ConsoleEx.IsOutputRedirected;
        }

        static void Main(string[] args)
        {
            UserCredential credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = "",
                    ClientSecret = "",
                },
                new[] { DriveService.Scope.Drive },
                "user",
                CancellationToken.None,
                new FileDataStore("Drive.Auth.Store")).Result;

            // Create the service.
            var service = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GDiskLoader",
            });

            if (args.Length > 0)
            {
                int ndx = 0;
                string strFileName = args[ndx++];

                using (System.IO.Stream stream = Console.OpenStandardInput())
                {
                    File body = new File();
                    body.Title = strFileName;
                    body.OriginalFilename = strFileName;
                    //body.Title = "My document";
                    //body.Description = "A test document";
                    body.MimeType = "application/binary"; //"video/x-mpegts";

                    const int KB = 0x400;
                    var minimumChunkSize = 256 * KB;

                    FilesResource.InsertMediaUpload request = service.Files.Insert(body, stream, "application/binary");//"video/x-mpegts");
                    request.ChunkSize = minimumChunkSize * 4;
                    GoogleDiskLoader UpLoader = new GoogleDiskLoader();
                    request.ProgressChanged += UpLoader.WriteLogMessage;
                    request.Upload();

                    File file = request.ResponseBody;
                    Console.WriteLine("File id: " + file.Id);
                    Console.WriteLine("File size: " + file.FileSize);
                    //Console.WriteLine("Press Enter to end this process.");
                    //Console.ReadLine();
                }
            }
        }

        private static void ProgressChanged(Google.Apis.Upload.IUploadProgress obj)
        {
            Console.WriteLine("Bytes sent: {0}, Status: {1}, Exception: {2}", obj.BytesSent, obj.Status, obj.Exception);
        }

        public void WriteLogMessage(Google.Apis.Upload.IUploadProgress obj)
        {
            bool bIsLast = (Google.Apis.Upload.UploadStatus.Completed == obj.Status);
            Int64 sizeLoaded = obj.BytesSent;
            DateTime timeNow = DateTime.Now;

            if (sizeLoaded > _sizeMax)
            {
                _sizeMax <<= 1;
                bIsLast = true;
                if (_lastProgress > 1)
                {
                    _lastProgress--;
                    _deltaProgress--;
                }
            }

            if (Google.Apis.Upload.UploadStatus.Starting == obj.Status)
            {
                _startUpdate = _lastUpdate = timeNow;
                _sizeMax = 256 * 1024 * 1024;
            }
            else if (bIsLast || Google.Apis.Upload.UploadStatus.Uploading == obj.Status)
            {
                int seconds = (int)Math.Max(0, (timeNow - _startUpdate).TotalSeconds);
                ushort uProgress = (ushort)((sizeLoaded * 100) / _sizeMax);
                bool bNextProgress = (bIsLast || uProgress > _lastProgress);
                ulong mseconds = Math.Max(1, (ulong)(timeNow - ((bNextProgress) ? _startUpdate : _lastUpdate)).TotalMilliseconds);
                ulong deltaBytes = (ulong)((bNextProgress) ? sizeLoaded : (sizeLoaded - _lastSentBytes));

                string message = string.Format(
                                        "{0} {1}> Upload {2} at {3} Kb/s, progress: {4}%",
                                        timeNow.ToLocalTime().ToShortDateString(),
                                        timeNow.ToLocalTime().ToShortTimeString(),
                                        GetSizeString(sizeLoaded),
                                        ((deltaBytes >> 10) * 1000) / mseconds,
                                        uProgress
                                        ).PadRight(75);

                if (bIsLast || (uProgress < 100 && uProgress > _lastProgress))
                {
                    Console.WriteLine(message);
                    _lastProgress = (ushort)(uProgress + _deltaProgress);
                }
                else if (!_bIsFileLog)
                {
                    Console.Write(message);
                    Console.CursorLeft = 0;
                }

                _lastUpdate = timeNow;
                _lastSentBytes = sizeLoaded;
            }
        }

        private static string GetSizeString(Int64 size)
        {
            if (size < 1024)
                return size.ToString() + " bytes";
            else if (size < 1024 * 1024)
                return (size / 1024).ToString() + " Kb";
            else if (size < 1024 * 1024 * 1024)
                return (size / (1024 * 1024)).ToString() + " Mb";
            else
                return ((double)size / (1024 * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture.NumberFormat) + " Gb";
        }
    }
}
