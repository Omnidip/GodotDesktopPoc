
using Silk.NET.Direct3D11;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.DXGI;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Godot;

namespace Desktop
{

    public class DesktopDuplicator
    {
        private ComPtr<IDXGIOutputDuplication> dxgiDuplication;
        private ComPtr<ID3D11Texture2D> d3d11Texture;

        private ComPtr<ID3D11Device> dx11Device;
        private ComPtr<ID3D11DeviceContext> deviceContext;

        private uint captureWidth;
        private uint captureHeight;

        public Texture2D gdTexture;

        public DesktopDuplicator() 
        {
            SetupDevices();

            var gdImage = Godot.Image.Create((int)captureWidth, (int)captureHeight, false, Godot.Image.Format.Rgb8);
            gdImage.Fill(new Color(1, 0, 0, 1)); // fill it with red as a test to make sure things are working
            gdTexture = ImageTexture.CreateFromImage(gdImage);

            SetupCapture();

        }

        private unsafe void SetupDevices()
        {
            //Setup the DirectX 11 contexts

            var dxgicontext = new DefaultNativeContext("dxgi.dll");
            var d11context = new DefaultNativeContext("d3d11.dll");
            DXGI dxgi = new DXGI(dxgicontext);
            D3D11 d3d11 = new D3D11(d11context);

            ComPtr<IDXGIAdapter> adapter = default;

            SilkMarshal.ThrowHResult
            (
                d3d11.CreateDevice
                (
                    adapter,
                    D3DDriverType.Hardware,
                    Software: default,
                    (uint)CreateDeviceFlag.Debug, //TODO
                    null,
                    0,
                    D3D11.SdkVersion,
                    ref dx11Device,
                    null,
                    ref deviceContext
                )
            );

            ComPtr<IDXGIDevice> dxgiDevice = default;
            SilkMarshal.ThrowHResult(D3D11DeviceVtblExtensions.QueryInterface(dx11Device, out dxgiDevice));


            // All of this is just to get the screen size
            // There are easier ways to do it but this is fine

            SilkMarshal.ThrowHResult(dxgiDevice.GetAdapter(ref adapter));

            ComPtr<IDXGIOutput> output = default;
            SilkMarshal.ThrowHResult(adapter.EnumOutputs(0, ref output));

            ComPtr<IDXGIOutput1> output1 = default;
            SilkMarshal.ThrowHResult(DXGIOutputVtblExtensions.QueryInterface(output, out output1));
            SilkMarshal.ThrowHResult(output1.DuplicateOutput(dx11Device, ref dxgiDuplication));

            OutduplDesc desc = new();
            dxgiDuplication.GetDesc(&desc);

            captureWidth = desc.ModeDesc.Width;
            captureHeight = desc.ModeDesc.Height;
        }

        private unsafe void SetupCapture()
        {
            // Grab the Godot Vulkan context

            var gdTextureRID = gdTexture.GetRid();
            var vkImage = new Silk.NET.Vulkan.Image( RenderingServer.TextureGetNativeHandle(gdTextureRID) );

            var VK = Vk.GetApi();
            var gdDevice = RenderingServer.GetRenderingDevice();
            var vkDevice = new Device((nint)gdDevice.GetDriverResource(RenderingDevice.DriverResource.Device, new Rid(), 0));
            var vkPhysicalDevice = new PhysicalDevice((nint)gdDevice.GetDriverResource(RenderingDevice.DriverResource.PhysicalDevice, new Rid(), 0));
            var vkInstance = new Instance((nint)gdDevice.GetDriverResource(RenderingDevice.DriverResource.Instance, new Rid(), 0));


            // Create the DirectX 11 texture that we want to share memory with

            Texture2DDesc texDesc =
                new Texture2DDesc
                (
                    width: captureWidth,
                    height: captureHeight,
                    mipLevels: 1,
                    arraySize: 1,
                    format: Silk.NET.DXGI.Format.FormatB8G8R8A8Unorm,
                    sampleDesc: new SampleDesc(count: 1, quality: 0),
                    usage: Usage.Default,
                    miscFlags: (uint)(ResourceMiscFlag.SharedNthandle | ResourceMiscFlag.SharedKeyedmutex)
                );

            SilkMarshal.ThrowHResult(dx11Device.CreateTexture2D(&texDesc, (SubresourceData*)IntPtr.Zero, ref d3d11Texture));

            ComPtr<IDXGIResource1> d3d11TextureResource = default;
            SilkMarshal.ThrowHResult(D3D11Texture2DVtblExtensions.QueryInterface(d3d11Texture, out d3d11TextureResource));

            void* sharedHandle = default;
            SilkMarshal.ThrowHResult
            (
                d3d11TextureResource.CreateSharedHandle
                (
                    (SecurityAttributes*)IntPtr.Zero,
                    DXGI.SharedResourceRead,
                    (char*)IntPtr.Zero,
                    &sharedHandle
                )
            );


            // Load the Vulkan extension needed to share windows handles
            // (This is why the 1 line change to Godot is needed)

            KhrExternalMemoryWin32 vkExternalMemory;
            if (!VK.TryGetDeviceExtension(vkInstance, vkDevice, out vkExternalMemory))
            {
                throw new Exception($"Failed to load KhrExternalMemoryWin32 Vulkan extension!");
            }


            // Allocate memory using the shared handle
            // This is the bit I am the least confident about

            var w32MemProps = new MemoryWin32HandlePropertiesKHR();
            vkExternalMemory.GetMemoryWin32HandleProperties(
                vkDevice,
                ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                (nint) sharedHandle,
                out w32MemProps
            );

            MemoryRequirements memReq;
            VK.GetImageMemoryRequirements(vkDevice, vkImage, out memReq);

            PhysicalDeviceMemoryProperties memProps;
            VK.GetPhysicalDeviceMemoryProperties(vkPhysicalDevice, out memProps);

            // Look for Device local memory
            int memTypeIndex = -1;
            for (var im = 0; im < memProps.MemoryTypeCount; ++im)
            {
                var current_bit = 0x1 << im;
                if (w32MemProps.MemoryTypeBits == current_bit)
                {
                    if ((memProps.MemoryTypes[im].PropertyFlags & MemoryPropertyFlags.DeviceLocalBit) != 0) memTypeIndex = im;
                    break;
                }
            }

            if (memTypeIndex < 0) GD.Print("Device local memory not found!");

            var dii = new MemoryDedicatedAllocateInfoKHR { Image = vkImage };
            var imi = new ImportMemoryWin32HandleInfoKHR 
            { 
                PNext = &dii,  
                HandleType = ExternalMemoryHandleTypeFlags.D3D11TextureBit,
                Handle = (nint) sharedHandle
            };
            var mi = new MemoryAllocateInfo
            {
                PNext = &imi,
                AllocationSize = memReq.Size,
                MemoryTypeIndex = (uint) memTypeIndex
            };

            DeviceMemory deviceMemory = default;

            VK.AllocateMemory(vkDevice, &mi, null, &deviceMemory);
            VK.BindImageMemory(vkDevice, vkImage, deviceMemory, 0);
        }

        public void CaptureDesktop()
        {
            // Do the actual desktop capture, this could be more efficient but should work as it is

            OutduplFrameInfo frameInfo = default;
            ComPtr<IDXGIResource> resource = default;

            SilkMarshal.ThrowHResult( DXGIOutputDuplicationVtblExtensions.AcquireNextFrame(dxgiDuplication, 0, ref frameInfo, ref resource) );

            ComPtr<ID3D11Texture2D> textureResource;
            SilkMarshal.ThrowHResult( DXGIResourceVtblExtensions.QueryInterface(resource, out textureResource) );
            D3D11DeviceContextVtblExtensions.CopyResource(deviceContext, d3d11Texture, textureResource);

            D3D11Texture2DVtblExtensions.Release(textureResource);
            DXGIResourceVtblExtensions.Release(resource);
            DXGIOutputDuplicationVtblExtensions.ReleaseFrame(dxgiDuplication);
        }

    }
}