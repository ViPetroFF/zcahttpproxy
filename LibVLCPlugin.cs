using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.ComponentModel;


namespace LibVLCPlugin
{
    public class WINAPIImport
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr LoadLibrary(string lpLibFileName);
        [DllImport("kernel32.dll")]
        public static extern bool FreeLibrary(IntPtr hLibModule);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandle(string lpLibFileName);
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    }

    public class InvokeImport
    {
        [DllImport("Invoke.dll", CharSet = CharSet.Ansi)]
        public static extern int InvokePluginInit(IntPtr funcptr, string strParam);
        [DllImport("Invoke.dll")]
        public static extern IntPtr InvokeFunc(IntPtr funcptr, IntPtr pAccess);
        [DllImport("Invoke.dll")]
        public static extern IntPtr InvokeFunc(IntPtr funcptr, byte[] pBuffer, UInt32 dwSize);
        [DllImport("Invoke.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr InvokeFunc(IntPtr funcptr, IntPtr pLibVLC, string strAccess, string strDemux, string strPath);
        [DllImport("Invoke.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr InvokeFunc(IntPtr funcptr, string strIp, UInt16 wPort);
    }

    public class WINSOCKImport
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct fd_set
        {
            public uint fd_count;
            public int fd_array0;
            public int fd_array1;
            public int fd_array2;
            public int fd_array3;
            public Int64 fd_A0, fd_A1, fd_A2, fd_A3, fd_A4, fd_A5, fd_A6, fd_A7, fd_A8, fd_A9;
            public Int64 fd_A10, fd_A11, fd_A12, fd_A13, fd_A14, fd_A15, fd_A16, fd_A17, fd_A18, fd_A19;
            public Int64 fd_A20, fd_A21, fd_A22, fd_A23, fd_A24, fd_A25, fd_A26, fd_A27, fd_A28, fd_A29;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct timeval
        {
            public int tv_sec;
            public int tv_usec;
        }

        [DllImport("Ws2_32.dll")]
        public static extern int closesocket(Int32 socket);
        [DllImport("Ws2_32.dll")]
        public static extern int select(Int32 nfds, ref fd_set readfds, IntPtr writefds, IntPtr exceptfds, ref timeval timeout);
    }

    //public class LibVLCHookImport
    //{
    //    [DllImport("LibVLCHook.dll", CharSet = CharSet.Ansi)]
    //    public static extern uint CAServerAddressInit(string strHost, Int16 wPort);
    //}

    public class LibVLCModule
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct libvlc_exception_t
        {
            public int b_raised;
            public int i_code;
            public string psz_message; //public StringBuilder psz_message;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct libvlc_instance_t
        {
            public IntPtr p_libvlc_int;
        }

        //[DllImport("IpTvPlayer.Plugin.InterZet.dll", CharSet = CharSet.Ansi)]
        //public static extern int IpTvPlayerPluginInit();//string str);

        //[DllImport("LibVlc.dll"), CharSet=CharSet.Ansi, CallingConvention=CallingConvention.Cdecl]
        [DllImport("LibVlc.dll")]
        public static extern void libvlc_exception_init(ref libvlc_exception_t pExp);
        [DllImport("LibVlc.dll")]
        public static extern int libvlc_exception_raised(ref libvlc_exception_t pExp);
        [DllImport("LibVlc.dll")]
        public static extern IntPtr libvlc_new(int nArgc, string[] Args, ref libvlc_exception_t pExp);
        [DllImport("LibVlc.dll")]
        public static extern void libvlc_release(IntPtr plibVLC);

        private const string _strCAPluginZetFileName = "IpTvProxy.Plugin.InterZet.dll";
        private const string _strCALibVLCHookFileName = "LibVLCHook.dll";
        private const string _strInitZetIpTvPlugin = " frmPar=\"99999999\" ver=\"0.28.1.8823\"" +
            " cfUser=\"${CFG_PATH}IpTvProxy.User.ini\"" +
            " cfVlc=\"${CFG_PATH}IpTvProxy.Vlc.ini\"" +
            " cfProv=\"${CFG_PATH}Provider.ini\"" +
            " HttpCmdFunc=\"4301632\" ";

        private string[] _LibVLCArgs = {
                                          "--config=${CFG_PATH}IpTvProxy.Vlc.ini",
                                          //"-vvv",
                                          "--plugin-path=.\\plugins",
                                          "--ignore-config",
                                          "--no-plugins-cache",
                                          "--no-osd",
                                          "--no-media-library",
                                          "--no-one-instance"
                                      };

        private IntPtr _hmodCAPluginZet;
        private IntPtr _hmodCALibVLCHook;
        private IntPtr _pLibVLCHandle;
        private libvlc_instance_t _instanceVLC;
        private LibVLCAccess _LibAccess;


        public IntPtr LibVLCObject
        {
            get
            {
                return _instanceVLC.p_libvlc_int;
            }
        }

        public LibVLCModule(string strConfigPath, string strCaUri, IPAddress MCastIPAddr)
        {
            if (!strConfigPath.EndsWith("\\"))
            {
                strConfigPath += "\\";
            }
            _LibVLCArgs[0] = _LibVLCArgs[0].Replace("${CFG_PATH}", strConfigPath);
            if (!MCastIPAddr.Equals(IPAddress.None) && !MCastIPAddr.Equals(IPAddress.Any))
            {
                Array.Resize(ref _LibVLCArgs, _LibVLCArgs.Length + 1);
                _LibVLCArgs.SetValue("--miface-addr=" + MCastIPAddr.ToString(), _LibVLCArgs.GetUpperBound(0));
            }

            int iCAPort = 0;
            if (strCaUri.Length > 0)
            {
                Uri url = new Uri(strCaUri);
                if (url.Port > 0)
                {
                    iCAPort = url.Port;
                }
                Array.Resize(ref _LibVLCArgs, _LibVLCArgs.Length + 1);
                _LibVLCArgs.SetValue("--ca-authuri=" + strCaUri, _LibVLCArgs.GetUpperBound(0));
            }

            LibVLCModuleInit(_strInitZetIpTvPlugin.Replace("${CFG_PATH}", strConfigPath), _LibVLCArgs, MCastIPAddr, iCAPort);
        }


        public LibVLCModule(string strInitParamCAPlugin, string[] args)
        {
            LibVLCModuleInit(strInitParamCAPlugin, args, IPAddress.None, 0);
        }

        private void LibVLCModuleInit(string strInitParamCAPlugin, string[] args, IPAddress MCastIPAddr, int iCAPort)
        {
            try
            {
                bool bSuccess = CALibVLCHookLoad();
                CAPluginZetInit(strInitParamCAPlugin);
                if (bSuccess)
                {
                    CALibVLCHookInit(MCastIPAddr, iCAPort);
                }
                LibVLCInit(args);
                LibVLCCoreInit();
            }
            catch (Exception ex)
            {
                LibVLCRelease();
                throw ex;
            }
        }

        public void LibVLCRelease()
        {
            _LibAccess = null;

            if (IntPtr.Zero != _pLibVLCHandle)
            {
                _instanceVLC.p_libvlc_int = IntPtr.Zero;
                libvlc_release(_pLibVLCHandle);
                _pLibVLCHandle = IntPtr.Zero;
            }

            if (IntPtr.Zero != _hmodCALibVLCHook)
            {
                WINAPIImport.FreeLibrary(_hmodCALibVLCHook);
                _hmodCAPluginZet = IntPtr.Zero;
            }

            if (IntPtr.Zero != _hmodCAPluginZet)
            {
                WINAPIImport.FreeLibrary(_hmodCAPluginZet);
                _hmodCAPluginZet = IntPtr.Zero;
            }
        }

        private void CAPluginZetInit(string strInitParam)
        {
            if (File.Exists(_strCAPluginZetFileName))
            {
                IntPtr hmodCAPluginZet = WINAPIImport.LoadLibrary(_strCAPluginZetFileName);
                if (IntPtr.Zero != hmodCAPluginZet)
                {
                    IntPtr funcaddr = WINAPIImport.GetProcAddress(hmodCAPluginZet, "IpTvPlayerPluginInit");
                    if (IntPtr.Zero != funcaddr)
                    {
                        InvokeImport.InvokePluginInit(funcaddr, strInitParam);
                        _hmodCAPluginZet = hmodCAPluginZet;
                    }
                    else
                    {
                        int iErrCode = Marshal.GetLastWin32Error();
                        WINAPIImport.FreeLibrary(hmodCAPluginZet);
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
        }

        private bool CALibVLCHookLoad()
        {
            bool IsLoaded = false;
            if (File.Exists(_strCALibVLCHookFileName))
            {
                IntPtr hmodCALibVLCHook = WINAPIImport.LoadLibrary(_strCALibVLCHookFileName);
                if (IntPtr.Zero != hmodCALibVLCHook)
                {
                    //Console.WriteLine("WINAPIImport.LoadLibrary( {0} )", _strCALibVLCHookFileName);

                    IntPtr funcaddr = WINAPIImport.GetProcAddress(hmodCALibVLCHook, "_CAServerAddressInit@8");
                    if (IntPtr.Zero != funcaddr)
                    {
                        funcaddr = WINAPIImport.GetProcAddress(hmodCALibVLCHook, "_GetBestMacAddress@8");
                    }

                    if (IntPtr.Zero != funcaddr)
                    {
                        _hmodCALibVLCHook = hmodCALibVLCHook;
                        IsLoaded = true;
                    }
                    else
                    {
                        int iErrCode = Marshal.GetLastWin32Error();
                        WINAPIImport.FreeLibrary(hmodCALibVLCHook);
                        throw new Win32Exception(iErrCode);
                    }
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            return IsLoaded;
        }

        private void CALibVLCHookInit(IPAddress MCastIPAddr, int iCAPort)
        {
            if (IntPtr.Zero != _hmodCALibVLCHook)
            {
                //Console.WriteLine("WINAPIImport.LoadLibrary( {0} )", _strCALibVLCHookFileName);

                IntPtr funcaddr = WINAPIImport.GetProcAddress(_hmodCALibVLCHook, "_CAServerAddressInit@8");
                if (IntPtr.Zero != funcaddr)
                {
                    IntPtr status = InvokeImport.InvokeFunc(
                                    funcaddr,
                                    (MCastIPAddr.Equals(IPAddress.None) || MCastIPAddr.Equals(IPAddress.Any)) ? null : MCastIPAddr.ToString(),
                                    (UInt16)iCAPort
                                    );
                    if (0 != (int)status)
                    {
                        throw new Win32Exception((int)status);
                    }
                    //Console.WriteLine("Result of invocation PluginInit is " + result);
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            else
            {
                throw new Win32Exception((int)1157L);
            }
        }

        private void LibVLCInit(string[] args)
        {
            IntPtr pLibVLC;
            libvlc_exception_t exp = new libvlc_exception_t();
            libvlc_exception_init(ref exp);

            pLibVLC = libvlc_new(args.Length, args, ref exp);
            if (0 != libvlc_exception_raised(ref exp))
            {
                //Console.WriteLine("Error: {0}", exp.psz_message);
                throw new Win32Exception(exp.psz_message);
            }
            else
            {
                _instanceVLC = (libvlc_instance_t)Marshal.PtrToStructure(pLibVLC, typeof(libvlc_instance_t));
                _pLibVLCHandle = pLibVLC;
                //Console.WriteLine("pLibVLC Done! - {0:X}", pLibVLC);
            }
        }

        private void LibVLCCoreInit()
        {
            IntPtr hmodVLCCore = WINAPIImport.GetModuleHandle("libvlccore.dll");

            if (IntPtr.Zero != hmodVLCCore)
            {
                IntPtr funcaddr = WINAPIImport.GetProcAddress(hmodVLCCore, "input_item_SetMeta");
                if (IntPtr.Zero != funcaddr)
                {
                    IntPtr funcAccessNew = (IntPtr)(funcaddr.ToInt32() + 0x300);
                    IntPtr funcAccessDelete = (IntPtr)(funcaddr.ToInt32() + 0x240);
                    _LibAccess = new LibVLCAccess(LibVLCObject, funcAccessNew, funcAccessDelete);
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            else
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        public string GetBestMacAddress()
        {
            string strBestMac = string.Empty;
            if (IntPtr.Zero != _hmodCALibVLCHook)
            {
                IntPtr funcaddr = WINAPIImport.GetProcAddress(_hmodCALibVLCHook, "_GetBestMacAddress@8");
                if (IntPtr.Zero != funcaddr)
                {
                    byte[] arrMacAddr = new byte[6];
                    UInt32 len = (UInt32)InvokeImport.InvokeFunc(funcaddr, arrMacAddr, (UInt32)arrMacAddr.Length);
                    if (len > 0)
                    {
                        Object[] arrMacObj = new Object[6] { (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0 };
                        Array.Copy(arrMacAddr, arrMacObj, len);
                        strBestMac = string.Format("{0:x2}:{1:x2}:{2:x2}:{3:x2}:{4:x2}:{5:x2}", arrMacObj);
                    }
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }

            return strBestMac;
        }

        public Stream NewAccess(string strMrl)
        {
            return _LibAccess.OpenMRL(strMrl, LibVLCAccess.StreamKind.Stream1, 7);
        }

        public Stream NewAccessStream0(string strMrl, int timeout=0)
        {
            return _LibAccess.OpenMRL(strMrl, LibVLCAccess.StreamKind.Stream0, timeout);
        }

        public Stream NewAccessStream1(string strMrl, int timeout=0)
        {
            return _LibAccess.OpenMRL(strMrl, LibVLCAccess.StreamKind.Stream1, timeout);
        }
    }

    public class LibVLCAccess
    {
        public enum StreamKind
        {
            Stream0 = 0,
            Stream1 = 1,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct access_t
        {
            public string psz_object_type;
            public string psz_object_name;

            public string psz_header;
            public int i_flags;

            //volatile bool b_error;
            //volatile bool b_die;
            //bool b_force;
            //bool be_sure_to_add_VLC_COMMON_MEMBERS_to_struct;
            public uint boolOptions;

            public IntPtr p_libvlc;

            public IntPtr p_parent;

            public IntPtr p_private;

            // access structure
            public IntPtr p_module;

            public string psz_access;

            public string psz_path;

            public string psz_demux;

            //ssize_t     (*pf_read) ( access_t *, uint8_t *, size_t );   Return -1 if no data yet, 0 if no more data, else real data read
            public IntPtr pf_read;
            //block_t    *(*pf_block)( access_t * );                   return a block of data in his 'natural' size, NULL if not yet data or eof
            public IntPtr pf_block;
            //int         (*pf_seek) ( access_t *, int64_t );         can be null if can't seek
            public IntPtr pf_seek;
            //int         (*pf_control)( access_t *, int i_query, va_list args);
            public IntPtr pf_control;

            // Access has to maintain them uptodate
            //struct
            //{
            //unsigned int i_update;  Access sets them on change, Input removes them once take into account

            public Int64 i_size;//  Write only for access, read only for input
            public Int64 i_pos; //idem
            //bool         b_eof; idem
            public int b_eof; // idem

            public int i_title;     //idem, start from 0 (could be menu)
            public int i_seekpoint; //idem, start from 0
            //} info;
            public IntPtr p_sys;

            static public int SysOffset
            {
                get
                {
                    return (26*4);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct block_t
        {
            public IntPtr p_next;

            public uint i_flags;

            public Int64 i_pts;
            public Int64 i_dts;
            public Int64 i_length;

            public int i_samples; // Used for audio
            public int i_rate;

            public int i_buffer;
            public IntPtr p_buffer;

            // Rudimentary support for overloading block (de)allocation.
            public IntPtr pf_release;
        }

        public static block_t _sEmptyBlock = new block_t();
        public const int MTU = 65535;

        private class MRLParts
        {
            private string _strAccess, _strDemux, _strPath;

            public string Access
            {
                get
                {
                    return _strAccess;
                }
            }

            public string Demux
            {
                get
                {

                    return _strDemux;
                }
            }

            public string Path
            {
                get
                {

                    return _strPath;
                }
            }

            public MRLParts(string strAccess, string strDemux, string strPath)
            {
                _strAccess = strAccess;
                _strDemux = strDemux;
                _strPath = strPath;
            }

            public static MRLParts ParseMRL(string strMrl)
            {
                MRLParts result;

                int ndx = strMrl.IndexOf("://");
                if (ndx >= 0)
                {
                    string strAccess = strMrl.Substring(0, ndx);
                    string strPath = strMrl.Substring(ndx + 3);
                    string[] arrAccessDemux = strAccess.Split(new Char[] { '/' });
                    string strDemux = string.Empty;
                    ndx = 0;
                    if (ndx < arrAccessDemux.Length)
                    {
                        strAccess = arrAccessDemux[ndx++];
                    }
                    if (ndx < arrAccessDemux.Length)
                    {
                        strDemux = arrAccessDemux[ndx++];
                    }
                    strAccess = strAccess.TrimStart(new Char[] { '$' });
                    strDemux = strDemux.TrimStart(new Char[] { '$' });

                    result = new MRLParts(strAccess, strDemux, strPath);
                }
                else
                {
                    result = new MRLParts(string.Empty, string.Empty, strMrl);
                }

                return result;
            }
        }

        private IntPtr _LibVLCObject;
        private IntPtr _funcAccessNew;
        private IntPtr _funcAccessDelete;

        public LibVLCAccess(IntPtr LibVLCObject, IntPtr funcAccessNew, IntPtr funcAccessDelete)
        {
            if (IntPtr.Zero == LibVLCObject || IntPtr.Zero == funcAccessNew || IntPtr.Zero == funcAccessDelete)
            {
                throw new System.ArgumentNullException();
            }
            _LibVLCObject = LibVLCObject;
            _funcAccessNew = funcAccessNew;
            _funcAccessDelete = funcAccessDelete;
        }

        public Stream OpenMRL(string strMrl, StreamKind kind, int timeout)
        {
            MRLParts parts = MRLParts.ParseMRL(strMrl);
            IntPtr pAccess = InvokeImport.InvokeFunc(_funcAccessNew, _LibVLCObject, parts.Access, parts.Demux, parts.Path);

            Stream stream;

            if (IntPtr.Zero != pAccess)
            {
                try
                {
                    if (StreamKind.Stream0 == kind)
                    {
                        stream = new CAUDPStream(pAccess, _funcAccessDelete, timeout);
                    }
                    else
                    {
                        stream = new CAUDPStream1(pAccess, _funcAccessDelete, timeout);
                    }

                    pAccess = IntPtr.Zero;
                }
                finally
                {
                    if (IntPtr.Zero != pAccess)
                    {
                        InvokeImport.InvokeFunc(_funcAccessDelete, pAccess);
                    }
                }
            }
            else
            {
                throw new System.ArgumentNullException("pAccess");
            }

            return stream;
        }
    }

    public class CAUDPStream : Stream
    {
        private IntPtr _AccessVLCObject;
        private IntPtr _UDPSocketObject;
        private IntPtr _funcAccessDelete;
        private IntPtr _funcReadBlock;

        //private string _strAccess;

        private IntPtr _LastBlockHandle;
        private LibVLCAccess.block_t _LastBlock;
        private int _LastBlockOffset;

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanTimeout { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException("Length"); } }
        public override long Position { get { throw new NotSupportedException("get Position"); } set { throw new NotSupportedException("set Position"); } }


        public CAUDPStream(IntPtr ptrAccess, IntPtr funcAccessDelete, int timeout=0)
        {
            LibVLCAccess.access_t accessUDP = (LibVLCAccess.access_t)Marshal.PtrToStructure(ptrAccess, typeof(LibVLCAccess.access_t));

            if (IntPtr.Zero != accessUDP.pf_block)
            {
                _AccessVLCObject = ptrAccess;
                //_strAccess = accessUDP.psz_access;
                _funcAccessDelete = funcAccessDelete;
                _funcReadBlock = accessUDP.pf_block;

                IntPtr SysObject = (IntPtr)(ptrAccess.ToInt32() + LibVLCAccess.access_t.SysOffset);
                if ("ca" == accessUDP.psz_access)
                {
                    IntPtr UdpObject = Marshal.ReadIntPtr(SysObject);
                    UdpObject = Marshal.ReadIntPtr(UdpObject);
                    _UDPSocketObject = (IntPtr)(UdpObject.ToInt32() + LibVLCAccess.access_t.SysOffset);
                }
                else
                {
                    _UDPSocketObject = SysObject;
                }

                if (timeout > 0)
                {
                    WINSOCKImport.fd_set fds = new WINSOCKImport.fd_set();
                    WINSOCKImport.timeval timeoutv = new WINSOCKImport.timeval();

                    fds.fd_count = 0;
                    fds.fd_array0 = 0;
                    timeoutv.tv_sec = timeout;
                    timeoutv.tv_usec = 0;

                    fds.fd_array0 = Marshal.ReadInt32(_UDPSocketObject);
                    fds.fd_count = 1;

                    // Wait until timeout or data received.
                    int rc = WINSOCKImport.select(-1, ref fds, IntPtr.Zero, IntPtr.Zero, ref timeoutv);
                    if (0 == rc)
                    {
                        //SetLastError(WSAETIMEDOUT);
                        throw new Win32Exception((int)10060L, "Initial read operation timed out.");
                    }
                    else if (-1 == rc)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Initial read operation error.");
                    }
                }
            }
            else
            {
                throw new System.ArgumentNullException("accessUDP.pf_block");
            }
        }

        private void HoldLastBlock(IntPtr pBlockHandle, LibVLCAccess.block_t block, int offset)
        {
            if (IntPtr.Zero != pBlockHandle)
            {
                if (offset < block.i_buffer)
                {
                    _LastBlockHandle = pBlockHandle;
                    _LastBlock = block;
                    _LastBlockOffset = offset;
                }
                else
                {
                    ReleaseBlock(pBlockHandle, block);
                }
            }
        }

        private int ReadFromLastBlock(byte[] buffer, int offset, int count)
        {
            int iLen = 0;
            if (IntPtr.Zero != _LastBlockHandle)
            {
                IntPtr pBuffer = (IntPtr)(_LastBlock.p_buffer.ToInt32() + _LastBlockOffset);
                iLen = Math.Min(count, (_LastBlock.i_buffer - _LastBlockOffset));
                Marshal.Copy(pBuffer, buffer, offset, iLen);
                _LastBlockOffset += iLen;

                if (_LastBlockOffset == _LastBlock.i_buffer)
                {
                    ReleaseLastBuffer(true);
                }
            }

            return iLen;
        }

        private void ReleaseLastBuffer(bool disposing)
        {
            if (IntPtr.Zero != _LastBlockHandle)
            {
                ReleaseBlock(_LastBlockHandle, _LastBlock);
                _LastBlockHandle = IntPtr.Zero;
                if (disposing)
                {
                    _LastBlock = new LibVLCAccess.block_t();
                }
                _LastBlockOffset = 0;
            }
        }

        protected IntPtr ReadBlock(out LibVLCAccess.block_t block)
        {
            if (IntPtr.Zero == _AccessVLCObject)
            {
                throw new ObjectDisposedException("AccessVLCObject");
            }

            IntPtr pBlock = InvokeImport.InvokeFunc(_funcReadBlock, _AccessVLCObject);

            if (IntPtr.Zero != pBlock)
            {
                block = (LibVLCAccess.block_t)Marshal.PtrToStructure(pBlock, typeof(LibVLCAccess.block_t));
            }
            else
            {
                block = LibVLCAccess._sEmptyBlock;
            }

            return pBlock;
        }

        protected static void ReleaseBlock(IntPtr pBlockHandle, LibVLCAccess.block_t block)
        {
            IntPtr funcBlockRelease = block.pf_release;
            InvokeImport.InvokeFunc(funcBlockRelease, pBlockHandle);
        }

        protected override void Dispose(bool disposing)
        {
            if (IntPtr.Zero != _AccessVLCObject)
            {
                ReleaseLastBuffer(disposing);
                InvokeImport.InvokeFunc(_funcAccessDelete, _AccessVLCObject);
                _AccessVLCObject = IntPtr.Zero;
                _funcAccessDelete = IntPtr.Zero;
                _funcReadBlock = IntPtr.Zero;
            }
        }

        public override void Flush()
        {
            int usock = Marshal.ReadInt32(_UDPSocketObject);

            if (-1 != usock)
            {
                //Console.Error.WriteLine("void Stream.Flush() = WINSOCKImport.closesocket({0})", usock);
                if (0 == WINSOCKImport.closesocket(usock))
                {
                    Marshal.WriteInt32(_UDPSocketObject, -1);
                }

                //int fdNew = Marshal.ReadInt32(_UDPSocketObject);
                //Console.Error.WriteLine("New socket = {0}", fdNew);
            }
#if false
            IntPtr[] ptrBuf = new IntPtr[18];
            Marshal.Copy(CAUDPSocketObject, ptrBuf, 0, ptrBuf.Length);
            foreach (IntPtr ptr in ptrBuf)
            {
                Console.Error.WriteLine("Ptr value = {0}", ptr);
            }
#endif // false
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("Seek");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Write");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (IntPtr.Zero == _AccessVLCObject)
            {
                throw new ObjectDisposedException("AccessVLCObject");
            }

            int iLen = ReadFromLastBlock(buffer, offset, count);

            offset += iLen;
            count -= iLen;

            if (count > 0)
            {
                LibVLCAccess.block_t blockUDP;
                IntPtr pBlock = ReadBlock(out blockUDP);
                if (IntPtr.Zero != pBlock)
                {
                    int iBufferLen = Math.Min(count, blockUDP.i_buffer);
                    Marshal.Copy(blockUDP.p_buffer, buffer, offset, iBufferLen);
                    iLen += iBufferLen;
                    count -= iBufferLen;

                    if (count > 0)
                    {
                        ReleaseBlock(pBlock, blockUDP);
                    }
                    else
                    {
                        HoldLastBlock(pBlock, blockUDP, iBufferLen);
                    }
                }
            }

            return iLen;
        }
    }


    public class CAUDPStream1 : CAUDPStream
    {
        public CAUDPStream1(IntPtr ptrAccess, IntPtr funcAccessDelete, int timeout)
            : base(ptrAccess, funcAccessDelete, timeout)
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int iLen = 0;

            while (count > 0)
            {
                int iBuffLen = base.Read(buffer, offset, count);
                if (0 == iBuffLen)
                {
                    break;
                }
                iLen += iBuffLen;
                offset += iBuffLen;
                count -= iBuffLen;
            }

            return iLen;
        }
    }
}
