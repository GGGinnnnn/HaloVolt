using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HaloVolt.Providers.Hid;

internal static class HidNative
{
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const uint FILE_FLAG_NO_BUFFERING = 0x20000000;

    public const int HIDP_STATUS_SUCCESS = 0x00110000;
    public const int HIDP_STATUS_BUFFER_TOO_SMALL = unchecked((int)0xC0110006);

    public enum HIDP_REPORT_TYPE
    {
        Input = 0,
        Output = 1,
        Feature = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAlias;

        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsStringRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsDesignatorRange;

        [MarshalAs(UnmanagedType.U1)]
        public bool IsAbsolute;

        [MarshalAs(UnmanagedType.U1)]
        public bool HasNull;

        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;

        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ushort[] Union;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetFeature(SafeFileHandle device, [Out] byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_SetFeature(SafeFileHandle device, byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool HidD_GetProductString(SafeFileHandle device, [Out] char[] buffer, int bufferLength);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(
        HIDP_REPORT_TYPE reportType,
        [Out] HIDP_VALUE_CAPS[] valueCaps,
        ref ushort valueCapsLength,
        IntPtr preparsedData);

    [DllImport("hid.dll")]
    public static extern int HidP_GetUsageValue(
        HIDP_REPORT_TYPE reportType,
        ushort usagePage,
        ushort linkCollection,
        ushort usage,
        out uint usageValue,
        IntPtr preparsedData,
        byte[] report,
        uint reportLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool ReadFile(
        SafeFileHandle file,
        [Out] byte[] buffer,
        int numberOfBytesToRead,
        out int numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        int numberOfBytesToWrite,
        out int numberOfBytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
