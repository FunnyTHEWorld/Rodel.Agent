// Copyright (c) Richasy. All rights reserved.

using Microsoft.UI.Input;
using RodelAgent.UI.ViewModels.Items;
using Windows.System;
using Windows.UI.Core;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using WinRT.Interop;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace RodelAgent.UI.Controls.Chat;

/// <summary>
/// Chat session footer.
/// </summary>
public sealed partial class ChatSessionMainFooter : ChatSessionControlBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSessionMainFooter"/> class.
    /// </summary>
    public ChatSessionMainFooter()
    {
        InitializeComponent();
        ImageButton.Visibility = GlobalDependencies.IsChatImageEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc/>
    protected override void OnControlLoaded()
    {
        CheckEnterSendItem();
        ViewModel.RequestFocusInput += OnRequestFocusInput;
        ViewModel.RequestCloseFlyout += OnRequestCloseFlyout;
    }

    /// <inheritdoc/>
    protected override void OnControlUnloaded()
    {
        ModelRepeater.ItemsSource = null;
        ServerRepeater.ItemsSource = null;
        ViewModel.RequestFocusInput -= OnRequestFocusInput;
        ViewModel.RequestCloseFlyout -= OnRequestCloseFlyout;
    }

    private void OnRequestFocusInput(object? sender, EventArgs? e)
    {
        CloseFlyout();
        InputBox.Focus(FocusState.Programmatic);
    }

    private void OnRequestCloseFlyout(object? sender, EventArgs e)
        => CloseFlyout();

    private void CloseFlyout()
    {
        if (ModelFlyout.IsOpen)
        {
            ModelFlyout.Hide();
        }
    }

    private async void OnInputBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            var isShiftDown = shiftState == CoreVirtualKeyStates.Down || shiftState == (CoreVirtualKeyStates.Down | CoreVirtualKeyStates.Locked);
            var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            var isCtrlDown = ctrlState == CoreVirtualKeyStates.Down || ctrlState == (CoreVirtualKeyStates.Down | CoreVirtualKeyStates.Locked);

            if ((ViewModel.IsEnterSend && !isShiftDown)
                || (!ViewModel.IsEnterSend && isCtrlDown))
            {
                e.Handled = true;
                await ViewModel.StartGenerateCommand.ExecuteAsync(default);
                // ViewModel.CheckRegenerateButtonShownCommand.Execute(default);
            }
        }
    }

    private void OnCtrlEnterSendItemClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsEnterSend = false;
        CheckEnterSendItem();
    }

    private void OnEnterSendItemClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsEnterSend = true;
        CheckEnterSendItem();
    }

    private void CheckEnterSendItem()
    {
        if (EnterSendItem != null && ViewModel != null)
        {
            EnterSendItem.IsChecked = ViewModel.IsEnterSend;
            CtrlEnterSendItem.IsChecked = !ViewModel.IsEnterSend;
        }
    }

    private void OnCleanMessageButtonClick(object sender, RoutedEventArgs e)
        => CleanMessageTip.IsOpen = true;

    private void OnClearMessageActionButtonClick(TeachingTip sender, object args)
    {
        ViewModel.ClearMessageCommand.Execute(default);
        _ = this;
        sender.IsOpen = false;
    }

    private void OnCleanMessageTipClosed(TeachingTip sender, TeachingTipClosedEventArgs args)
        => InputBox.Focus(FocusState.Programmatic);

    private void OnSendButtonClick(SplitButton sender, SplitButtonClickEventArgs args)
        => ViewModel.StartGenerateCommand.Execute(default);

    private void OnMcpServerItemClick(object sender, RoutedEventArgs e)
    {
        var data = (sender as FrameworkElement)?.DataContext as McpServerItemViewModel;
        data!.IsSelected = !data.IsSelected;
    }

    private async void OnScreenshotButtonClick(object sender, RoutedEventArgs e)
    {
        var picker = new GraphicsCapturePicker();
        var hwnd = WindowNative.GetWindowHandle(App.Current.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        var item = await picker.PickSingleItemAsync();

        if (item != null)
        {
            var file = await CaptureItemAsync(item);
            if (file != null)
            {
                await ViewModel.AddImageToMessageAsync(file.Path);
            }
        }
    }

    private async Task<StorageFile> CaptureItemAsync(GraphicsCaptureItem item)
    {
        var d3dDevice = CreateD3DDevice();
        var d3dContext = d3dDevice.ImmediateContext;
        var device = Direct3D11Helpers.CreateSharpDXDevice(d3dDevice);
        var framePool = Direct3D11CaptureFramePool.Create(
            item,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);

        var session = framePool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = true;

        using (var frame = await framePool.TryGetNextFrameAsync())
        {
            var texture = Direct3D11Helpers.GetSharpDXTexture2D(frame.Surface);
            var desc = texture.Description;
            desc.CpuAccessFlags = SharpDX.Direct3D11.CpuAccessFlags.Read;
            desc.Usage = SharpDX.Direct3D11.ResourceUsage.Staging;
            desc.OptionFlags = SharpDX.Direct3D11.ResourceOptionFlags.None;
            desc.BindFlags = SharpDX.Direct3D11.BindFlags.None;

            using (var stagingTexture = new SharpDX.Direct3D11.Texture2D(device, desc))
            {
                d3dContext.CopyResource(texture, stagingTexture);
                var dataBox = d3dContext.MapSubresource(stagingTexture, 0, SharpDX.Direct3D11.MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                var file = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("screenshot.png", CreationCollisionOption.GenerateUniqueName);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var bitmapEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                    bitmapEncoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        (uint)desc.Width,
                        (uint)desc.Height,
                        96,
                        96,
                        dataBox.RowPitch,
                        dataBox.Scan0);
                    await bitmapEncoder.FlushAsync();
                }
                d3dContext.UnmapSubresource(stagingTexture, 0);
                return file;
            }
        }
    }

    private ID3D11Device CreateD3DDevice()
    {
        var d3d11Device = new SharpDX.Direct3D11.Device(SharpDX.Direct3D.DriverType.Hardware, SharpDX.Direct3D11.DeviceCreationFlags.BgraSupport);
        return Direct3D11Helpers.CreateDirect3DDeviceFromSharpDXDevice(d3d11Device);
    }
}

public static class Direct3D11Helpers
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern HRESULT CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    public static ID3D11Device CreateDirect3DDeviceFromSharpDXDevice(SharpDX.Direct3D11.Device sharpDXDevice)
    {
        var iid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        var d3dDevice = CreateDirect3D11DeviceFromDXGIDevice(sharpDXDevice.NativePointer, out var graphicsDevice);
        if (d3dDevice.Succeeded)
        {
            return Marshal.GetObjectForIUnknown(graphicsDevice) as ID3D11Device;
        }
        return null;
    }

    public static SharpDX.Direct3D11.Device CreateSharpDXDevice(ID3D11Device d3dDevice)
    {
        var iid = new Guid("DB6F6DDB-AC77-4E88-8253-819DF9BBF140");
        var unknown = Marshal.GetIUnknownForObject(d3dDevice);
        Marshal.QueryInterface(unknown, ref iid, out var nativeDevice);
        Marshal.Release(unknown);
        return new SharpDX.Direct3D11.Device(nativeDevice);
    }

    public static SharpDX.Direct3D11.Texture2D GetSharpDXTexture2D(IDirect3DSurface surface)
    {
        var iid = new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
        var unknown = Marshal.GetIUnknownForObject(surface);
        Marshal.QueryInterface(unknown, ref iid, out var nativeTexture);
        Marshal.Release(unknown);
        return new SharpDX.Direct3D11.Texture2D(nativeTexture);
    }
}

[ComImport]
[Guid("3E68D4BD-7135-4D10-8018-9FB6D9F33FA1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IInitializeWithWindow
{
    void Initialize(IntPtr hwnd);
}

[StructLayout(LayoutKind.Sequential)]
public struct HRESULT
{
    public int Value;
    public bool Succeeded => Value >= 0;
}
