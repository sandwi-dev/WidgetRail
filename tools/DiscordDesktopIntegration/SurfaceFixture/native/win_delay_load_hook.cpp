// Independent implementation of the standard Electron/node-gyp Windows delay-
// load pattern: resolve node.exe imports against the current executable's exports.
// Reference: https://www.electronjs.org/docs/latest/tutorial/using-native-node-modules
// No upstream source is copied. This hook does not load another DLL or executable.
#include <windows.h>
#include <delayimp.h>
#include <cstring>

namespace {
FARPROC WINAPI ResolveNodeExecutable(unsigned notification, PDelayLoadInfo info) noexcept {
    if (notification != dliNotePreLoadLibrary || info == nullptr || info->szDll == nullptr ||
        _stricmp(info->szDll, "node.exe") != 0) return nullptr;
    return reinterpret_cast<FARPROC>(GetModuleHandleW(nullptr));
}
}

// Modern MSVC delayimp.h declares the hook const. Keep the hook read-only rather
// than defining DELAYIMP_INSECURE_WRITABLE_HOOKS for the old writable form.
extern "C" const PfnDliHook __pfnDliNotifyHook2 = ResolveNodeExecutable;
