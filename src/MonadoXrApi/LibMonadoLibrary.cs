using System.Runtime.InteropServices;

namespace MonadoXrApi;

/// <summary>Loads a supported libmonado ABI and resolves its exported functions.</summary>
public sealed class LibMonadoLibrary : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ApiVersion(out uint major, out uint minor, out uint patch);

    private nint handle;

    public LibMonadoLibrary(string libraryPath)
    {
        handle = NativeLibrary.Load(libraryPath);
        try
        {
            GetDelegate<ApiVersion>("mnd_api_get_version")(out var major, out var minor, out _);
            if (major != 1 || minor < 4)
                throw new NotSupportedException($"libmonado API 1.4 以降の 1.x が必要です（検出: {major}.{minor}）。");
        }
        catch
        {
            NativeLibrary.Free(handle);
            handle = 0;
            throw;
        }
    }

    internal T GetDelegate<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(handle, name));

    public void Dispose()
    {
        if (handle != 0) { NativeLibrary.Free(handle); handle = 0; }
    }
}
