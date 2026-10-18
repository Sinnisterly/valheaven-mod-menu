#include "valheaven_host.h"

#include <stdio.h>

#pragma comment(lib, "kernel32.lib")

static HMODULE g_realVersion;

#define FORWARD_DECL(ret, name, args, callargs) \
    typedef ret (WINAPI *name##_fn) args; \
    static name##_fn p_##name; \
    ret WINAPI name args { \
        if (!p_##name) return (ret)0; \
        return p_##name callargs; \
    }

FORWARD_DECL(BOOL, GetFileVersionInfoA, (LPCSTR a, DWORD b, DWORD c, LPVOID d), (a,b,c,d))
FORWARD_DECL(BOOL, GetFileVersionInfoByHandle, (DWORD a, HANDLE b, LPVOID c, DWORD d), (a,b,c,d))
FORWARD_DECL(BOOL, GetFileVersionInfoExA, (DWORD a, LPCSTR b, DWORD c, DWORD d, LPVOID e), (a,b,c,d,e))
FORWARD_DECL(BOOL, GetFileVersionInfoExW, (DWORD a, LPCWSTR b, DWORD c, DWORD d, LPVOID e), (a,b,c,d,e))
FORWARD_DECL(DWORD, GetFileVersionInfoSizeA, (LPCSTR a, LPDWORD b), (a,b))
FORWARD_DECL(DWORD, GetFileVersionInfoSizeExA, (DWORD a, LPCSTR b, LPDWORD c), (a,b,c))
FORWARD_DECL(DWORD, GetFileVersionInfoSizeExW, (DWORD a, LPCWSTR b, LPDWORD c), (a,b,c))
FORWARD_DECL(DWORD, GetFileVersionInfoSizeW, (LPCWSTR a, LPDWORD b), (a,b))
FORWARD_DECL(BOOL, GetFileVersionInfoW, (LPCWSTR a, DWORD b, DWORD c, LPVOID d), (a,b,c,d))
FORWARD_DECL(DWORD, VerFindFileA, (DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPSTR e, PUINT f, LPSTR g, PUINT h), (a,b,c,d,e,f,g,h))
FORWARD_DECL(DWORD, VerFindFileW, (DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPWSTR e, PUINT f, LPWSTR g, PUINT h), (a,b,c,d,e,f,g,h))
FORWARD_DECL(DWORD, VerInstallFileA, (DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPCSTR e, LPCSTR f, LPSTR g, PUINT h), (a,b,c,d,e,f,g,h))
FORWARD_DECL(DWORD, VerInstallFileW, (DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPCWSTR e, LPCWSTR f, LPWSTR g, PUINT h), (a,b,c,d,e,f,g,h))
FORWARD_DECL(DWORD, VerLanguageNameA, (DWORD a, LPSTR b, DWORD c), (a,b,c))
FORWARD_DECL(DWORD, VerLanguageNameW, (DWORD a, LPWSTR b, DWORD c), (a,b,c))
FORWARD_DECL(BOOL, VerQueryValueA, (LPCVOID a, LPCSTR b, LPVOID* c, PUINT d), (a,b,c,d))
FORWARD_DECL(BOOL, VerQueryValueW, (LPCVOID a, LPCWSTR b, LPVOID* c, PUINT d), (a,b,c,d))

static void BindRealVersion(void)
{
    if (g_realVersion) return;
    wchar_t path[MAX_PATH];
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    if (n == 0 || n >= MAX_PATH) return;
    if (wcslen(path) + 12 >= MAX_PATH) return;
    wcscat_s(path, MAX_PATH, L"\\version.dll");
    g_realVersion = LoadLibraryW(path);
    if (!g_realVersion) return;

#define BIND(name) p_##name = (name##_fn)GetProcAddress(g_realVersion, #name)
    BIND(GetFileVersionInfoA);
    BIND(GetFileVersionInfoByHandle);
    BIND(GetFileVersionInfoExA);
    BIND(GetFileVersionInfoExW);
    BIND(GetFileVersionInfoSizeA);
    BIND(GetFileVersionInfoSizeExA);
    BIND(GetFileVersionInfoSizeExW);
    BIND(GetFileVersionInfoSizeW);
    BIND(GetFileVersionInfoW);
    BIND(VerFindFileA);
    BIND(VerFindFileW);
    BIND(VerInstallFileA);
    BIND(VerInstallFileW);
    BIND(VerLanguageNameA);
    BIND(VerLanguageNameW);
    BIND(VerQueryValueA);
    BIND(VerQueryValueW);
#undef BIND
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        BindRealVersion();
        Valheaven_StartWorker();
    }
    return TRUE;
}
