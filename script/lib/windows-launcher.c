#include <windows.h>
#include <shellapi.h>
#include <wchar.h>

// Start the shared player with the headset requested.
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR arguments, int show)
{
    wchar_t path[32768], command[32768];
    DWORD length = GetModuleFileNameW(NULL, path, 32768);
    if (!length || length >= 32768) return 1;
    wchar_t *name = wcsrchr(path, L'\\');
    if (!name) return 1;
    *name = 0;
    // The mirror is a desktop window. SteamVR sizes the separate eye textures.
    SetProcessDPIAware();
    RECT work;
    wchar_t size[96] = L"";
    int count;
    wchar_t **args = CommandLineToArgvW(GetCommandLineW(), &count);
    BOOL customSize = FALSE;
    for (int i = 1; args && i + 1 < count; i++)
        if (!_wcsicmp(args[i], L"-screen-width") || !_wcsicmp(args[i], L"-screen-height")) customSize = TRUE;
    LocalFree(args);
    if (!customSize && SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0))
        swprintf(size, 96, L"-screen-width %ld -screen-height %ld", (work.right - work.left) * 3 / 4, (work.bottom - work.top) * 3 / 4);
    if (swprintf(command, 32768, L"\"%ls\\Infinigame.exe\" -infinigame-vr -force-d3d11 -force-d3d11-bitblt-model -screen-fullscreen 0 %ls %ls", path, size, arguments) < 0) return 1;
    STARTUPINFOW startup = { .cb = sizeof(STARTUPINFOW) };
    PROCESS_INFORMATION process;
    if (!CreateProcessW(NULL, command, NULL, NULL, FALSE, 0, NULL, path, &startup, &process)) {
        MessageBoxW(NULL, L"Extract the entire Infinigame folder before launching.", L"Infinigame", MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread);
    WaitForSingleObject(process.hProcess, INFINITE);
    DWORD result = 1;
    GetExitCodeProcess(process.hProcess, &result);
    CloseHandle(process.hProcess);
    return (int)result;
}
