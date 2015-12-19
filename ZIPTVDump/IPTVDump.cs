using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Globalization;
using System.Reflection;
using System.Threading;
using LibVLCPlugin;

namespace ZIPTVDump
{
    class WebClientEx : WebClient
    {
        private string _strUsrName;
        private string _strPasswd;

        public WebClientEx(string strUserName, string strPassword)
        {
            _strUsrName = strUserName;
            _strPasswd = strPassword;
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            HttpWebRequest req = (HttpWebRequest)base.GetWebRequest(address);

            if (_strUsrName.Length > 0)
            {
                //Console.WriteLine("User:" + _strUsrName);
                req.Credentials = new NetworkCredential(_strUsrName, _strPasswd);
                string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(_strUsrName + ":" + _strPasswd));
                req.Headers[HttpRequestHeader.Authorization] = "Basic " + credentials;
            }
            req.ContentType = "application/binary";
            req.SendChunked = true;
            req.Timeout = Timeout.Infinite;
            req.AllowWriteStreamBuffering = false;
            req.KeepAlive = false;

            //int sizeMax = 100;
            //req.ContentLength = sizeMax <<= 20;
            //req.KeepAlive = false;
            //req.Connection = "close";

            return req;
        }
    }

    class IPTVDump
    {
        private string _Mrl;
        private int _iSecondsStop;
        private ushort _lastProgress = 4;
        private Int64 _sizeMax;
        private Int64 _lastDumpBytes;
        private DateTime _startUpdate;
        private DateTime _lastUpdate;

        private static readonly bool _bIsFileLog = true;
        private static bool _bIsEnableLog = true;

        static IPTVDump()
        {
            //Console.CursorVisible = false;
#if false
            try
            {
                int iWidth = Console.BufferWidth;
                _bIsFileLog = false;
            }
            catch
            {
            }
#endif // false

            _bIsFileLog = Console.IsOutputRedirected;
            _bIsEnableLog = !_bIsFileLog;
        }

        static int Main(string[] args)
        {
            ShowVersion();

            if (1 > args.Length || 5 < args.Length)
            {
                return ShowUsage();
            }

            if (_bIsEnableLog)
            {
                Console.WriteLine("Press any key to exit...");
                Console.WriteLine();
            }

            int ndx=0;
            string strMrl = args[ndx++];
            string strFilePath = (args.Length > ndx)?args[ndx++]:string.Empty;
            string strSize = (args.Length > ndx) ? args[ndx++] : string.Empty;

            IPAddress mifaceIP = IPAddress.None;
            if (args.Length > ndx)
            {
                string strStr = args[ndx++];
                if (strStr.Length > 0)
                {
                    mifaceIP = IPAddress.Parse(strStr);
                }
            }

            string strCaUri = "http://localhost:7781/ca/";// = string.Empty;
            if (args.Length > ndx)
            {
                strCaUri = args[ndx++];
            }

            DateTime stopDump = new DateTime(0);
            ndx = strSize.IndexOf(':');
            if (ndx >= 0)
            {
                string strTime = strSize.Substring(ndx + 1);
                strSize = strSize.Substring(0, ndx);
                if (strTime.Length > 0 && '.' == strTime[0])
                {
                    stopDump = DateTime.Now.AddMinutes(double.Parse(strTime.Substring(1)));
                }
                else
                {
                    stopDump = DateTime.Parse(strTime);
                }
            }

            CultureInfo ci = CultureInfo.InvariantCulture;//new CultureInfo("en-US");
            Int64 sizeMax=0;
            if (strSize.Length > 0)
            {
                if (strSize.EndsWith("G", true, ci))
                {
                    sizeMax = Int64.Parse(strSize.TrimEnd(new Char[] { 'G', 'g' }));
                    sizeMax <<= 30;
                }
                else
                {
                    sizeMax = Int64.Parse(strSize);
                    sizeMax <<= 20;
                }
            }

            bool bFileStdOutput = false;
            if (_bIsFileLog)
            {
                _bIsEnableLog = !(bFileStdOutput = (0 == strFilePath.Length || "-" == strFilePath));
            }
            else if (0 == strFilePath.Length)
            {
                Uri uri = new Uri(strMrl);
                strFilePath = uri.Segments[uri.Segments.Length-1];
            }

            LibVLCModule moduleVLC=null;
            WebClient httpClientSrc = null;
            WebClient httpClientDst = null;

            int iExitCode = 0;

            try
            {
                string strFileName = strMrl;
                ndx = strMrl.IndexOf('?');
                if(ndx > 0)
                {
                    strFileName = strMrl.Substring(0, ndx);
                }
                if (strFileName.EndsWith(".m3u"))
                {
                    WebClient httpClient = new WebClient();
                    httpClient.Headers.Add(HttpRequestHeader.UserAgent, GetUserAgentString());

                    using (StreamReader readerM3U = new StreamReader(httpClient.OpenRead(strMrl)))
                    {
                        string strLine;
                        while (null != (strLine = readerM3U.ReadLine()) && !strLine.StartsWith("#EXTINF:")) ;
                        if (null != strLine && null != (strLine = readerM3U.ReadLine()))
                        {
                            strMrl = strLine;

                            if (strFilePath.EndsWith(".m3u"))
                            {
                                strFilePath = strFilePath.Substring(0, strFilePath.Length - 4) + ".ts";
                            }
                        }
                    }

                }

                if (strMrl.StartsWith("udp:", true, ci) || strMrl.StartsWith("ca:", true, ci))
                {
                    if (bFileStdOutput)
                    {
                        throw new Exception("Dump not supported for standard output, use http source.");
                    }

                    if (strFilePath.StartsWith(Uri.UriSchemeHttp) || strFilePath.StartsWith(Uri.UriSchemeHttps))
                    {
                        throw new Exception("Dump not supported from udp source to http destination.");
                    }

                    using (Socket sockFake = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                    {
                        moduleVLC = new LibVLCModule(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), strCaUri, mifaceIP);
                        Thread.Sleep(4000);
                        //Console.WriteLine("Press any key to continue...");
                        //Console.ReadKey();
                    }
                }
                else
                {
                    httpClientSrc = new WebClient();
                    httpClientSrc.Headers.Add(HttpRequestHeader.UserAgent, GetUserAgentString());

                    //Console.WriteLine(strFilePath);
                    if (strFilePath.StartsWith(Uri.UriSchemeHttp) || strFilePath.StartsWith(Uri.UriSchemeHttps))
                    {
                        Uri uri = new Uri(strFilePath);
                        string[] auth = new string[] { string.Empty, string.Empty };
                        if (null != uri.UserInfo)
                        {
                            auth = uri.UserInfo.Split(new char[] { ':' });
                        }
                        httpClientDst = new WebClientEx(auth[0], auth[1]);
                        httpClientDst.Headers.Add(HttpRequestHeader.UserAgent, GetUserAgentString());
                    }
                }

                byte[] buffer = new byte[0x4000];
                using (
                    Stream streamSrc = (null != httpClientSrc) ? httpClientSrc.OpenRead(strMrl) : moduleVLC.NewAccess(strMrl),
                    streamDst = (null != httpClientDst)
                        ? httpClientDst.OpenWrite(strFilePath, "PUT")
                            : (bFileStdOutput) ? Console.OpenStandardOutput() : File.Open(strFilePath, FileMode.OpenOrCreate)
                    )
                {
                    IPTVDump dump = new IPTVDump(strMrl, stopDump, sizeMax);
                    Int64 sizeDumped = 0;
                    int iLength;
                    while ((iLength = streamSrc.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        streamDst.Write(buffer, 0, iLength);
                        sizeDumped += iLength;

                        bool bIsLast = !dump.IsContinue(sizeDumped);
                        dump.WriteLogMessage(sizeDumped, bIsLast);

                        if (Console.KeyAvailable || bIsLast)
                        {
                            break;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Console.Error.WriteLine("Dump failed, error: {0}", e.Message);
                iExitCode = 1;
            }

            if (null != moduleVLC)
            {
                moduleVLC.LibVLCRelease();
            }

            return iExitCode;
        }

        protected IPTVDump(string strmrl, DateTime stopTime, Int64 sizeMax)
        {
            _Mrl = strmrl;
            _sizeMax = sizeMax;
            _startUpdate = DateTime.Now;
            _lastUpdate = _startUpdate;
            _iSecondsStop = (int)Math.Max(0, (stopTime - _startUpdate).TotalSeconds);

            if (_bIsEnableLog)
            {
                Console.WriteLine();
                Console.WriteLine("File size limit: {0}.", (0 == _sizeMax) ? "unknown" : GetSizeString(_sizeMax));
                Console.WriteLine("Time to stop: {0}.", (_startUpdate < stopTime) ? stopTime.ToString() : "unknown");
                Console.WriteLine("{0} is successfully opened.", strmrl);
            }
        }

        private bool IsContinue(Int64 sizeDumped)
        {
            return (0 == _sizeMax || sizeDumped < _sizeMax) && (0 == _iSecondsStop || _lastUpdate < _startUpdate.AddSeconds(_iSecondsStop));
        }

        private void WriteLogMessage(Int64 sizeDumped, bool bIsLast)
        {
            DateTime timeNow = DateTime.Now;
            if (bIsLast || _lastUpdate.AddSeconds(1) < timeNow)
            {
                int seconds = (int)Math.Max(0, (timeNow - _startUpdate).TotalSeconds);
                ushort uProgress = 100;
                if (_sizeMax > 0 || (seconds > 0 && _iSecondsStop > seconds))
                {
                    uProgress = (ushort)((_sizeMax > 0) ? ((sizeDumped * 100) / _sizeMax) : ((seconds * 100) / _iSecondsStop));
                }
                bool bNextProgress = (bIsLast || uProgress > _lastProgress);
                ulong mseconds = Math.Max(1, (ulong)(timeNow - ((bNextProgress) ? _startUpdate : _lastUpdate)).TotalMilliseconds);
                ulong deltaBytes = (ulong)((bNextProgress) ? sizeDumped : (sizeDumped - _lastDumpBytes));

                string message = string.Format(
                                        "{0} {1}> Dump {2} at {3} Kb/s, progress: {4}%",
                                        timeNow.ToLocalTime().ToShortDateString(),
                                        timeNow.ToLocalTime().ToShortTimeString(),
                                        GetSizeString(sizeDumped),
                                        ((deltaBytes >> 10) * 1000) / mseconds,
                                        uProgress
                                        ).PadRight(75);

                if (bIsLast || (uProgress < 100 && uProgress > _lastProgress))
                {
                    if (_bIsEnableLog)
                    {
                        Console.WriteLine(message);
                    }
                    _lastProgress = (ushort)(uProgress + 4);
                }
                else if (!_bIsFileLog)
                {
                    Console.Write(message);
                    Console.CursorLeft = 0;
                }

                _lastUpdate = timeNow;
                _lastDumpBytes = sizeDumped;
            }
        }

        private static string GetUserAgentString()
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            return ("Mozilla/5.0 (" + Environment.OSVersion + ") " + assemName.Name + "/" + assemName.Version.ToString(4));
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

        private static void ShowVersion()
        {
            if (_bIsEnableLog)
            {
                Assembly assem = Assembly.GetExecutingAssembly();
                AssemblyName assemName = assem.GetName();

                Console.WriteLine("{0} version {1}", assemName.Name, assemName.Version.ToString(4));
                Console.WriteLine("Dumper IPTV traffic to video file.");
                Console.WriteLine();
            }
        }

        private static int ShowUsage()
        {
            Console.WriteLine(
                "Usage: {0} mrl [file|-] [size[G][:[.]time]|:[.]time] [mifaceIP] [caURI]",
                Path.GetFileName(Assembly.GetExecutingAssembly().Location));

            return 1;
        }
    }
}
