using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Wallflow;

/// <summary>
/// Vignettes via l'API shell (IShellItemImageFactory) : c'est l'Explorateur qui décode,
/// tous nos formats sont couverts, aucun code de génération ni de cache chez nous.
/// </summary>
public static class Thumbnail
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size size, int flags, out IntPtr hBitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size { public int Width, Height; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    public static BitmapSource? For(string path, int size = 160)
    {
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            if (factory.GetImage(new Size { Width = size, Height = size }, 0, out var hBitmap) != 0)
                return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero,
                    System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch (Exception)
        {
            return null; // pas de vignette = pas grave, l'entrée s'affiche avec son nom
        }
    }
}
