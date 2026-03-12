using System.Runtime.InteropServices;
using XUnityToolkit_WebUI.Models;

namespace XUnityToolkit_WebUI.Services;

/// <summary>
/// Detects GPU information using DXGI (DirectX Graphics Infrastructure).
/// Unlike WMI Win32_VideoController.AdapterRAM (uint32, caps at 4GB),
/// DXGI reports accurate VRAM via 64-bit DedicatedVideoMemory.
/// </summary>
public static class DxgiGpuDetector
{
    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    // DXGI_ADAPTER_FLAG_SOFTWARE = 2
    private const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdapters1Delegate(IntPtr thisPtr, uint index, out IntPtr adapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Delegate(IntPtr thisPtr, out DXGI_ADAPTER_DESC1 desc);

    public static List<GpuInfo> DetectGpus()
    {
        var results = new List<GpuInfo>();

        try
        {
            var iid = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref iid, out var factoryPtr);
            if (hr < 0) return results;

            try
            {
                var factoryVtable = Marshal.ReadIntPtr(factoryPtr);

                for (uint i = 0; ; i++)
                {
                    // IDXGIFactory1 vtable slot 12 = EnumAdapters1
                    var enumAdapters1 = Marshal.GetDelegateForFunctionPointer<EnumAdapters1Delegate>(
                        Marshal.ReadIntPtr(factoryVtable, 12 * IntPtr.Size));
                    hr = enumAdapters1(factoryPtr, i, out var adapterPtr);
                    if (hr < 0) break; // DXGI_ERROR_NOT_FOUND

                    try
                    {
                        var adapterVtable = Marshal.ReadIntPtr(adapterPtr);
                        // IDXGIAdapter1 vtable slot 10 = GetDesc1
                        var getDesc1 = Marshal.GetDelegateForFunctionPointer<GetDesc1Delegate>(
                            Marshal.ReadIntPtr(adapterVtable, 10 * IntPtr.Size));

                        hr = getDesc1(adapterPtr, out var desc);
                        if (hr < 0) continue;

                        // Skip software adapters (e.g. Microsoft Basic Render Driver)
                        if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue;

                        // Skip adapters with no dedicated VRAM (e.g. some virtual adapters)
                        if (desc.DedicatedVideoMemory == 0) continue;

                        var vendor = desc.VendorId switch
                        {
                            0x10DE => "NVIDIA",
                            0x1002 => "AMD",
                            0x8086 => "Intel",
                            _ => "Other"
                        };

                        var backend = desc.VendorId switch
                        {
                            0x10DE => GpuBackend.CUDA,
                            0x1002 or 0x8086 => GpuBackend.Vulkan,
                            _ => GpuBackend.CPU
                        };

                        results.Add(new GpuInfo(
                            desc.Description ?? "",
                            vendor,
                            (long)desc.DedicatedVideoMemory,
                            backend));
                    }
                    finally
                    {
                        Marshal.Release(adapterPtr);
                    }
                }
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }
        }
        catch
        {
            // DXGI unavailable (e.g. DllNotFoundException) — return empty, caller falls back to WMI
        }

        return results;
    }
}
