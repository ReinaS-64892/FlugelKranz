using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace FlugelKranz.OpenVR;

/// <summary>Owns the process-wide OpenVR connection and its pinned SDK loader.</summary>
internal sealed unsafe class OpenVrSession : IDisposable
{
    private static readonly object Gate = new();
    private static bool inUse;
    private nint library;
    private bool initialized, ownsConnection, quitRequested;
    private delegate* unmanaged[Cdecl]<void> shutdown;
    private delegate* unmanaged[Cdecl]<byte*, int*, nint> getInterface;
    public nint System { get; }
    public nint Input { get; }
    public nint Overlay { get; }
    public nint Paths { get; }
    public nint Applications { get; }
    private ulong overlayHandle;

    public OpenVrSession()
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("OpenVR バックエンドは Windows / Linux x64 に対応します。");
        lock (Gate)
        {
            if (inUse) throw new InvalidOperationException("OpenVR 接続は既に使用中です。");
            ownsConnection = inUse = true;
        }
        try
        {
            string rid = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
            string name = OperatingSystem.IsWindows() ? "openvr_api.dll" : "libopenvr_api.so";
            library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name));
            shutdown = (delegate* unmanaged[Cdecl]<void>)NativeLibrary.GetExport(library, "VR_ShutdownInternal");
            getInterface = (delegate* unmanaged[Cdecl]<byte*, int*, nint>)NativeLibrary.GetExport(library, "VR_GetGenericInterface");
            var init = (delegate* unmanaged[Cdecl]<int*, int, uint>)NativeLibrary.GetExport(library, "VR_InitInternal");
            int error = 0;
            init(&error, 2); // VRApplication_Overlay: never becomes a scene application.
            if (error != 0)
            {
                var description = (delegate* unmanaged[Cdecl]<int, nint>)NativeLibrary.GetExport(library, "VR_GetVRInitErrorAsEnglishDescription");
                throw new InvalidOperationException($"SteamVR 接続失敗 ({error}): {Marshal.PtrToStringUTF8(description(error))}");
            }
            initialized = true;
            System = Interface(OpenVrSlots.System.Version);
            Input = Interface(OpenVrSlots.Input.Version);
            Overlay = Interface(OpenVrSlots.Overlay.Version);
            Paths = Interface(OpenVrSlots.Paths.Version);
            Applications = Interface(OpenVrSlots.Applications.Version);
            RegisterApplication();
            var create = (delegate* unmanaged[Stdcall]<byte*, byte*, ulong*, int>)Slot(Overlay, OpenVrSlots.Overlay.CreateOverlay);
            ulong handle = 0;
            fixed (byte* key = "flugelkranz.overlay\0"u8)
            fixed (byte* title = "FlugelKranz\0"u8)
                Check(create(key, title, &handle), "overlay 登録");
            overlayHandle = handle;
            // No texture, graphics device or visible surface is needed for input/space control.
        }
        catch { Dispose(); throw; }
    }

    private void RegisterApplication()
    {
        const string appKey = "org.flugelkranz.overlay";
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlugelKranz", "steamvr");
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, "application.vrmanifest");
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
        bool dotnetHost = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var application = new Dictionary<string, object>
        {
            ["app_key"] = appKey, ["launch_type"] = "binary", ["is_dashboard_overlay"] = true,
            [OperatingSystem.IsWindows() ? "binary_path_windows" : "binary_path_linux"] = executable,
            ["arguments"] = (dotnetHost ? "\"" + global::System.Reflection.Assembly.GetEntryAssembly()!.Location + "\" " : "") + "--runtime steamvr",
            ["action_manifest_path"] = Path.Combine(AppContext.BaseDirectory, "steamvr", "actions.json"),
            ["strings"] = new { en_us = new { name = "FlugelKranz" } }
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new { source = "builtin", applications = new[] { application } }));
        byte[] path = Encoding.UTF8.GetBytes(manifestPath + '\0');
        fixed (byte* p = path)
            Check(((delegate* unmanaged[Stdcall]<byte*, byte, int>)Slot(Applications,
                OpenVrSlots.Applications.AddApplicationManifest))(p, 1), "アプリの一時登録");
        fixed (byte* key = "org.flugelkranz.overlay\0"u8)
            Check(((delegate* unmanaged[Stdcall]<uint, byte*, int>)Slot(Applications,
                OpenVrSlots.Applications.IdentifyApplication))((uint)Environment.ProcessId, key), "アプリの識別");
    }

    private nint Interface(string version)
    {
        byte[] name = Encoding.UTF8.GetBytes(version + '\0');
        int error = 0;
        fixed (byte* p = name)
        {
            nint result = getInterface(p, &error);
            if (error != 0 || result == 0)
                throw new NotSupportedException($"SteamVR が {version} を提供していません ({error})。");
            return result;
        }
    }

    internal static nint Slot(nint table, int index) => Marshal.ReadIntPtr(table, index * nint.Size);
    internal static void Check(int error, string operation)
    {
        if (error != 0) throw new InvalidOperationException($"SteamVR {operation}に失敗しました ({error})。");
    }

    public bool WaitFrameSync(uint timeoutMs) =>
        ((delegate* unmanaged[Stdcall]<uint, int>)Slot(Overlay, OpenVrSlots.Overlay.WaitFrameSync))(timeoutMs) == 0;

    public void PumpEvents()
    {
        // VREvent_t's union has 48 bytes. Legacy Linux event layout packs at 4.
        uint size = OperatingSystem.IsWindows() ? 64u : 60u;
        byte* ev = stackalloc byte[64];
        var poll = (delegate* unmanaged[Stdcall]<byte*, uint, byte>)Slot(System, OpenVrSlots.System.PollNextEvent);
        while (poll(ev, size) != 0)
        {
            if (*(uint*)ev == 700) // VREvent_Quit
            {
                quitRequested = true;
                throw new InvalidOperationException("SteamVR が終了を要求しました。");
            }
        }
    }

    public void Dispose()
    {
        if (!ownsConnection) return;
        try
        {
            if (initialized)
            {
                if (overlayHandle != 0)
                    ((delegate* unmanaged[Stdcall]<ulong, int>)Slot(Overlay, OpenVrSlots.Overlay.DestroyOverlay))(overlayHandle);
                if (quitRequested)
                    ((delegate* unmanaged[Stdcall]<void>)Slot(System, OpenVrSlots.System.AcknowledgeQuit_Exiting))();
                shutdown();
            }
            if (library != 0) NativeLibrary.Free(library);
        }
        finally
        {
            library = 0;
            initialized = false;
            ownsConnection = false;
            lock (Gate) inUse = false;
        }
    }
}
