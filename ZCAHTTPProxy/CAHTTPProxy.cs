using System;
using System.Collections;
using System.Text;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Diagnostics;
using System.ServiceProcess;
using System.Runtime.InteropServices;

//using Microsoft.VisualC.StlClr;
//using IWshRuntimeLibrary;

//using System.Security.Permissions;
//using System.Globalization;

using LibVLCPlugin;

namespace ZCAHTTPProxy
{
    public interface ITcpServer
    {
        int AddNewSocket(Socket sock, TcpListener listener);
        void DeleteSocket(Socket sock, TcpListener listener, int iIndex);
    }

    public interface ICAModule
    {
        LibVLCModule ModuleVLC
        {
            get;
        }

        IThreadPool LowThreadPool
        {
            get;
        }

        string MacAddress
        {
            get;
            set;
        }

        string HostName
        {
            get;
        }

        string DnsHostName
        {
            get;
        }

        string CABodyTopics
        {
            get;
        }

        ProxyChannelsWriterAbstract.ChannelsListCache CABodyCache
        {
            get;
        }

        DateTime CABodyCacheUpdateTime
        {
            get;
        }

        bool IsHostAddress(IPAddress addr);
    }

    public interface IStreamProxyServer
    {
        int AddNewStream(IStreamProxy streamProxy);
        void DeleteStream(IStreamProxy streamProxy, int iIndex);
        void KeepStream(IStreamProxy streamProxy, int iIndex);
        IStreamProxy TakePostedStream(int iIndex);
        bool IsExistPostedStream(string mrl, out int iIndex);
    }

    class CAHTTPProxy : ServiceBase, IStreamProxyServer, ICAModule, ITcpServer
    {
        public class ProcessTuneSection : ConfigurationSection
        {
            public enum PriorityLevel
            {
                Low = 0,
                High = 1,
            }

            public const string SECTION_NAME = "ProcessTune";
            private static readonly ProcessTuneSection s_section = (ProcessTuneSection)ConfigurationManager.GetSection(SECTION_NAME);

            public static ProcessTuneSection Section
            {
                get
                {
                    if (null == s_section)
                    {
                        //s_section = (CAAuthData)ConfigurationManager.GetSection(SECTION_NAME);
                        //Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                        //Log.Message(config.FilePath);
                        //s_section = (CAAuthData)config.Sections[SECTION_NAME];

                        // Get the sections in the machine.config. 
                        //foreach (ConfigurationSection section in config.Sections)
                        //{
                        //string name = section.SectionInformation.Name;
                        //Console.WriteLine("Name: {0}", name);
                        //}


                        //if (s_section == null)
                        //{
                        throw new ConfigurationErrorsException("The <" + SECTION_NAME +
                              "> section is not defined in your .config file!");
                        //}
                    }

                    return s_section;
                }
            }

            public static ProcessTuneSection GetSection()
            {
                //Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                //Log.Message(config.FilePath);

                return s_section;
            }

            public ProcessTuneSection()
            {
            }

            public ProcessTuneSection(PriorityLevel Priority)
            {
                HighPriority = (PriorityLevel.High == Priority);
                UseAuxLowPriorityThreadPool = (PriorityLevel.Low == Priority);

                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                //Console.WriteLine(config.FilePath);
                // Add configuration information to the
                // configuration file.
                if (null != config.Sections.Get(SECTION_NAME))
                {
                    config.Sections.Remove(SECTION_NAME);
                }
                config.Sections.Add(SECTION_NAME, this);
                config.Save();
                // Force a reload of the changed section.
                // This makes the new values available for reading.
                //ConfigurationManager.RefreshSection(sectionName);
            }

            [ConfigurationProperty("HighPriority", IsRequired = true)]
            public bool HighPriority
            {
                get { return (bool)this["HighPriority"]; }
                set { this["HighPriority"] = value; }

            }

            [ConfigurationProperty("UseAuxLowPriorityThreadPool", DefaultValue = (bool)false, IsRequired = false)]
            public bool UseAuxLowPriorityThreadPool
            {
                get { return (bool)this["UseAuxLowPriorityThreadPool"]; }
                set { this["UseAuxLowPriorityThreadPool"] = value; }
            }
        }

        [DllImport("Kernel32.dll")]
        public static extern int SetStdHandle(int device, IntPtr handle); 

        private const int DEFAULT_PORT = 7781;

        private static string _strMacAddress;
        private static FileStream _streamRedirect;
        private static StreamWriter _writerRedirect;

        private readonly string _strHostName;
        private readonly string _strDnsHostName;
        private readonly IPAddress _Address = IPAddress.Any;
        private readonly IPAddress[] _HostAddresses;
        private readonly int _iPort;

        private TcpListener _listener;
        //private TcpListener _listenerCA;
        private LibVLCModule _moduleVLC;
        private Socket[] _connections = new Socket[32];
        private int _nConnections;
        private int _iPostedStreamID = -1;
        private DateTime _timePostedStream;
        private IStreamProxy[] _proxy = new IStreamProxy[16];
        private EventWaitHandle _noneConnections = new EventWaitHandle(true, EventResetMode.ManualReset);
        private string _CABodyTopics;
        private ProxyChannelsWriterAbstract.ChannelsListCache _CACache;

        private LowPriorityThreadPool _lowThreadPool;
        //private IAsyncResult _AcceptResult;

        static CAHTTPProxy()
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));

            if (!ConfigurationManager.AppSettings.HasKeys())
            {
                Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                config.AppSettings.Settings.Add("InternalIP", IPAddress.Any.ToString());
                config.AppSettings.Settings.Add("InternalPort", DEFAULT_PORT.ToString());
                config.AppSettings.Settings.Add("mifaceIP", IPAddress.None.ToString());
                config.Save();
            }

            Guid guidMac = Guid.NewGuid();
            Object[] arrMac = new Object[6];
            Array.Copy(guidMac.ToByteArray(), arrMac, arrMac.Length);
            _strMacAddress = string.Format("{0:x2}:{1:x2}:{2:x2}:{3:x2}:{4:x2}:{5:x2}", arrMac);
        }

        public CAHTTPProxy(string strIP)
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            this.ServiceName = assemName.Name;
            //this.ServiceName = "ZCAHTTPProxy";
            this.CanStop = true;
            //this.CanPauseAndContinue = true;
            //this.AutoLog = true;

            string strIPOnly = strIP;
            int iPort = DEFAULT_PORT;

            if (strIP.Length > 0)
            {
                int ndx = strIP.IndexOf(':');
                if (ndx >= 0)
                {
                    strIPOnly = strIP.Substring(0, ndx);
                    iPort = int.Parse(strIP.Substring(ndx + 1));
                }
            }
            else
            {
                string strAppParam;
                if (null != (strAppParam = ConfigurationManager.AppSettings["InternalIP"]))
                {
                    strIPOnly = strAppParam;
                }

                if (null != (strAppParam = ConfigurationManager.AppSettings["InternalPort"]))
                {
                    iPort = int.Parse(strAppParam);
                }
            }

            string strHostName = System.Net.Dns.GetHostName();

            _HostAddresses = System.Net.Dns.GetHostAddresses(strHostName);

            _strHostName = strHostName;
            if (strIPOnly.Length > 0 && !(_Address = IPAddress.Parse(strIPOnly)).Equals(IPAddress.Any) && !_Address.Equals(IPAddress.None))
            {
                IPHostEntry ipDnsHostName = System.Net.Dns.GetHostEntry(strIPOnly);
                _strDnsHostName = ipDnsHostName.HostName;
            }
            else
            {
                _strDnsHostName = strHostName;
            }

            _iPort = iPort;
        }

        static int Main(string[] args)
        {
            ShowVersion();

            if (3 < args.Length)
            {
                return ShowUsage();
            }

            string strServerIP = string.Empty;
            if (0 == args.Length)
            {
                ShowUsage();
            }
            else
            {
                strServerIP = args[0];
            }

            if ("/ca-setup" == strServerIP)
            {
                string strIPAddr = string.Empty;
                if (args.Length > 1)
                {
                    strIPAddr = args[1];
                }

                if (strIPAddr.Length > 0)
                {
                    if ('*' == strIPAddr[0])
                    {
                        if (1 == strIPAddr.Length)
                        {
                            strIPAddr += ":";
                            strIPAddr += CAHTTPProxy.DEFAULT_PORT.ToString();
                        }

                        CAHTTPProxy proxy = new CAHTTPProxy(string.Empty);
                        strIPAddr = strIPAddr.Replace("*", proxy._strDnsHostName);
                    }
                }
                else
                {
                    strIPAddr = string.Format("localhost:{0}", CAHTTPProxy.DEFAULT_PORT);
                }

                string strMacAddr = string.Empty;
                if (args.Length > 2)
                {
                    strMacAddr = args[2];
                }

                string strUDevNum = Console.ReadLine();
                string strUDevPin = Console.ReadLine();
                if ("*" == strMacAddr)
                {
                    strMacAddr = Console.ReadLine();
                }

                new CAWebClient.CAAuthData(strUDevNum, strUDevPin, string.Format("http://{0}/ca/", strIPAddr), strMacAddr);
            }
            else if ("/m3u-setup" == strServerIP)
            {
                TextReader iniReader = Console.In;
                if (args.Length > 1)
                {
                    iniReader = new StreamReader(File.Open(args[1], FileMode.Open), Encoding.Default);
                }

                string strRndMacAddress = _strMacAddress;
                CAHTTPProxy proxy = new CAHTTPProxy(string.Empty);

                string strAppParam;
                IPAddress mifaceIP = IPAddress.None;
                if (null != (strAppParam = ConfigurationManager.AppSettings["mifaceIP"]))
                {
                    mifaceIP = IPAddress.Parse(strAppParam);
                }

                proxy.Start(mifaceIP);

                int ndx = 0x100;
                while (strRndMacAddress == proxy.MacAddress && ndx-- > 0)
                {
                    proxy.Loop(80);
                }

                CAWebClient client = new CAWebClient(proxy.MacAddress);
                string xmlCA = client.CAXmlString;

                TVGMapConfigWriter writerTVG = new TVGMapConfigWriter(iniReader, xmlCA);
                int iCount = writerTVG.WriteCA(Stream.Null);
                Log.Message(string.Format("{0} tvg names successfully added to configuration.", iCount));

                proxy.Stop();
            }
            else if ("/stream-setup" == strServerIP)
            {
                string strclass;
                if (args.Length > 1)
                {
                    switch (args[1])
                    {
                        case StreamProxy.CLASS_NAME:
                            strclass = StreamProxy.Name;
                            break;
                        case MemoryCacheClass16.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass16>.Name;
                            break;
                        case MemoryCacheClass32.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass32>.Name;
                            break;
                        case MemoryCacheClass64.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass64>.Name;
                            break;
                        case MemoryCacheClass128.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass128>.Name;
                            break;
                        case MemoryCacheClass256.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass256>.Name;
                            break;
                        case MemoryCacheClass512.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass512>.Name;
                            break;
                        case MemoryCacheClass1024.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass1024>.Name;
                            break;
                        case MemoryCacheClass2048.CLASS_NAME:
                            strclass = StreamProxyEx<MemoryCacheClass2048>.Name;
                            break;

                        default:
                            strclass = string.Empty;
                            break;
                    }
                }
                else
                {
                    strclass = StreamProxyEx<MemoryCacheClass32>.Name;
                }

                if (strclass.Length > 0)
                {
                    new ProxyStreamConfig(strclass);
                }
                else
                {
                    Log.Message(string.Format("Proxy stream {0} not found.", args[1]));
                }
            }
            else if ("/process-setup" == strServerIP)
            {
                ProcessTuneSection.PriorityLevel? prPriority = ProcessTuneSection.PriorityLevel.High;
                if (args.Length > 1)
                {
                    if (0 == string.Compare(ProcessTuneSection.PriorityLevel.Low.ToString(), args[1], true))
                    {
                        prPriority = ProcessTuneSection.PriorityLevel.Low;
                    }
                    else if (0 != string.Compare(ProcessTuneSection.PriorityLevel.High.ToString(), args[1], true))
                    {
                        prPriority = null;
                    }
                }

                if (prPriority.HasValue)
                {
                    new ProcessTuneSection(prPriority.Value);
                }
                else
                {
                    Log.Message(string.Format("Unknown process tune setting {0}, allowed only \"low\" or \"high\".", args[1]));
                }
            }
            else if ("/service-install" == strServerIP)
            {
                System.Configuration.Install.TransactedInstaller ti = null;
                ti = new System.Configuration.Install.TransactedInstaller();
                ti.Installers.Add(new ProjectInstaller());
                ti.Context = new System.Configuration.Install.InstallContext("", null);
                string path = Assembly.GetExecutingAssembly().Location;
                ti.Context.Parameters["assemblypath"] = path;
                ti.Install(new Hashtable());
            }
            else if ("/service-uninstall" == strServerIP)
            {
                System.Configuration.Install.TransactedInstaller ti = null;
                ti = new System.Configuration.Install.TransactedInstaller();
                ti.Installers.Add(new ProjectInstaller());
                ti.Context = new System.Configuration.Install.InstallContext("", null);
                string path = Assembly.GetExecutingAssembly().Location;
                ti.Context.Parameters["assemblypath"] = path;
                ti.Uninstall(null);
            }
            else if ("/service-run" == strServerIP)
            {
                RedirectStd();
                System.ServiceProcess.ServiceBase.Run(new CAHTTPProxy(string.Empty));
            }
            else
            {
                string strAppParam;
                IPAddress mifaceIP = IPAddress.None;
                if (args.Length > 1)
                {
                    mifaceIP = IPAddress.Parse(args[1]);
                }
                else if (null != (strAppParam = ConfigurationManager.AppSettings["mifaceIP"]))
                {
                    mifaceIP = IPAddress.Parse(strAppParam);
                }

                CAHTTPProxy CAHTTPProxy = new CAHTTPProxy(strServerIP);
                CAHTTPProxy.Start(mifaceIP);

                while (!Console.KeyAvailable)
                {
                    CAHTTPProxy.Loop(125);
                }

                CAHTTPProxy.Stop();
            }

            return 0;
        }

        private static void ShowVersion()
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            Console.WriteLine("{0} version {1}", assemName.Name, assemName.Version.ToString(4));
            Console.WriteLine("Multicast UDP to HTTP proxy with Interzet CA descrambler.");
            Console.WriteLine("This software developed by Viktor PetroFF.");
            Console.WriteLine();
        }

        private static int ShowUsage()
        {
            Console.WriteLine("Usage: {0} [internalIP[:Port]] [mifaceIP]", Path.GetFileName(Assembly.GetExecutingAssembly().Location));

            return 1;
        }

        private static void RedirectStd()
        {
            if (File.Exists("log.txt"))
            {
                File.Delete("old.log.txt");
                File.Move("log.txt", "old.log.txt");
            }

            _streamRedirect = new FileStream("log.txt", FileMode.Create);
            _writerRedirect = new StreamWriter(_streamRedirect);
            _writerRedirect.AutoFlush = true;
            Console.SetOut(_writerRedirect);
            Console.SetError(_writerRedirect);
#if FALSE
            int status;
            IntPtr handle = _streamRedirect.SafeFileHandle.DangerousGetHandle();
            status = SetStdHandle(-11, handle); // set stdout
            // Check status as needed
            //Console.WriteLine("status stdout = {0}", status);
            status = SetStdHandle(-12, handle); // set stderr
            // Check status as needed
            //Console.WriteLine("status stderr = {0}", status);
#endif // FALSE
            Log.EnableFileLog = true;
            //Console.Out.WriteLine("Redirect Out OK!!!!!!!!!!!!!!!");
            //Console.Error.WriteLine("Redirect Error OK!!!!!!!!!!!!!!!");
            ShowVersion();
        }

        private void PrintConfiguration(IPAddress mifaceIP)
        {
            Console.WriteLine("Network configuration:");
            Console.WriteLine("----------------------");
            Console.WriteLine("Multicast interface: {0}", (mifaceIP.Equals(IPAddress.None)) ? "unknown" : mifaceIP.ToString());
            Console.WriteLine("Internal interface: {0}, port: {1}", _Address.ToString(), _iPort);
        }

        protected void Start(IPAddress mifaceIP)
        {
            ServicePointManager.ServerCertificateValidationCallback = (obj, certificate, chain, errors) => true;

            ProcessTuneSection process = ProcessTuneSection.GetSection();
            if (null != process && process.HighPriority)
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }

            PrintConfiguration(mifaceIP);

            _listener = new TcpListener(_Address, _iPort);
            _listener.Start();
#if NET_45_OR_GREATER
            AcceptSocketAsync(_listener);
#else
            _listener.BeginAcceptSocket(OnAcceptNewClient, _listener);
#endif // NET_45_OR_GREATER

            Console.WriteLine("Listening, hit enter to stop");
            Console.WriteLine();

            CAWebClient.CAAuthData auth = CAWebClient.CAAuthData.GetSection();
            _moduleVLC = new LibVLCModule(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                (null == auth) ? string.Format("http://localhost:{0}/ca/", _iPort) : auth.CaUri,
                mifaceIP
                );

            string strBestMac = _moduleVLC.GetBestMacAddress();
            if (strBestMac.Length > 0)
            {
                MacAddress = strBestMac;
            }

            if (null != process && process.UseAuxLowPriorityThreadPool)
            {
                _lowThreadPool = new LowPriorityThreadPool();
            }
        }


        protected void Loop(int millisecondsTimeout)
        {
            Thread.Sleep(millisecondsTimeout);

            lock (_proxy.SyncRoot)
            {
                foreach (IStreamProxy stream in _proxy)
                {
                    if (null != stream)
                    {
                        stream.WriteLogProgress();
                    }
                }
            }
        }

        protected void Stop()
        {
            //_listenerCA.Stop();
            _listener.Stop();

            lock (_proxy.SyncRoot)
            {
                foreach (IStreamProxy stream in _proxy)
                {
                    if (null != stream)
                    {
                        stream.Shutdown();
                    }
                }
            }

            lock (_connections.SyncRoot)
            {
                foreach (Socket sock in _connections)
                {
                    if (null != sock)
                    {
                        sock.Close();
                        //sock.Disconnect(false);
                    }
                }
            }

            _noneConnections.WaitOne(4000);

            _moduleVLC.LibVLCRelease();

            if (null != _lowThreadPool)
            {
                _lowThreadPool.Dispose();
            }
        }

        public int AddNewSocket(Socket sock, TcpListener listener)
        {
            int ndx = 0;

            lock (_connections.SyncRoot)
            {
                while (null != _connections[ndx])
                {
                    ndx++;
                }

                _connections[ndx] = sock;
            }

            int iCount = Interlocked.Increment(ref _nConnections);

            if (iCount < _connections.Length)
            {
#if NET_45_OR_GREATER
                AcceptSocketAsync(listener);
#else
                listener.BeginAcceptSocket(OnAcceptNewClient, listener);
#endif // NET_45_OR_GREATER
            }

            if (1 == iCount)
            {
                _noneConnections.Reset();
            }

            return ndx;
        }

        public void DeleteSocket(Socket sock, TcpListener listener, int iIndex)
        {
            lock (_connections.SyncRoot)
            {
                _connections[iIndex] = null;
            }

            int iCount = Interlocked.Decrement(ref _nConnections);

            if(0 == iCount)
            {
                _noneConnections.Set();
            }
            else if ((iCount + 1) == _connections.Length)
            {
#if NET_45_OR_GREATER
                AcceptSocketAsync(listener);
#else
                listener.BeginAcceptSocket(OnAcceptNewClient, listener);
#endif // NET_45_OR_GREATER
            }
        }

        public int AddNewStream(IStreamProxy streamProxy)
        {
            int ndx = 0;

            lock (_proxy.SyncRoot)
            {
                while (ndx < _proxy.Length && null != _proxy[ndx])
                {
                    ndx++;
                }

                if (ndx < _proxy.Length)
                {
                    _proxy[ndx] = streamProxy;
                }
                else
                {
                    ndx = -1;
                }
            }

            if (ndx < 0)
            {
                throw new Exception("Maximum stream count exceeded.");
            }

            return ndx;
        }

        public void DeleteStream(IStreamProxy streamProxy, int iIndex)
        {
            lock (_proxy.SyncRoot)
            {
                if (_iPostedStreamID == iIndex)
                {
                    _iPostedStreamID = -1;
                }
                _proxy[iIndex] = null;
            }
        }

        public void KeepStream(IStreamProxy streamProxy, int iIndex)
        {
            DateTime timeNow = DateTime.UtcNow;
            IStreamProxy prevPostedStream = null;

            lock (_proxy.SyncRoot)
            {
                if (!(_iPostedStreamID < 0) && _timePostedStream > timeNow)
                {
                    prevPostedStream = _proxy[_iPostedStreamID];
                }

                if (null != _proxy[iIndex])
                {
                    _iPostedStreamID = iIndex;
                    _timePostedStream = timeNow.AddMinutes(2);
                }
            }

            if (null != prevPostedStream)
            {
                prevPostedStream.Shutdown();
            }
        }

        public IStreamProxy TakePostedStream(int iIndex)
        {
            IStreamProxy postedStream = null;

            lock (_proxy.SyncRoot)
            {
                if (iIndex == _iPostedStreamID && _timePostedStream > DateTime.UtcNow)
                {
                    postedStream = _proxy[_iPostedStreamID];
                    _iPostedStreamID = -1;
                }
            }

            return postedStream;
        }

        public bool IsExistPostedStream(string mrl, out int iIndex)
        {
            int iID = -1;
            IStreamProxy postedStream = null;

            lock (_proxy.SyncRoot)
            {
                if (!(_iPostedStreamID < 0) && _timePostedStream > DateTime.UtcNow.AddSeconds(8))
                {
                    postedStream = _proxy[_iPostedStreamID];
                    iID = _iPostedStreamID;
                }
            }

            iIndex = -1;
            bool bIsExist = false;

            if(null != postedStream && mrl == ((StreamProxyAbstract)postedStream).Mrl)
            {
                iIndex = iID;
                bIsExist = true;
            }

            return bIsExist;
        }

        public LibVLCModule ModuleVLC
        {
            get
            {
                return _moduleVLC;
            }
        }

        public IThreadPool LowThreadPool
        {
            get
            {
                return _lowThreadPool;
            }
        }

        public string MacAddress
        {
            get
            {
                return _strMacAddress;
            }

            set
            {
                _strMacAddress = value;
            }
        }

        public string HostName
        {
            get
            {
                return _strHostName;
            }
        }

        public string DnsHostName
        {
            get
            {
                return _strDnsHostName;
            }
        }

        public string CABodyTopics
        {
            get
            {
                UpdateCATopics();
                return _CABodyTopics;
            }
        }

        public ProxyChannelsWriterAbstract.ChannelsListCache CABodyCache
        {
            get
            {
                UpdateCACache();
                return _CACache;
            }
        }

        public DateTime CABodyCacheUpdateTime
        {
            get
            {
                ProxyChannelsWriterAbstract.ChannelsListCache cache = _CACache;
                return (null == cache) ? Process.GetCurrentProcess().StartTime : cache.UpdateLastTime;
            }
        }

        public bool IsHostAddress(IPAddress addr)
        {
            bool bIsFound = false;

            foreach (IPAddress ip in _HostAddresses)
            {
                if (addr.Equals(ip))
                {
                    bIsFound = true;
                    break;
                }
            }

            return bIsFound;
        }

        protected override void OnStart(string[] args)
        {
            //IntPtr handle = this.ServiceHandle;
            //myServiceStatus.currentState = (int)State.SERVICE_START_PENDING;
            //SetServiceStatus(handle, myServiceStatus);

            string strAppParam;
            IPAddress mifaceIP = IPAddress.None;
            if (null != (strAppParam = ConfigurationManager.AppSettings["mifaceIP"]))
            {
                mifaceIP = IPAddress.Parse(strAppParam);
            }

            this.Start(mifaceIP);

            //myServiceStatus.currentState = (int)State.SERVICE_RUNNING;
            //SetServiceStatus(handle, myServiceStatus);
        }

        protected override void OnStop()
        {
            this.Stop();
        }

#if NET_45_OR_GREATER
        private async void AcceptSocketAsync(TcpListener listener)
        {
            bool bRepeat = true;

            do
            {
                try
                {
                    // End the operation and display the received data on 
                    // the console.
                    Socket client = await listener.AcceptSocketAsync();

                    // Process the connection here. (Add the client to a
                    // server table, read data, etc.)
                    //Console.WriteLine("Client connected completed");

                    new CAHttpClient(this, client, listener);
                    //new HttpClient(this, client, listener);
                    bRepeat = false;
                }
                catch (SocketException excep)
                {
                    Console.WriteLine("Accept tcp connection failed, error:");
                    Console.WriteLine(excep.Message);
                    //listener.BeginAcceptSocket(OnAcceptNewClient, listener);
                }
                catch (Exception excep)
                {
                    Console.WriteLine("Exception listener.AcceptSocketAsync()");
                    Console.WriteLine(excep.Message);
                    bRepeat = false;
                }
            }
            while (bRepeat);
        }

#else
        private void OnAcceptNewClient(IAsyncResult asyncResult)
        {
            // Get the listener that handles the client request.
            TcpListener listener = (TcpListener)asyncResult.AsyncState;

            try
            {
                // End the operation and display the received data on 
                // the console.
                Socket client = listener.EndAcceptSocket(asyncResult);

                // Process the connection here. (Add the client to a
                // server table, read data, etc.)
                //Console.WriteLine("Client connected completed");

                new CAHttpClient(this, client, listener);
                //new HttpClient(this, client, listener);
            }
            catch (SocketException excep)
            {
                Console.WriteLine("Accept tcp connection failed, error:");
                Console.WriteLine(excep.Message);
                listener.BeginAcceptSocket(OnAcceptNewClient, listener);
            }
            catch (Exception excep)
            {
                Console.WriteLine("Exception OnAcceptNewClient(IAsyncResult asyncResult):");
                Console.WriteLine(excep.Message);
            }
        }
#endif // !NET_45_OR_GREATER

        private void InitializeComponent()
        {
            // 
            // CAHTTPProxy
            // 
            this.AutoLog = false;
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();
            this.ServiceName = assemName.Name;
        }

        private void UpdateCACache()
        {
            ProxyChannelsWriterAbstract.ChannelsListCache cache = _CACache;

            if (null == cache || !cache.UpToDate)
            {
                if (null == cache)
                {
                    if (null != CAWebClient.CAAuthData.GetSection())
                    {
                        ProxyChannelsWriterAbstract.CAListCache CAcache = new ProxyChannelsWriterAbstract.CAListCache(MacAddress);
                        _CABodyTopics = CAcache.TopicsXml;
                        _CACache = CAcache;
                    }
                    else
                    {
                        _CACache = new ProxyChannelsWriterAbstract.XSPFCache("tvlist.xspf");
                    }
                }
                else
                {
                    cache.Update();
                }
            }
        }

        private void UpdateCATopics()
        {
            string xmlTopics = _CABodyTopics;

            if (null == xmlTopics)
            {
                if (null == CAWebClient.CAAuthData.GetSection())
                {
                    using (StreamReader streamXml = File.OpenText("tvgroup.xml"))
                    {
                        _CABodyTopics = streamXml.ReadToEnd();
                    }

                }
                else
                {
                    CAWebClient client = new CAWebClient();
                    _CABodyTopics = client.TopicsXmlString;
                }
            }
        }
    }
}