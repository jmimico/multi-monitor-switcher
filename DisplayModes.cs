// DisplayModes.cs - core Win32 display-configuration engine for the Work/Personal monitor switcher.
//
// Uses the current (Windows 11 24H2/25H2) Display Configuration (CCD) API:
//   GetDisplayConfigBufferSizes / QueryDisplayConfig / SetDisplayConfig (5-arg)
//
// Struct layouts verified against the Windows SDK 10.0.26xxx wingdi.h:
//   DISPLAYCONFIG_PATH_INFO   = 72 bytes (PATH_SOURCE_INFO 20 + PATH_TARGET_INFO 48 + flags 4)
//   DISPLAYCONFIG_MODE_INFO   = 64 bytes (infoType 4 + id 4 + adapterId 8 + union 48)
//
// Primary monitor = the display whose source-mode position is (0,0).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayModes
{
    public static class Core
    {
        // ---------- constants ----------
        const uint QDC_ALL_PATHS = 0x1;
        const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
        const uint SDC_APPLY = 0x80;
        const uint SDC_VALIDATE = 0x40;
        const uint SDC_USE_SUPPLIED = 0x20;
        const uint SDC_SAVE_TO_DATABASE = 0x200;
        const uint SDC_ALLOW_CHANGES = 0x400;
        const uint PDS_ACTIVE = 0x1;
        const uint MODE_IDX_INVALID = 0xFFFFFFFF;
        const uint SOURCE_IN_USE = 0x1;
        const uint TARGET_IN_USE = 0x1;
        const uint CDS_SET_PRIMARY = 0x00000010;
        const uint CDS_UPDATEREGISTRY = 0x00000001;

        const int PATH_SIZE = 72;
        const int MODE_SIZE = 64;

        // PATH_INFO offsets
        const int OFF_SRC_STATUS = 16;
        const int OFF_TGT_ID = 28;
        const int OFF_TGT_MODE_IDX = 32;
        const int OFF_TGT_TECH = 36;
        const int OFF_TGT_STATUS = 64;
        const int OFF_PATH_FLAGS = 68;
        // SOURCE_INFO offsets
        const int OFF_SRC_ID = 8;
        const int OFF_SRC_MODE_IDX = 12;
        // MODE_INFO offsets (union starts at 16)
        const int MO_INFO_TYPE = 0;
        const int MO_SRC_WIDTH = 16;
        const int MO_SRC_HEIGHT = 20;
        const int MO_SRC_POS_X = 28;
        const int MO_SRC_POS_Y = 32;

        // ---------- interop ----------
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetDisplayConfigBufferSizes(uint flags, ref uint pathCount, ref uint modeCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint QueryDisplayConfig(uint flags, ref uint pathCount, IntPtr pathArray,
            ref uint modeCount, IntPtr modeArray, IntPtr currentTopologyId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray,
            uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int DisplayConfigGetDeviceInfo(IntPtr requestPacket);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwFlags, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_DEVICE_NAME
        {
            public uint type;           // 2 = GET_TARGET_NAME
            public uint size;
            public uint luidLo;
            public int luidHi;
            public uint id;
            public uint nameFlags;
            public int outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_DEVICE_NAME
        {
            public uint type;           // 1 = GET_SOURCE_NAME
            public uint size;
            public uint luidLo;
            public int luidHi;
            public uint id;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        // ---------- state ----------
        class PathInfo
        {
            public int Index;
            public bool Active;
            public uint SrcId, TgtId;
            public uint SrcModeIdx, TgtModeIdx;
            public int X, Y, W, H;
            public bool HasSourceMode;
            public string Name = "";
            public uint Mfr, Prod;
        }

        class State
        {
            public List<PathInfo> Paths = new List<PathInfo>();
            public byte[][] Modes = new byte[0][];
            public byte[] PathBytes = new byte[0]; // raw path buffer, for editing/replaying
            public uint PathCount, ModeCount;
        }

        static uint ReadU32(byte[] b, int off) { return BitConverter.ToUInt32(b, off); }
        static void WriteU32(byte[] b, int off, uint v) { Buffer.BlockCopy(BitConverter.GetBytes(v), 0, b, off, 4); }

        static TARGET_DEVICE_NAME GetTargetName(uint adapterLo, int adapterHi, uint id)
        {
            var t = new TARGET_DEVICE_NAME();
            t.type = 2;
            t.size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>();
            t.luidLo = adapterLo; t.luidHi = adapterHi; t.id = id;
            IntPtr h = Marshal.AllocHGlobal((int)t.size);
            try
            {
                Marshal.StructureToPtr(t, h, false);
                if (DisplayConfigGetDeviceInfo(h) != 0)
                    return new TARGET_DEVICE_NAME();
                return (TARGET_DEVICE_NAME)Marshal.PtrToStructure(h, typeof(TARGET_DEVICE_NAME));
            }
            finally { Marshal.FreeHGlobal(h); }
        }

        static string GetSourceGdiName(uint adapterLo, int adapterHi, uint id)
        {
            var s = new SOURCE_DEVICE_NAME();
            s.type = 1;
            s.size = (uint)Marshal.SizeOf<SOURCE_DEVICE_NAME>();
            s.luidLo = adapterLo; s.luidHi = adapterHi; s.id = id;
            IntPtr h = Marshal.AllocHGlobal((int)s.size);
            try
            {
                Marshal.StructureToPtr(s, h, false);
                if (DisplayConfigGetDeviceInfo(h) != 0) return "";
                s = (SOURCE_DEVICE_NAME)Marshal.PtrToStructure(h, typeof(SOURCE_DEVICE_NAME));
                return s.viewGdiDeviceName ?? "";
            }
            finally { Marshal.FreeHGlobal(h); }
        }

        // Reads the live full configuration (all paths incl. inactive).
        static State QueryFull()
        {
            var st = new State();
            uint pc = 0, mc = 0;
            uint rc = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, ref pc, ref mc);
            if (rc != 0) throw new Exception("GetDisplayConfigBufferSizes failed: 0x" + rc.ToString("X"));
            st.PathCount = pc; st.ModeCount = mc;
            IntPtr pPaths = Marshal.AllocHGlobal((int)pc * PATH_SIZE);
            IntPtr pModes = Marshal.AllocHGlobal((int)mc * MODE_SIZE);
            try
            {
                uint pc2 = pc, mc2 = mc;
                rc = QueryDisplayConfig(QDC_ALL_PATHS, ref pc2, pPaths, ref mc2, pModes, IntPtr.Zero);
                if (rc != 0) throw new Exception("QueryDisplayConfig failed: 0x" + rc.ToString("X"));
                st.PathCount = pc2; st.ModeCount = mc2;

                var pathBytes = new byte[(int)pc2 * PATH_SIZE];
                Marshal.Copy(pPaths, pathBytes, 0, pathBytes.Length);
                st.PathBytes = pathBytes;
                var modeBytes = new byte[(int)mc2 * MODE_SIZE];
                Marshal.Copy(pModes, modeBytes, 0, modeBytes.Length);
                st.Modes = new byte[(int)mc2][];

                for (int i = 0; i < mc2; i++)
                {
                    var m = new byte[MODE_SIZE];
                    Buffer.BlockCopy(modeBytes, i * MODE_SIZE, m, 0, MODE_SIZE);
                    st.Modes[i] = m;
                }
                for (int i = 0; i < pc2; i++)
                {
                    var p = new PathInfo { Index = i };
                    int o = i * PATH_SIZE;
                    uint adapterLo = ReadU32(pathBytes, o + 0);
                    int adapterHi = (int)ReadU32(pathBytes, o + 4);
                    p.SrcId = ReadU32(pathBytes, o + OFF_SRC_ID);
                    p.SrcModeIdx = ReadU32(pathBytes, o + OFF_SRC_MODE_IDX);
                    p.TgtId = ReadU32(pathBytes, o + OFF_TGT_ID);
                    p.TgtModeIdx = ReadU32(pathBytes, o + OFF_TGT_MODE_IDX);
                    p.Active = (ReadU32(pathBytes, o + OFF_PATH_FLAGS) & PDS_ACTIVE) != 0;

                    if (p.SrcModeIdx != MODE_IDX_INVALID && p.SrcModeIdx < mc2)
                    {
                        var m = st.Modes[(int)p.SrcModeIdx];
                        if (ReadU32(m, MO_INFO_TYPE) == 1)
                        {
                            p.HasSourceMode = true;
                            p.W = (int)ReadU32(m, MO_SRC_WIDTH);
                            p.H = (int)ReadU32(m, MO_SRC_HEIGHT);
                            p.X = (int)ReadU32(m, MO_SRC_POS_X);
                            p.Y = (int)ReadU32(m, MO_SRC_POS_Y);
                        }
                    }
                    var tn = GetTargetName(adapterLo, adapterHi, p.TgtId);
                    p.Mfr = tn.edidManufactureId;
                    p.Prod = tn.edidProductCodeId;
                    p.Name = tn.monitorFriendlyDeviceName ?? "";
                    st.Paths.Add(p);
                }
            }
            finally { Marshal.FreeHGlobal(pPaths); Marshal.FreeHGlobal(pModes); }
            return st;
        }

        // ---------- public API ----------

        /// Returns JSON describing the live display configuration.
        public static string QueryStateJson()
        {
            return StateToJson(QueryFull());
        }

        /// Returns JSON describing the live mode pool (source + target modes).
        public static string QueryModesJson()
        {
            var st = QueryFull();
            var sb = new StringBuilder();
            sb.Append("{\"modes\":[");
            for (int i = 0; i < st.Modes.Length; i++)
            {
                var m = st.Modes[i];
                if (i > 0) sb.Append(",");
                uint t = ReadU32(m, MO_INFO_TYPE);
                uint id = ReadU32(m, 4);
                if (t == 1)
                {
                    sb.Append(string.Format(
                        "{{\"index\":{0},\"type\":\"SRC\",\"id\":{1},\"w\":{2},\"h\":{3},\"x\":{4},\"y\":{5}}}",
                        i, id, ReadU32(m, MO_SRC_WIDTH), ReadU32(m, MO_SRC_HEIGHT),
                        (int)ReadU32(m, MO_SRC_POS_X), (int)ReadU32(m, MO_SRC_POS_Y)));
                }
                else if (t == 2)
                {
                    // target: VIDEO_SIGNAL_INFO at union offset 16: activeSize.cx @40, .cy @44, pixelRate @16
                    sb.Append(string.Format(
                        "{{\"index\":{0},\"type\":\"TGT\",\"id\":{1},\"activeW\":{2},\"activeH\":{3},\"pixelRate\":{4}}}",
                        i, id, ReadU32(m, 16 + 24), ReadU32(m, 16 + 28),
                        (ReadU32(m, 16) | ((long)ReadU32(m, 20) << 32))));
                }
                else
                {
                    sb.Append(string.Format("{{\"index\":{0},\"type\":\"OTHER-{1}\",\"id\":{2}}}", i, t, id));
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// Applies a display recipe.
        /// identityMap: "msi=6936:3EA5;acer=7204:549"  (tag=EDIDmfr:EDIDprod, hex)
        /// recipe:      "acer=on@0,0;msi=off@0,0"       (tag=on@x,y | tag=off)
        /// Returns 0 on success, 100 if already in this state, otherwise an error code.
        public static int ApplyMode(string identityMap, string recipe, bool save, out string afterJson, out string error)
        {
            error = "";
            afterJson = "";
            try
            {
                // parse identity map
                var identity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var part in SplitSemicolon(identityMap))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    identity[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
                }
                // parse recipe
                var entries = new List<KeyValuePair<string, string>>();
                foreach (var part in SplitSemicolon(recipe))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    entries.Add(new KeyValuePair<string, string>(part.Substring(0, eq).Trim(), part.Substring(eq + 1).Trim()));
                }
                if (entries.Count == 0) { error = "empty recipe"; return 1; }

                var st = QueryFull();
                byte[] pathBytes = (byte[])st.PathBytes.Clone();

                // resolve each recipe tag to a path index
                var chosen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // tag -> path index
                var usedSrc = new HashSet<uint>();
                foreach (var p in st.Paths)
                    if (p.Active) usedSrc.Add(p.SrcId);

                foreach (var e in entries)
                {
                    string tag = e.Key;
                    bool wantOn = e.Value.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    int mfr, prod;
                    string ident;
                    if (!identity.TryGetValue(tag, out ident) || !ParseHexPair(ident, out mfr, out prod))
                    { error = "unknown identity for tag '" + tag + "'"; return 1; }

                    int bestIdx = -1;
                    if (wantOn)
                    {
                        // 1) already active on this monitor -> keep its path
                        foreach (var p in st.Paths)
                            if (p.Active && p.Mfr == (uint)mfr && p.Prod == (uint)prod) { bestIdx = p.Index; break; }
                        if (bestIdx < 0)
                        {
                            // 2) choose an inactive path for this monitor.
                            //    Priority: free source id + valid source mode >
                            //              free source id, no mode (best-mode logic fills it) >
                            //              busy source id + valid mode (last resort)
                            int freeWithMode = -1, freeNoMode = -1, busyWithMode = -1, busyNoMode = -1;
                            foreach (var p in st.Paths)
                            {
                                if (p.Active || p.Mfr != (uint)mfr || p.Prod != (uint)prod) continue;
                                bool free = !usedSrc.Contains(p.SrcId);
                                if (free)
                                {
                                    if (p.HasSourceMode) { if (freeWithMode < 0) freeWithMode = p.Index; }
                                    else { if (freeNoMode < 0) freeNoMode = p.Index; }
                                }
                                else
                                {
                                    if (p.HasSourceMode) { if (busyWithMode < 0) busyWithMode = p.Index; }
                                    else { if (busyNoMode < 0) busyNoMode = p.Index; }
                                }
                            }
                            if (freeWithMode >= 0) bestIdx = freeWithMode;
                            else if (freeNoMode >= 0) bestIdx = freeNoMode;
                            else if (busyWithMode >= 0) bestIdx = busyWithMode;
                            else if (busyNoMode >= 0) bestIdx = busyNoMode;
                        }
                        if (bestIdx >= 0) usedSrc.Add(st.Paths[bestIdx].SrcId);
                    }
                    else
                    {
                        foreach (var p in st.Paths)
                            if (p.Active && p.Mfr == (uint)mfr && p.Prod == (uint)prod) { bestIdx = p.Index; break; }
                        // if already inactive, nothing to do
                    }
                    if (bestIdx < 0 && wantOn)
                    { error = "monitor '" + tag + "' (EDID " + mfr.ToString("X4") + ":" + prod.ToString("X4") + ") not found - is it connected?"; return 2; }
                    if (bestIdx >= 0) chosen[tag] = bestIdx;
                }

                // no-op check: true if every tag already matches the desired state
                bool noChange = true;
                foreach (var e in entries)
                {
                    bool wantOn = e.Value.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    int cIdx;
                    if (!chosen.TryGetValue(e.Key, out cIdx))
                    {
                        // no path selected: for "on" this is an error (returned earlier);
                        // for "off" it means already inactive -> no change
                        if (wantOn) noChange = false;
                        continue;
                    }
                    var p = st.Paths[cIdx];
                    int px = 0, py = 0;
                    ParsePosition(e.Value, out px, out py);
                    if (p.Active != wantOn || (wantOn && (p.X != px || p.Y != py))) { noChange = false; break; }
                }
                if (noChange)
                {
                    afterJson = StateToJson(st);
                    return 100;
                }

                // apply edits to pathBytes
                foreach (var e in entries)
                {
                    int idx2;
                    if (!chosen.TryGetValue(e.Key, out idx2)) continue;
                    int idx = idx2;
                    bool wantOn = e.Value.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    int px, py; ParsePosition(e.Value, out px, out py);
                    int o = idx * PATH_SIZE;
                    if (wantOn)
                    {
                        WriteU32(pathBytes, o + OFF_PATH_FLAGS, ReadU32(pathBytes, o + OFF_PATH_FLAGS) | PDS_ACTIVE);
                        WriteU32(pathBytes, o + OFF_SRC_STATUS, ReadU32(pathBytes, o + OFF_SRC_STATUS) | SOURCE_IN_USE);
                        WriteU32(pathBytes, o + OFF_TGT_STATUS, ReadU32(pathBytes, o + OFF_TGT_STATUS) | TARGET_IN_USE);
                        var p = st.Paths[idx];
                        if (p.HasSourceMode && p.SrcModeIdx < st.ModeCount)
                        {
                            var m = st.Modes[(int)p.SrcModeIdx];
                            if (ReadU32(m, MO_INFO_TYPE) == 1)
                            {
                                WriteU32(m, MO_SRC_POS_X, (uint)px);
                                WriteU32(m, MO_SRC_POS_Y, (uint)py);
                            }
                        }
                    }
                    else
                    {
                        WriteU32(pathBytes, o + OFF_PATH_FLAGS, ReadU32(pathBytes, o + OFF_PATH_FLAGS) & ~PDS_ACTIVE);
                        WriteU32(pathBytes, o + OFF_SRC_STATUS, ReadU32(pathBytes, o + OFF_SRC_STATUS) & ~SOURCE_IN_USE);
                        WriteU32(pathBytes, o + OFF_TGT_STATUS, ReadU32(pathBytes, o + OFF_TGT_STATUS) & ~TARGET_IN_USE);
                    }
                }

                string err;
                uint rc = ApplyBuffers(st, pathBytes, save, out err);
                if (rc != 0) { error = err; return 10; }

                // pass 2: if best-mode logic placed a re-activated monitor somewhere
                // else, correct its position now that it has a source mode in the pool
                System.Threading.Thread.Sleep(300);
                var st2 = QueryFull();
                bool needPosFix = false;
                foreach (var e in entries)
                {
                    bool wantOn = e.Value.StartsWith("on", StringComparison.OrdinalIgnoreCase);
                    if (!wantOn) continue;
                    int mfr, prod;
                    string ident;
                    if (!identity.TryGetValue(e.Key, out ident) || !ParseHexPair(ident, out mfr, out prod)) continue;
                    var p = FindPath(st2.Paths, (uint)mfr, (uint)prod, true);
                    if (p == null) continue;
                    int px, py; ParsePosition(e.Value, out px, out py);
                    if (p.X != px || p.Y != py)
                    {
                        if (p.HasSourceMode && p.SrcModeIdx < st2.ModeCount &&
                            ReadU32(st2.Modes[(int)p.SrcModeIdx], MO_INFO_TYPE) == 1)
                        {
                            WriteU32(st2.Modes[(int)p.SrcModeIdx], MO_SRC_POS_X, (uint)px);
                            WriteU32(st2.Modes[(int)p.SrcModeIdx], MO_SRC_POS_Y, (uint)py);
                            needPosFix = true;
                        }
                    }
                }
                if (needPosFix)
                {
                    rc = ApplyBuffers(st2, st2.PathBytes, save, out err);
                    if (rc != 0) { error = "position fix failed: " + err; return 11; }
                    System.Threading.Thread.Sleep(300);
                    st2 = QueryFull();
                }
                afterJson = StateToJson(st2);
                return 0;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return 20;
            }
        }

        // validates and applies a set of raw path/mode buffers
        static uint ApplyBuffers(State st, byte[] pathBytes, bool save, out string err)
        {
            err = "";
            IntPtr npPaths = Marshal.AllocHGlobal(pathBytes.Length);
            IntPtr npModes = Marshal.AllocHGlobal((int)(st.ModeCount * MODE_SIZE));
            try
            {
                Marshal.Copy(pathBytes, 0, npPaths, pathBytes.Length);
                for (int i = 0; i < st.ModeCount; i++) Marshal.Copy(st.Modes[i], 0, (IntPtr)(npModes.ToInt64() + i * MODE_SIZE), MODE_SIZE);
                uint baseFlags = SDC_USE_SUPPLIED;
                uint rc = SetDisplayConfig(st.PathCount, npPaths, st.ModeCount, npModes, SDC_VALIDATE | baseFlags);
                if (rc != 0 && rc == 0x59 /* ERROR_BAD_CONFIGURATION */)
                {
                    rc = SetDisplayConfig(st.PathCount, npPaths, st.ModeCount, npModes, SDC_VALIDATE | baseFlags | SDC_ALLOW_CHANGES);
                    baseFlags |= SDC_ALLOW_CHANGES;
                }
                if (rc != 0) { err = "validate failed: 0x" + rc.ToString("X") + " (" + DecodeErr(rc) + ")"; return rc; }
                rc = SetDisplayConfig(st.PathCount, npPaths, st.ModeCount, npModes, SDC_APPLY | baseFlags | (save ? SDC_SAVE_TO_DATABASE : 0));
                if (rc != 0) { err = "apply failed: 0x" + rc.ToString("X") + " (" + DecodeErr(rc) + ")"; return rc; }
                return 0;
            }
            finally { Marshal.FreeHGlobal(npPaths); Marshal.FreeHGlobal(npModes); }
        }

        static PathInfo FindPath(List<PathInfo> paths, uint mfr, uint prod, bool activeOnly)
        {
            foreach (var p in paths)
                if (p.Mfr == mfr && p.Prod == prod && (!activeOnly || p.Active)) return p;
            return null;
        }

        /// Sets the given GDI device (e.g. \\.\DISPLAY2) as the primary monitor.
        public static int SetPrimaryGdi(string gdiName, out string error)
        {
            error = "";
            int rc = ChangeDisplaySettingsEx(gdiName, IntPtr.Zero, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_SET_PRIMARY, IntPtr.Zero);
            if (rc < 0) { error = "ChangeDisplaySettingsEx failed: " + rc; return 1; }
            return 0;
        }

        // ---------- helpers ----------

        static string[] SplitSemicolon(string s)
        {
            var list = new List<string>();
            foreach (var t in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (t.Trim().Length > 0) list.Add(t.Trim());
            return list.ToArray();
        }

        static bool ParseHexPair(string s, out int mfr, out int prod)
        {
            mfr = 0; prod = 0;
            var parts = s.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;
            int a, b;
            if (!int.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.HexNumber, null, out a)) return false;
            if (!int.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.HexNumber, null, out b)) return false;
            mfr = a; prod = b;
            return true;
        }

        static void ParsePosition(string value, out int x, out int y)
        {
            x = 0; y = 0;
            int at = value.IndexOf('@');
            if (at < 0) return;
            var coords = value.Substring(at + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (coords.Length == 2) { int.TryParse(coords[0], out x); int.TryParse(coords[1], out y); }
        }

        static string DecodeErr(uint rc)
        {
            switch (rc)
            {
                case 0x57: return "ERROR_INVALID_PARAMETER";
                case 0x59: return "ERROR_BAD_CONFIGURATION";
                case 0x5: return "ERROR_ACCESS_DENIED (no console session?)";
                case 0x32: return "ERROR_NOT_SUPPORTED";
                case 0x41: return "ERROR_GEN_FAILURE";
                default: return "(code " + (int)rc + ")";
            }
        }

        static string StateToJson(State st)
        {
            var sb = new StringBuilder();
            sb.Append("{\"paths\":[");
            for (int i = 0; i < st.Paths.Count; i++)
            {
                var p = st.Paths[i];
                if (i > 0) sb.Append(",");
                sb.Append(string.Format(
                    "{{\"index\":{0},\"active\":{1},\"srcId\":{2},\"tgtId\":{3},\"srcModeIdx\":{4},\"x\":{5},\"y\":{6},\"w\":{7},\"h\":{8},\"name\":\"{9}\",\"mfr\":{10},\"prod\":{11}}}",
                    p.Index, p.Active ? "true" : "false", p.SrcId, p.TgtId, p.SrcModeIdx,
                    p.X, p.Y, p.W, p.H, (p.Name ?? "").Replace("\"", "'"), (int)p.Mfr, (int)p.Prod));
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
