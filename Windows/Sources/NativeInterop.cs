using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AntigravityAssistant
{
    // MARK: - Win32 User32 / Shell32 / Kernel32 Native Interop

    internal static class Win32Native
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_TOPMOST = 0x00000008L;
        public const long WS_EX_TRANSPARENT = 0x00000020L;
        public const long WS_EX_LAYERED = 0x00080000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        public const int WM_HOTKEY = 0x0312;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_NOREPEAT = 0x4000;
        public const uint VK_A = 0x41;
        public const ushort VK_CONTROL = 0x11;
        public const ushort VK_K = 0x4B;
        public const ushort VK_RETURN = 0x0D;
        public const ushort VK_ESCAPE = 0x1B;

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        public static readonly uint WM_WAKEUP_INSTANCE = RegisterWindowMessageW("AntigravityAssistant_WakeUp_v1");
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;

        public const int ATTACH_PARENT_PROCESS = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AttachConsole(int dwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint RegisterWindowMessageW(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        public static long GetExStyle(IntPtr hWnd)
        {
            if (IntPtr.Size == 8)
            {
                return GetWindowLongPtr64(hWnd, GWL_EXSTYLE).ToInt64();
            }
            return GetWindowLong32(hWnd, GWL_EXSTYLE);
        }

        public static void SetExStyle(IntPtr hWnd, long style)
        {
            if (IntPtr.Size == 8)
            {
                SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(style));
            }
            else
            {
                SetWindowLong32(hWnd, GWL_EXSTYLE, (int)style);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static double GetWindowDpiScale(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    uint dpi = GetDpiForWindow(hwnd);
                    if (dpi >= 72 && dpi <= 576)
                    {
                        return dpi / 96.0;
                    }
                }
                catch
                {
                }
            }
            return 1.0;
        }

        public static void ForceActivateWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            IntPtr fg = GetForegroundWindow();
            uint dummyPid;
            uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out dummyPid) : 0;
            uint curThread = GetCurrentThreadId();
            bool attached = false;
            if (fgThread != 0 && fgThread != curThread)
            {
                attached = AttachThreadInput(curThread, fgThread, true);
            }
            try
            {
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
            }
            finally
            {
                if (attached)
                {
                    AttachThreadInput(curThread, fgThread, false);
                }
            }
        }

        // MARK: - Win32 SendInput Keyboard Helpers (Ctrl+K & Unicode Text)

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static void SendKeyShortcut(ushort vk, bool withControl)
        {
            List<INPUT> list = new List<INPUT>();
            if (withControl)
            {
                list.Add(MakeKeyInput(VK_CONTROL, 0, 0));
            }
            list.Add(MakeKeyInput(vk, 0, 0));
            list.Add(MakeKeyInput(vk, 0, KEYEVENTF_KEYUP));
            if (withControl)
            {
                list.Add(MakeKeyInput(VK_CONTROL, 0, KEYEVENTF_KEYUP));
            }
            INPUT[] arr = list.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendUnicodeString(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            List<INPUT> list = new List<INPUT>(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                ushort ch = text[i];
                list.Add(MakeKeyInput(0, ch, KEYEVENTF_UNICODE));
                list.Add(MakeKeyInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
            INPUT[] arr = list.ToArray();
            SendInput((uint)arr.Length, arr, Marshal.SizeOf(typeof(INPUT)));
        }

        private static INPUT MakeKeyInput(ushort vk, ushort scan, uint flags)
        {
            INPUT inp = new INPUT();
            inp.type = INPUT_KEYBOARD;
            inp.U.ki.wVk = vk;
            inp.U.ki.wScan = scan;
            inp.U.ki.dwFlags = flags;
            return inp;
        }

        // MARK: - Shell32 Recycle Bin Helper

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

        public static bool MoveDirectoryToRecycleBin(string fullPath)
        {
            try
            {
                SHFILEOPSTRUCTW op = new SHFILEOPSTRUCTW();
                op.wFunc = FO_DELETE;
                op.pFrom = fullPath + "\0\0";
                op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT);
                return SHFileOperationW(ref op) == 0 && !Directory.Exists(fullPath);
            }
            catch
            {
                return false;
            }
        }
    }

    // MARK: - Native Windows Built-in SQLite3 (`C:\Windows\System32\winsqlite3.dll`)

    internal static class WinSqlite3
    {
        public const int SQLITE_OK = 0;
        public const int SQLITE_ROW = 100;
        public const int SQLITE_OPEN_READONLY = 0x00000001;
        public const int SQLITE_OPEN_NOMUTEX = 0x00008000;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_open_v2(byte[] filenameUtf8, out IntPtr ppDb, int flags, IntPtr zVfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_busy_timeout(IntPtr db, int ms);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSqlUtf8, int nByte, out IntPtr ppStmt, IntPtr pzTail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_step(IntPtr pStmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr sqlite3_column_text(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_column_bytes(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_column_int(IntPtr pStmt, int iCol);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int sqlite3_finalize(IntPtr pStmt);

        public static int OpenReadOnly(string path, out IntPtr db)
        {
            byte[] utf8 = ToNullTerminatedUtf8(path);
            return sqlite3_open_v2(utf8, out db, SQLITE_OPEN_READONLY | SQLITE_OPEN_NOMUTEX, IntPtr.Zero);
        }

        public static int Prepare(IntPtr db, string sql, out IntPtr stmt)
        {
            byte[] utf8 = ToNullTerminatedUtf8(sql);
            return sqlite3_prepare_v2(db, utf8, -1, out stmt, IntPtr.Zero);
        }

        public static string ColumnString(IntPtr stmt, int col)
        {
            IntPtr ptr = sqlite3_column_text(stmt, col);
            if (ptr == IntPtr.Zero) return null;
            int byteLen = sqlite3_column_bytes(stmt, col);
            if (byteLen <= 0) return string.Empty;
            byte[] buf = new byte[byteLen];
            Marshal.Copy(ptr, buf, 0, byteLen);
            return Encoding.UTF8.GetString(buf);
        }

        private static byte[] ToNullTerminatedUtf8(string s)
        {
            int count = Encoding.UTF8.GetByteCount(s);
            byte[] bytes = new byte[count + 1];
            Encoding.UTF8.GetBytes(s, 0, s.Length, bytes, 0);
            bytes[count] = 0;
            return bytes;
        }
    }

    // MARK: - Lightweight Zero-Dependency RFC-8259 JSON Parser & Serializer

    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int idx = 0;
            return ParseValue(json, ref idx);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            return Parse(json) as Dictionary<string, object>;
        }

        public static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (!dict.TryGetValue(key, out val) || val == null) return null;
            string s = val as string;
            return s != null ? s : Convert.ToString(val, CultureInfo.InvariantCulture);
        }

        public static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (dict == null) return fallback;
            object val;
            if (!dict.TryGetValue(key, out val) || val == null) return fallback;
            if (val is long) return (int)(long)val;
            if (val is int) return (int)val;
            if (val is double) return (int)(double)val;
            int parsed;
            if (int.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture), out parsed)) return parsed;
            return fallback;
        }

        public static double? GetNullableDouble(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (!dict.TryGetValue(key, out val) || val == null) return null;
            if (val is double) return (double)val;
            if (val is long) return (double)(long)val;
            if (val is int) return (double)(int)val;
            double parsed;
            if (double.TryParse(Convert.ToString(val, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        public static bool? GetNullableBool(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (!dict.TryGetValue(key, out val) || val == null) return null;
            if (val is bool) return (bool)val;
            return null;
        }

        public static Dictionary<string, object> GetDict(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (!dict.TryGetValue(key, out val)) return null;
            return val as Dictionary<string, object>;
        }

        public static List<object> GetList(Dictionary<string, object> dict, string key)
        {
            if (dict == null) return null;
            object val;
            if (!dict.TryGetValue(key, out val)) return null;
            return val as List<object>;
        }

        public static string Serialize(object obj)
        {
            StringBuilder sb = new StringBuilder(256);
            WriteValue(sb, obj);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object val)
        {
            if (val == null)
            {
                sb.Append("null");
                return;
            }
            string s = val as string;
            if (s != null)
            {
                WriteString(sb, s);
                return;
            }
            if (val is bool)
            {
                sb.Append(((bool)val) ? "true" : "false");
                return;
            }
            if (val is int || val is long)
            {
                sb.Append(Convert.ToInt64(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (val is double || val is float)
            {
                double d = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("0");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            IDictionary<string, object> dict = val as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    WriteValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            System.Collections.IEnumerable list = val as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, val.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.AppendFormat(CultureInfo.InvariantCulture, "\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SkipWs(string s, ref int i)
        {
            int n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            if (c == '{') return ParseDict(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't')
            {
                if (i + 3 < s.Length && s[i + 1] == 'r' && s[i + 2] == 'u' && s[i + 3] == 'e')
                {
                    i += 4;
                    return true;
                }
                i++;
                return null;
            }
            if (c == 'f')
            {
                if (i + 4 < s.Length && s[i + 1] == 'a' && s[i + 2] == 'l' && s[i + 3] == 's' && s[i + 4] == 'e')
                {
                    i += 5;
                    return false;
                }
                i++;
                return null;
            }
            if (c == 'n')
            {
                if (i + 3 < s.Length && s[i + 1] == 'u' && s[i + 2] == 'l' && s[i + 3] == 'l')
                {
                    i += 4;
                    return null;
                }
                i++;
                return null;
            }
            return ParseNumber(s, ref i);
        }

        private static Dictionary<string, object> ParseDict(string s, ref int i)
        {
            Dictionary<string, object> dict = new Dictionary<string, object>(StringComparer.Ordinal);
            i++; // '{'
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == '}') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] != '"') { i++; break; }
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                object val = ParseValue(s, ref i);
                dict[key] = val;
            }
            return dict;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++; // '['
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) break;
                if (s[i] == ']') { i++; break; }
                if (s[i] == ',') { i++; continue; }
                list.Add(ParseValue(s, ref i));
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening '"'
            int start = i;
            int n = s.Length;
            bool hasEscape = false;
            while (i < n)
            {
                char c = s[i];
                if (c == '"')
                {
                    if (!hasEscape)
                    {
                        string fast = s.Substring(start, i - start);
                        i++;
                        return fast;
                    }
                    break;
                }
                if (c == '\\')
                {
                    hasEscape = true;
                    i += 2;
                    continue;
                }
                i++;
            }

            // Re-scan with escapes
            i = start;
            StringBuilder sb = new StringBuilder(32);
            while (i < n)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < n)
                {
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= n)
                            {
                                int code;
                                if (int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                }
                                i += 4;
                            }
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+')
                {
                    i++;
                }
                else if (c == '.' || c == 'e' || c == 'E')
                {
                    isFloat = true;
                    i++;
                }
                else
                {
                    break;
                }
            }
            string token = s.Substring(start, i - start);
            if (!isFloat)
            {
                long lv;
                if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out lv))
                    return lv;
            }
            double dv;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                return dv;
            return 0L;
        }
    }
}
