using System;
using System.Text;
using System.IO;
using System.Globalization;

namespace ZCAHTTPProxy
{
    static class Utf8Helper
    {

        private static DecoderExceptionFallback _decoderExceptionFallback = new DecoderExceptionFallback();

        public static bool Validate(this Encoding encoding, byte[] bytes, int offset, int length)
        {
            if (encoding == null)
            {
                throw new ArgumentNullException("encoding");
            }

            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }


            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Offset is out of range.");
            }

            if (length < 0 || length > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("length", "Length is out of range.");
            }
            else if ((offset + length) > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "The specified range is outside of the specified buffer.");
            }

            var decoder = encoding.GetDecoder();
            decoder.Fallback = _decoderExceptionFallback;

            try
            {
                var charCount = decoder.GetCharCount(bytes, 0, bytes.Length);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            return true;
        }

        // <summary>
        // Determines whether the bytes in this buffer at the specified offset represent a UTF-8 multi-byte character.
        // </summary>
        // <remarks>
        // It is not guaranteed that these bytes represent a sensical character - only that the binary pattern matches UTF-8 encoding.
        // </remarks>
        // <param name="bytes">This buffer.</param>
        // <param name="offset">The position in the buffer to check.</param>
        // <param name="length">The number of bytes to check, of 4 if not specified.</param>
        // <returns>The rank of the UTF</returns>
        public static MultibyteRank GetUtf8MultibyteRank(this byte[] bytes, int offset, int length)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }
            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Offset is out of range.");
            }
            else if (length < 0 || length > 5)
            {
                throw new ArgumentOutOfRangeException("length", "Only values 1-5 are valid.");
            }
            else if ((offset + length) > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "The specified range is outside of the specified buffer.");
            }

            // Possible 4 byte sequence
            if (length > 3 && IsLead4(bytes[offset]))
            {
                if (IsExtendedByte(bytes[offset + 1]) && IsExtendedByte(bytes[offset + 2]) && IsExtendedByte(bytes[offset + 3]))
                {
                    return MultibyteRank.Four;
                }
            }
            // Possible 3 byte sequence
            else if (length > 2 && IsLead3(bytes[offset]))
            {
                if (IsExtendedByte(bytes[offset + 1]) && IsExtendedByte(bytes[offset + 2]))
                {
                    return MultibyteRank.Three;
                }
            }
            // Possible 2 byte sequence
            else if (length > 1 && IsLead2(bytes[offset]) && IsExtendedByte(bytes[offset + 1]))
            {
                return MultibyteRank.Two;
            }

            if (bytes[offset] < 0x80)
            {
                return MultibyteRank.One;
            }
            else
            {
                return MultibyteRank.None;
            }
        }

        private static bool IsLead4(byte b)
        {
            return b >= 0xF0 && b < 0xF8;
        }

        private static bool IsLead3(byte b)
        {
            return b >= 0xE0 && b < 0xF0;
        }

        private static bool IsLead2(byte b)
        {
            return b >= 0xC0 && b < 0xE0;
        }

        private static bool IsExtendedByte(byte b)
        {
            return b >= 0x80 && b < 0xC0;
        }

        public enum MultibyteRank
        {
            None = 0,
            One = 1,
            Two = 2,
            Three = 3,
            Four = 4
        }

        public static bool IsUtf8(this byte[] bytes, int offset, int length)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException("bytes");
            }

            if (offset < 0 || offset > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Offset is out of range.");
            }
            else if (length < 0)
            {
                throw new ArgumentOutOfRangeException("length");
            }
            else if ((offset + length) > bytes.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "The specified range is outside of the specified buffer.");
            }

            int bytesRemaining = length;
            while (bytesRemaining > 0)
            {
                var rank = bytes.GetUtf8MultibyteRank(offset, Math.Min((int)MultibyteRank.Four, bytesRemaining));
                if (rank == MultibyteRank.None)
                {
                    return false;
                }
                else
                {
                    var charsRead = (int)rank;
                    offset += charsRead;
                    bytesRemaining -= charsRead;
                }
            }

            return true;
        }
    }

    static class ShellLink
    {
        public static string GetShortcutTarget(string strLinkPath)
        {
            if (Path.GetExtension(strLinkPath).ToLower() != ".lnk")
            {
                throw new FileNotFoundException("Supplied file must be a .LNK file");
            }

            FileStream fileStream = File.Open(strLinkPath, FileMode.Open, FileAccess.Read);
            using (BinaryReader fileReader = new BinaryReader(fileStream, Encoding.Default))
            {
                fileStream.Seek(0x14, SeekOrigin.Begin);     // Seek to flags
                uint flags = fileReader.ReadUInt32();        // Read flags
                if ((flags & 0x2) == 0 || 1 == (flags & 0x100))
                {
                    throw new NotSupportedException("Not found location information.");
                }

                if (1 == (flags & 0x1))
                {                      // Bit 1 set means we have to
                    // skip the shell item ID list
                    fileStream.Seek(0x4c, SeekOrigin.Begin); // Seek to the end of the header
                    uint offset = fileReader.ReadUInt16();   // Read the length of the Shell item ID list
                    fileStream.Seek(offset, SeekOrigin.Current); // Seek past it (to the file locator info)
                }

                long fileInfoStartsAt = fileStream.Position; // Store the offset where the file info
                // structure begins
                uint totalStructLength = fileReader.ReadUInt32(); // read the length of the whole struct
                fileStream.Seek(0xc, SeekOrigin.Current); // seek to offset to base pathname
                uint fileOffset = fileReader.ReadUInt32(); // read offset to base pathname
                fileStream.Seek(0x4, SeekOrigin.Current); // seek to offset to base pathname
                uint fileMoreOffset = fileReader.ReadUInt32(); // read offset to base pathname
                // the offset is from the beginning of the file info struct (fileInfoStartsAt)
                fileStream.Seek((fileInfoStartsAt + fileOffset), SeekOrigin.Begin); // Seek to beginning of
                // base pathname (target)
                int pathLength = (int)(totalStructLength + fileInfoStartsAt);
                // the base pathname. I don't need the 2 terminating nulls.
                char[] strbLocalPath = fileReader.ReadChars(pathLength - (int)fileStream.Position);
                fileStream.Seek((fileInfoStartsAt + fileMoreOffset), SeekOrigin.Begin); // Seek to beginning of
                char[] strbCommonPath = fileReader.ReadChars(pathLength - (int)fileStream.Position);
                //string str = new string(linkTarget);
                string strLocalPath = new string(strbLocalPath, 0, Array.IndexOf(strbLocalPath, char.MinValue));
                string strCommonPath = new string(strbCommonPath, 0, Array.IndexOf(strbCommonPath, char.MinValue));

                if (strCommonPath.Length > 0)
                {
                    strLocalPath = Path.Combine(strLocalPath, strCommonPath);
                }

                return strLocalPath;
            }
        }
    }

    static class Log
    {
        private static readonly int _iPadSize = 75;
        private static int _iProgressHigh = 0;
        private static bool _bIsFileLog = true;

        public static bool EnableFileLog
        {
            get { return _bIsFileLog; }
            set { _bIsFileLog = value; }
        }

        static Log()
        {
            //Console.CursorVisible = false;
            try
            {
                _iPadSize = Console.BufferWidth - 1;
                _bIsFileLog = false;
            }
            catch
            {
            }
        }

        private static void WriteLine(string msg, TextWriter writer)
        {
            DateTime timeNow = DateTime.UtcNow.ToLocalTime();
            string date = timeNow.ToShortDateString();

            string message = string.Format("{0} {1}> {2}", date.Substring(0, date.Length - 5), timeNow.ToShortTimeString(), msg);

            if (_bIsFileLog)
            {
                writer.WriteLine(message);
            }
            else
            {
                lock (typeof(Log))
                {
                    Console.SetCursorPosition(0, Console.CursorTop - _iProgressHigh);
                    writer.WriteLine(message.PadRight(_iPadSize));

                    for (int ndx = 0; ndx < _iProgressHigh; ndx++)
                    {
                        writer.WriteLine(string.Empty.PadLeft(_iPadSize));
                    }
                }
            }
        }

        public static int PadSize
        {
            get { return _iPadSize - 16; }
        }

        public static void Message(string msg)
        {
            WriteLine(msg, Console.Out);
        }

        public static void Error(string msg)
        {
            WriteLine(msg, Console.Error);
        }

        public static void MessageProgress(string msg, int ID)
        {
            if (!_bIsFileLog)
            {
                lock (typeof(Log))
                {
                    if (ID > _iProgressHigh)
                    {
                        _iProgressHigh = ID;
                        for (int ndx = 0; ndx < (ID - _iProgressHigh + 1); ndx++)
                        {
                            Console.WriteLine(string.Empty.PadLeft(_iPadSize));
                        }
                    }
                    int CursorTop = Console.CursorTop;
                    Console.SetCursorPosition(0, Console.CursorTop - ID);
                    Console.Write(msg.PadRight(_iPadSize));
                    Console.SetCursorPosition(0, CursorTop);
                }
            }
        }

        public static string TrimPort(string url)
        {
            string strResult = url;
            int ndx = strResult.IndexOf(':');

            if (ndx >= 0)
            {
                strResult = strResult.Substring(0, ndx);
            }

            return strResult;
        }

        public static string GetSizeHtmlString(long size)
        {
            if (0 == size)
                return "0";
            else if (size < 1024)
                return size.ToString();
            else if (size < 1024 * 1024)
                return (size / 1024).ToString() + "K";
            else if (size < 1024 * 1024 * 1024)
                return (size / (1024 * 1024)).ToString() + "M";
            else
                return (size / (1024 * 1024 * 1024)).ToString() + "G";
        }

        public static string GetSizeString(ulong size)
        {
            if (size < 1024)
                return size.ToString() + " bytes";
            else if (size < 1024 * 1024)
                return (size / 1024).ToString() + " KB";
            else if (size < 1024 * 1024 * 1024)
                return ((float)size / (1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture.NumberFormat) + " MB";
            else
                return ((double)size / (1024 * 1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture.NumberFormat) + " GB";
        }

        public static string GetSmallSizeString(int size)
        {
            if (size < 1024)
                return size.ToString() + " bytes";
            else if (size < 1024 * 1024)
                return (size / 1024).ToString() + " KB";
            else
                return ((float)size / (1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture.NumberFormat) + " MB";
        }
    }
}