#include <windows.h>
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
    if (swprintf(command, 32768, L"\"%ls\\Infinigame.exe\" -vr -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -force-d3d11 %ls", path, arguments) < 0) return 1;
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
