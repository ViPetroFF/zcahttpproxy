//#define PROXY_VERSION_1_2_3
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.ComponentModel;

#if NET_45_OR_GREATER
using System.Threading;
using System.Threading.Tasks;
#endif // NET_45_OR_GREATER

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

        public LibVLCModule(string strConfigPath, string strCaUri, IPAddress mcifIPAddr)
        {
            if (!strConfigPath.EndsWith("\\"))
            {
                strConfigPath += "\\";
            }
            _LibVLCArgs[0] = _LibVLCArgs[0].Replace("${CFG_PATH}", strConfigPath);
            if (!mcifIPAddr.Equals(IPAddress.None) && !mcifIPAddr.Equals(IPAddress.Any))
            {
                Array.Resize(ref _LibVLCArgs, _LibVLCArgs.Length + 1);
                _LibVLCArgs.SetValue("--miface-addr=" + mcifIPAddr.ToString(), _LibVLCArgs.GetUpperBound(0));
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

            LibVLCModuleInit(_strInitZetIpTvPlugin.Replace("${CFG_PATH}", strConfigPath), _LibVLCArgs, mcifIPAddr, iCAPort);
        }


        public LibVLCModule(string strInitParamCAPlugin, string[] args)
        {
            LibVLCModuleInit(strInitParamCAPlugin, args, IPAddress.None, 0);
        }

        private void LibVLCModuleInit(string strInitParamCAPlugin, string[] args, IPAddress mcifIPAddr, int iCAPort)
        {
            try
            {
                bool bSuccess = CALibVLCHookLoad();
                CAPluginZetInit(strInitParamCAPlugin);
                if (bSuccess)
                {
                    CALibVLCHookInit(mcifIPAddr, iCAPort);
                }
                LibVLCInit(args);
                LibVLCCoreInit(mcifIPAddr);
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

        private void CALibVLCHookInit(IPAddress mcifIPAddr, int iCAPort)
        {
            if (IntPtr.Zero != _hmodCALibVLCHook)
            {
                //Console.WriteLine("WINAPIImport.LoadLibrary( {0} )", _strCALibVLCHookFileName);

                IntPtr funcaddr = WINAPIImport.GetProcAddress(_hmodCALibVLCHook, "_CAServerAddressInit@8");
                if (IntPtr.Zero != funcaddr)
                {
                    IntPtr status = InvokeImport.InvokeFunc(
                                    funcaddr,
                                    (mcifIPAddr.Equals(IPAddress.None) || mcifIPAddr.Equals(IPAddress.Any)) ? null : mcifIPAddr.ToString(),
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

        private void LibVLCCoreInit(IPAddress mcifIPAddr)
        {
            IntPtr hmodVLCCore = WINAPIImport.GetModuleHandle("libvlccore.dll");

            if (IntPtr.Zero != hmodVLCCore)
            {
                IntPtr funcaddr = WINAPIImport.GetProcAddress(hmodVLCCore, "input_item_SetMeta");
                //IntPtr funcaddr2 = WINAPIImport.GetProcAddress(hmodVLCCore, "block_Alloc");

                if (IntPtr.Zero != funcaddr)
                {
                    IntPtr funcAccessNew = (IntPtr)(funcaddr.ToInt32() + 0x300);
                    IntPtr funcAccessDelete = (IntPtr)(funcaddr.ToInt32() + 0x240);
                    if (mcifIPAddr.Equals(IPAddress.None))
                    {
                        mcifIPAddr = IPAddress.Any;
                    }
                    _LibAccess = new LibVLCAccess(LibVLCObject, funcAccessNew, funcAccessDelete, mcifIPAddr);
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

        public Stream NewAccessStream0(string strMrl, int timeout = 0)
        {
            return _LibAccess.OpenMRL(strMrl, LibVLCAccess.StreamKind.Stream0, timeout);
        }

        public Stream NewAccessStream1(string strMrl, int timeout = 0)
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
                    return (26 * 4);
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

        public const int MaxMTU = 65535;
        public const int EthernetMTU = 1500;
        public const int MTU = EthernetMTU;

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
        private IPAddress _mcifIPAddr;

        public LibVLCAccess(IntPtr LibVLCObject, IntPtr funcAccessNew, IntPtr funcAccessDelete, IPAddress mcifIPAddr)
        {
            if (IntPtr.Zero == LibVLCObject || IntPtr.Zero == funcAccessNew || IntPtr.Zero == funcAccessDelete)
            {
                throw new System.ArgumentNullException();
            }
            _LibVLCObject = LibVLCObject;
            _funcAccessNew = funcAccessNew;
            _funcAccessDelete = funcAccessDelete;
            _mcifIPAddr = mcifIPAddr;
        }

        public Stream OpenMRL(string strMrl, StreamKind kind, int timeout)
        {
            MRLParts parts = MRLParts.ParseMRL(strMrl);
            IntPtr pAccess;
            if ("ca" == parts.Access && parts.Path.Length > 0)
            {
                pAccess = InvokeImport.InvokeFunc(_funcAccessNew, _LibVLCObject, parts.Access, parts.Demux, parts.Path);

                if (IntPtr.Zero == pAccess)
                {
                    throw new System.ArgumentNullException("pAccess");
                }
            }
            else
            {
                pAccess = IntPtr.Zero;
            }

            string strPath = parts.Path;
            int ndx = strPath.LastIndexOf(':');
            if (ndx < 0)
            {
                throw new System.ArgumentNullException("strIP");
            }

            string strIPAddr = strPath.Substring(0, ndx);
            strIPAddr = strIPAddr.TrimStart(new Char[] { '@' });

            int iPort = int.Parse(strPath.Substring(ndx + 1));
            IPEndPoint multicastAddr = new IPEndPoint(IPAddress.Parse(strIPAddr), iPort);

            Stream stream;

            try
            {
                if (StreamKind.Stream0 == kind)
                {
                    stream = new CAUDPStream(multicastAddr, pAccess, _funcAccessDelete, _mcifIPAddr, timeout);
                }
                else
                {
                    stream = new CAUDPStream1(multicastAddr, pAccess, _funcAccessDelete, _mcifIPAddr, timeout);
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

            return stream;
        }
    }

#if PROXY_VERSION_1_2_3
    public class CAUDPStream : Stream
    {
        private IntPtr _AccessVLCObject;
        private IntPtr _UDPSocketObject;
        private IntPtr _funcAccessDelete;
        private IntPtr _funcReadBlock;

        private IntPtr _LastBlockHandle;
        private LibVLCAccess.block_t _LastBlock;
        private int _LastBlockOffset;

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanTimeout { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException("Length"); } }
        public override long Position { get { throw new NotSupportedException("get Position"); } set { throw new NotSupportedException("set Position"); } }


        public CAUDPStream(string strIP, IntPtr ptrAccess, IntPtr funcAccessDelete, int timeout = 0)
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
                block = new LibVLCAccess.block_t();
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
                if (0 == WINSOCKImport.closesocket(usock))
                {
                    Marshal.WriteInt32(_UDPSocketObject, -1);
                }
            }

            //IntPtr[] ptrBuf = new IntPtr[18];
            //Marshal.Copy(CAUDPSocketObject, ptrBuf, 0, ptrBuf.Length);
            //foreach (IntPtr ptr in ptrBuf)
            //{
                //Console.Error.WriteLine("Ptr value = {0}", ptr);
            //}
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

            if (iLen > 0)
            {
                offset += iLen;
                count -= iLen;
            }

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
#endif // PROXY_VERSION_1_2_3

#if !PROXY_VERSION_1_2_3
    public class CAUDPStream : Stream
    {
        protected class VLCBlock : IDisposable
        {
            public const int BlockStructSize = 10 * 8;
            private IntPtr _ptrBlock;
            private GCHandle _handle;


            public VLCBlock(byte[] data)
            {
                Init(data, data.Length);
            }

            public VLCBlock(byte[] data, int length)
            {
                Init(data, length);
            }

            public IntPtr VLCBlockPtr
            {
                get
                {
                    return _ptrBlock;
                }
            }

            public void Dispose()
            {
                if (IntPtr.Zero != _ptrBlock)
                {
                    _handle.Free();
                    Marshal.FreeHGlobal(_ptrBlock);
                    _ptrBlock = IntPtr.Zero;
                }

                GC.SuppressFinalize(this);
            }

            private void Init(byte[] data, int length)
            {
                _ptrBlock = Marshal.AllocHGlobal(BlockStructSize);
                _handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr ptrBuffer = _handle.AddrOfPinnedObject();
                //Marshal.WriteInt32(ptr + 5 * 8, Length);
                //Marshal.WriteIntPtr(ptr + 5 * 8 + 4, ptrBuffer);

                LibVLCAccess.block_t vlcblock = new LibVLCAccess.block_t();
                vlcblock.i_buffer = data.Length;
                vlcblock.p_buffer = ptrBuffer;
                Marshal.StructureToPtr(vlcblock, _ptrBlock, false);
                //Marshal.WriteInt32(ptr + 6 * 8 + 4, GetAllocSize());
            }
        }

        private Socket _UdpSocket;
#if NET_45_OR_GREATER
        private UdpClient _UdpClient;
#endif // NET_45_OR_GREATER
        private IntPtr _AccessVLCObject;
        private IntPtr _UDPSocketObject;
        private IntPtr _funcAccessDelete;
        private IntPtr _funcReadBlock;

        private byte[] _LastBlock;
        private int _LastBlockOffset;
        private bool _bCaDecrypt;

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanTimeout { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException("Length"); } }
        public override long Position { get { throw new NotSupportedException("get Position"); } set { throw new NotSupportedException("set Position"); } }


        public CAUDPStream(IPEndPoint multicastAddr, IntPtr ptrAccess, IntPtr funcAccessDelete, IPAddress mcifIPAddr, int timeout = 0)
        {
#if NET_45_OR_GREATER
            _UdpClient = new UdpClient();
            _UdpClient.ExclusiveAddressUse = false;
            _UdpSocket = _UdpClient.Client;
#else
            _UdpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
#endif // NET_45_OR_GREATER
            _UdpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

//#if !NET_45_OR_GREATER
            // Increase the receive buffer size to 1/2MB (8Mb/s during 1/2s)
            // to avoid packet loss caused in case of scheduling hiccups
            _UdpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, 0x80000);
            //_UdpSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendBuffer, 0x80000);
//#endif // NET_45_OR_GREATER

            IPEndPoint localEp = new IPEndPoint(IPAddress.Any, multicastAddr.Port);
            _UdpSocket.Bind(localEp);
#if NET_45_OR_GREATER
            _UdpClient.JoinMulticastGroup(multicastAddr.Address, mcifIPAddr);
#else
            MulticastOption mcastOption = new MulticastOption(multicastAddr.Address, mcifIPAddr);
            _UdpSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, mcastOption);
#endif // NET_45_OR_GREATER

            if (timeout > 0)
            {
                bool bStatus = _UdpSocket.Poll(timeout * 1000000, SelectMode.SelectRead);
                if (!bStatus)
                {
                    //SetLastError(WSAETIMEDOUT);
                    _UdpSocket.Close();
                    throw new Win32Exception((int)10060L, "Initial read operation timed out.");
                }
            }

            bool bCaDecrypt = false;

            if (IntPtr.Zero != ptrAccess)
            {
                LibVLCAccess.access_t accessUDP = (LibVLCAccess.access_t)Marshal.PtrToStructure(ptrAccess, typeof(LibVLCAccess.access_t));

                if (IntPtr.Zero != accessUDP.pf_block)
                {
                    _AccessVLCObject = ptrAccess;
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

                    bCaDecrypt = true;
                }
                else
                {
                    throw new System.ArgumentNullException("accessUDP.pf_block");
                }
            }

            _bCaDecrypt = bCaDecrypt;
        }

        private void HoldLastBlock(byte[] block, int offset)
        {
            if (offset < block.Length)
            {
                _LastBlock = block;
                _LastBlockOffset = offset;
            }
        }

        private int ReadFromLastBlock(byte[] buffer, int offset, int count)
        {
            int iLen = 0;
            if (null != _LastBlock)
            {
                iLen = Math.Min(count, (_LastBlock.Length - _LastBlockOffset));
                Buffer.BlockCopy(_LastBlock, _LastBlockOffset, buffer, offset, iLen);
                _LastBlockOffset += iLen;

                if (_LastBlockOffset == _LastBlock.Length)
                {
                    _LastBlock = null;
                }
            }

            return iLen;
        }

#if NET_45_OR_GREATER
        protected Task<UdpReceiveResult> ReadBlockAsync()
        {
            Task<UdpReceiveResult> result = _UdpClient.ReceiveAsync();

            return result;
        }

        protected byte[] DecryptBlockAsyncContinue(byte[] dataBlock)
        {
            using (VLCBlock vlcBlock = new VLCBlock(dataBlock))
            {
                IntPtr ptr = vlcBlock.VLCBlockPtr;

                Marshal.WriteIntPtr(_UDPSocketObject, ptr);
                IntPtr pBlock = InvokeImport.InvokeFunc(_funcReadBlock, _AccessVLCObject);
                //Marshal.WriteInt32(_UDPSocketObject, -1);

                LibVLCAccess.block_t blockNew = (LibVLCAccess.block_t)Marshal.PtrToStructure(pBlock, typeof(LibVLCAccess.block_t));

                if (IntPtr.Zero == pBlock)// || ptr != pBlock || blockNew.p_buffer != ptr + UDPBlock.BufferOffset)
                {
                    Array.Resize(ref dataBlock, 0);
                    //Console.WriteLine("data block was not decrypted");
                    throw new IOException("data block was not decrypted");
                }
                else
                {
                    Array.Resize(ref dataBlock, blockNew.i_buffer);//dataBlock.Resize(blockNew.i_buffer);
                }
            }

            return dataBlock;
        }
#endif // NET_45_OR_GREATER

        protected byte[] ReadBlock()
        {
            byte[] dataBlock = new byte[LibVLCAccess.EthernetMTU + 120];
            SocketError err;
            int iLen = _UdpSocket.Receive(dataBlock, 0, dataBlock.Length, SocketFlags.None, out err);

            if (IntPtr.Zero != _AccessVLCObject && iLen > 0)
            {
                using (VLCBlock vlcBlock = new VLCBlock(dataBlock, iLen))
                {

                    IntPtr ptr = vlcBlock.VLCBlockPtr;

                    Marshal.WriteIntPtr(_UDPSocketObject, ptr);
                    IntPtr pBlock = InvokeImport.InvokeFunc(_funcReadBlock, _AccessVLCObject);
                    //Marshal.WriteInt32(_UDPSocketObject, -1);

                    LibVLCAccess.block_t blockNew = (LibVLCAccess.block_t)Marshal.PtrToStructure(pBlock, typeof(LibVLCAccess.block_t));

                    if (IntPtr.Zero == pBlock)// || ptr != pBlock || blockNew.p_buffer != ptr + UDPBlock.BufferOffset)
                    {
                        Array.Resize(ref dataBlock, 0);
                        //Console.WriteLine("data block was not decrypted");
                        throw new IOException("data block was not decrypted");
                    }
                    else
                    {
                        Array.Resize(ref dataBlock, blockNew.i_buffer);
                    }
                }
            }

            return dataBlock;
        }

        protected override void Dispose(bool disposing)
        {
            _LastBlock = null;

            if (IntPtr.Zero != _AccessVLCObject)
            {
                InvokeImport.InvokeFunc(_funcAccessDelete, _AccessVLCObject);
                _AccessVLCObject = IntPtr.Zero;
                _funcAccessDelete = IntPtr.Zero;
                _funcReadBlock = IntPtr.Zero;
            }

#if NET_45_OR_GREATER
            if (disposing)// && null != _UdpClient)
            {
                _UdpClient.Close();
                //_UdpClient = null;
            }
#else
            if (disposing && null != _UdpSocket)
            {
                _UdpSocket.Dispose();
                _UdpSocket = null;
            }
#endif // NET_45_OR_GREATER
        }

        public override void Flush()
        {
            _UdpSocket.Close();
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
            int iLen = ReadFromLastBlock(buffer, offset, count);

            if (iLen > 0)
            {
                offset += iLen;
                count -= iLen;
            }

            if (count > 0)
            {
                byte[] blockUDP = ReadBlock();
                if (blockUDP.Length > 0)
                {
                    int iBufferLen = Math.Min(count, blockUDP.Length);
                    //Marshal.Copy(blockUDP.p_buffer, buffer, offset, iBufferLen);
                    Buffer.BlockCopy(blockUDP, 0, buffer, offset, iBufferLen);
                    iLen += iBufferLen;
                    count -= iBufferLen;

                    if (!(count > 0))
                    {
                        HoldLastBlock(blockUDP, iBufferLen);
                    }
                }
            }

            return iLen;
        }

#if NET_45_OR_GREATER
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int iLen = ReadFromLastBlock(buffer, offset, count);

            if (iLen > 0)
            {
                offset += iLen;
                count -= iLen;
            }

            Task result;

            if (count > 0)
            {
                result = ReadBlockAsync();

                result = result.ContinueWith<int>(t =>
                {
                    if (t.IsFaulted)
                    {
                        return 0;
                        //throw t.Exception.InnerException;
                    }

                    if (t.IsCanceled)
                    {
                        throw (null == t.Exception) ? new OperationCanceledException() : t.Exception.InnerException;
                    }

                    Task<UdpReceiveResult> task = (Task<UdpReceiveResult>)t;

                    byte[] blockUDP = task.Result.Buffer;

                    if (_bCaDecrypt)
                    {
                        blockUDP = DecryptBlockAsyncContinue(blockUDP);
                    }

                    if (blockUDP.Length > 0)
                    {
                        int iBufferLen = Math.Min(count, blockUDP.Length);
                        //Marshal.Copy(blockUDP.p_buffer, buffer, offset, iBufferLen);
                        Buffer.BlockCopy(blockUDP, 0, buffer, offset, iBufferLen);
                        iLen += iBufferLen;
                        count -= iBufferLen;

                        if (!(count > 0))
                        {
                            HoldLastBlock(blockUDP, iBufferLen);
                        }
                    }

                    return iLen;
                }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                result = Task<int>.Run(() =>
                {
                    return iLen;
                }
                    , cancellationToken);
            }

            return (Task<int>)result;
        }
#endif // NET_45_OR_GREATER
    }
#endif // !PROXY_VERSION_1_2_3

    public class CAUDPStream1 : CAUDPStream
    {
        public CAUDPStream1(IPEndPoint multicastAddr, IntPtr ptrAccess, IntPtr funcAccessDelete, IPAddress mcifIPAddr, int timeout)
            : base(multicastAddr, ptrAccess, funcAccessDelete, mcifIPAddr, timeout)
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