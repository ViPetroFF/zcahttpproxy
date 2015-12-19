using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Configuration;

namespace ZCAHTTPProxy
{
    public class ProxyStreamConfig : ConfigurationSection
    {
        public const string SECTION_NAME = "ProxyStream";
        private static readonly ProxyStreamConfig s_section = (ProxyStreamConfig)ConfigurationManager.GetSection(SECTION_NAME);

        public static ProxyStreamConfig Section
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

        public static ProxyStreamConfig GetSection()
        {
            //Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            //Log.Message(config.FilePath);

            return s_section;
        }

        public ProxyStreamConfig()
        {
        }

        public ProxyStreamConfig(string strName)
        {
            Name = strName;
            TmpPath = string.Empty;

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

        [ConfigurationProperty("name")]
        public string Name
        {
            get { return (string)this["name"]; }
            set { this["name"] = value; }
        }

        [ConfigurationProperty("TempFolderPath", DefaultValue = "")]
        public string TmpPath
        {
            get { return (string)this["TempFolderPath"]; }
            set { this["TempFolderPath"] = value; }
        }

        [ConfigurationProperty("DisableFileCache", DefaultValue = (bool)false, IsRequired = false)]
        public bool DisableFileCache
        {
            get { return (bool)this["DisableFileCache"]; }
            set { this["DisableFileCache"] = value; }
        }

        [ConfigurationProperty("DisableSharedTimeShift", DefaultValue = (bool)false, IsRequired = false)]
        public bool DisableSharedTimeShift
        {
            get { return (bool)this["DisableSharedTimeShift"]; }
            set { this["DisableSharedTimeShift"] = value; }
        }

        [ConfigurationProperty("InitialReceiveTimeout", DefaultValue = (int)7, IsRequired = false)]
        public int InitialReceiveTimeout
        {
            get { return (int)this["InitialReceiveTimeout"]; }
            set { this["InitialReceiveTimeout"] = value; }
        }
    }

    public interface IStreamProxy
    {
        void WriteLogProgress();
        void WriteLogException(Exception e);

        int AddRef();
        void Release();
        void Shutdown();
    }

    abstract class StreamProxyAbstract : IDisposable, IStreamProxy
    {
        private static int _nCountProxy;

        private readonly IStreamProxyServer _server;
        private readonly string _Mrl;
        private readonly string _MrlIP;
        private readonly string _MrlIPPort;

        private HttpClient _connection;
        protected Stream _inStream;
        protected Socket _outSocket;
        private NetworkStream _outStream;
        private int _iProxyID = -1;
        private int _inOp;
        private int _inReceiveOp = 1;
        private int _inSendOp = 2;
        private int _deltaBuff;
        private ulong _sentOffset;

        private readonly byte[] _Buffer = new byte[0x4];
        private readonly DateTime _timeStart = DateTime.UtcNow;

        private DateTime _lastUpdate = DateTime.UtcNow;
        private string _strMessageLog = string.Empty;
        private string _strLastMessageLog = string.Empty;


        public StreamProxyAbstract(HttpClient connection, string mrl, Stream inStream)
        {
            _server = (IStreamProxyServer)connection.Server;
            _connection = connection;
            _Mrl = mrl;
            _MrlIP = mrl;
            _outSocket = connection.Socket;
            _inStream = inStream;
            _outStream = new NetworkStream(_outSocket, false);

            string strIP = mrl;
            int ndx = strIP.IndexOf('@');
            if (ndx > 0)
            {
                strIP = strIP.Substring(++ndx);
            }
            _MrlIPPort = strIP;
            ndx = strIP.IndexOf(':');
            if (ndx > 0)
            {
                _MrlIP = strIP.Substring(0, ndx);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            // Check to see if Dispose has already been called.
            if (Stream.Null != _inStream)
            {
                // If disposing equals true, dispose all managed
                // and unmanaged resources.
                if (disposing)
                {
                    // Dispose managed resources.
                    _inStream.Dispose();
                    _inStream = Stream.Null;
                    //_outSocket
                }
            }
        }

        ~StreamProxyAbstract()
        {
            // Do not re-create Dispose clean-up code here.
            // Calling Dispose(false) is optimal in terms of
            // readability and maintainability.
            Dispose(false);
        }

        public int AddRef()
        {
            return Interlocked.Increment(ref _inOp);
        }

        public void Release()
        {
            if (0 == Interlocked.Decrement(ref _inOp))
            {
                Interlocked.Decrement(ref _nCountProxy);
                _server.DeleteStream(this, _iProxyID);
                WriteLogCloseConnection();
                if (ReleaseOutSocket)
                {
                    _connection.Release();
                }
                else
                {
                    _connection.Continue();
                }

                Dispose();
            }
        }

        public void Shutdown()
        {
            _inStream.Flush();
        }

        public HttpClient ResumeSend(HttpClient connection)
        {
            HttpClient prevConn = _connection;
            _connection = connection;
            _outSocket = connection.Socket;
            _outStream = new NetworkStream(_outSocket, false);

            if (OnResumeSend() && StartReadOutSocket)
            {
#if NET_45_OR_GREATER
                OutSocketReadAsync();
#else
                AddRef();
                BeginOutSocketRead(0);
#endif // NET_45_OR_GREATER
            }

            return prevConn;
        }

        protected void ResetDelta()
        {
            lock (this)
            {
                _deltaBuff = 0;
            }
        }

#if NET_45_OR_GREATER
        protected void Start()
        {
            try
            {
                _iProxyID = _server.AddNewStream(this);
                Interlocked.Increment(ref _nCountProxy);
                if (StartReadOutSocket)
                {
                    OutSocketReadAsync();
                }

                ReceiveAsync();
            }
            catch (Exception e)
            {
                WriteLogException(e);
            }
        }

        protected void Continue(ulong startOffset)
        {
            _sentOffset += startOffset;
            ReceiveAsync();
        }
#endif // NET_45_OR_GREATER

        protected bool Resume()
        {
            bool bSuccess = false;

            if (CountOp < 2 && DeltaOffset > 0 && _inSendOp < 2)
            {
#if NET_45_OR_GREATER
                SendAsync();
#else
                AddRef();
                if (!BeginSend())
                {
                    Release();
                }
#endif // NET_45_OR_GREATER

                bSuccess = true;
            }

            return bSuccess;
        }

#if !NET_45_OR_GREATER
        protected void Start()
        {
            try
            {
                //using(EPGWebClient epg = new EPGWebClient(_MrlIPPort))
                //{
                //}

                _iProxyID = _server.AddNewStream(this);
                Interlocked.Increment(ref _nCountProxy);
                if (StartReadOutSocket)
                {
                    AddRef();
                    BeginOutSocketRead(0);
                }
                AddRef();
                if (!BeginReceive())
                {
                    Release();
                }
            }
            catch (Exception e)
            {
                WriteLogException(e);
                if(_iProxyID >= 0)
                {
                    Release();
                }
            }
        }

        protected void Continue(ulong startOffset)
        {
            try
            {
                _sentOffset += startOffset;
                AddRef();
                if (!BeginReceive())
                {
                    Release();
                }
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }
#endif // NET_45_OR_GREATER

        protected abstract byte[] GetBuffer(ulong offset);
        protected abstract int GetOffset(ulong offset);
        protected abstract int GetReceiveBufferSize();
        protected abstract int GetSentBufferSize();

        protected virtual bool StartReadOutSocket
        {
            get
            {
                return true;
            }
        }
        protected virtual bool ReleaseOutSocket
        {
            get
            {
                return true;
            }
        }

        protected abstract bool OnReceive(ulong offset, int count);
        protected abstract bool OnSend(ulong offset, int count);
        protected virtual void OnEOF(ulong offset) { }
        protected abstract bool OnReceiveContinue(ulong sentOffset, ulong recvOffset, int delta);
        protected abstract bool OnTrySendOp(int delta);
        protected abstract bool OnTryReceiveOp(int delta);
        protected virtual bool OnOutSocketRead() { return false; }
        protected virtual bool OnResumeSend() { return false; }

#if NET_45_OR_GREATER
        private async void ReceiveAsync()
        {
            bool bLoop = true;

            AddRef();
            do
            {
                int delta;
                ulong sentOffset;

                lock (this)
                {
                    delta = _deltaBuff;
                    sentOffset = _sentOffset;
                }

                if (OnTrySendOp(delta))
                {
                    TrySendOp();
                }

                ulong recvOffset = sentOffset + (ulong)delta;
                int iRecvSize = GetReceiveBufferSize();

                int read;
                if (OnReceiveContinue(sentOffset, recvOffset, delta + iRecvSize))
                {
                    try
                    {
                        read = await _inStream.ReadAsync(GetBuffer(recvOffset), GetOffset(recvOffset), iRecvSize);

                        if (read > 0)
                        {
                            lock (this)
                            {
                                _deltaBuff += read;
                            }

                            bLoop = OnReceive(recvOffset, read);
                        }
                        else
                        {
                            OnEOF(recvOffset);
                            bLoop = false;
                        }
                    }
                    catch (Exception e)
                    {
                        WriteLogException(e);
                        bLoop = false;
                    }
                }
                else
                {
                    Interlocked.Increment(ref _inReceiveOp);
                    bLoop = false;
                }
            }
            while (bLoop);

            Release();
        }
#else
        protected virtual bool BeginReceive()
        {
            int delta;
            ulong sentOffset;

            lock (this)
            {
                delta = _deltaBuff;
                sentOffset = _sentOffset;
            }

            ulong recvOffset = sentOffset + (ulong)delta;
            int iRecvSize = GetReceiveBufferSize();

            bool bBegin = true;

            if (OnReceiveContinue(sentOffset, recvOffset, delta + iRecvSize))
            {
                _inStream.BeginRead(GetBuffer(recvOffset), GetOffset(recvOffset), iRecvSize, OnReceive, recvOffset);
            }
            else
            {
                Interlocked.Increment(ref _inReceiveOp);
                bBegin = false;
            }

            if (OnTrySendOp(delta))
            {
                TrySendOp();
            }

            return bBegin;
        }
#endif // NET_45_OR_GREATER

#if NET_45_OR_GREATER
        private async void SendAsync()
        {
            bool bLoop = true;

            AddRef();
            do
            {
                int delta;
                ulong sentOffset;

                lock (this)
                {
                    delta = _deltaBuff;
                    sentOffset = _sentOffset;
                }

                if (OnTryReceiveOp(delta))
                {
                    if (Interlocked.Decrement(ref _inReceiveOp) > 0)
                    {
                        ReceiveAsync();
                    }
                    else
                    {
                        Interlocked.Increment(ref _inReceiveOp);
                    }
                }

                if (delta > 0)
                {
                    int iSentSize = GetSentBufferSize();
                    int iLen = (int)Math.Min((long)iSentSize, delta);

                    try
                    {
                        await _outStream.WriteAsync(GetBuffer(sentOffset), GetOffset(sentOffset), iLen);
                        int sendCount = iLen;

                        if (sendCount > 0)
                        {
                            lock (this)
                            {
                                _sentOffset += (ulong)sendCount;
                                _deltaBuff -= sendCount;
                            }

                            bLoop = OnSend(sentOffset, sendCount);
                        }
                        else
                        {
                            bLoop = false;
                        }
                    }
                    catch (Exception e)
                    {
                        //WriteLogException(e);
                        bLoop = false;
                    }
                }
                else
                {
                    Interlocked.Increment(ref _inSendOp);
                    bLoop = false;
                }
            }
            while (bLoop);

            Release();
        }
#else
        protected virtual bool BeginSend()
        {
            int delta;
            ulong sentOffset;

            lock (this)
            {
                delta = _deltaBuff;
                sentOffset = _sentOffset;
            }

            bool bBegin = true;

            if (delta > 0)
            {
                int iSentSize = GetSentBufferSize();
                int iLen = Math.Min(iSentSize, delta);
                _outSocket.BeginSend(GetBuffer(sentOffset), GetOffset(sentOffset), iLen, SocketFlags.None, OnSend, sentOffset);
            }
            else
            {
                Interlocked.Increment(ref _inSendOp);
                bBegin = false;
            }

            if (OnTryReceiveOp(delta))
            {
                if (Interlocked.Decrement(ref _inReceiveOp) > 0)
                {
                    AddRef();
                    if (!BeginReceive())
                    {
                        Release();
                    }
                }
                else
                {
                    Interlocked.Increment(ref _inReceiveOp);
                }
            }

            return bBegin;
        }
#endif // NET_45_OR_GREATER

        protected void TrySendOp()
        {
            if (Interlocked.Decrement(ref _inSendOp) > 0)
            {
#if NET_45_OR_GREATER
                SendAsync();
#else
                AddRef();
                if (!BeginSend())
                {
                    Release();
                }
#endif // NET_45_OR_GREATER
            }
            else
            {
                Interlocked.Increment(ref _inSendOp);
            }
        }

#if NET_45_OR_GREATER
        private async void OutSocketReadAsync()
        {
            AddRef();
            try
            {
                int count = 0;
                int read;

                do
                {
                    read = await _outStream.ReadAsync(_Buffer, 0, _Buffer.Length);
                    count++;
                }
                while (_Buffer.Length == read && count <= 8);
            }
            catch (Exception e)
            {
                WriteLogException(e);
            }

            if (OnOutSocketRead())
            {
                _server.KeepStream(this, _iProxyID);
            }
            else
            {
                _inStream.Flush();
            }

            Release();
        }
#else
        private void BeginOutSocketRead(int nCount)
        {
            _outSocket.BeginReceive(_Buffer, 0, _Buffer.Length, SocketFlags.None, OnOutSocketRead, nCount + 1);
        }
#endif // NET_45_OR_GREATER

#if !NET_45_OR_GREATER
        private void OnReceive(IAsyncResult asyncResult)
        {
            try
            {
                int read = _inStream.EndRead(asyncResult);
                if (read > 0)
                {
                    lock (this)
                    {
                        _deltaBuff += read;
                    }

                    if (OnReceive((ulong)asyncResult.AsyncState, read))
                    {
                        if (!BeginReceive())
                        {
                            Release();
                        }
                    }
                    else
                    {
                        Release();
                    }
                }
                else
                {
                    OnEOF((ulong)asyncResult.AsyncState);
                    Release();
                }
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }

        private void OnSend(IAsyncResult asyncResult)
        {
            try
            {
                int sendCount = _outSocket.EndSend(asyncResult);

                if (sendCount > 0)
                {
                    lock (this)
                    {
                        _sentOffset += (ulong)sendCount;
                        _deltaBuff -= sendCount;
                    }

                    if (OnSend((ulong)asyncResult.AsyncState, sendCount))
                    {
                        if (!BeginSend())
                        {
                            Release();
                        }
                    }
                    else
                    {
                        Release();
                    }
                }
                else
                {
                    Release();
                }
            }
            catch (Exception e)
            {
                //WriteLogException(e);
                Release();
            }
        }

        private void OnOutSocketRead(IAsyncResult asyncResult)
        {
            bool bRelease = true;

            try
            {
                int read = _outSocket.EndReceive(asyncResult);
                int count = (int)asyncResult.AsyncState;
                if (_Buffer.Length == read && count <= 8)
                {
                    BeginOutSocketRead(count + 1);
                    bRelease = false;
                }
            }
            catch (Exception e)
            {
                WriteLogException(e);
            }

            if(bRelease)
            {
                if (OnOutSocketRead())
                {
                    _server.KeepStream(this, _iProxyID);
                }
                else
                {
                    _inStream.Flush();
                }

                Release();
            }
        }
#endif // !NET_45_OR_GREATER
        public int CountOp
        {
            get
            {
                return _inOp;
            }
        }

        public string Mrl
        {
            get
            {
                return _Mrl;
            }
        }

        public string MrlIP
        {
            get
            {
                return _MrlIP;
            }
        }

        public ulong SentOffset
        {
            get
            {
                ulong sentOffset;

                lock (this)
                {
                    sentOffset = _sentOffset;
                }

                return sentOffset;
            }

            //set
            //{
            //lock (this)
            //{
            //_sentOffset = value;
            //}
            //}
        }

        public ulong ReceiveOffset
        {
            get
            {
                ulong sentOffset;
                int deltaOffset;

                lock (this)
                {
                    sentOffset = _sentOffset;
                    deltaOffset = _deltaBuff;
                }

                return (sentOffset + (ulong)deltaOffset);
            }
        }

        public int DeltaOffset
        {
            get
            {
                int deltaOffset;

                lock (this)
                {
                    deltaOffset = _deltaBuff;
                }

                return deltaOffset;
            }
        }

        public DateTime LastUpdate
        {
            get
            {
                return _lastUpdate;
            }

            set
            {
                _lastUpdate = value;
            }
        }

        public string StrMessageLog
        {
            get
            {
                return _strMessageLog;
            }

            set
            {
                _strMessageLog = value;
            }
        }

        public void WriteLogProgress()
        {
            string message = _strMessageLog;
            if (message != _strLastMessageLog)
            {
                Log.MessageProgress(message, _iProxyID);
                _strLastMessageLog = message;
            }
        }

        public void WriteLogException(Exception e)
        {
            Log.Error(string.Format("Streaming {0} to address {1} is stopped, error:",
                    _MrlIP,
                    _connection.RemoteEndPointAddress.ToString()//_RemoteEndPoint.Address.ToString()
                    ).PadRight(Log.PadSize) + "\n" + e.Message);
        }

        public static int CountProxy
        {
            get
            {
                return _nCountProxy;
            }
        }

        private void WriteLogCloseConnection()
        {
            ulong seconds = Math.Max(1, (ulong)(_lastUpdate - _timeStart).TotalSeconds);

            Log.Message(
                string.Format("{0} at {1:F1} Mbit/s was streaming from {2}",
                Log.GetSizeString(_sentOffset), ((double)_sentOffset * 8) / (double)(seconds * 1000000), _MrlIP)
                );
        }
    }

    class StreamProxy : StreamProxyAbstract
    {
        //private const int RECV_BUFFER_SIZE = 0x800;
        //private const int BUFFER_SIZE = 0x1000;
        //private const int CACHE_SIZE = 0x69;
        //private const int BUFFER_CACHE_SIZE = CACHE_SIZE * BUFFER_SIZE;
        //private const int OVERFLOW_SIZE = 0x40 * BUFFER_SIZE;
        //private const int UNDERFLOW_SIZE = 0x20 * BUFFER_SIZE;

        private const int RECV_BUFFER_SIZE = 0x800;
        protected const int BUFFER_SIZE = 0x2000;
        private const int CACHE_SIZE = 0x10;
        private const int BUFFER_CACHE_SIZE = CACHE_SIZE * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0xA * BUFFER_SIZE;
        private const int UNDERFLOW_SIZE = 0x5 * BUFFER_SIZE;

        public const string CLASS_NAME = "RealTime";

        private readonly byte[] _dataCache = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];

        private ulong _lastSentBytes;

        static StreamProxy()
        {
            Log.Message("Buffer configuration:");
            Log.Message("---------------------");
            Log.Message(string.Format("Proxy stream name: {0}", CLASS_NAME));
            Log.Message(string.Format("Stream buffer size: {0} Kb", BUFFER_CACHE_SIZE / 1024));
            Log.Message(string.Format("Overflowing size: {0} Kb", OVERFLOW_SIZE / 1024));
            Log.Message(string.Format("Underflowing size: {0} Kb", UNDERFLOW_SIZE / 1024));
        }

        public static string Name
        {
            get { return CLASS_NAME; }
        }

        public StreamProxy(HttpClient connection, string mrl, Stream inStream)
            : base(connection, mrl, inStream)
        {
            Start();
        }

        protected override byte[] GetBuffer(ulong offset)
        {
            return _dataCache;
        }

        protected override int GetOffset(ulong offset)
        {
            return (int)(offset % BUFFER_CACHE_SIZE);
        }

        protected override int GetReceiveBufferSize()
        {
            return RECV_BUFFER_SIZE;
        }

        protected override int GetSentBufferSize()
        {
            return BUFFER_SIZE;
        }

        protected override bool OnReceive(ulong offset, int count)
        {
            int offsetBuff = GetOffset(offset);

            if (offsetBuff < BUFFER_SIZE)
            {
                int iLen = Math.Min(count, BUFFER_SIZE - offsetBuff);
                Buffer.BlockCopy(_dataCache, offsetBuff, _dataCache, BUFFER_CACHE_SIZE + offsetBuff, iLen);
            }

            if ((offsetBuff + count) > BUFFER_CACHE_SIZE)
            {
                Buffer.BlockCopy(_dataCache, BUFFER_CACHE_SIZE, _dataCache, 0, (offsetBuff + count) - BUFFER_CACHE_SIZE);
            }

            return true;
        }

        protected override bool OnSend(ulong offset, int count)
        {
            ulong sentBytes = offset + (ulong)count;
            DateTime timeNow = DateTime.UtcNow;
            if (LastUpdate.AddSeconds(1) < timeNow)
            {
                double mseconds = Math.Max(1, (timeNow - LastUpdate).TotalMilliseconds);
                string message = string.Format(
                                        "<{0}>: Streaming {1} at {2:F1} Mbit/s, buffer: {3}%",
                                        MrlIP,
                                        Log.GetSizeString(sentBytes),
                                        ((double)(sentBytes - _lastSentBytes) * 8000) / (ulong)(mseconds * 1000000),
                                        (DeltaOffset * 100) / BUFFER_CACHE_SIZE
                                        );

                LastUpdate = timeNow;
                _lastSentBytes = sentBytes;
                StrMessageLog = message;
            }

            return true;
        }

        protected override bool OnReceiveContinue(ulong sentOffset, ulong recvOffset, int delta)
        {
            return (delta < BUFFER_CACHE_SIZE);
        }
        protected override bool OnTrySendOp(int delta)
        {
            return (delta >= OVERFLOW_SIZE);
        }

        protected override bool OnTryReceiveOp(int delta)
        {
            return (delta <= UNDERFLOW_SIZE);
        }
    }

    class FileStreamProxy : StreamProxy
    {
        public const int FS_BUFFER_SIZE = 0x20 * BUFFER_SIZE;
        private const int RECV_BUFFER_SIZE = BUFFER_SIZE;

        private ulong _offsetEOF;
        private bool _bIsEOFSended;

        public FileStreamProxy(HttpClient connection, string mrl, Stream inStream)
            : base(connection, mrl, inStream)
        {
        }

        protected override int GetReceiveBufferSize()
        {
            return RECV_BUFFER_SIZE;
        }

        protected override bool StartReadOutSocket
        {
            get
            {
                return false;
            }
        }
        protected override bool ReleaseOutSocket
        {
            get
            {
                return !_bIsEOFSended;
            }
        }

        protected override bool OnReceive(ulong offset, int count)
        {
            bool bBegin = true;

            if (count < GetReceiveBufferSize())
            {
                //Log.Message("End of File ???????");
                OnEOF(offset + (ulong)count);
                bBegin = false;
            }

            return bBegin;
        }

        protected override bool OnSend(ulong offset, int count)
        {
            bool bBegin = base.OnSend(offset, count);

            if (_offsetEOF > 0 && _offsetEOF == offset + (ulong)count)
            {
                //Log.Message("End of File Sended !!!!!!");
                _bIsEOFSended = true;
                bBegin = false;
            }

            return bBegin;
        }

        protected override void OnEOF(ulong offset)
        {
            //Log.Message(string.Format("End of File {0} !!!!!!!!!", offset));
            _offsetEOF = offset;
            TrySendOp();
        }
    }

    public class FileCacheStream : Stream
    {
        private class IOState
        {
            private readonly Stream _stream;
            private readonly AsyncCallback _callback;
            private readonly long _offset;
            private readonly int _count;

            public IOState(Stream stream, long offset, int count, AsyncCallback callback)
            {
                _stream = stream;
                _offset = offset;
                _count = count;
                _callback = callback;
            }

            public Stream Stream
            {
                get
                {
                    return _stream;
                }
            }

            public long Offset
            {
                get
                {
                    return _offset;
                }
            }

            public int Count
            {
                get
                {
                    return _count;
                }
            }

            public void OnIOComplete(IAsyncResult asyncResult)
            {
                this._callback(new IOResult(asyncResult, this));
            }
        }

        private class IOResult : IAsyncResult
        {
            private readonly IAsyncResult _result;
            private readonly IOState _state;

            public IOResult(IAsyncResult result, IOState state)
            {
                _result = result;
                _state = state;
            }

            public IAsyncResult AsyncResult
            {
                get
                {
                    return _result;
                }
            }

            public IOState IOState
            {
                get
                {
                    return _state;
                }
            }

            public object AsyncState
            {
                get
                {
                    return _result.AsyncState;
                }
            }

            public WaitHandle AsyncWaitHandle
            {
                get
                {
                    return _result.AsyncWaitHandle;
                }
            }

            public bool CompletedSynchronously
            {
                get
                {
                    return _result.CompletedSynchronously;
                }
            }

            public bool IsCompleted
            {
                get
                {
                    return _result.IsCompleted;
                }
            }
        }

        private class EOFResult : IAsyncResult
        {
            private readonly object _state;

            public EOFResult(object state)
            {
                _state = state;
            }

            public object AsyncState
            {
                get
                {
                    return _state;
                }
            }

            public WaitHandle AsyncWaitHandle
            {
                get
                {
                    return null;
                }
            }

            public bool CompletedSynchronously
            {
                get
                {
                    return true;
                }
            }

            public bool IsCompleted
            {
                get
                {
                    return true;
                }
            }
        }

        private const int BUFFER_SIZE = 0x2000;//0x40000;
        private const long UNDERFLOW_SIZE = 0x8000000;
        public const long FILE_CACHE_SIZE = 0x10000000;
        private const FileOptions FileFlagNoBuffering = (FileOptions)0x20000000;
        private const string FILE_NAME = "tshcache{0:x}.tmp";
        private const FileShare FILE_SHARE = FileShare.Delete | FileShare.Write | FileShare.Read;
        private const FileOptions FILE_OPTIONS = FileOptions.Asynchronous | FileOptions.DeleteOnClose;

        private static readonly string FILE_CACHE_PATH;

        private static int FILE_CACHE_COUNTER;

        private long _deltaSize;
        private long _readOffset;
        private FileStream[] _dataCacheIn = null;
        private FileStream[] _dataCacheOut = null;

        static FileCacheStream()
        {
            ProxyStreamConfig proxyStreamTmp = ProxyStreamConfig.GetSection();
            string strTmpFolder;

            if (null != proxyStreamTmp && proxyStreamTmp.TmpPath.Length > 0 && Directory.Exists(proxyStreamTmp.TmpPath))
            {
                strTmpFolder = proxyStreamTmp.TmpPath;
            }
            else
            {
                strTmpFolder = Path.GetTempPath();
            }

            FILE_CACHE_PATH = Path.Combine(strTmpFolder, FILE_NAME);
        }

        private FileStream[] ResizeCache(FileStream[] dataCache, FileStream[] dataCacheOut, long offsetStart, long delta)
        {
            FileStream[] dataCacheNew = dataCache;

            if (delta > ((dataCache.Length - 1) * FILE_CACHE_SIZE - 4 * BUFFER_SIZE))
            {
                string fname = Path.Combine(Path.GetTempPath(), FILE_NAME);
                dataCacheNew = (FileStream[])Array.CreateInstance(typeof(FileStream), dataCache.Length + 1);

                int indexStart = (int)(offsetStart / FILE_CACHE_SIZE);

                for (int i = 0; i < dataCacheNew.Length; i++)
                {
                    int index = indexStart + i;

                    if (i < dataCache.Length)
                    {
                        dataCacheNew[index % dataCacheNew.Length] = dataCache[index % dataCache.Length];
                    }
                    else
                    {
                        FileOptions options = (null == dataCacheOut) ? FileOptions.WriteThrough : FileFlagNoBuffering;
                        FileAccess access = (null == dataCacheOut) ? FileAccess.Write : FileAccess.Read;
                        string strNewName = (null == dataCacheOut) ? NextFileName : dataCacheOut[index % dataCacheOut.Length].Name;
                        dataCacheNew[index % dataCacheNew.Length] =
                            new FileStream(strNewName, FileMode.OpenOrCreate, access, FILE_SHARE, 8, FILE_OPTIONS | options);
                    }
                }
            }
            else if (
                (3 == dataCache.Length && delta < UNDERFLOW_SIZE)
                || (dataCache.Length > 3 && delta < ((dataCache.Length - 3) * FILE_CACHE_SIZE + 4 * BUFFER_SIZE))
                )
            {
                dataCacheNew = (FileStream[])Array.CreateInstance(typeof(FileStream), dataCache.Length - 1);
                int indexStart = (int)(offsetStart / FILE_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    int index = indexStart + i;

                    if (i < dataCacheNew.Length)
                    {
                        dataCacheNew[index % dataCacheNew.Length] = dataCache[index % dataCache.Length];
                    }
                    else
                    {
                        dataCache[index % dataCache.Length].Dispose();
                        //dataCache[index % dataCache.Length] = null;
                    }
                }
            }

            return dataCacheNew;
        }

        private bool ResizeCache(long offsetStart, long delta)
        {
            FileStream[] dataCacheOut;
            FileStream[] dataCacheIn;

            lock (this)
            {
                dataCacheOut = _dataCacheOut;
                dataCacheIn = _dataCacheIn;
            }

            FileStream[] dataCacheOutNew = ResizeCache(dataCacheOut, null, offsetStart, delta);
            bool bIsResized = (dataCacheOut != dataCacheOutNew);

            if (bIsResized)
            {
                FileStream[] dataCacheInNew = ResizeCache(dataCacheIn, dataCacheOutNew, offsetStart, delta);

                lock (this)
                {
                    _dataCacheIn = dataCacheInNew;
                    _dataCacheOut = dataCacheOutNew;
                }
            }

            return bIsResized;
        }

        protected string NextFileName
        {
            get
            {
                int fileIndex = Interlocked.Increment(ref FILE_CACHE_COUNTER);

                return string.Format(FILE_CACHE_PATH, fileIndex);
            }
        }

        protected FileStream InFileStream
        {
            get
            {
                long readOffset;
                FileStream[] dataCacheIn;

                lock (this)
                {
                    readOffset = _readOffset;
                    dataCacheIn = _dataCacheIn;
                }

                int index = ((int)(readOffset / FILE_CACHE_SIZE)) % dataCacheIn.Length;

                return dataCacheIn[index];
            }
        }

        protected FileStream OutFileStream
        {
            get
            {
                long writeOffset;
                FileStream[] dataCacheOut;

                lock (this)
                {
                    writeOffset = _readOffset + _deltaSize;
                    dataCacheOut = _dataCacheOut;
                }

                int index = ((int)(writeOffset / FILE_CACHE_SIZE)) % dataCacheOut.Length;

                return dataCacheOut[index];
            }
        }

        protected virtual void OnRead(long offset, int count)
        {
            lock (this)
            {
                _readOffset += count;
                _deltaSize -= count;
            }
        }

        protected virtual void OnWrite(long offset, int count)
        {
            lock (this)
            {
                _deltaSize += count;
            }
        }

        public FileCacheStream()
        {
            Reset();
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { return _deltaSize; } }
        public override long Position { get { throw new NotSupportedException("get Position"); } set { throw new NotSupportedException("set Position"); } }

        protected override void Dispose(bool disposing)
        {
            // If disposing equals true, dispose all managed
            // and unmanaged resources.
            if (disposing)
            {
                // Dispose managed resources.
                if (null != _dataCacheIn)
                {
                    foreach (FileStream stream in _dataCacheIn)
                    {
                        stream.Dispose();
                    }
                    _dataCacheIn = null;
                }

                if (null != _dataCacheOut)
                {
                    foreach (FileStream stream in _dataCacheOut)
                    {
                        stream.Dispose();
                    }
                    _dataCacheOut = null;
                }
            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            IOResult result = (IOResult)asyncResult;
            IOState state = result.IOState;
            Stream stream = state.Stream;

            int nRead = 0;

            if (null != stream)
            {
                nRead = stream.EndRead(result.AsyncResult);

                OnRead(state.Offset, nRead);

                if (stream != InFileStream)
                {
                    stream.Seek(0, SeekOrigin.Begin);
                }
            }

            return nRead;
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            IOResult result = (IOResult)asyncResult;
            IOState state = result.IOState;
            Stream stream = state.Stream;

            stream.EndWrite(result.AsyncResult);
            OnWrite(state.Offset, state.Count);

            if (stream != OutFileStream)
            {
                //stream.Flush();
                stream.Seek(0, SeekOrigin.Begin);
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seek");
        }

        public override void SetLength(long value)
        {
            Stream streamPrev = OutFileStream;
            long writeOffset;

            lock (this)
            {
                if (value > _deltaSize)
                {
                    throw new NotSupportedException("SetLength");
                }
                else
                {
                    _deltaSize = value;
                    writeOffset = _readOffset + _deltaSize;
                }
            }

            Stream stream = OutFileStream;

            stream.Seek(writeOffset % FILE_CACHE_SIZE, SeekOrigin.Begin);

            if (streamPrev != stream)
            {
                streamPrev.Seek(0, SeekOrigin.Begin);
            }
        }

        public override void Flush()
        {
            OutFileStream.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Read");
        }

#if NET_45_OR_GREATER
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            FileStream[] dataCacheIn;
            long readOffset;
            long delta;

            lock (this)
            {
                dataCacheIn = _dataCacheIn;
                readOffset = _readOffset;
                delta = _deltaSize;
            }

            Task<int> result;

            if (delta < count)
            {
                //IOState stateIO = new IOState(null, readOffset, count, callback);
                result = Task<int>.Run(() =>
                {
                    return 0;
                }
                    , cancellationToken);
            }
            else
            {
                int index = ((int)(readOffset / FILE_CACHE_SIZE)) % dataCacheIn.Length;
                Stream fileIn = dataCacheIn[index];
                //IOState stateIO = new IOState(fileIn, readOffset, count, callback);

                result = fileIn.ReadAsync(buffer, offset, count, cancellationToken);

                result = result.ContinueWith<int>(t =>
                {
                    if (t.IsFaulted)
                    {
                        throw t.Exception.InnerException;
                    }

                    if (t.IsCanceled)
                    {
                        throw (null == t.Exception) ? new OperationCanceledException() : t.Exception.InnerException;
                    }

                    Stream stream = fileIn;
                    int nRead = t.Result;

                    OnRead(readOffset, nRead);

                    if (stream != InFileStream)
                    {
                        stream.Seek(0, SeekOrigin.Begin);
                    }

                    return nRead;
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            return result;
        }
#endif // NET_45_OR_GREATER

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            FileStream[] dataCacheIn;
            long readOffset;
            long delta;

            lock (this)
            {
                dataCacheIn = _dataCacheIn;
                readOffset = _readOffset;
                delta = _deltaSize;
            }

            IAsyncResult result;

            if (delta < count)
            {
                //throw new EndOfStreamException();
                IOState stateIO = new IOState(null, readOffset, count, callback);
                result = new EOFResult(state);
                callback(new IOResult(result, stateIO));
            }
            else
            {
                int index = ((int)(readOffset / FILE_CACHE_SIZE)) % dataCacheIn.Length;
                Stream fileIn = dataCacheIn[index];
                IOState stateIO = new IOState(fileIn, readOffset, count, callback);

                result = fileIn.BeginRead(buffer, offset, count, stateIO.OnIOComplete, state);
            }

            return result;
        }

#if NET_45_OR_GREATER
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            FileStream[] dataCacheOut;
            long readOffset;
            long delta;

            lock (this)
            {
                dataCacheOut = _dataCacheOut;
                readOffset = _readOffset;
                delta = _deltaSize;
            }

            long writeOffset = readOffset + delta;
            ResizeCache(readOffset, delta + count);

            int index = ((int)(writeOffset / FILE_CACHE_SIZE)) % dataCacheOut.Length;
            Stream fileOut = dataCacheOut[index];
            //IOState stateIO = new IOState(fileOut, writeOffset, count, callback);

            //return fileOut.BeginWrite(buffer, offset, count, stateIO.OnIOComplete, state);
            Task result = fileOut.WriteAsync(buffer, offset, count, cancellationToken);

            return result.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    throw t.Exception.InnerException;
                }

                if (t.IsCanceled)
                {
                    throw (null == t.Exception) ? new OperationCanceledException() : t.Exception.InnerException;
                }

                OnWrite(writeOffset, count);

                Stream stream = (Stream)fileOut;
                if (stream != OutFileStream)
                {
                    //stream.Flush();
                    stream.Seek(0, SeekOrigin.Begin);
                }
            }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
#endif // NET_45_OR_GREATER

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            FileStream[] dataCacheOut;
            long readOffset;
            long delta;

            lock (this)
            {
                dataCacheOut = _dataCacheOut;
                readOffset = _readOffset;
                delta = _deltaSize;
            }

            long writeOffset = readOffset + delta;
            ResizeCache(readOffset, delta + count);

            int index = ((int)(writeOffset / FILE_CACHE_SIZE)) % dataCacheOut.Length;
            Stream fileOut = dataCacheOut[index];
            IOState stateIO = new IOState(fileOut, writeOffset, count, callback);

            return fileOut.BeginWrite(buffer, offset, count, stateIO.OnIOComplete, state);
        }

        public void Reset()
        {
            FileStream[] dataCacheOut = {
                new FileStream(NextFileName, FileMode.OpenOrCreate, FileAccess.Write, FILE_SHARE, 8, FILE_OPTIONS|FileOptions.WriteThrough),
                new FileStream(NextFileName, FileMode.OpenOrCreate, FileAccess.Write, FILE_SHARE, 8, FILE_OPTIONS|FileOptions.WriteThrough)
                };
            FileStream[] dataCacheIn = {
                new FileStream(dataCacheOut[0].Name, FileMode.Open, FileAccess.Read, FILE_SHARE, 8, FILE_OPTIONS|FileFlagNoBuffering),
                new FileStream(dataCacheOut[1].Name, FileMode.Open, FileAccess.Read, FILE_SHARE, 8, FILE_OPTIONS|FileFlagNoBuffering)
                };

            lock (this)
            {
                _deltaSize = 0;
                _dataCacheIn = dataCacheIn;
                _dataCacheOut = dataCacheOut;
            }
        }
    }

    abstract class StreamPipeLineAbstract
    {
        private int _inReadOp = 1;
        private int _inWriteOp = 2;
        private int _deltaBuff;
        private ulong _writeOffset;

        private Stream _inStream;
        private Stream _outStream;

        private IStreamProxy _proxy;

        private Func<ulong, int, bool> _OnWriteCallback = delegate(ulong offset, int count) { return true; };
        private Func<ulong, int, bool> _OnReadCallback = delegate(ulong offset, int count) { return true; };
        private Action<ulong, int> _OnFlushCallback = delegate(ulong offset, int count) { };
        private Action<ulong> _OnEOFCallback = delegate(ulong offset) { };

        public Func<ulong, int, bool> OnReadCallback
        {
            set { _OnReadCallback = value; }
        }
        public Func<ulong, int, bool> OnWriteCallback
        {
            set { _OnWriteCallback = value; }
        }
        public Action<ulong, int> OnFlushCallback
        {
            set { _OnFlushCallback = value; }
        }

        public Action<ulong> OnEOFCallback
        {
            set { _OnEOFCallback = value; }
        }

        public Stream InStream
        {
            get
            {
                return _inStream;
            }
        }

        public Stream OutStream
        {
            get
            {
                return _outStream;
            }
        }

        public ulong WriteOffset
        {
            get
            {
                ulong writeOffset;

                lock (this)
                {
                    writeOffset = _writeOffset;
                }

                return writeOffset;
            }
        }

        public int DeltaOffset
        {
            get
            {
                int deltaOffset;

                lock (this)
                {
                    deltaOffset = _deltaBuff;
                }

                return deltaOffset;
            }
        }

        public StreamPipeLineAbstract(IStreamProxy proxy, Stream inStream, Stream outStream)
        {
            _proxy = proxy;
            _inStream = inStream;
            _outStream = outStream;
        }

#if NET_45_OR_GREATER
        public void Start()
        {
            ReadAsync();
        }

        public void Flush(Stream outStream)
        {
            FlushAsync(outStream);
        }
#endif // NET_45_OR_GREATER

        public bool Resume(Stream outStream)
        {
            bool bSuccess = false;

            if (DeltaOffset > 0 && _inWriteOp < 2)
            {
                _outStream = outStream;
#if NET_45_OR_GREATER
                WriteAsync();
#else
                _proxy.AddRef();
                if (!BeginWrite())
                {
                    _proxy.Release();
                }
#endif // NET_45_OR_GREATER

                bSuccess = true;
            }

            return bSuccess;
        }

#if !NET_45_OR_GREATER
        public void Start()
        {
            try
            {
                _proxy.AddRef();
                if (!BeginRead())
                {
                    _proxy.Release();
                }
            }
            catch (Exception e)
            {
                _proxy.WriteLogException(e);
                _proxy.Release();
            }
        }

        public void Flush(Stream outStream)
        {
            try
            {
                _proxy.AddRef();
                BeginFlush(outStream);
            }
            catch (Exception e)
            {
                _proxy.WriteLogException(e);
                _OnFlushCallback(_writeOffset, _deltaBuff);
                _proxy.Release();
            }
        }
#endif // NET_45_OR_GREATER

        protected void ResetDelta()
        {
            lock (this)
            {
                _deltaBuff = 0;
            }
        }

        protected abstract byte[] GetBuffer(ulong offset);
        protected abstract int GetOffset(ulong offset);
        protected abstract int GetReadBufferSize();
        protected abstract int GetWriteBufferSize();

        protected abstract bool OnReadContinue(ulong sentOffset, ulong recvOffset, int delta);
        protected abstract bool OnTryWriteOp(int delta);
        protected abstract bool OnTryReadOp(int delta);
        protected virtual void OnRead(ulong offset, int count) { }
        protected virtual void OnWrite(ulong offset, int count) { }

#if NET_45_OR_GREATER
        private async void ReadAsync()
        {
            bool bLoop = true;
            int iReadSize = GetReadBufferSize();

            _proxy.AddRef();
            do
            {
                int delta;
                ulong writeOffset;

                lock (this)
                {
                    delta = _deltaBuff;
                    writeOffset = _writeOffset;
                }

                ulong readOffset = writeOffset + (uint)delta;

                if (OnReadContinue(writeOffset, readOffset, delta + iReadSize))
                {
                    try
                    {
                        int read = await _inStream.ReadAsync(GetBuffer(readOffset), GetOffset(readOffset), iReadSize);

                        lock (this)
                        {
                            _deltaBuff += read;
                            delta = _deltaBuff;
                        }

                        bool bFlush = false;

                        if (read > 0)
                        {
                            OnRead(readOffset, read);

                            bLoop = _OnReadCallback(readOffset, read);
                            bFlush = !bLoop;
                        }
                        else
                        {
                            bLoop = false;
                            bFlush = true;
                            _OnEOFCallback(readOffset);
                        }

                        if (OnTryWriteOp(delta) || bFlush)
                        {
                            if (Interlocked.Decrement(ref _inWriteOp) > 0)
                            {
                                WriteAsync();
                            }
                            else
                            {
                                Interlocked.Increment(ref _inWriteOp);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        _proxy.WriteLogException(e);
                        bLoop = false;
                    }
                }
                else
                {
                    Interlocked.Increment(ref _inReadOp);
                    bLoop = false;
                }
            }
            while (bLoop);

            _proxy.Release();
        }

#else
        private bool BeginRead()
        {
            int delta;
            ulong writeOffset;

            lock (this)
            {
                delta = _deltaBuff;
                writeOffset = _writeOffset;
            }

            ulong readOffset = writeOffset + (uint)delta;
            int iReadSize = GetReadBufferSize();
            bool bBegin = true;

            if (OnReadContinue(writeOffset, readOffset, delta + iReadSize))
            {
                _inStream.BeginRead(GetBuffer(readOffset), GetOffset(readOffset), iReadSize, OnRead, readOffset);
            }
            else
            {
                Interlocked.Increment(ref _inReadOp);
                bBegin = false;
            }

            return bBegin;
        }
#endif // NET_45_OR_GREATER

#if NET_45_OR_GREATER
        private async void WriteAsync()
        {
            bool bLoop = true;
            int iWriteSize = GetWriteBufferSize();

            _proxy.AddRef();
            do
            {
                int delta;
                ulong writeOffset;

                lock (this)
                {
                    delta = _deltaBuff;
                    writeOffset = _writeOffset;
                }

                if (delta >= iWriteSize)
                {
                    try
                    {
                        await _outStream.WriteAsync(GetBuffer(writeOffset), GetOffset(writeOffset), iWriteSize);

                        lock (this)
                        {
                            _writeOffset += (ulong)iWriteSize;
                            _deltaBuff -= iWriteSize;
                            delta = _deltaBuff;
                        }

                        OnWrite(writeOffset, iWriteSize);

                        bLoop = _OnWriteCallback(writeOffset, iWriteSize);
                        if (bLoop)
                        {
                            if (OnTryReadOp(delta))
                            {
                                if (Interlocked.Decrement(ref _inReadOp) > 0)
                                {
                                    ReadAsync();
                                }
                                else
                                {
                                    Interlocked.Increment(ref _inReadOp);
                                }
                            }
                        }
                        else
                        {
                            _outStream.Flush();
                        }
                    }
                    catch (Exception e)
                    {
                        _proxy.WriteLogException(e);
                        bLoop = false;
                    }
                }
                else
                {
                    Interlocked.Increment(ref _inWriteOp);
                    bLoop = false;
                }
            }
            while (bLoop);

            _proxy.Release();
        }
#else
        private bool BeginWrite()
        {
            int delta;
            ulong writeOffset;

            lock (this)
            {
                delta = _deltaBuff;
                writeOffset = _writeOffset;
            }

            bool bBegin = true;
            int iWriteSize = GetWriteBufferSize();

            if (delta >= iWriteSize)
            {
                //int iLen = Math.Min(iWriteSize, delta);
                _outStream.BeginWrite(GetBuffer(writeOffset), GetOffset(writeOffset), iWriteSize, OnWrite, writeOffset);
            }
            else
            {
                Interlocked.Increment(ref _inWriteOp);
                bBegin = false;
            }

            return bBegin;
        }
#endif // NET_45_OR_GREATER

#if NET_45_OR_GREATER
        private async void FlushAsync(Stream outStream)
        {
            int writeCount;
            ulong writeOffset;

            lock (this)
            {
                writeCount = _deltaBuff;
                writeOffset = _writeOffset;
            }

            if (writeCount > 0)
            {
                _proxy.AddRef();

                try
                {
                    await outStream.WriteAsync(GetBuffer(writeOffset), GetOffset(writeOffset), writeCount);

                    lock (this)
                    {
                        writeOffset = _writeOffset;
                        writeCount = _deltaBuff;
                        _writeOffset += (ulong)writeCount;
                    }

                    _OnFlushCallback(writeOffset, writeCount);
                }
                catch (Exception e)
                {
                    _proxy.WriteLogException(e);
                }

                _proxy.Release();
            }
            else
            {
                _OnFlushCallback(writeOffset, writeCount);
            }
        }
#else
        private void BeginFlush(Stream outStream)
        {
            int delta;
            ulong writeOffset;

            lock (this)
            {
                delta = _deltaBuff;
                writeOffset = _writeOffset;
            }

            if (delta > 0)
            {
                outStream.BeginWrite(GetBuffer(writeOffset), GetOffset(writeOffset), delta, OnFlush, outStream);
            }
            else
            {
                _OnFlushCallback(writeOffset, delta);
                _proxy.Release();
            }
        }
#endif // NET_45_OR_GREATER

#if !NET_45_OR_GREATER
        private void OnRead(IAsyncResult asyncResult)
        {
            try
            {
                int delta;
                int read = _inStream.EndRead(asyncResult);
                ulong readOffset = (ulong)asyncResult.AsyncState;

                lock (this)
                {
                    _deltaBuff += read;
                    delta = _deltaBuff;
                }

                bool bBegin = false;
                bool bFlush = false;

                if (read > 0)
                {
                    OnRead(readOffset, read);

                    if (_OnReadCallback(readOffset, read))
                    {
                        bBegin = BeginRead();
                    }
                    else
                    {
                        bFlush = true;
                    }
                }
                else
                {
                    bFlush = true;
                    _OnEOFCallback(readOffset);
                }

                if (OnTryWriteOp(delta) || bFlush)
                {
                    if (Interlocked.Decrement(ref _inWriteOp) > 0)
                    {
                        _proxy.AddRef();
                        if (!BeginWrite())
                        {
                            _proxy.Release();
                        }
                    }
                    else
                    {
                        Interlocked.Increment(ref _inWriteOp);
                    }
                }

                if (!bBegin)
                {
                    _proxy.Release();
                }
            }
            catch (Exception e)
            {
                _proxy.WriteLogException(e);
                _proxy.Release();
            }
        }
        private void OnWrite(IAsyncResult asyncResult)
        {
            try
            {
                _outStream.EndWrite(asyncResult);

                int delta;
                int writeCount = GetWriteBufferSize();
                ulong writeOffset = (ulong)asyncResult.AsyncState;

                lock (this)
                {
                    _writeOffset += (ulong)writeCount;
                    _deltaBuff -= writeCount;
                    delta = _deltaBuff;
                }

                OnWrite(writeOffset, writeCount);

                if (_OnWriteCallback(writeOffset, writeCount))
                {
                    bool bBegin = BeginWrite();

                    if (OnTryReadOp(delta))
                    {
                        if (Interlocked.Decrement(ref _inReadOp) > 0)
                        {
                            _proxy.AddRef();
                            if (!BeginRead())
                            {
                                _proxy.Release();
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref _inReadOp);
                        }
                    }

                    if (!bBegin)
                    {
                        _proxy.Release();
                    }
                }
                else
                {
                    _outStream.Flush();
                    _proxy.Release();
                }
            }
            catch (Exception e)
            {
                _proxy.WriteLogException(e);
                _proxy.Release();
            }
        }
        private void OnFlush(IAsyncResult asyncResult)
        {
            try
            {
                Stream stream = (Stream)asyncResult.AsyncState;
                stream.EndWrite(asyncResult);

                ulong writeOffset;
                int writeCount;

                lock (this)
                {
                    writeOffset = _writeOffset;
                    writeCount = _deltaBuff;
                    _writeOffset += (ulong)writeCount;
                }

                _OnFlushCallback(writeOffset, writeCount);
            }
            catch (Exception e)
            {
                _proxy.WriteLogException(e);
            }

            _proxy.Release();
        }
#endif // !NET_45_OR_GREATER
    }

    class StreamPipeLine : StreamPipeLineAbstract
    {
        private const int BUFFER_SIZE = 0x2000;
        private const int CACHE_SIZE = 0xc;
        private const int BUFFER_CACHE_SIZE = CACHE_SIZE * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0x8 * BUFFER_SIZE;
        private const int UNDERFLOW_SIZE = 0x4 * BUFFER_SIZE;

        private readonly byte[] _dataCache = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];


        public StreamPipeLine(IStreamProxy proxy, Stream inStream, Stream outStream) :
            base(proxy, inStream, outStream)
        {
        }

        protected override byte[] GetBuffer(ulong offset)
        {
            return _dataCache;
        }
        protected override int GetOffset(ulong offset)
        {
            return (int)(offset % BUFFER_CACHE_SIZE);
        }
        protected override int GetReadBufferSize()
        {
            return BUFFER_SIZE;
        }
        protected override int GetWriteBufferSize()
        {
            return BUFFER_SIZE;
        }

        protected override bool OnReadContinue(ulong sentOffset, ulong recvOffset, int delta)
        {
            return (delta < BUFFER_CACHE_SIZE);
        }
        protected override bool OnTryWriteOp(int delta)
        {
            return (delta >= OVERFLOW_SIZE);
        }
        protected override bool OnTryReadOp(int delta)
        {
            return (delta <= UNDERFLOW_SIZE);
        }
    }

    class StreamPipeLineEx : StreamPipeLineAbstract
    {
        public const int BUFFER_SIZE = 0x2000;
        private const int CACHE_SIZE = 0x200;
        private const int BUFFER_CACHE_SIZE = CACHE_SIZE * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0x20 * BUFFER_SIZE;

        private readonly int _readBufferSize = BUFFER_SIZE;
        private readonly byte[] _dataCache = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];

        public StreamPipeLineEx(IStreamProxy proxy, Stream inStream, Stream outStream, int readBufferSize) :
            base(proxy, inStream, outStream)
        {
            _readBufferSize = readBufferSize;
            if (0 == _readBufferSize)
            {
                _readBufferSize = GetWriteBufferSize();
            }
        }

        protected override byte[] GetBuffer(ulong offset)
        {
            return _dataCache;
        }
        protected override int GetOffset(ulong offset)
        {
            return (int)(offset % BUFFER_CACHE_SIZE);
        }
        protected override int GetReadBufferSize()
        {
            return _readBufferSize;
        }
        protected override int GetWriteBufferSize()
        {
            return BUFFER_SIZE;
        }

        protected override bool OnReadContinue(ulong sentOffset, ulong recvOffset, int delta)
        {
            return true;
        }
        protected override bool OnTryWriteOp(int delta)
        {
            return (delta >= OVERFLOW_SIZE);
        }
        protected override bool OnTryReadOp(int delta)
        {
            return false;
        }
        protected override void OnRead(ulong offset, int count)
        {
            if (DeltaOffset > (BUFFER_CACHE_SIZE - BUFFER_SIZE))
            {
                Log.Message(string.Format("Pipeline buffer size has exceeded its limit."));
                ResetDelta();
            }
            else
            {
                int offsetBuff = GetOffset(offset);

                if (offsetBuff < BUFFER_SIZE)
                {
                    int iLen = Math.Min(count, BUFFER_SIZE - offsetBuff);
                    Buffer.BlockCopy(_dataCache, offsetBuff, _dataCache, BUFFER_CACHE_SIZE + offsetBuff, iLen);
                }

                if ((offsetBuff + count) > BUFFER_CACHE_SIZE)
                {
                    Buffer.BlockCopy(_dataCache, BUFFER_CACHE_SIZE, _dataCache, 0, (offsetBuff + count) - BUFFER_CACHE_SIZE);
                }
            }
        }
    }

    interface IMemoryCacheClass
    {
        string Name
        {
            get;
        }
        int Size
        {
            get;
        }
        int FileSize
        {
            get;
        }
    }

    class MemoryCacheClass16 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache16";
        public const int MEMORY_CACHE_SIZE = 0x40;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass32 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache32";
        public const int MEMORY_CACHE_SIZE = 0x80;
        public const int FILE_CACHE_SIZE = MemoryCacheClass16.FILE_CACHE_SIZE;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass64 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache64";
        public const int MEMORY_CACHE_SIZE = 0x100;
        public const int FILE_CACHE_SIZE = MemoryCacheClass16.FILE_CACHE_SIZE;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass128 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache128";
        public const int MEMORY_CACHE_SIZE = 0x200;
        public const int FILE_CACHE_SIZE = MemoryCacheClass16.FILE_CACHE_SIZE;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass256 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache256";
        public const int MEMORY_CACHE_SIZE = 0x400;
        public const int FILE_CACHE_SIZE = MemoryCacheClass16.FILE_CACHE_SIZE;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }

    class MemoryCacheClass512 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache512";
        public const int MEMORY_CACHE_SIZE = 0x800;
        public const int FILE_CACHE_SIZE = 0x4;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass1024 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache1024";
        public const int MEMORY_CACHE_SIZE = 0x1000;
        public const int FILE_CACHE_SIZE = 0x0;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }
    class MemoryCacheClass2048 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache2048";
        public const int MEMORY_CACHE_SIZE = 0x2000;
        public const int FILE_CACHE_SIZE = 0x0;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return MEMORY_CACHE_SIZE; }
        }
        public int FileSize
        {
            get { return FILE_CACHE_SIZE; }
        }
    }

    class StreamProxyEx<ClassMC> : StreamProxyAbstract where ClassMC : IMemoryCacheClass, new()
    {
        private const int RECV_BUFFER_SIZE = 0x800;
        private const int BUFFER_SIZE = 0x2000;
        private const int BUFFER_CACHE_SIZE = 0x20 * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0x8 * BUFFER_SIZE;

        private static readonly string CLASS_NAME = MemoryCacheClass32.CLASS_NAME;
        private static readonly int CACHE_SIZE = MemoryCacheClass32.MEMORY_CACHE_SIZE;
        private static readonly long FILE_SIZE = MemoryCacheClass32.FILE_CACHE_SIZE * FileCacheStream.FILE_CACHE_SIZE;
        private static readonly long UNDER_FILE_SIZE = (BUFFER_CACHE_SIZE * MemoryCacheClass16.MEMORY_CACHE_SIZE) >> 1;
        private static readonly bool SHARED_TIME_SHIFT = true;

        private ulong _prevSentBytes;
        private ulong _lastSentBytes;
        private ulong _offsetEOF;
        private ulong _offsetEOFEx;
        private long _underflowFileSize;
        private int _timeLastSendOp;
        private DateTime _lastTimeSendOp = DateTime.MinValue;
        private DateTime _lastTimeSendOpPrev = DateTime.MinValue;
        private ulong _lastSendOpBytes;

        private byte[][] _dataCache = new byte[2][];
        private FileCacheStream _dataFileCache = null;
        private FileCacheStream _dataPrevFileCache = null;
        private StreamPipeLine _pipeFromFileCache = null;
        private StreamPipeLineEx _pipeToFileCache = null;
        private StreamPipeLineEx _pipePrevToFileCache = null;

        static StreamProxyEx()
        {
            ClassMC mc = new ClassMC();
            CLASS_NAME = mc.Name;
            CACHE_SIZE = mc.Size;
            ProxyStreamConfig proxyStreamFile = ProxyStreamConfig.GetSection();
            FILE_SIZE = (null != proxyStreamFile && proxyStreamFile.DisableFileCache) ? 0 : mc.FileSize * FileCacheStream.FILE_CACHE_SIZE;
            SHARED_TIME_SHIFT = !(null != proxyStreamFile && proxyStreamFile.DisableSharedTimeShift);

            Log.Message("Cache configuration:");
            Log.Message("---------------------");
            Log.Message(string.Format("Proxy stream name: {0}", CLASS_NAME));
            Log.Message(string.Format("Memory cache size limit: {0} MB", CACHE_SIZE * BUFFER_CACHE_SIZE / (1024 * 1024)));
            Log.Message(string.Format("File cache size limit: {0} GB", FILE_SIZE / (1024 * 1024 * 1024)));
            Log.Message(string.Format("Buffer size: {0} KB", BUFFER_CACHE_SIZE / 1024));
            Log.Message(string.Format("Overflowing size: {0} KB", OVERFLOW_SIZE / 1024));
            Log.Message(string.Format("Concurrent streams limit: {0}", MAX_STREAM_PROXY_COUNT));
        }

        public static string Name
        {
            get { return CLASS_NAME; }
        }

        public StreamProxyEx(HttpClient connection, string mrl, Stream inStream)
            : base(connection, mrl, inStream)
        {
            _dataCache[0] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
            _dataCache[1] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
            Start();
        }

        private bool ResizeCache(ulong offsetStart, int delta)
        {
            bool bIsResized = false;

            if (delta > ((_dataCache.Length - 1) * BUFFER_CACHE_SIZE - BUFFER_SIZE))
            {
                byte[][] dataCache = new byte[_dataCache.Length + 2][];
                int indexStart = (int)(offsetStart / BUFFER_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    int index = indexStart + i;

                    if (i < _dataCache.Length)
                    {
                        dataCache[index % dataCache.Length] = _dataCache[index % _dataCache.Length];
                    }
                    else
                    {
                        dataCache[index % dataCache.Length] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
                    }
                }

                lock (this)
                {
                    _dataCache = dataCache;
                    bIsResized = true;
                }
            }
            else if (
                (4 == _dataCache.Length && delta < OVERFLOW_SIZE)
                || (_dataCache.Length > 5 && delta < ((_dataCache.Length - 5) * BUFFER_CACHE_SIZE + BUFFER_SIZE))
                )
            {
                byte[][] dataCache = new byte[_dataCache.Length - 2][];
                int indexStart = (int)(offsetStart / BUFFER_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    int index = indexStart + i;
                    dataCache[index % dataCache.Length] = _dataCache[index % _dataCache.Length];
                }

                lock (this)
                {
                    _dataCache = dataCache;
                    bIsResized = true;
                }
            }

            return bIsResized;
        }

        private void ClearCache()
        {
            byte[][] dataCache = new byte[2][];
            dataCache[0] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
            dataCache[1] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];

            lock (this)
            {
                ResetDelta();
                _dataCache = dataCache;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (null != _dataFileCache)
                {
                    _dataFileCache.Dispose();
                    _dataFileCache = null;
                }

                if (null != _dataPrevFileCache)
                {
                    _dataPrevFileCache.Dispose();
                    _dataPrevFileCache = null;
                }
            }

            base.Dispose(disposing);
        }

        protected byte[] GetPrevBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            int ndx = (int)(offset / BUFFER_CACHE_SIZE);
            int index = (ndx > 0) ? ((ndx - 1) % dataCache.Length) : 0;

            return dataCache[index];
        }

        protected byte[] GetNextBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            int index = ((int)(offset / BUFFER_CACHE_SIZE) + 1) % dataCache.Length;

            return dataCache[index];
        }

        protected override byte[] GetBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            int index = (int)(offset / BUFFER_CACHE_SIZE) % dataCache.Length;

            return dataCache[index];
        }

        protected override int GetOffset(ulong offset)
        {
            return (int)(offset % BUFFER_CACHE_SIZE);
        }

        protected override int GetReceiveBufferSize()
        {
            return RECV_BUFFER_SIZE;
        }

        protected override int GetSentBufferSize()
        {
            return BUFFER_SIZE;
        }

        protected override bool OnReceive(ulong offset, int count)
        {
            int offsetBuff = GetOffset(offset);

            if (offsetBuff < BUFFER_SIZE)
            {
                int iLen = Math.Min(count, BUFFER_SIZE - offsetBuff);
                Buffer.BlockCopy(GetBuffer(offset), offsetBuff, GetPrevBuffer(offset), BUFFER_CACHE_SIZE + offsetBuff, iLen);
            }

            if ((offsetBuff + count) > BUFFER_CACHE_SIZE)
            {
                Buffer.BlockCopy(GetBuffer(offset), BUFFER_CACHE_SIZE, GetNextBuffer(offset), 0, (offsetBuff + count) - BUFFER_CACHE_SIZE);
            }

            bool bDoRead = true;

            if (_dataCache.Length > (CACHE_SIZE - 1))
            {
                Log.Message(string.Format("{0}: Size of memory cache has exceeded its limit.", MrlIP));
                if (FILE_SIZE > 0)
                {
                    _dataFileCache = new FileCacheStream();
                    _pipeToFileCache = new StreamPipeLineEx(this, _inStream, _dataFileCache, RECV_BUFFER_SIZE);
                    _pipeToFileCache.OnReadCallback = OnReadFromNetCallback;
                    _pipeToFileCache.OnWriteCallback = OnWriteToFileCallback;
                    _pipeToFileCache.OnFlushCallback = OnFlushToNetCallback;
                    _pipeToFileCache.Start();

                    bDoRead = false;
                }
                else
                {
                    ClearCache();
                }
            }
            else if (_lastTimeSendOp > DateTime.MinValue && _lastTimeSendOp < DateTime.UtcNow)
            {
                bDoRead = false;
            }


            return bDoRead;
        }

        protected override bool OnSend(ulong offset, int count)
        {
            DateTime timeNow = DateTime.UtcNow;

            if (LastUpdate.AddSeconds(1) < timeNow)
            {
                TimeSpan timeDiff = (timeNow - LastUpdate);
                _timeLastSendOp = (int)timeDiff.TotalSeconds;
                if (!Log.EnableFileLog)
                {
                    ulong sentBytes = offset + (ulong)count;
                    double mseconds = Math.Max(1, timeDiff.TotalMilliseconds);
                    string message = string.Format(
                                            "<{0}>: Streaming {1} at {2:F1} Mbit/s, memory[{3}%]: {4}",
                                            MrlIP,
                                            Log.GetSizeString(sentBytes),
                                            ((double)(sentBytes - _lastSentBytes) * 8000) / (mseconds * 1000000),
                                            ((long)DeltaOffset * 100) / (CACHE_SIZE * BUFFER_CACHE_SIZE),
                                            Log.GetSmallSizeString(DeltaOffset)
                                            );

                    StrMessageLog = message;
                    _lastSentBytes = sentBytes;
                }
                LastUpdate = timeNow;
            }

            return true;
        }

        protected override bool OnReceiveContinue(ulong sentOffset, ulong recvOffset, int delta)
        {
            ResizeCache(sentOffset, delta);

            return true;
        }

        protected override bool OnTrySendOp(int delta)
        {
            return (null == _pipeFromFileCache && delta < (OVERFLOW_SIZE + 2 * BUFFER_SIZE) && delta >= OVERFLOW_SIZE);
        }

        protected override bool OnTryReceiveOp(int delta)
        {
            if (null != _pipeToFileCache && 0 == delta)
            {
                DateTime timeNow = DateTime.UtcNow;
                LastUpdate = timeNow;
                _lastSentBytes = SentOffset;
                StrMessageLog = string.Empty;

                _prevSentBytes = _lastSentBytes;
                _underflowFileSize = Math.Min(_dataFileCache.Length >> 1, UNDER_FILE_SIZE);

                if (_underflowFileSize > OVERFLOW_SIZE)
                {
                    _pipeFromFileCache = new StreamPipeLine(this, _dataFileCache, new NetworkStream(_outSocket, FileAccess.Write, false));
                    _pipeFromFileCache.OnWriteCallback = OnWriteToNetCallback;
                    _pipeFromFileCache.OnEOFCallback = OnEndOfFileCallback;
                    _pipeFromFileCache.Start();
                }
                else
                {
                    _underflowFileSize = -1;
                }

                ClearCache();
            }

            return false;
        }

        protected override bool OnOutSocketRead()
        {
            bool bInStreamKeep = false;

            if (SHARED_TIME_SHIFT && ReceiveOffset > 0)
            {
                DateTime timeNow = DateTime.UtcNow;
                int timeLastSendOpMax = (int)(timeNow - LastUpdate).TotalSeconds;

                if (timeLastSendOpMax < _timeLastSendOp)
                {
                    timeLastSendOpMax = _timeLastSendOp;
                }

                bInStreamKeep = (timeLastSendOpMax > 24);
                bool bIsStreamSniffed = false;

                if (_lastTimeSendOpPrev > DateTime.MinValue)
                {
                    StreamPipeLineAbstract fileCache = _pipeFromFileCache;
                    ulong SendOpBytes;
                    if (null != fileCache)
                    {
                        SendOpBytes = SentOffset + _pipeFromFileCache.WriteOffset;
                    }
                    else
                    {
                        SendOpBytes = SentOffset;
                    }

                    if (SendOpBytes < _lastSendOpBytes + 128 * 1024)
                    {
                        _lastSendOpBytes = SendOpBytes;
                        bIsStreamSniffed = true;
                    }
                }

                if (bInStreamKeep || bIsStreamSniffed)
                {
                    Log.Message(string.Format("{0}: Keep stream alive for two minutes.", MrlIP));
                    if (!bInStreamKeep)
                    {
                        _lastTimeSendOp = _lastTimeSendOpPrev;
                        //_lastTimeSendOpPrev = DateTime.MinValue;
                        bInStreamKeep = true;
                    }
                    else
                    {
                        _lastTimeSendOp = timeNow.AddMinutes(2).AddSeconds(8);
                    }
                }
            }

            return bInStreamKeep;
        }

        protected override bool OnResumeSend()
        {
            bool bSuccess = (_lastTimeSendOp > DateTime.MinValue && _lastTimeSendOp > DateTime.UtcNow);

            if (bSuccess)
            {
                if (DeltaOffset > 0)
                {
                    bSuccess = Resume();
                }
                else if (null != _pipeFromFileCache)
                {
                    bSuccess = _pipeFromFileCache.Resume(new NetworkStream(_outSocket, FileAccess.Write, false));
                }
            }

            if (bSuccess)
            {
                _lastTimeSendOpPrev = _lastTimeSendOp;
                _lastTimeSendOp = DateTime.MinValue;
            }
            else
            {
                _inStream.Flush();
            }

            return bSuccess;
        }

        protected bool OnReadFromNetCallback(ulong offset, int count)
        {
            bool bDoRead = true;

            if (_dataFileCache.Length < _underflowFileSize || _underflowFileSize < 0)
            {
                Log.Message(string.Format("{0}: File cache is empty.", MrlIP));

                _offsetEOFEx = (offset + (ulong)count);
                Continue(_offsetEOFEx);
                _offsetEOFEx -= _offsetEOFEx % StreamPipeLineEx.BUFFER_SIZE;

                bDoRead = false;
            }

            if (bDoRead && _lastTimeSendOp > DateTime.MinValue && _lastTimeSendOp < DateTime.UtcNow)
            {
                bDoRead = false;
            }

            return bDoRead;
        }

        protected void OnEndOfFileCallback(ulong offset)
        {
            _offsetEOF = offset;
        }

        protected bool OnWriteToFileCallback(ulong offset, int count)
        {
            bool bDoWrite = true;

            if (_offsetEOFEx == (offset + (ulong)count))
            {
                if (_underflowFileSize < 0)
                {
                    _dataFileCache.Dispose();
                    _dataFileCache = null;
                    _pipeToFileCache = null;
                }
                else
                {
                    _dataPrevFileCache = _dataFileCache;
                    _pipePrevToFileCache = _pipeToFileCache;
                    _pipeToFileCache = null;
                }

                _underflowFileSize = 0;
                _offsetEOFEx = 0;

                bDoWrite = false;
            }
            else if (_dataFileCache.Length > (FILE_SIZE + (FileCacheStream.FILE_CACHE_SIZE>>1)))
            {
                Log.Message(string.Format("{0}: Size of file cache has exceeded its limit.", MrlIP));
                //_dataFileCache.SetLength(BUFFER_CACHE_SIZE * MemoryCacheClass16.MEMORY_CACHE_SIZE);
                _dataFileCache.SetLength(FILE_SIZE>>1);
            }

            return bDoWrite;
        }

        protected bool OnWriteToNetCallback(ulong offset, int count)
        {
            bool bDoWrite = true;
            ulong offsetEnd = offset + (ulong)count;

            if (_offsetEOF == offsetEnd)
            {
                //Log.Message("OnWriteToNetCallback(ulong offset, int count)");
                _pipePrevToFileCache.Flush(_pipeFromFileCache.OutStream);
                _offsetEOF = 0;

                bDoWrite = false;
            }
            else
            {
                DateTime timeNow = DateTime.UtcNow;

                if (LastUpdate.AddSeconds(1) < timeNow)
                {
                    TimeSpan timeDiff = (timeNow - LastUpdate);
                    _timeLastSendOp = (int)timeDiff.TotalSeconds;

                    if (!Log.EnableFileLog)
                    {
                        ulong sentBytes = _prevSentBytes + offsetEnd;
                        double mseconds = Math.Max(1, (timeNow - LastUpdate).TotalMilliseconds);
                        string message = string.Format(
                                                "<{0}>: Streaming {1} at {2:F1} Mbit/s, file[{3}%]: {4}",
                                                MrlIP,
                                                Log.GetSizeString(sentBytes),
                                                ((double)(sentBytes - _lastSentBytes) * 8000) / (mseconds * 1000000),
                                                (_dataFileCache.Length * 100) / FILE_SIZE,
                                                Log.GetSizeString((ulong)_dataFileCache.Length)
                                                );

                        _lastSentBytes = sentBytes;
                        StrMessageLog = message;
                    }
                    LastUpdate = timeNow;
                }
            }

            return bDoWrite;
        }

        protected void OnFlushToNetCallback(ulong offset, int count)
        {
            //Log.Message(string.Format("OnFlushToNetCallback(ulong offset, int count) {0}", this.CountOp));
            _pipePrevToFileCache = null;
            _pipeFromFileCache = null;
            _dataPrevFileCache.Dispose();
            _dataPrevFileCache = null;

            TrySendOp();
        }
    }

}