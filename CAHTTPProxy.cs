using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Diagnostics;
//using Microsoft.VisualC.StlClr;
using LibVLCPlugin;

namespace ZCAHTTPProxy
{
    public interface IStreamProxy
    {
        void WriteLogProgress();
    }

    public interface IStreamProxyServer
    {
        int AddNewStream(IStreamProxy streamProxy);
        void DeleteStream(IStreamProxy sock, int iIndex);
    }

    abstract class StreamProxyAbstract : IDisposable, IStreamProxy
    {
        private static int _nCountProxy;

        private IStreamProxyServer _server;
        private HttpClient _connection;
        private string _Mrl;
        private string _MrlIP;
        private Stream _inStream;
        private Socket _outSocket;
        private int _iProxyID;

        private int _inOp = 1;
        private int _inReceiveOp = 1;
        private int _inSendOp = 2;
        private int _deltaBuff;
        private ulong _sentOffset;

        private DateTime _timeStart = DateTime.UtcNow;
        private DateTime _lastUpdate = DateTime.UtcNow;
        private string _strMessageLog = string.Empty;
        private string _strLastMessageLog = string.Empty;


        public StreamProxyAbstract(HttpClient connection, string mrl, Stream inStream)
        {
            _server = (IStreamProxyServer)connection.Server;
            _connection = connection;
            _Mrl = mrl;
            _MrlIP = mrl;
            _inStream = inStream;
            _outSocket = connection.Socket;

            string strIP = mrl;
            int ndx = strIP.IndexOf('@');
            if (ndx > 0)
            {
                strIP = strIP.Substring(++ndx);
            }
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

        private void Dispose(bool disposing)
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

        protected virtual int AddRef()
        {
            return Interlocked.Increment(ref _inOp);
        }

        protected virtual void Release()
        {
            if (0 == Interlocked.Decrement(ref _inOp))
            {
                Interlocked.Decrement(ref _nCountProxy);
                _server.DeleteStream(this, _iProxyID);
                WriteLogCloseConnection();
                _connection.Release();
                Dispose();
            }
        }

        protected void ResetDelta()
        {
            lock (this)
            {
                _deltaBuff = 0;
            }
        }

        protected void Start()
        {
            try
            {
                Interlocked.Increment(ref _nCountProxy);
                _iProxyID = _server.AddNewStream(this);
                BeginReceive();
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }

        protected abstract byte[] GetBuffer(ulong offset);
        protected abstract int GetOffset(ulong offset);
        protected abstract int GetReceiveBufferSize();
        protected abstract int GetSentBufferSize();

        protected abstract bool OnReceive(ulong offset, int count);
        protected abstract bool OnSend(ulong offset, int count);
        protected abstract bool OnReceiveContinue(ulong sentOffset, ulong recvOffset, int delta);
        protected abstract bool OnTrySendOp(int delta);
        protected abstract bool OnTryReceiveOp(int delta);

        private void BeginReceive()
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

            if (OnReceiveContinue(sentOffset, recvOffset, delta + iRecvSize))
            {
                _inStream.BeginRead(GetBuffer(recvOffset), GetOffset(recvOffset), iRecvSize, OnReceive, recvOffset);
            }
            else
            {
                Interlocked.Increment(ref _inReceiveOp);
                Release();
            }

            if (OnTrySendOp(delta))
            {
                if (Interlocked.Decrement(ref _inSendOp) > 0)
                {
                    AddRef();
                    BeginSend();
                }
                else
                {
                    Interlocked.Increment(ref _inSendOp);
                }
            }
        }

        private void BeginSend()
        {
            int delta;
            ulong sentOffset;

            lock (this)
            {
                delta = _deltaBuff;
                sentOffset = _sentOffset;
            }

            if (delta > 0)
            {
                int iSentSize = GetSentBufferSize();
                int iLen = Math.Min(iSentSize, delta);
                _outSocket.BeginSend(GetBuffer(sentOffset), GetOffset(sentOffset), iLen, SocketFlags.None, OnSend, sentOffset);
            }
            else
            {
                Interlocked.Increment(ref _inSendOp);
                Release();
            }

            if (OnTryReceiveOp(delta))
            {
                if (Interlocked.Decrement(ref _inReceiveOp) > 0)
                {
                    AddRef();
                    BeginReceive();
                }
                else
                {
                    Interlocked.Increment(ref _inReceiveOp);
                }
            }
        }

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
                        BeginReceive();
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
                        BeginSend();
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
                WriteLogException(e);
                Release();
            }
        }

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

        private void WriteLogException(Exception e)
        {
            Log.Message(string.Format("Streaming {0} to address {1} is stopped, error:",
                    _MrlIP,
                    ((IPEndPoint)_outSocket.RemoteEndPoint).Address.ToString()
                    ).PadRight(Log.PadSize) + "\n" + e.Message);
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
        private const int BUFFER_SIZE = 0x2000;
        private const int CACHE_SIZE = 0x1A;
        private const int BUFFER_CACHE_SIZE = CACHE_SIZE * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0x10 * BUFFER_SIZE;
        private const int UNDERFLOW_SIZE = 0x8 * BUFFER_SIZE;

        public const string CLASS_NAME = "RealTime";

        private byte[] _dataCache = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];

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
                ulong seconds = Math.Max(1, (ulong)(timeNow - LastUpdate).TotalSeconds);
                string message = string.Format(
                                        "<{0}>: Streaming {1} at {2:F1} Mbit/s, buffer: {3}%",
                                        MrlIP,
                                        Log.GetSizeString(sentBytes),
                                        ((double)(sentBytes - _lastSentBytes) * 8) / (ulong)(seconds * 1000000),
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
    }

    class MemoryCacheClass16 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache16";
        public const int CACHE_SIZE = 0x40;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return CACHE_SIZE; }
        }

    }
    class MemoryCacheClass32 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache32";
        private const int CACHE_SIZE = 0x80;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return CACHE_SIZE; }
        }
    }
    class MemoryCacheClass64 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache64";
        public const int CACHE_SIZE = 0x100;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return CACHE_SIZE; }
        }
    }
    class MemoryCacheClass128 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache128";
        public const int CACHE_SIZE = 0x200;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return CACHE_SIZE; }
        }
    }
    class MemoryCacheClass256 : IMemoryCacheClass
    {
        public const string CLASS_NAME = "MemoryCache256";
        public const int CACHE_SIZE = 0x400;

        public string Name
        {
            get { return CLASS_NAME; }
        }
        public int Size
        {
            get { return CACHE_SIZE; }
        }
    }

    class StreamProxyEx<ClassMC> : StreamProxyAbstract where ClassMC : IMemoryCacheClass, new()
    {
        private const int RECV_BUFFER_SIZE = 0x800;
        private const int BUFFER_SIZE = 0x2000;
        private const int BUFFER_CACHE_SIZE = 0x20 * BUFFER_SIZE;
        private const int OVERFLOW_SIZE = 0x10 * BUFFER_SIZE;

        private static int CACHE_SIZE = MemoryCacheClass64.CACHE_SIZE;
        public static string CLASS_NAME = MemoryCacheClass64.CLASS_NAME;

        private byte[][] _dataCache = new byte[1][];

        private ulong _lastSentBytes;

        static StreamProxyEx()
        {
            ClassMC mc = new ClassMC();
            CLASS_NAME = mc.Name;
            CACHE_SIZE = mc.Size;

            Log.Message("Cache configuration:");
            Log.Message("---------------------");
            Log.Message(string.Format("Proxy stream name: {0}", CLASS_NAME));
            Log.Message(string.Format("Stream cache size limit: {0} MB", CACHE_SIZE * BUFFER_CACHE_SIZE / (1024 * 1024)));
            Log.Message(string.Format("Stream buffer size: {0} KB", BUFFER_CACHE_SIZE / 1024));
            Log.Message(string.Format("Overflowing size: {0} KB", OVERFLOW_SIZE / 1024));
        }

        public static string Name
        {
            get { return CLASS_NAME; }
        }

        public StreamProxyEx(HttpClient connection, string mrl, Stream inStream)
            : base(connection, mrl, inStream)
        {
            _dataCache[0] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
            Start();
        }

        private bool ResizeBuffer(ulong offsetStart, ulong offsetEnd, int delta)
        {
            bool bIsResized = false;

            if (delta > (_dataCache.Length * BUFFER_CACHE_SIZE - BUFFER_SIZE))
            {
                byte[][] dataCache = new byte[_dataCache.Length + 2][];

                long indexStart = (long)(offsetStart / BUFFER_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    if (i < _dataCache.Length)
                    {
                        dataCache[(indexStart + i) % dataCache.Length] = _dataCache[(indexStart + i) % _dataCache.Length];
                    }
                    else
                    {
                        dataCache[(indexStart + i) % dataCache.Length] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];
                    }
                }

                long indexEnd = (long)(offsetEnd / BUFFER_CACHE_SIZE);
                if (indexStart % _dataCache.Length == indexEnd % _dataCache.Length)
                {
                    int offsetBuff = GetOffset(offsetEnd);

                    if (offsetBuff < BUFFER_CACHE_SIZE / 2)
                    {
                        Buffer.BlockCopy(
                            _dataCache[indexStart % _dataCache.Length], 0,
                            dataCache[indexEnd % dataCache.Length], 0,
                            offsetBuff);
                    }
                    else
                    {
                        byte[] tmp = dataCache[indexEnd % dataCache.Length];
                        dataCache[indexEnd % dataCache.Length] = dataCache[indexStart % dataCache.Length];
                        dataCache[indexStart % dataCache.Length] = tmp;

                        offsetBuff = GetOffset(offsetStart);
                        Buffer.BlockCopy(
                            _dataCache[indexStart % _dataCache.Length], offsetBuff,
                            dataCache[indexStart % dataCache.Length], offsetBuff,
                            _dataCache[indexStart % _dataCache.Length].Length - offsetBuff);
                    }
                }

                lock (this)
                {
                    _dataCache = dataCache;
                    bIsResized = true;
                }
            }
            else if (_dataCache.Length > 3 && delta < ((_dataCache.Length - 3) * BUFFER_CACHE_SIZE + OVERFLOW_SIZE))
            {
                byte[][] dataCache = new byte[_dataCache.Length - 1][];
                long indexStart = (long)(offsetStart / BUFFER_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    dataCache[(indexStart + i) % dataCache.Length] = _dataCache[(indexStart + i) % _dataCache.Length];
                }

                lock (this)
                {
                    _dataCache = dataCache;
                    bIsResized = true;
                }
            }
            else if (3 == _dataCache.Length && delta < OVERFLOW_SIZE)
            {
                byte[][] dataCache = new byte[1][];
                long indexStart = (long)(offsetStart / BUFFER_CACHE_SIZE);

                for (int i = 0; i < dataCache.Length; i++)
                {
                    dataCache[(indexStart + i) % dataCache.Length] = _dataCache[(indexStart + i) % _dataCache.Length];
                }

                bool bCopy = true;
                long indexEnd = (long)(offsetEnd / BUFFER_CACHE_SIZE);
                if (indexStart % _dataCache.Length == indexEnd % _dataCache.Length)
                {
                    bCopy = false;
                }

                if (bCopy)
                {
                    Buffer.BlockCopy(
                        _dataCache[indexEnd % _dataCache.Length], 0,
                        dataCache[indexStart % dataCache.Length], 0,
                        GetOffset(offsetEnd));
                }

                lock (this)
                {
                    _dataCache = dataCache;
                    bIsResized = true;
                }
            }

            return bIsResized;
        }

        private void ClearBuffer()
        {
            byte[][] dataCache = new byte[1][];
            dataCache[0] = new byte[BUFFER_CACHE_SIZE + BUFFER_SIZE];

            lock (this)
            {
                _dataCache = dataCache;
            }

            ResetDelta();
        }

        protected byte[] GetPrevBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            long ndx = (long)(offset / BUFFER_CACHE_SIZE);
            int index = (ndx > 0) ? ((int)((long)(ndx - 1) % dataCache.LongLength)) : 0;

            return dataCache[index];
        }

        protected byte[] GetNextBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            int index = (int)((long)(offset / BUFFER_CACHE_SIZE + 1) % dataCache.LongLength);

            return dataCache[index];
        }

        protected override byte[] GetBuffer(ulong offset)
        {
            byte[][] dataCache;

            lock (this)
            {
                dataCache = _dataCache;
            }

            int index = (int)((long)(offset / BUFFER_CACHE_SIZE) % dataCache.LongLength);

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

            if (_dataCache.Length >= (CACHE_SIZE - 1))
            {
                Log.Message(string.Format("{0}: Stream cache size has exceeded its limit.", MrlIP));
                ClearBuffer();
            }
            else
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
                                        "<{0}>: Streaming {1} at {2:F1} Mbit/s, cache[{3}%]: {4}",
                                        MrlIP,
                                        Log.GetSizeString(sentBytes),
                                        ((double)(sentBytes - _lastSentBytes) * 8000) / (mseconds * 1000000),
                                        ((long)DeltaOffset * 100) / (CACHE_SIZE * BUFFER_CACHE_SIZE),
                                        Log.GetSmallSizeString(DeltaOffset)
                                        );

                LastUpdate = timeNow;
                _lastSentBytes = sentBytes;
                StrMessageLog = message;
            }

            return true;
        }

        protected override bool OnReceiveContinue(ulong sentOffset, ulong recvOffset, int delta)
        {
            bool bContinue = false;
            if (CountOp > 1 || delta < (OVERFLOW_SIZE + 2 * BUFFER_SIZE))
            {
                ResizeBuffer(sentOffset, recvOffset, delta);
                bContinue = true;
            }

            return bContinue;
        }
        protected override bool OnTrySendOp(int delta)
        {
            return (delta < (OVERFLOW_SIZE + 2 * BUFFER_SIZE) && delta >= OVERFLOW_SIZE);
        }

        protected override bool OnTryReceiveOp(int delta)
        {
            return false;
        }
    }

    public interface ITcpServer
    {
        int AddNewSocket(Socket sock, TcpListener listener);
        void DeleteSocket(Socket sock, TcpListener listener, int iIndex);
    }

    abstract class HttpClient
    {
        public delegate void CompleteCallback();

        private class SendState
        {
            private byte[] _body;
            private CompleteCallback _complete;

            public SendState(byte[] body, CompleteCallback complete)
            {
                _body = body;
                _complete = complete;
            }

            public byte[] Body
            {
                get
                {
                    return _body;
                }
            }


            public CompleteCallback Complete
            {
                get
                {
                    return _complete;
                }
            }
        }

        private const string CRLF = "\r\n";
        private const int MAX_HEADER_SIZE = 0x800;
        private const int MAX_BODY_SIZE = 0x10000;

        protected const string HTTP_VERSION = "1.1";

        private static byte[] arrCRLF = Encoding.ASCII.GetBytes(CRLF);
        private ITcpServer _server;
        private TcpListener _listener;
        private Socket _socket;
        private int _iIndex;

        byte[] _Buffer = new byte[MAX_HEADER_SIZE];
        byte[] _BodyBuffer;
        private string _strRequest;
        private string _strHeaders;
        private string _strBody;
        private int _iBodyLength;
        private string _Version;
        private string _Method;
        private string _Url;
        private string _Query;
        private NameValueCollection _Headers = new NameValueCollection();

        public HttpClient(ITcpServer server, Socket socket, TcpListener listener)
        {
            _server = server;
            _socket = socket;
            _listener = listener;
            _iIndex = _server.AddNewSocket(_socket, _listener);

            WriteLogNewConnection();

            try
            {
                BeginReceiveHeader(0);
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }

        public string RemoteEndPointString
        {
            get
            {
                return ((IPEndPoint)_socket.RemoteEndPoint).Address.ToString();
            }
        }

        public string Version
        {
            get
            {
                return _Version;
            }
        }

        public Uri Url
        {
            get
            {
                return new Uri(string.Format("http://{0}{1}", Host, _Url));
            }
        }

        public string UrlPath
        {
            get
            {
                return _Url;
            }
        }

        public string Query
        {
            get
            {
                return _Query;
            }
        }

        public string Method
        {
            get
            {
                return _Method;
            }
        }

        public string Host
        {
            get
            {
                return _Headers["Host"];
            }
        }

        public string ContentLength
        {
            get
            {
                return _Headers["Content-Length"];
            }
        }

        public string ContentType
        {
            get
            {
                return _Headers["Content-Type"];
            }
        }

        public string Connection
        {
            get
            {
                return _Headers["Connection"];
            }
        }

        public string UserAgent
        {
            get
            {
                return _Headers["User-Agent"];
            }
        }

        public string IcyMetadata
        {
            get
            {
                return _Headers["Icy-Metadata"];
            }
        }

        public string Body
        {
            get
            {
                return _strBody;
            }
        }

        public Socket Socket
        {
            get
            {
                return _socket;
            }
        }

        public int ID
        {
            get
            {
                return _iIndex;
            }
        }

        public ITcpServer Server
        {
            get
            {
                return _server;
            }
        }

        protected TcpListener Listener
        {
            get
            {
                return _listener;
            }
        }

        private void BeginReceiveHeader(int offset)
        {
            _socket.BeginReceive(_Buffer, offset, _Buffer.Length - offset, SocketFlags.None, OnReceiveHeader, offset);
        }

        private void BeginReceiveBody()
        {
            int iLen = Math.Min(_Buffer.Length, (_iBodyLength - _strBody.Length));
            _socket.BeginReceive(_Buffer, 0, iLen, SocketFlags.None, OnReceiveBody, null);
        }

        public virtual void Release()
        {
            WriteLogCloseConnection();
            _server.DeleteSocket(_socket, _listener, _iIndex);
            _socket.Close();
        }

        private void OnReceiveHeader(IAsyncResult asyncResult)
        {
            try
            {
                int read = _socket.EndReceive(asyncResult);
                if (read > 0)
                {
                    int offset = (int)asyncResult.AsyncState;
                    int ndx = offset;

                    while ((ndx = Array.FindIndex(_Buffer, ndx, (byte byChar) => { return (byChar.Equals((byte)'\n')); })) >= 0)
                    {
                        if (
                            ndx > 2
                            && (
                                arrCRLF[0] == _Buffer[ndx - 1]
                                && arrCRLF[1] == _Buffer[ndx - 2]
                                && arrCRLF[0] == _Buffer[ndx - 3]
                                || arrCRLF[1] == _Buffer[ndx - 1]
                                )
                            )
                        {
                            break;
                        }

                        ndx++;
                    }

                    int offsetEnd = offset + read;
                    if (ndx >= 0)
                    {
                        Encoding enc = Encoding.ASCII;
                        if (!Utf8Helper.Validate(enc, _Buffer, 0, ndx))
                        {
                            enc = (Utf8Helper.IsUtf8(_Buffer, 0, ndx)) ? Encoding.UTF8 : Encoding.Default;
                        }
                        _strRequest = enc.GetString(_Buffer, 0, ndx++);
                        _strBody = Encoding.ASCII.GetString(_Buffer, ndx, offsetEnd - ndx);
                        ndx = _strRequest.IndexOf(CRLF);
                        if (ndx > 0 && ++ndx <= _strRequest.Length && ++ndx <= _strRequest.Length)
                        {
                            _strHeaders = _strRequest.Substring(ndx);
                            _strRequest = _strRequest.Substring(0, ndx);
                        }
                        OnHttpHeaderComplete();
                    }
                    else
                    {
                        if (offsetEnd < _Buffer.Length)
                        {
                            BeginReceiveHeader(offsetEnd);
                        }
                        else
                        {
                            WriteLogException(new Exception(HttpStatusCode.RequestEntityTooLarge.ToString()));
                            Release();
                        }
                    }
                }
                else
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

        private void OnReceiveBody(IAsyncResult asyncResult)
        {
            try
            {
                int read = _socket.EndReceive(asyncResult);
                if (read > 0)
                {
                    _strBody += Encoding.ASCII.GetString(_Buffer, 0, read);
                    if (_strBody.Length < _iBodyLength)
                    {
                        BeginReceiveBody();
                    }
                    else
                    {
                        OnHttpRequestComplete();
                    }
                }
                else
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

        private void OnSendHeader(IAsyncResult asyncResult)
        {
            try
            {
                int sendCount = _socket.EndSendTo(asyncResult);

                if (sendCount > 0)
                {
                    SendState state = (SendState)asyncResult.AsyncState;

                    if (null != state.Body && state.Body.Length > 0)
                    {
                        _socket.BeginSend(state.Body, 0, state.Body.Length, SocketFlags.None, OnSendBody, state.Complete);
                    }
                    else
                    {
                        state.Complete();
                    }
                }
                else
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

        private void OnSendBody(IAsyncResult asyncResult)
        {
            try
            {
                int sendCount = _socket.EndSendTo(asyncResult);

                if (sendCount > 0)
                {
                    if (_BodyBuffer != _Buffer)
                    {
                        _BodyBuffer = null;
                    }
                    CompleteCallback state = (CompleteCallback)asyncResult.AsyncState;
                    state();
                }
                else
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

        private void OnHttpHeaderComplete()
        {
            Match ReqMatch = Regex.Match(_strRequest, @"^(\w+)\s+([^\s\?]+)(\?[^\s\?]+)?[^\s]*\s+HTTP/(\d\.\d)\r\n", RegexOptions.Compiled);

            if (Match.Empty != ReqMatch)
            {
                int ndx = 1;
                _Method = ReqMatch.Groups[ndx++].Value;
                _Url = ReqMatch.Groups[ndx++].Value;
                _Query = ReqMatch.Groups[ndx++].Value;
                _Version = ReqMatch.Groups[ndx++].Value;

                if (null != _Query)
                {
                    _Query = _Query.TrimStart(new char[] { '?' });
                }

                // Приводим ее к изначальному виду, преобразуя экранированные символы
                // Например, "%20" -> " "
                _Url = Uri.UnescapeDataString(_Url);
                _Query = Uri.UnescapeDataString(_Query);

                string[] arrHdr = _strHeaders.Split('\n');
                foreach (string str in arrHdr)
                {
                    string strStr = str.TrimEnd(CRLF.ToCharArray());
                    if (strStr.Length > 0)
                    {
                        ndx = strStr.IndexOf(':');
                        if (ndx >= 0)
                        {
                            _Headers.Add(strStr.Substring(0, ndx).Trim(), strStr.Substring(++ndx).Trim());
                        }
                    }
                }

                if ("GET" != _Method)
                {
                    string strLength = ContentLength;
                    if (null != strLength)
                    {
                        _iBodyLength = int.Parse(strLength);
                    }
                }

                if (_iBodyLength > MAX_BODY_SIZE)
                {
                    WriteLogException(new Exception(HttpStatusCode.RequestEntityTooLarge.ToString()));
                    Release();
                }
                else if (_strBody.Length < _iBodyLength)
                {
                    BeginReceiveBody();
                }
                else
                {
                    OnHttpRequestComplete();
                }
            }
            else
            {
                WriteLogException(new Exception(HttpStatusCode.BadRequest.ToString()));
                Release();
            }
        }

        private void OnEndSendAndKeepAlive()
        {
            _strRequest = String.Empty;
            _strHeaders = String.Empty;
            _strBody = String.Empty;
            _iBodyLength = 0;
            _Version = String.Empty;
            _Method = String.Empty;
            _Url = String.Empty;
            _Query = String.Empty;
            _Headers.Clear();

            BeginReceiveHeader(0);
        }

        private void OnEndSendAndClose()
        {
            Release();
        }

        protected abstract void OnHttpRequestComplete();

        protected void SendSuccess(string ContentType, string Body, Encoding encCodePage, CompleteCallback Complete)
        {
            //int Code = 200;
            //string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\n", HTTP_VERSION, Code, Code.ToString(), ContentType);
            SendResponse(Str, Body, encCodePage, (null == Complete) ? OnEndSendAndClose : Complete);
        }

        protected void SendSuccess(string ContentType, byte[] Body, CompleteCallback Complete)
        {
            //int Code = 200;
            //string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\n", HTTP_VERSION, Code, Code.ToString(), ContentType);
            SendResponse(Str, Body, (null == Complete) ? OnEndSendAndClose : Complete);
        }

        protected void SendError(HttpStatusCode code)
        {
            string CodeStr = string.Format("{0:d} {1}", code, code.ToString());
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: text/html\r\n", HTTP_VERSION, code, code.ToString());

            string Body = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            WriteLogException(new Exception(code.ToString()));

            CompleteCallback Complete = OnEndSendAndClose;
            if ("Keep-Alive" == Connection)
            {
                Complete = OnEndSendAndKeepAlive;
            }

            SendResponse(Str, Body, Encoding.ASCII, Complete);
        }

        protected void SendResponse(string Response, string Body, Encoding encCodePage, CompleteCallback Complete)
        {
            byte[] byteBody = null;

            if (null != Body && Body.Length > 0)
            {
                byteBody = encCodePage.GetBytes(Body);
            }

            SendResponse(Response, byteBody, Complete);
        }

        protected void SendResponse(string Response, byte[] Body, CompleteCallback Complete)
        {
            if (null != Body && Body.Length > 0)
            {
                Response += string.Format("Content-Length: {0}\r\n", Body.Length);
            }

            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            if (OnEndSendAndKeepAlive == Complete)
            {
                Response += "Connection: Keep-Alive\r\n";
            }
            else
            {
                Response += "Connection: close\r\n";
            }

            Response += string.Format("Server: {0}/{1}\r\n", assemName.Name, assemName.Version.ToString(3));
            Response += string.Format("Date: {0}\r\n", DateTime.UtcNow.ToString("r"));
            Response += "\r\n";

            int iSize = Encoding.ASCII.GetBytes(Response, 0, Response.Length, _Buffer, 0);

            try
            {
                _socket.BeginSend(_Buffer, 0, iSize, SocketFlags.None, OnSendHeader, new SendState(Body, Complete));
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }

        private void WriteLogNewConnection()
        {
            Log.Message(string.Format("Accept new connection: {0}", RemoteEndPointString));
        }

        private void WriteLogCloseConnection()
        {
            Log.Message(string.Format("Close connection: {0}", RemoteEndPointString));
        }

        protected void WriteLogException(Exception e)
        {
            Log.Message(
                string.Format("Http request from connection {0} failed, error:", RemoteEndPointString).PadRight(Log.PadSize)
                + "\n" + e.Message);
        }
    }
}