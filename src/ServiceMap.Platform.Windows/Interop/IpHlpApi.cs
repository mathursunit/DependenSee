using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ServiceMap.Platform.Windows.Interop;

/// <summary>
/// P/Invoke wrappers around iphlpapi.dll's GetExtendedTcpTable / GetExtendedUdpTable.
/// These are the only Win32 APIs that return the owning process id for a socket;
/// the managed <c>IPGlobalProperties</c> APIs do not expose the PID.
/// </summary>
internal static class IpHlpApi
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;

    private const int NO_ERROR = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    // TCP_TABLE_CLASS
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    // UDP_TABLE_CLASS
    private const int UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder,
        int ulAf, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable, ref int pdwSize, bool bOrder,
        int ulAf, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;   // only low 16 bits, network byte order
        public uint remoteAddr;
        public uint remotePort;  // only low 16 bits, network byte order
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] remoteAddr;
        public uint remoteScopeId;
        public uint remotePort;
        public uint state;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public uint localPort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] localAddr;
        public uint localScopeId;
        public uint localPort;
        public uint owningPid;
    }

    public readonly record struct TcpRow(
        IPAddress LocalAddress, int LocalPort,
        IPAddress RemoteAddress, int RemotePort,
        int State, int ProcessId);

    public readonly record struct UdpRow(
        IPAddress LocalAddress, int LocalPort, int ProcessId);

    public static List<TcpRow> GetTcpConnections()
    {
        var rows = new List<TcpRow>();
        rows.AddRange(GetTcpTable(AF_INET));
        rows.AddRange(GetTcpTable(AF_INET6));
        return rows;
    }

    public static List<UdpRow> GetUdpListeners()
    {
        var rows = new List<UdpRow>();
        rows.AddRange(GetUdpTable(AF_INET));
        rows.AddRange(GetUdpTable(AF_INET6));
        return rows;
    }

    private static IEnumerable<TcpRow> GetTcpTable(int af)
    {
        var buffer = QueryTable(
            (IntPtr ptr, ref int size) =>
                GetExtendedTcpTable(ptr, ref size, true, af, TCP_TABLE_OWNER_PID_ALL, 0));
        if (buffer == IntPtr.Zero) yield break;

        try
        {
            // Table layout: DWORD count, followed by count rows.
            int count = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);

            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                    yield return new TcpRow(
                        new IPAddress(row.localAddr), NetworkPort(row.localPort),
                        new IPAddress(row.remoteAddr), NetworkPort(row.remotePort),
                        (int)row.state, (int)row.owningPid);
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_TCP6ROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(rowPtr);
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                    yield return new TcpRow(
                        new IPAddress(row.localAddr, row.localScopeId), NetworkPort(row.localPort),
                        new IPAddress(row.remoteAddr, row.remoteScopeId), NetworkPort(row.remotePort),
                        (int)row.state, (int)row.owningPid);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IEnumerable<UdpRow> GetUdpTable(int af)
    {
        var buffer = QueryTable(
            (IntPtr ptr, ref int size) =>
                GetExtendedUdpTable(ptr, ref size, true, af, UDP_TABLE_OWNER_PID, 0));
        if (buffer == IntPtr.Zero) yield break;

        try
        {
            int count = Marshal.ReadInt32(buffer);
            IntPtr rowPtr = IntPtr.Add(buffer, 4);

            if (af == AF_INET)
            {
                int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                    yield return new UdpRow(
                        new IPAddress(row.localAddr), NetworkPort(row.localPort), (int)row.owningPid);
                }
            }
            else
            {
                int rowSize = Marshal.SizeOf<MIB_UDP6ROW_OWNER_PID>();
                for (int i = 0; i < count; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_UDP6ROW_OWNER_PID>(rowPtr);
                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                    yield return new UdpRow(
                        new IPAddress(row.localAddr, row.localScopeId), NetworkPort(row.localPort),
                        (int)row.owningPid);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private delegate uint TableQuery(IntPtr buffer, ref int size);

    /// <summary>
    /// Handles the standard two-call Win32 pattern: query required size, allocate,
    /// then fill. Retries on race where the table grows between calls.
    /// Returns an unmanaged buffer the caller must free, or Zero on failure.
    /// </summary>
    private static IntPtr QueryTable(TableQuery query)
    {
        int size = 0;
        uint result = query(IntPtr.Zero, ref size);
        if (result != ERROR_INSUFFICIENT_BUFFER && result != NO_ERROR)
            return IntPtr.Zero;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            result = query(buffer, ref size);
            if (result == NO_ERROR)
                return buffer;

            Marshal.FreeHGlobal(buffer);
            if (result != ERROR_INSUFFICIENT_BUFFER)
                return IntPtr.Zero;
            // else: size was updated, loop and retry with the larger buffer.
        }
        return IntPtr.Zero;
    }

    /// <summary>Convert a port stored in network byte order (low 16 bits) to host order.</summary>
    private static int NetworkPort(uint value)
    {
        // Bytes 0 and 1 hold the port in network (big-endian) order.
        return ((int)(value & 0xFF) << 8) | (int)((value >> 8) & 0xFF);
    }
}
