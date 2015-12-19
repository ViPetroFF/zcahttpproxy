using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.Globalization;

namespace ZCAHTTPProxy
{
    abstract class HttpClient
    {
        protected delegate void CompleteCallback();

        private class SendState
        {
            private readonly byte[] _body;
            private readonly CompleteCallback _complete;

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

        private static readonly byte[] arrCRLF = Encoding.ASCII.GetBytes(CRLF);
        private readonly ITcpServer _server;
        private readonly TcpListener _listener;
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly IPEndPoint _RemoteEndPoint;
        private readonly int _iIndex;

        readonly byte[] _Buffer = new byte[MAX_HEADER_SIZE];

        //byte[] _BodyBuffer;
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
            _stream = new NetworkStream(_socket, false);
            _RemoteEndPoint = (IPEndPoint)_socket.RemoteEndPoint;

            WriteLogNewConnection();
        }

        public IPAddress RemoteEndPointAddress
        {
            get
            {
                //return ((IPEndPoint)_socket.RemoteEndPoint).Address;
                return _RemoteEndPoint.Address;
            }
        }

        public string RemoteEndPointString
        {
            get
            {
                //return ((IPEndPoint)_socket.RemoteEndPoint).Address.ToString();
                return _RemoteEndPoint.Address.ToString();
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
                return new Uri(string.Format("http://{0}{1}", Host, _Url), true);
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

        public string HostName
        {
            get
            {
                string strHostName = Host;
                int ndx = strHostName.IndexOf(':');
                if (ndx >= 0)
                {
                    strHostName = strHostName.Substring(0, ndx);
                }
                return strHostName;
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

        public string Range
        {
            get
            {
                return _Headers["Range"];
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

        private TcpListener Listener
        {
            get
            {
                return _listener;
            }
        }

#if NET_45_OR_GREATER
        private async void ReceiveHeaderAsync()
        {
            bool bLoop = true;
            bool bRelease = true;

            int offset = 0;
            do
            {
                try
                {
                    int read = await _stream.ReadAsync(_Buffer, offset, _Buffer.Length - offset);
                    if (read > 0)
                    {
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
                            bLoop = bRelease = false;
                        }
                        else
                        {
                            bLoop = (offsetEnd < _Buffer.Length);

                            if (bLoop)
                            {
                                offset = offsetEnd;
                            }
                            else
                            {
                                WriteLogException(new Exception(HttpStatusCode.RequestEntityTooLarge.ToString()));
                            }
                        }
                    }
                    else
                    {
                        bLoop = false;
                    }
                }
                catch (Exception e)
                {
                    WriteLogException(e);
                    bLoop = false;
                }
            }
            while (bLoop);

            if (bRelease)
            {
                Release();
            }
        }

        private async void ReceiveBodyAsync()
        {
            bool bLoop = true;

            do
            {
                try
                {
                    int iLen = Math.Min(_Buffer.Length, (_iBodyLength - _strBody.Length));
                    int read = await _stream.ReadAsync(_Buffer, 0, iLen);
                    if (read > 0)
                    {
                        _strBody += Encoding.ASCII.GetString(_Buffer, 0, read);
                        bLoop = (_strBody.Length < _iBodyLength);
                        if (!bLoop)
                        {
                            OnHttpRequestComplete();
                            //ThreadPool.QueueUserWorkItem(new WaitCallback(HttpRequestCompleteThreadProc), this);
                            //Thread lowThread = new Thread(HttpRequestCompleteThreadProc);
                            //lowThread.Priority = ThreadPriority.Lowest;
                            //lowThread.Start(this);
                        }
                    }
                    else
                    {
                        bLoop = false;
                        Release();
                    }
                }
                catch (Exception e)
                {
                    WriteLogException(e);
                    bLoop = false;
                    Release();
                }
            }
            while (bLoop);
        }

        private async void SendHeaderAsync(int size, SendState state)
        {
            try
            {
                await _stream.WriteAsync(_Buffer, 0, size);

                if (null != state.Body && state.Body.Length > 0)
                {
                    await _stream.WriteAsync(state.Body, 0, state.Body.Length);

                    //if (_BodyBuffer != _Buffer)
                    //{
                    //_BodyBuffer = null;
                    //}
                }

                state.Complete();
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
        }

#else
        private void BeginReceiveHeader(int offset)
        {
            _socket.BeginReceive(_Buffer, offset, _Buffer.Length - offset, SocketFlags.None, OnReceiveHeader, offset);
        }


        private void BeginReceiveBody()
        {
            int iLen = Math.Min(_Buffer.Length, (_iBodyLength - _strBody.Length));
            _socket.BeginReceive(_Buffer, 0, iLen, SocketFlags.None, OnReceiveBody, null);
        }
#endif // NET_45_OR_GREATER

        public virtual void Continue()
        {
            OnEndSendAndKeepAlive();
        }

        public virtual void Release()
        {
            WriteLogCloseConnection();
            _server.DeleteSocket(_socket, _listener, _iIndex);
            _socket.Close();
        }

#if !NET_45_OR_GREATER
        private void OnReceiveHeader(IAsyncResult asyncResult)
        {
            try
            {
                int read = _socket.EndReceive(asyncResult);
                if (read > 0)
                {
                    int offset = (int)asyncResult.AsyncState;
                    int ndx = offset;

                    while((ndx = Array.FindIndex(_Buffer, ndx, (byte byChar) => { return (byChar.Equals((byte)'\n')); })) >= 0)
                    {
                        if(
                            ndx > 2
                            &&(
                                arrCRLF[0] == _Buffer[ndx-1]
                                && arrCRLF[1] == _Buffer[ndx-2]
                                && arrCRLF[0] == _Buffer[ndx-3]
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
                            enc = (Utf8Helper.IsUtf8(_Buffer, 0, ndx))?Encoding.UTF8:Encoding.Default;
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
                        //ThreadPool.QueueUserWorkItem(new WaitCallback(HttpRequestCompleteThreadProc), this);
                        //Thread lowThread = new Thread(HttpRequestCompleteThreadProc);
                        //lowThread.Priority = ThreadPriority.Lowest;
                        //lowThread.Start(this);
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
                    //if (_BodyBuffer != _Buffer)
                    //{
                        //_BodyBuffer = null;
                    //}
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
#endif // NET_45_OR_GREATER

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
#if NET_45_OR_GREATER
                    ReceiveBodyAsync();
#else
                    BeginReceiveBody();
#endif // NET_45_OR_GREATER
                }
                else
                {
                    //Log.Message(string.Format("Request: {0}", _strRequest));
                    //for (int Ndx = 0; Ndx < _Headers.Count; Ndx++)
                    //{
                    //Log.Message(string.Format("   [{0}]     {1,-10} {2}", Ndx, _Headers.GetKey(Ndx), _Headers.Get(Ndx)));
                    //}

                    //Console.WriteLine("Range: {0}", (null == strRange) ? "null" : strRange);
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

#if NET_45_OR_GREATER
            ReceiveHeaderAsync();
#else
            BeginReceiveHeader(0);
#endif // NET_45_OR_GREATER
        }

        private void OnEndSendAndClose()
        {
            Release();
        }

        protected abstract void OnHttpRequestComplete();

        protected void Start()
        {
#if NET_45_OR_GREATER
            ReceiveHeaderAsync();
#else
            try
            {
                BeginReceiveHeader(0);
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
#endif // NET_45_OR_GREATER
        }

        protected abstract bool DoCloseConnection
        {
            get;
        }

        // This thread procedure performs the task.
        static void HttpRequestCompleteThreadProc(Object This)
        {
            HttpClient clientThis = (HttpClient)This;
            clientThis.OnHttpRequestComplete();
        }

        protected void SendPartialSuccess(string ContentType, long lDownMark, long lUpMark, long lLen, DateTime timeModified, CompleteCallback Complete)
        {
            HttpStatusCode Code = HttpStatusCode.PartialContent;
            string Str = string.Format(
                "HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\nContent-Length: {4}\r\nContent-Range: bytes {5}-{6}/{7}\r\nLast-Modified: {8}\r\n",
                HTTP_VERSION, Code, Code.ToString(), ContentType, lLen - lDownMark, lDownMark, lUpMark, lLen, timeModified.ToString("r"));
            SendResponse(Str, null, Complete);
        }

        protected void SendSuccess(string ContentType, long lContentLength, DateTime timeModified, CompleteCallback Complete)
        {
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\nContent-Length: {4}\r\nLast-Modified: {5}\r\n",
                HTTP_VERSION, Code, Code.ToString(), ContentType, lContentLength, timeModified.ToString("r"));
            SendResponse(Str, null, Complete);
        }

        protected void SendSuccess(string ContentType, string Body, Encoding encCodePage, CompleteCallback Complete)
        {
            //int Code = 200;
            //string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\n", HTTP_VERSION, Code, Code.ToString(), ContentType);
            SendResponse(Str, Body, encCodePage, Complete);
        }

        protected void SendSuccess(string ContentType, byte[] Body, DateTime timeModified, CompleteCallback Complete)
        {
            //int Code = 200;
            //string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\nLast-Modified: {4}\r\n",
                HTTP_VERSION, Code, Code.ToString(), ContentType, timeModified.ToString("r"));
            SendResponse(Str, Body, Complete);
        }

        protected void SendSuccess(string ContentType, byte[] Body, CompleteCallback Complete)
        {
            //int Code = 200;
            //string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            HttpStatusCode Code = HttpStatusCode.OK;
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: {3}\r\n", HTTP_VERSION, Code, Code.ToString(), ContentType);
            SendResponse(Str, Body, Complete);
        }

        protected void SendError(HttpStatusCode code)
        {
            string CodeStr = string.Format("{0:d} {1}", code, code.ToString());
            string Str = string.Format("HTTP/{0} {1:d} {2}\r\nContent-Type: text/html\r\n", HTTP_VERSION, code, code.ToString());

            string Body = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            WriteLogException(new Exception(code.ToString()));

            SendResponse(Str, Body, Encoding.ASCII, null);
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

            bool bIsConnectionKeepAlive = (!DoCloseConnection && 0 == string.Compare("keep-alive", Connection, true));

            if (null == Complete)
            {
                Complete = OnEndSendAndClose;
                if (bIsConnectionKeepAlive)
                {
                    Complete = OnEndSendAndKeepAlive;
                }
            }

            if ("HEAD" == Method)
            {
                Body = null;
                if (bIsConnectionKeepAlive)
                {
                    Complete = OnEndSendAndKeepAlive;
                }
                else
                {
                    Complete = OnEndSendAndClose;
                }
            }

            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            if (OnEndSendAndKeepAlive == Complete)
            {
                Response += "Connection: keep-alive\r\n";
            }
            else if (OnEndSendAndClose == Complete)
            {
                Response += "Connection: close\r\n";
            }
            else
            {
                Response += string.Format("Connection: {0}\r\n", (bIsConnectionKeepAlive) ? "keep-alive" : "close");
            }

            Response += string.Format("Server: {0}/{1}\r\n", assemName.Name, assemName.Version.ToString());
            Response += string.Format("Date: {0}\r\n", DateTime.UtcNow.ToString("r"));
            Response += "\r\n";

            //Log.Message(string.Format("Response: {0}", Response));

            int iSize = Encoding.ASCII.GetBytes(Response, 0, Response.Length, _Buffer, 0);

#if NET_45_OR_GREATER
            SendHeaderAsync(iSize, new SendState(Body, Complete));
#else
            try
            {
                _socket.BeginSend(_Buffer, 0, iSize, SocketFlags.None, OnSendHeader, new SendState(Body, Complete));
            }
            catch (Exception e)
            {
                WriteLogException(e);
                Release();
            }
#endif // NET_45_OR_GREATER
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
            Log.Error(
                string.Format("Http request from connection {0} failed, error:", RemoteEndPointString).PadRight(Log.PadSize)
                + "\n" + e.Message);
        }
    }

    class CAHttpClient : HttpClient
    {
        interface IReqProccess
        {
            void Proccess();
        }

        private delegate void ReqProcCallback0();
        private delegate void ReqProcCallback1(string str1);
        private delegate void ReqProcCallback2(string str1, string str2);
        private delegate void ReqProcCallback3(string str1, string str2, string str3);

        private class ReqProc0 : IReqProccess
        {
            private readonly ReqProcCallback0 _proc;

            public ReqProc0(ReqProcCallback0 proc)
            {
                _proc = proc;
            }

            public void Proccess()
            {
                _proc();
            }
        }

        private class ReqProc1 : IReqProccess
        {
            private readonly string _str1;
            private readonly ReqProcCallback1 _proc;

            public ReqProc1(ReqProcCallback1 proc, string str1)
            {
                _str1 = str1;
                _proc = proc;
            }

            public void Proccess()
            {
                _proc(_str1);
            }
        }

        private class ReqProc2 : IReqProccess
        {
            private readonly string _str1;
            private readonly string _str2;
            private readonly ReqProcCallback2 _proc;

            public ReqProc2(ReqProcCallback2 proc, string str1, string str2)
            {
                _str1 = str1;
                _str2 = str2;
                _proc = proc;
            }

            public void Proccess()
            {
                _proc(_str1, _str2);
            }
        }

        private class ReqProc3 : IReqProccess
        {
            private readonly string _str1;
            private readonly string _str2;
            private readonly string _str3;
            private readonly ReqProcCallback3 _proc;

            public ReqProc3(ReqProcCallback3 proc, string str1, string str2, string str3)
            {
                _str1 = str1;
                _str2 = str2;
                _str3 = str3;
                _proc = proc;
            }

            public void Proccess()
            {
                _proc(_str1, _str2, _str3);
            }
        }

        private static readonly bool _bCreateCaFile = false;
        private static readonly bool _bCreateM3UFile = false;
        private static readonly bool _bRemoteCaRequest = false;
        private static readonly SortedList<int, int> _listGroupIds = new SortedList<int, int>();

        //private DateTime _lastUpdate = DateTime.UtcNow;
        private readonly ICAModule _CAModule;
        private readonly IStreamProxyServer _StreamServer;

        private string _MRL;
        private Stream _streamVLCAccess;
        private bool _bDoCloseConnection = false;
        //private StreamProxy _proxy;
        private int _indexPostedStream = -1;

        // This thread procedure performs the task.
        static void RequestLowThreadProc(Object ReqProc)
        {
            IReqProccess Proc = (IReqProccess)ReqProc;
            Proc.Proccess();
        }

        static CAHttpClient()
        {
            string strVal;
            if (null != (strVal = ConfigurationManager.AppSettings["CaFileCreate"]))
            {
                _bCreateCaFile = bool.Parse(strVal);
            }

            if (null != (strVal = ConfigurationManager.AppSettings["M3UFileCreate"]))
            {
                _bCreateM3UFile = bool.Parse(strVal);
            }

            if (null != (strVal = ConfigurationManager.AppSettings["CaRequestRemote"]))
            {
                _bRemoteCaRequest = bool.Parse(strVal);
            }

            int ndx = 1;

            for (; ndx < 12; ndx++)
            {
                _listGroupIds.Add(ndx, ndx);
            }

            //ndx = 11;
            //_listGroupIds.Add(ndx, ndx);
            ndx = 16;
            _listGroupIds.Add(ndx, ndx);
        }

        public CAHttpClient(ITcpServer server, Socket socket, TcpListener listener)
            : base(server, socket, listener)
        {
            _CAModule = (ICAModule)server;
            _StreamServer = (IStreamProxyServer)server;
            Start();
        }


        public override void Continue()
        {
            _bDoCloseConnection = false;
            if (null != _streamVLCAccess)
            {
                _streamVLCAccess.Close();
            }
            base.Continue();
        }

        public override void Release()
        {
            if (null != _streamVLCAccess)
            {
                _streamVLCAccess.Close();
            }
            base.Release();
        }

        public Stream DetachAccessStream()
        {
            Stream result = _streamVLCAccess;

            _streamVLCAccess = null;

            return result;
        }

        protected void SendContentBytes(Stream stream, string strContentType, Encoding encCodePage, DateTime timeModified)
        {
            stream.Seek(0, SeekOrigin.Begin);

            using (BinaryReader streamReader = new BinaryReader(stream, encCodePage))
            {
                string strCodePage = encCodePage.WebName;

                if (strCodePage.Length > 0)
                {
                    strContentType += "; charset=";
                    strContentType += strCodePage;
                }

                SendSuccess(
                    strContentType,
                    streamReader.ReadBytes((int)stream.Length),
                    timeModified,
                    null);
            }
        }

        protected override void OnHttpRequestComplete()
        {
            if ("GET" == Method || "HEAD" == Method)
            {
                IPAddress addrMcast;
                string strFileName;

                if (
                    3 == Url.Segments.Length
                    && ("ca/" == Url.Segments[1] || "udp/" == Url.Segments[1])
                    && IPAddress.TryParse(Log.TrimPort(Url.Segments[2]), out addrMcast)
                    )
                {
                    OnProxyStreamRequest(addrMcast);
                }
                else if (2 == Url.Segments.Length && UrlPath.Length > 0 && "/index.m3u" == UrlPath)
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyIndexM3URequest();
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc, new ReqProc0(OnProxyIndexM3URequest));
                    }
                }
                else if (1 == Url.Segments.Length || 2 == Url.Segments.Length && UrlPath.Length > 0 && "/index.html" == UrlPath)
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyIndexHTMRequest();
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc, new ReqProc0(OnProxyIndexHTMRequest));
                    }
                }
                else if (2 == Url.Segments.Length && UrlPath.Length > 0 && "/topics.html" == UrlPath)
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyTopicsRequest();
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc, new ReqProc0(OnProxyTopicsRequest));
                    }
                }
                else if (2 == Url.Segments.Length && UrlPath.Length > 0 && (strFileName = UrlPath.Substring(1)).EndsWith(".m3u"))
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyM3UListRequest(strFileName);
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc, new ReqProc1(OnProxyM3UListRequest, strFileName));
                    }
                }
                else if (2 == Url.Segments.Length && UrlPath.Length > 0 && (strFileName = UrlPath.Substring(1)).EndsWith(".ts"))
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyTVGroupRequest(string.Empty, strFileName.Insert(0, "/"));
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc,
                            new ReqProc2(
                                OnProxyTVGroupRequest,
                                string.Empty,
                                strFileName.Insert(0, "/")
                                )
                            );
                    }
                }
                else if (2 == Url.Segments.Length && UrlPath.Length > 0 &&
                    ((strFileName = UrlPath.Substring(1)).EndsWith(".html") || strFileName.EndsWith(".htm")))
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyHTMListRequest(strFileName);
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc, new ReqProc1(OnProxyHTMListRequest, strFileName));
                    }
                }
                else if (2 <= Url.Segments.Length && UrlPath.Length > 0 && (strFileName = Url.Segments[1]).EndsWith("~/"))
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyTVGroupRequest(strFileName.Remove(strFileName.Length - 2), UrlPath.Substring(strFileName.Length));
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc,
                            new ReqProc2(
                                OnProxyTVGroupRequest,
                                strFileName.Remove(strFileName.Length - 2),
                                UrlPath.Substring(strFileName.Length)
                                )
                            );
                    }
                }
                else if (2 <= Url.Segments.Length && UrlPath.Length > 0 && (strFileName = Url.Segments[1]).EndsWith("$/"))
                {
                    IThreadPool lowThPool = _CAModule.LowThreadPool;

                    if (null == lowThPool)
                    {
                        OnProxyFileRequest(strFileName.Remove(strFileName.Length - 2), UrlPath.Substring(strFileName.Length));
                    }
                    else
                    {
                        lowThPool.QueueUserWorkItem(RequestLowThreadProc,
                            new ReqProc2(
                                OnProxyFileRequest,
                                strFileName.Remove(strFileName.Length - 2),
                                UrlPath.Substring(strFileName.Length)
                                )
                            );
                    }
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            else if ("POST" == Method && "application/json" == ContentType)
            {
                if ("/ca" == Url.AbsolutePath.TrimEnd(new Char[] { '/' }))
                {
                    if (
                        _bRemoteCaRequest
                        || 0 == string.Compare("localhost", HostName, true)
                        || "127.0.0.1" == HostName
                        || IPAddress.IsLoopback(RemoteEndPointAddress)
                        || _CAModule.IsHostAddress(RemoteEndPointAddress)
                        )
                    {
                        OnProxyCARequest();
                    }
                    else
                    {
                        SendError(HttpStatusCode.Gone);
                    }
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            else
            {
                SendError(HttpStatusCode.NotImplemented);
            }
        }

        protected override bool DoCloseConnection
        {
            get
            {
                return _bDoCloseConnection;
            }
        }

        private void OnProxyIndexM3URequest()
        {
            try
            {
                string xmlTopics = _CAModule.CABodyTopics;

                Stream streamOut = new MemoryStream(0xa000);
                Encoding encCodePage = ContentCodePage();

                byte[] buffLine = encCodePage.GetBytes("#EXTM3U\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);

                XmlTextReader xml = new XmlTextReader(new StringReader(xmlTopics));
                xml.WhitespaceHandling = WhitespaceHandling.None;

                if (xml.Read() && xml.Read() && xml.Read() && xml.MoveToAttribute("count"))
                {
                    int nCount = int.Parse(xml.Value);

                    int ndx = 0;

                    while (ndx < nCount && xml.Read())
                    {
                        string strName = xml.GetAttribute("name").Replace(',', '.');
                        string strId = xml.GetAttribute("id");

                        if (_listGroupIds.ContainsKey(int.Parse(strId)))
                        {
                            buffLine = encCodePage.GetBytes(string.Format("#EXTINF:-1,{0}~\r\n", strName));
                            streamOut.Write(buffLine, 0, buffLine.Length);
                            buffLine = encCodePage.GetBytes(string.Format("http://{0}/^{1}.m3u?format=lv2id\r\n", Host, strId));
                            streamOut.Write(buffLine, 0, buffLine.Length);
                        }

                        ndx++;
                    }

                    buffLine = encCodePage.GetBytes(string.Format("#EXTINF:-1,Все каналы~\r\n"));
                    streamOut.Write(buffLine, 0, buffLine.Length);
                    buffLine = encCodePage.GetBytes(string.Format("http://{0}/^21.m3u?format=lv2id\r\n", Host));
                    streamOut.Write(buffLine, 0, buffLine.Length);
                }

                DirectoryInfo dir = new DirectoryInfo(".");

                FileInfo[] fileEntries = dir.GetFiles("*.lnk");
                foreach (FileInfo fileEntry in fileEntries)
                {
                    string strFile = Path.GetFileNameWithoutExtension(fileEntry.Name);

                    Uri urlDir = new Uri(string.Format("http://{0}/{1}$/index.m3u", Host, strFile));
                    buffLine = encCodePage.GetBytes(string.Format("#EXTINF:-1,{0}$\r\n", strFile));
                    streamOut.Write(buffLine, 0, buffLine.Length);
                    buffLine = encCodePage.GetBytes(urlDir.AbsoluteUri + "\r\n");
                    streamOut.Write(buffLine, 0, buffLine.Length);
                }

                SendContentBytes(streamOut, "application/x-mpegurl", encCodePage, dir.LastWriteTime);
            }
            catch (Exception e)
            {
                WriteLogM3UException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyIndexHTMRequest()
        {
            try
            {
                string xmlTopics = _CAModule.CABodyTopics;

                Stream streamOut = new MemoryStream(0xf000);
                Encoding encCodePage = Encoding.UTF8;

                DateTime timeModified = _CAModule.CABodyCacheUpdateTime;
                string strFolderUrl = "/";

                byte[] buffLine = encCodePage.GetBytes("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes(string.Format("<HTML><HEAD><TITLE>Index of {0}</TITLE></HEAD><BODY>\r\n", strFolderUrl));
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes("<A HREF=\"topics.html\">Topics</A> "
                    + "<A HREF=\"table.html\">Table</A> "
                    + "<A HREF=\"Readme.htm\">Help</A>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes(string.Format("<H1>Index of {0}</H1>\r\n", strFolderUrl));
                streamOut.Write(buffLine, 0, buffLine.Length);
                string strLine = "<PRE>Name";
                strLine = strLine.PadRight(56, ' ') + "Last modified\tSize<HR>\r\n";
                buffLine = encCodePage.GetBytes(strLine);//"<PRE>Name\tLast modified\tSize<HR>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);


                XmlTextReader xml = new XmlTextReader(new StringReader(xmlTopics));
                xml.WhitespaceHandling = WhitespaceHandling.None;

                if (xml.Read() && xml.Read() && xml.Read() && xml.MoveToAttribute("count"))
                {
                    int nCount = int.Parse(xml.Value);

                    int ndx = 0;

                    while (ndx < nCount && xml.Read())
                    {
                        string strName = xml.GetAttribute("name").Replace(',', '.');
                        string strId = xml.GetAttribute("id");

                        if (_listGroupIds.ContainsKey(int.Parse(strId)))
                        {
                            string strLinkName = string.Format("{0}.{1}~/", strId, strName);
                            string strHref = string.Format("<A HREF=\"{0}.{1}~/\">{2}</A>", strId, Uri.EscapeUriString(strName), strLinkName);
                            //"<A HREF="system-docs/">system-docs/</A>            01-Nov-1995 09:28      -  "
                            strLine = string.Format(
                                    "{0,-51}{1}{2,8}\r\n",
                                    strLinkName,
                                    timeModified.ToString("dd'-'MMM'-'yyyy HH':'mm", CultureInfo.InvariantCulture),
                                    "-"
                                    );
                            strLine = strHref + strLine.Substring(strLinkName.Length);
                            buffLine = encCodePage.GetBytes(strLine);
                            streamOut.Write(buffLine, 0, buffLine.Length);
                        }

                        ndx++;
                    }

                    string strLinkName21 = string.Format("21.{0}~/", "Все каналы");
                    string strHref21 = string.Format("<A HREF=\"21.{0}~/\">{1}</A>", Uri.EscapeUriString("Все каналы"), strLinkName21);
                    strLine = string.Format(
                            "{0,-51}{1}{2,8}\r\n",
                        //"<A HREF=\"~/\">~~.Все каналы~/</A>",
                            strLinkName21,
                            timeModified.ToString("dd'-'MMM'-'yyyy HH':'mm", CultureInfo.InvariantCulture),
                            "-"
                            );
                    strLine = strHref21 + strLine.Substring(strLinkName21.Length);
                    buffLine = encCodePage.GetBytes(strLine);
                    streamOut.Write(buffLine, 0, buffLine.Length);

                }

                DirectoryInfo dir = new DirectoryInfo(".");

                FileInfo[] fileEntries = dir.GetFiles("*.lnk");
                foreach (FileInfo fileEntry in fileEntries)
                {
                    string strFile = Path.GetFileNameWithoutExtension(fileEntry.Name);
                    timeModified = Directory.GetLastWriteTime(ShellLink.GetShortcutTarget(fileEntry.FullName));//fileEntry.LastWriteTime;

                    string strLinkName = string.Format("{0}$/", strFile);
                    string strHref = string.Format("<A HREF=\"{0}$/\">{1}</A>", Uri.EscapeUriString(strFile), strLinkName);
                    //"<A HREF="system-docs/">system-docs/</A>            01-Nov-1995 09:28      -  "
                    strLine = string.Format(
                            "{0,-51}{1}{2,8}\r\n",
                            strLinkName,
                            timeModified.ToString("dd'-'MMM'-'yyyy HH':'mm", CultureInfo.InvariantCulture),
                            "-"
                            );
                    strLine = strHref + strLine.Substring(strLinkName.Length);
                    buffLine = encCodePage.GetBytes(strLine);
                    streamOut.Write(buffLine, 0, buffLine.Length);
                }

                buffLine = encCodePage.GetBytes("<HR></PRE>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes("<ADDRESS>" + GetAddress() + "</ADDRESS>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes("</BODY></HTML>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);

                SendContentBytes(streamOut, "text/html", encCodePage, dir.LastWriteTime);
            }
            catch (Exception e)
            {
                WriteLogHTMException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyTopicsRequest()
        {
            try
            {
                string xmlTopics = _CAModule.CABodyTopics;

                Stream streamOut = new MemoryStream(0xf000);
                Encoding encCodePage = Encoding.UTF8;

                byte[] buffLine = encCodePage.GetBytes("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);
                buffLine = encCodePage.GetBytes(
                    string.Format("<html><head><title>Topics</title><meta charset=\"{0}\"></head><body><h1>Topics</h1><ul>\r\n",
                    encCodePage.WebName)
                    );
                streamOut.Write(buffLine, 0, buffLine.Length);

                XmlTextReader xml = new XmlTextReader(new StringReader(xmlTopics));
                xml.WhitespaceHandling = WhitespaceHandling.None;

                if (xml.Read() && xml.Read() && xml.Read() && xml.MoveToAttribute("count"))
                {
                    int nCount = int.Parse(xml.Value);

                    int ndx = 0;

                    while (ndx < nCount && xml.Read())
                    {
                        string strName = xml.GetAttribute("name").Replace(',', '.');
                        string strId = xml.GetAttribute("id");

                        if (_listGroupIds.ContainsKey(int.Parse(strId)))
                        {
                            string strUrl = string.Format("http://{0}/topic.html?group={1}&view=tblv1&high=22", Host, strId);
                            buffLine = encCodePage.GetBytes(
                                string.Format("<li><a href=\"{0}\">{1}</a></li>\r\n", strUrl, strName)
                                );
                            streamOut.Write(buffLine, 0, buffLine.Length);
                        }

                        ndx++;
                    }

                    string strUrlAll = string.Format("http://{0}/topic.html?group=21&type=flat&view=tblv1&high=22", Host);
                    buffLine = encCodePage.GetBytes(
                        string.Format("<li><a href=\"{0}\">{1}</a></li>\r\n", strUrlAll, "Все каналы")
                        );
                    streamOut.Write(buffLine, 0, buffLine.Length);

                }

                buffLine = encCodePage.GetBytes("</ul></body></html>\r\n");
                streamOut.Write(buffLine, 0, buffLine.Length);

                DateTime timeModified = _CAModule.CABodyCacheUpdateTime;

                SendContentBytes(streamOut, "text/html", encCodePage, timeModified);
            }
            catch (Exception e)
            {
                WriteLogHTMException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyTVGroupRequest(string strGroupName, string strFileName)
        {
            try
            {
                int ID = -1;
                int ndx = strGroupName.IndexOf('.');
                if ((ndx > 0 && int.TryParse(strGroupName.Substring(0, ndx), out ID)) || 0 == strGroupName.Length)
                {
                    strGroupName = strGroupName.Substring(ndx + 1);

                    ProxyChannelsWriterAbstract.ChannelsListCache cache = _CAModule.CABodyCache;
                    string xmlTopics = _CAModule.CABodyTopics;

                    if ("/" != strFileName)
                    {
                        OrderedDictionary CADataRO = _CAModule.CABodyCache.CAData;
                        ndx = strFileName.IndexOf('.');
                        if (ndx > 0 && int.TryParse(strFileName.Substring(1, ndx - 1), out ID) && CADataRO.Contains(ID))
                        {
                            ProxyChannelsWriterAbstract.ChDataEntry entry = (ProxyChannelsWriterAbstract.ChDataEntry)CADataRO[(Object)ID];

                            string strUrl = string.Format("http://{0}/{1}/{2}", Host, entry.bIsEncrypted ? "ca" : "udp", entry.Source);
                            _MRL = URLToMRL(strUrl);
                            OnProxyStreamRequest();
                        }
                        else
                        {
                            SendError(HttpStatusCode.NotFound);
                        }
                    }
                    else
                    {
                        DateTime timeModified = cache.UpdateLastTime;
                        Stream streamHTML = new MemoryStream(0xf000);

                        ProxyHTMLTvGrpWriter writerHTML = new ProxyHTMLTvGrpWriter(cache, GetAddress(), Host, strGroupName, ID);
                        int iCount = writerHTML.WriteCA(streamHTML);

                        Log.Message(string.Format("{0} successfully prepared html response of {1} channels", RemoteEndPointString, iCount));

                        if (iCount > 0)
                        {
                            SendContentBytes(streamHTML, "text/html", writerHTML.CodePage, timeModified);
                        }
                        else
                        {
                            SendError(HttpStatusCode.NotFound);
                        }
                    }
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            catch (Exception e)
            {
                WriteLogHTMException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyStreamRequest(IPAddress addrMcast)
        {
            try
            {
                _MRL = URLToMRL(Url);
                OnProxyStreamRequest();
            }
            catch (Exception e)
            {
                //WriteLogException(e);
                WriteFileException(e, Url.AbsolutePath);
                SendError(HttpStatusCode.BadRequest);
            }
        }

        private void OnProxyStreamRequest()
        {
            try
            {
                int iIndex = -1;

                if (
                    (StreamProxyAbstract.CountProxy > 0 && _StreamServer.IsExistPostedStream(_MRL, out iIndex))
                    || StreamProxyAbstract.CountProxy < StreamProxyAbstract.MAX_STREAM_PROXY_COUNT
                    )
                {
                    string agent = UserAgent;
                    //if ("1.0" == Version && null != agent && "Media Player Classic" == agent)
                    //{
                    //SendError(HttpStatusCode.HttpVersionNotSupported);
                    //}
                    //else
                    if (
                        "1.0" == Version && null != agent && "shoutcastsource" == agent
                        || 0 == agent.IndexOf("NSPlayer/12") && null != IcyMetadata
                        )
                    {
                        SendError(HttpStatusCode.UnsupportedMediaType);
                    }
                    else
                    {
                        _bDoCloseConnection = !("HEAD" == Method);

                        CompleteCallback callback = OnProxyStreamSend;
                        if (iIndex < 0)
                        {
                            ProxyStreamConfig proxyStreamCfg = ProxyStreamConfig.GetSection();
                            int timeout = (null != proxyStreamCfg) ? proxyStreamCfg.InitialReceiveTimeout : 4;

                            _streamVLCAccess = _CAModule.ModuleVLC.NewAccessStream0(_MRL, timeout);
                            WriteLogNewConnection();

                            if (!("GET" == Method))
                            {
                                _streamVLCAccess.Close();
                            }
                        }
                        else
                        {
                            _indexPostedStream = iIndex;
                            callback = OnPostedProxyStreamSend;
                        }

                        SendSuccess("video/MP2T", null, null, callback);
                        //SendSuccess("video/x-mpegts", null, null, callback);
                        //SendSuccess("application/x-mpeg-ts", null, null, callback);
                    }
                }
                else
                {
                    SendError(HttpStatusCode.ServiceUnavailable);
                }
            }
            catch (System.ComponentModel.Win32Exception eWin32)
            {
                WriteFileException(eWin32, Url.AbsolutePath);
                if (10060L == eWin32.NativeErrorCode)
                {
                    SendError(HttpStatusCode.NotFound);
                }
                else
                {
                    SendError(HttpStatusCode.BadGateway);
                }
            }
            catch (Exception e)
            {
                //WriteLogException(e);
                WriteFileException(e, Url.AbsolutePath);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyFileStreamRequest(string strFilePath)
        {
            try
            {
                if (StreamProxyAbstract.CountProxy < StreamProxyAbstract.MAX_STREAM_PROXY_COUNT)
                {
                    string strFileName = Path.GetFileName(UrlPath);
                    _MRL = (strFileName.Length > 20) ? ("~" + strFileName.Substring(strFileName.Length - 20)) : strFileName;

                    _streamVLCAccess =
                        new FileStream(strFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileStreamProxy.FS_BUFFER_SIZE, true);
                    Log.Message(string.Format("{0} successfully open file {1}", RemoteEndPointString, strFilePath));
                    WriteLogNewConnection();

                    long lDownMark = -1;
                    long lUpMark = -1;

                    string strRange = Range;
                    if (null != strRange)
                    {
                        int ndx = strRange.IndexOf('=');
                        if (ndx != -1)
                        {
                            string strName = strRange.Substring(0, ndx);
                            strName.TrimStart(null);
                            string strValue = strRange.Substring(ndx + 1);
                            strValue.TrimEnd(null);
                            ndx = strValue.IndexOf(',');
                            if (ndx == -1)
                            {
                                ndx = strValue.IndexOf('-');
                                if (ndx != -1)
                                {
                                    string strStart = strValue.Substring(0, ndx);
                                    string strEnd = strValue.Substring(ndx + 1);

                                    if (strStart.Length > 0)
                                    {
                                        lDownMark = (long)Math.Min(long.MaxValue, ulong.Parse(strStart));
                                    }

                                    if (strEnd.Length > 0)
                                    {
                                        lUpMark = (long)Math.Min(long.MaxValue, ulong.Parse(strEnd));
                                    }

                                    if (-1 == lDownMark && -1 != lUpMark)
                                    {
                                        lDownMark = _streamVLCAccess.Length;
                                        if (lDownMark > lUpMark)
                                        {
                                            lDownMark -= lUpMark;
                                        }
                                        else
                                        {
                                            lDownMark = 0;
                                        }

                                        lUpMark = -1;
                                    }

                                    if (lDownMark > 0)
                                    {
                                        //Log.Message(string.Format("Seek file {0}", lDownMark));
                                        lDownMark = _streamVLCAccess.Seek(lDownMark, SeekOrigin.Begin);
                                    }

                                    if (-1 == lUpMark || lUpMark > _streamVLCAccess.Length)
                                    {
                                        lUpMark = Math.Max(0, _streamVLCAccess.Length - 1);
                                    }
                                }
                            }
                        }
                    }

                    if (null != strRange && lDownMark < 0)
                    {
                        SendError(HttpStatusCode.RequestedRangeNotSatisfiable);
                    }
                    else
                    {
                        if (lDownMark > 0)
                        {
                            SendPartialSuccess("application/octet-stream",
                                lDownMark, lUpMark, _streamVLCAccess.Length, File.GetLastWriteTimeUtc(strFilePath), OnProxyFileStreamSend);
                        }
                        else
                        {
                            //SendSuccess("video/x-msvideo", null, null, OnProxyFileStreamSend);
                            SendSuccess("application/octet-stream",
                                _streamVLCAccess.Length, File.GetLastWriteTimeUtc(strFilePath), OnProxyFileStreamSend);
                        }
                    }
                }
                else
                {
                    SendError(HttpStatusCode.ServiceUnavailable);
                }
            }
            catch (Exception e)
            {
                WriteFileException(e, strFilePath);
                //Release();
                SendError(HttpStatusCode.Forbidden);
            }
        }

        private void OnProxyCARequest()
        {
            try
            {
                string strMacAddr = ParseMrlKeysRequest(Body);

                Stream streamCA = Stream.Null;

                if (File.Exists(_cstrCaFileName))
                {
                    streamCA = File.Open(_cstrCaFileName, FileMode.Open);
                    Log.Message(string.Format("{0} successfully prepared CA json response from {1} file", RemoteEndPointString, _cstrCaFileName));
                }
                else if (null != CAWebClient.CAAuthData.GetSection())
                {
                    if (_bCreateCaFile)
                    {
                        streamCA = File.Open(_cstrCaFileName, FileMode.CreateNew);
                    }
                    else
                    {
                        streamCA = new MemoryStream(0xa000);
                    }

                    ProxyCABodyWriter writerCA = new ProxyCABodyWriter(strMacAddr);
                    int iCount = writerCA.WriteCA(streamCA);
                    streamCA.Seek(0, SeekOrigin.Begin);
                    Log.Message(string.Format("{0} successfully prepared CA json response of {1} channels", RemoteEndPointString, iCount));
                }

                if (Stream.Null == streamCA)
                {
                    _bDoCloseConnection = true;
                    //string strCA = "{\"jsonrpc\": \"2.0\", \"id\": 1, \"result\": {\"mrl_list\": " +
                    SendSuccess("application/json", null, null);
                }
                else
                {
                    using (BinaryReader streamReader = new BinaryReader(streamCA, Encoding.ASCII))
                    {
                        _CAModule.MacAddress = strMacAddr;
                        SendSuccess("application/json", streamReader.ReadBytes((int)streamCA.Length), null);
                        //string fakeJson = "{\"jsonrpc\": \"2.0\", \"id\": 1, \"result\": {\"mrl_list\": [{\"keys\": [{\"key_hex\": \"77777777777777777777777777777777\", \"key_id\": 1}], \"uri\": \"224.0.0.0:1234\"}]}}";
                        //SendSuccess("application/json", fakeJson, Encoding.ASCII, null);
                    }
                }
            }
            catch (Exception e)
            {
                WriteLogCaException(e);
                //Release();
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyM3UListRequest(string strFileName)
        {
            try
            {
                Stream streamCA;
                DateTime timeModified;
                Encoding encCodePage = Encoding.UTF8;
                string strCodePage = string.Empty;

                if (File.Exists(strFileName))
                {
                    timeModified = File.GetLastWriteTimeUtc(strFileName);
                    streamCA = File.Open(strFileName, FileMode.Open);
                    Log.Message(string.Format("{0} successfully prepared m3u response from {1} file", RemoteEndPointString, strFileName));
                }
                else
                {
                    ProxyChannelsWriterAbstract.ChannelsListCache cache = _CAModule.CABodyCache;
                    string xmlTopics = _CAModule.CABodyTopics;

                    timeModified = cache.UpdateLastTime;

                    if (_bCreateM3UFile)
                    {
                        streamCA = File.Open(strFileName, FileMode.CreateNew);
                    }
                    else
                    {
                        streamCA = new MemoryStream(0xa000);
                    }

                    ProxyM3UBodyWriter writerM3U = new ProxyM3UBodyWriter(
                                                            cache,
                                                            new StringReader(xmlTopics),
                                                            Host,
                                                            strFileName.Substring(0, strFileName.Length - 4),
                                                            Query);
                    int iCount = writerM3U.WriteM3U(streamCA);

                    Log.Message(string.Format("{0} successfully prepared m3u response of {1} channels", RemoteEndPointString, iCount));

                    if (iCount > 0)
                    {
                        encCodePage = writerM3U.CodePage;
                        strCodePage = writerM3U.CodePageName;
                        streamCA.Seek(0, SeekOrigin.Begin);
                    }
                    else
                    {
                        streamCA = null;
                    }
                }

                if (null != streamCA)
                {
                    using (BinaryReader streamReader = new BinaryReader(streamCA, encCodePage))
                    {
                        //"audio/x-mpegurl; charset=windows-1251"
                        //string strContentType = "audio/x-mpegurl";
                        string strContentType = "application/x-mpegurl";

                        if (strCodePage.Length > 0)
                        {
                            strContentType += "; charset=";
                            strContentType += strCodePage;
                        }
                        SendSuccess(strContentType, streamReader.ReadBytes((int)streamCA.Length), timeModified, null);
                    }
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            catch (Exception e)
            {
                WriteLogM3UException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyHTMListRequest(string strFileName)
        {
            try
            {
                Stream streamHTM;
                DateTime timeModified;
                Encoding encCodePage = Encoding.UTF8;
                string strCodePage = string.Empty;

                if (File.Exists(strFileName))
                {
                    timeModified = File.GetLastWriteTimeUtc(strFileName);
                    streamHTM = File.Open(strFileName, FileMode.Open);
                    Log.Message(string.Format("{0} successfully prepared html response from {1} file", RemoteEndPointString, strFileName));
                }
                else
                {
                    ProxyChannelsWriterAbstract.ChannelsListCache cache = _CAModule.CABodyCache;
                    string xmlTopics = _CAModule.CABodyTopics;

                    timeModified = cache.UpdateLastTime;

                    if (_bCreateM3UFile)
                    {
                        streamHTM = File.Open(strFileName, FileMode.CreateNew);
                    }
                    else
                    {
                        streamHTM = new MemoryStream(0xf000);
                    }

                    ProxyHTMLListWriter writerHTM = new ProxyHTMLListWriter(
                                                            cache,
                                                            new StringReader(xmlTopics),
                                                            Host,
                                                            strFileName.Substring(0, strFileName.Length - 5),
                                                            Query);
                    int iCount = writerHTM.WriteHTML(streamHTM);

                    Log.Message(string.Format("{0} successfully prepared html response of {1} channels", RemoteEndPointString, iCount));

                    if (iCount > 0)
                    {
                        encCodePage = writerHTM.CodePage;
                    }
                    else
                    {
                        streamHTM = null;
                    }
                }

                if (null != streamHTM)
                {
                    SendContentBytes(streamHTM, "text/html", encCodePage, timeModified);
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            catch (Exception e)
            {
                WriteLogHTMException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        private void OnProxyFileRequest(string strLinkName, string strFilePath)
        {
            try
            {
                string strLink = strLinkName + ".lnk";

                if (File.Exists(strLink))
                {
                    string strTargetPath = ShellLink.GetShortcutTarget(strLink);
                    if (Directory.Exists(strTargetPath))
                    {
                        if (strFilePath.EndsWith("/"))
                        {
                            strFilePath = strFilePath + "index.html";
                        }

                        string strFolderPath = Path.Combine(strTargetPath, strFilePath.Substring(1));
                        string strFileName = Path.GetFileName(strFolderPath);
                        //Console.WriteLine("strFilePath: {0}", strTargetPath);

                        if (File.Exists(strFolderPath))
                        {
                            OnProxyFileStreamRequest(strFolderPath);
                        }
                        else if (
                            Directory.Exists(strFolderPath = Path.GetDirectoryName(strFolderPath))
                            && ("index.m3u" == strFileName || "index.html" == strFileName || "index.htm" == strFileName)
                            )
                        {
                            string strFolderUrl = UrlPath;

                            int ndx = strFolderUrl.LastIndexOf('/');
                            if (ndx > 0)
                            {
                                strFolderUrl = strFolderUrl.Substring(0, ndx);
                            }

                            Stream streamOut = new MemoryStream(0xf000);
                            Encoding encCodePage = ContentCodePage();

                            string strContentType = string.Empty;
                            DateTime timeModified;

                            if ("index.m3u" == strFileName)
                            {
                                byte[] buffLine = encCodePage.GetBytes("#EXTM3U\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);

                                // Process the list of files found in the directory.
                                string[] subdirectoryEntries = Directory.GetDirectories(strFolderPath);
                                foreach (string subdirectory in subdirectoryEntries)
                                {
                                    //Console.WriteLine("subdirectory: {0}", subdirectory);
                                    string strDir = Path.GetFileName(subdirectory);
                                    Uri urlDir = new Uri(string.Format("http://{0}{1}/{2}/index.m3u", Host, strFolderUrl, strDir));
                                    buffLine = encCodePage.GetBytes(string.Format("#EXTINF:-1,{0}\r\n", strDir));
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                    buffLine = encCodePage.GetBytes(urlDir.AbsoluteUri + "\r\n");
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                }

                                Log.Message(
                                    string.Format(
                                        "{0} successfully prepared m3u response of {1} folders",
                                        RemoteEndPointString,
                                        subdirectoryEntries.Length)
                                        );

                                // Recurse into subdirectories of this directory.
                                string[] fileEntries = Directory.GetFiles(strFolderPath);
                                foreach (string fileName in fileEntries)
                                {
                                    //Console.WriteLine("fileName: {0}", fileName);
                                    string strFile = Path.GetFileName(fileName);
                                    Uri urlFile = new Uri(string.Format("http://{0}{1}/{2}", Host, strFolderUrl, strFile));
                                    buffLine = encCodePage.GetBytes(string.Format("#EXTINF:-1,{0}\r\n", strFile));
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                    buffLine = encCodePage.GetBytes(urlFile.AbsoluteUri + "\r\n");
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                }

                                Log.Message(
                                    string.Format(
                                        "{0} successfully prepared m3u response of {1} files",
                                        RemoteEndPointString,
                                        fileEntries.Length)
                                        );

                                strContentType = "application/x-mpegurl";
                            }
                            else
                            {
                                byte[] buffLine = encCodePage.GetBytes("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.0 Transitional//EN\">\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);
                                buffLine = encCodePage.GetBytes(
                                    string.Format("<HTML><HEAD><TITLE>Index of {0}</TITLE></HEAD><BODY><H1>Index of {0}</H1>\r\n", strFolderUrl));
                                streamOut.Write(buffLine, 0, buffLine.Length);

                                string strLine = "<PRE>Name";
                                strLine = strLine.PadRight(56, ' ') + "Last modified\tSize<HR>\r\n";
                                buffLine = encCodePage.GetBytes(strLine);//"<PRE>Name\tLast modified\tSize<HR>\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);

                                DirectoryInfo dir = new DirectoryInfo(strFolderPath);
                                // Process the list of files found in the directory.
                                DirectoryInfo[] subdirectoryEntries = dir.GetDirectories();
                                foreach (DirectoryInfo subdirectory in subdirectoryEntries)
                                {
                                    //Console.WriteLine("subdirectory: {0}", subdirectory);
                                    string strDir = subdirectory.Name;
                                    timeModified = subdirectory.LastWriteTime;
                                    string strHref = string.Format("<A HREF=\"{0}/\">{1}/</A>", Uri.EscapeUriString(strDir), strDir);
                                    //"<A HREF="system-docs/">system-docs/</A>            01-Nov-1995 09:28      -  "
                                    strLine = string.Format(
                                            "{0,-49}  {1}{2,8}\r\n",
                                            strDir,
                                            timeModified.ToString("dd'-'MMM'-'yyyy HH':'mm", CultureInfo.InvariantCulture),
                                            "-"
                                            );
                                    strLine = strHref + strLine.Substring(strDir.Length + 1);
                                    buffLine = encCodePage.GetBytes(strLine);
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                }

                                Log.Message(
                                    string.Format(
                                        "{0} successfully prepared html response of {1} folders",
                                        RemoteEndPointString,
                                        subdirectoryEntries.Length)
                                        );

                                FileInfo[] fileEntries = dir.GetFiles();
                                foreach (FileInfo fileEntry in fileEntries)
                                {
                                    string strFile = fileEntry.Name;
                                    timeModified = fileEntry.LastWriteTime;
                                    string strHref = string.Format("<A HREF=\"{0}\">{1}</A>", Uri.EscapeUriString(strFile), strFile);
                                    //"<A HREF="system-docs/">system-docs/</A>            01-Nov-1995 09:28      -  "
                                    strLine = string.Format(
                                            "{0,-50} {1}{2,8}\r\n",
                                            strFile,
                                            timeModified.ToString("dd'-'MMM'-'yyyy HH':'mm", CultureInfo.InvariantCulture),
                                            Log.GetSizeHtmlString(fileEntry.Length)
                                            );
                                    strLine = strHref + strLine.Substring(strFile.Length);
                                    buffLine = encCodePage.GetBytes(strLine);
                                    streamOut.Write(buffLine, 0, buffLine.Length);
                                }

                                Log.Message(
                                    string.Format(
                                        "{0} successfully prepared html response of {1} files",
                                        RemoteEndPointString,
                                        fileEntries.Length)
                                        );

                                buffLine = encCodePage.GetBytes("<HR></PRE>\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);
                                buffLine = encCodePage.GetBytes("<ADDRESS>" + GetAddress() + "</ADDRESS>\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);
                                buffLine = encCodePage.GetBytes("</BODY></HTML>\r\n");
                                streamOut.Write(buffLine, 0, buffLine.Length);

                                strContentType = "text/html";
                            }

                            timeModified = Directory.GetLastWriteTimeUtc(strFolderPath);

                            SendContentBytes(streamOut, strContentType, encCodePage, timeModified);
                        }
                        else
                        {
                            SendError(HttpStatusCode.NotFound);
                        }
                    }
                    else
                    {
                        SendError(HttpStatusCode.NotFound);
                    }
                }
                else
                {
                    SendError(HttpStatusCode.NotFound);
                }
            }
            catch (Exception e)
            {
                WriteLogHTMException(e);
                SendError(HttpStatusCode.BadGateway);
            }
        }

        protected void OnProxyStreamSend()
        {
            ProxyStreamConfig classProxyStream = ProxyStreamConfig.GetSection();
            string strClass = (null == classProxyStream) ? "Default" : classProxyStream.Name;

            switch (strClass)
            {
                case StreamProxy.CLASS_NAME:
                    new StreamProxy(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass16.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass16>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass32.CLASS_NAME:
                    goto default;
                case MemoryCacheClass64.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass64>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass128.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass128>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass256.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass256>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass512.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass512>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass1024.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass1024>(this, _MRL, _streamVLCAccess);
                    break;
                case MemoryCacheClass2048.CLASS_NAME:
                    new StreamProxyEx<MemoryCacheClass2048>(this, _MRL, _streamVLCAccess);
                    break;


                default:
                    new StreamProxyEx<MemoryCacheClass32>(this, _MRL, _streamVLCAccess);
                    break;
            }
        }

        protected void OnPostedProxyStreamSend()
        {
            StreamProxyAbstract stream = (StreamProxyAbstract)_StreamServer.TakePostedStream(_indexPostedStream);

            if (null != stream && _MRL == stream.Mrl)
            {
                CAHttpClient conn = (CAHttpClient)stream.ResumeSend(this);
                _streamVLCAccess = conn.DetachAccessStream();
                conn.Release();
            }
            else
            {
                SendError(HttpStatusCode.NotFound);
            }
        }

        protected void OnProxyFileStreamSend()
        {
            new FileStreamProxy(this, _MRL, _streamVLCAccess);
        }

        private void WriteLogNewConnection()
        {
            Log.Message(string.Format("Open input stream {0}", _MRL));
        }

        private void WriteLogCaException(Exception e)
        {
            Log.Error("CA request failed, error:".PadRight(Log.PadSize) + "\n" + e.Message);
        }

        private void WriteLogM3UException(Exception e)
        {
            Log.Error("Request M3U list failed, error:".PadRight(Log.PadSize) + "\n" + e.Message);
        }

        private void WriteLogHTMException(Exception e)
        {
            Log.Error("Request HTML list failed, error:".PadRight(Log.PadSize) + "\n" + e.Message);
        }

        private void WriteFileException(Exception e, string strFilePath)
        {
            Log.Error(string.Format("Opening resource {0} failed, error:", strFilePath).PadRight(Log.PadSize) + "\n" + e.Message);
        }

        private string GetAddress()
        {
            Assembly assem = Assembly.GetExecutingAssembly();
            AssemblyName assemName = assem.GetName();

            return assemName.Name + "/" + assemName.Version.ToString(4) + " (" + Environment.OSVersion + ") at " + Host;

        }

        private Encoding ContentCodePage()
        {
            Encoding encCodePage = Encoding.UTF8;
            string strQuery = Query;

            if (null != strQuery && strQuery.Length > 0)
            {
                string[] caParams = strQuery.Split('&');
                foreach (string str in caParams)
                {
                    if (str.StartsWith("charset"))
                    {
                        string[] caPairQuery = str.Split('=');
                        if (2 == caPairQuery.Length && "ansi" == caPairQuery[1])
                        {
                            encCodePage = Encoding.Default;
                        }
                    }
                }
            }

            return encCodePage;
        }

        protected static string URLToMRL(string url)
        {
            return URLToMRL(new Uri(url, true));
        }

        protected static string URLToMRL(Uri url)
        {
            if (url.Segments.Length < 3)
            {
                throw new ArgumentException();
            }

            int ndx = 1;
            string strAccess = url.Segments[ndx++];
            string strAddress = url.Segments[ndx++];

            Char[] slash = new Char[] { '/' };
            return string.Format("{0}://@{1}", strAccess.TrimEnd(slash), strAddress.TrimEnd(slash));
        }

        private static string ParseMrlKeysRequest(string strJson)
        {
            Regex parser = new Regex(@"^{\s*""jsonrpc""\s*:\s*""2.0""\s*,\s*""id""\s*:\s*1\s*," +
            @"\s*""method""\s*:\s*""get_mrl_keys""\s*,\s*""params""\s*:\s*{\s*""auth""\s*:\s*""macaddr""\s*," +
            @"\s*""macaddr""\s*:\s*""(?<macaddr>[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2})""\s*}\s*}\s*$", RegexOptions.Compiled);

            Match result = parser.Match(strJson);
            if (!result.Success)
            {
                throw new Exception("jSON request error, can't parse mac address.");
            }

            return result.Groups["macaddr"].Value.Replace('-', ':');
        }
    }
}