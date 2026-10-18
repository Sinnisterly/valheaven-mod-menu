#include "valheaven_host.h"

#include <stdio.h>
#include <string.h>

typedef void* MonoDomain;
typedef void* MonoAssembly;
typedef void* MonoImage;
typedef void* MonoClass;
typedef void* MonoMethod;
typedef void* MonoObject;
typedef void* MonoString;

typedef MonoDomain* (*mono_get_root_domain_fn)(void);
typedef MonoDomain* (*mono_thread_attach_fn)(MonoDomain* domain);
typedef MonoImage* (*mono_image_loaded_fn)(const char* name);
typedef MonoImage* (*mono_image_open_from_data_fn)(char* data, UINT32 data_len, int need_copy, int* status);
typedef MonoAssembly* (*mono_assembly_load_from_full_fn)(MonoImage* image, const char* fname, int* status, int refonly);
typedef MonoImage* (*mono_assembly_get_image_fn)(MonoAssembly* assembly);
typedef MonoClass* (*mono_class_from_name_fn)(MonoImage* image, const char* name_space, const char* name);
typedef MonoMethod* (*mono_class_get_method_from_name_fn)(MonoClass* klass, const char* name, int param_count);
typedef MonoObject* (*mono_runtime_invoke_fn)(MonoMethod* method, void* obj, void** params, MonoObject** exc);
typedef MonoString* (*mono_object_to_string_fn)(MonoObject* obj, MonoObject** exc);
typedef char* (*mono_string_to_utf8_fn)(MonoString* s);
typedef void (*mono_free_fn)(void* ptr);

static char g_log_path[MAX_PATH];
static volatile LONG g_worker_started;
static HANDLE g_worker_mutex;

void Valheaven_Log(const char* message)
{
    if (!message) return;
    FILE* f = NULL;
    if (g_log_path[0] && fopen_s(&f, g_log_path, "a") == 0 && f)
    {
        SYSTEMTIME st;
        GetLocalTime(&st);
        fprintf(f, "%04u-%02u-%02u %02u:%02u:%02u [NATIVE] %s\n",
            st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, message);
        fclose(f);
    }
}

static void ResolveLogPath(HMODULE self)
{
    char modulePath[MAX_PATH];
    if (!GetModuleFileNameA(self, modulePath, MAX_PATH)) return;
    char* slash = strrchr(modulePath, '\\');
    if (!slash) return;
    slash[1] = 0;
    snprintf(g_log_path, MAX_PATH, "%sValheaven.log", modulePath);
}

static BOOL LoadResourceBytes(HMODULE self, int id, void** outData, DWORD* outSize)
{
    HRSRC res = FindResourceW(self, MAKEINTRESOURCEW(id), MAKEINTRESOURCEW(10)); /* RT_RCDATA */
    if (!res) return FALSE;
    HGLOBAL loaded = LoadResource(self, res);
    if (!loaded) return FALSE;
    void* data = LockResource(loaded);
    DWORD size = SizeofResource(self, res);
    if (!data || size == 0) return FALSE;
    *outData = data;
    *outSize = size;
    return TRUE;
}

static MonoAssembly* LoadAssemblyFromResource(
    HMODULE self,
    int resourceId,
    const char* fileName,
    mono_image_open_from_data_fn mono_image_open_from_data,
    mono_assembly_load_from_full_fn mono_assembly_load_from_full)
{
    void* bytes = NULL;
    DWORD size = 0;
    if (!LoadResourceBytes(self, resourceId, &bytes, &size))
    {
        Valheaven_Log("missing embedded resource");
        return NULL;
    }
    int status = 0;
    MonoImage* image = mono_image_open_from_data((char*)bytes, size, 1, &status);
    if (!image || status != 0)
    {
        char buf[128];
        snprintf(buf, sizeof(buf), "mono_image_open_from_data failed status=%d size=%lu", status, (unsigned long)size);
        Valheaven_Log(buf);
        return NULL;
    }
    status = 0;
    MonoAssembly* assembly = mono_assembly_load_from_full(image, fileName, &status, 0);
    if (!assembly || status != 0)
    {
        char buf[128];
        snprintf(buf, sizeof(buf), "mono_assembly_load_from_full failed for %s status=%d", fileName, status);
        Valheaven_Log(buf);
        return NULL;
    }
    return assembly;
}

static void LogManagedException(
    MonoObject* exc,
    mono_object_to_string_fn mono_object_to_string,
    mono_string_to_utf8_fn mono_string_to_utf8,
    mono_free_fn mono_free)
{
    if (!exc || !mono_object_to_string || !mono_string_to_utf8)
    {
        Valheaven_Log("Entrypoint.Start raised a managed exception");
        return;
    }
    MonoObject* inner = NULL;
    MonoString* s = mono_object_to_string(exc, &inner);
    if (!s)
    {
        Valheaven_Log("Entrypoint.Start raised a managed exception (to_string failed)");
        return;
    }
    char* utf8 = mono_string_to_utf8(s);
    if (utf8)
    {
        char line[1024];
        snprintf(line, sizeof(line), "Entrypoint.Start exception: %s", utf8);
        Valheaven_Log(line);
        if (mono_free) mono_free(utf8);
        else free(utf8);
    }
}

/* mono_get_root_domain can be non-null before foreign thread attach is safe. */
static BOOL WaitForMonoRuntime(
    mono_get_root_domain_fn mono_get_root_domain,
    mono_image_loaded_fn mono_image_loaded,
    MonoDomain** outDomain)
{
    MonoDomain* domain = NULL;
    for (int i = 0; i < 600; i++)
    {
        domain = mono_get_root_domain();
        if (domain) break;
        Sleep(100);
    }
    if (!domain)
    {
        Valheaven_Log("mono root domain not ready");
        return FALSE;
    }

    /* Wait for UnityEngine.CoreModule - attaching earlier crashes. */
    if (mono_image_loaded)
    {
        for (int i = 0; i < 600; i++)
        {
            if (mono_image_loaded("UnityEngine.CoreModule"))
            {
                /* Unity 6: domain/image present is not always attach-safe yet. */
                Sleep(750);
                *outDomain = domain;
                return TRUE;
            }
            Sleep(100);
        }
        Valheaven_Log("UnityEngine.CoreModule image not observed; attempting attach anyway");
    }

    Sleep(1500);
    *outDomain = domain;
    return TRUE;
}

static DWORD WINAPI Worker(LPVOID param)
{
    HMODULE self = (HMODULE)param;
    ResolveLogPath(self);
    Valheaven_Log("version.dll worker started; waiting for mono-2.0-bdwgc.dll");

    HMODULE mono = NULL;
    for (int i = 0; i < 600; i++)
    {
        mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
        if (mono) break;
        Sleep(100);
    }
    if (!mono)
    {
        Valheaven_Log("mono-2.0-bdwgc.dll not found; aborting bootstrap");
        return 1;
    }
    Valheaven_Log("mono-2.0-bdwgc.dll found");

    mono_get_root_domain_fn mono_get_root_domain =
        (mono_get_root_domain_fn)GetProcAddress(mono, "mono_get_root_domain");
    mono_thread_attach_fn mono_thread_attach =
        (mono_thread_attach_fn)GetProcAddress(mono, "mono_thread_attach");
    mono_image_loaded_fn mono_image_loaded =
        (mono_image_loaded_fn)GetProcAddress(mono, "mono_image_loaded");
    mono_image_open_from_data_fn mono_image_open_from_data =
        (mono_image_open_from_data_fn)GetProcAddress(mono, "mono_image_open_from_data");
    mono_assembly_load_from_full_fn mono_assembly_load_from_full =
        (mono_assembly_load_from_full_fn)GetProcAddress(mono, "mono_assembly_load_from_full");
    mono_assembly_get_image_fn mono_assembly_get_image =
        (mono_assembly_get_image_fn)GetProcAddress(mono, "mono_assembly_get_image");
    mono_class_from_name_fn mono_class_from_name =
        (mono_class_from_name_fn)GetProcAddress(mono, "mono_class_from_name");
    mono_class_get_method_from_name_fn mono_class_get_method_from_name =
        (mono_class_get_method_from_name_fn)GetProcAddress(mono, "mono_class_get_method_from_name");
    mono_runtime_invoke_fn mono_runtime_invoke =
        (mono_runtime_invoke_fn)GetProcAddress(mono, "mono_runtime_invoke");
    mono_object_to_string_fn mono_object_to_string =
        (mono_object_to_string_fn)GetProcAddress(mono, "mono_object_to_string");
    mono_string_to_utf8_fn mono_string_to_utf8 =
        (mono_string_to_utf8_fn)GetProcAddress(mono, "mono_string_to_utf8");
    mono_free_fn mono_free =
        (mono_free_fn)GetProcAddress(mono, "mono_free");

    if (!mono_get_root_domain || !mono_thread_attach || !mono_image_open_from_data ||
        !mono_assembly_load_from_full || !mono_assembly_get_image || !mono_class_from_name ||
        !mono_class_get_method_from_name || !mono_runtime_invoke)
    {
        Valheaven_Log("required mono_* exports missing");
        return 2;
    }

    MonoDomain* domain = NULL;
    if (!WaitForMonoRuntime(mono_get_root_domain, mono_image_loaded, &domain) || !domain)
        return 3;

    __try
    {
        mono_thread_attach(domain);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        Valheaven_Log("mono_thread_attach raised a native exception; aborting bootstrap");
        return 9;
    }
    Valheaven_Log("attached to mono root domain");

    /* Let Unity finish engine assemblies before Harmony. */
    Sleep(500);

    /* RCDATA 102 = Harmony, 101 = menu (ILRepack cannot merge Unity assemblies). */
    if (mono_image_loaded("0Harmony"))
    {
        Valheaven_Log("0Harmony already loaded (likely by BepInEx); skipping embedded Harmony.");
    }
    else if (!LoadAssemblyFromResource(self, 102, "0Harmony", mono_image_open_from_data, mono_assembly_load_from_full))
    {
        Valheaven_Log("failed to load embedded 0Harmony");
        return 4;
    }
    else
    {
        Valheaven_Log("loaded embedded 0Harmony");
    }
    MonoAssembly* menu = LoadAssemblyFromResource(self, 101, "ValheavenModMenu", mono_image_open_from_data, mono_assembly_load_from_full);
    if (!menu)
    {
        Valheaven_Log("failed to load embedded ValheavenModMenu");
        return 5;
    }
    Valheaven_Log("loaded embedded ValheavenModMenu");

    MonoImage* asmImage = mono_assembly_get_image(menu);
    MonoClass* klass = mono_class_from_name(asmImage, "Valheaven", "Entrypoint");
    if (!klass)
    {
        Valheaven_Log("type Valheaven.Entrypoint not found");
        return 6;
    }
    MonoMethod* start = mono_class_get_method_from_name(klass, "Start", 0);
    if (!start)
    {
        Valheaven_Log("method Entrypoint.Start not found");
        return 7;
    }

    MonoObject* exc = NULL;
    __try
    {
        mono_runtime_invoke(start, NULL, NULL, &exc);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        Valheaven_Log("Entrypoint.Start raised a native exception");
        return 10;
    }
    if (exc)
    {
        LogManagedException(exc, mono_object_to_string, mono_string_to_utf8, mono_free);
        return 8;
    }

    Valheaven_Log("Entrypoint.Start scheduled main-thread bootstrap");
    return 0;
}

void Valheaven_StartWorker(void)
{
    if (InterlockedCompareExchange(&g_worker_started, 1, 0) != 0)
        return;

    HMODULE self = NULL;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCWSTR)Valheaven_StartWorker, &self))
        return;
    ResolveLogPath(self);

    /* Named mutex: duplicate mappings / reload can reset statics. */
    g_worker_mutex = CreateMutexW(NULL, FALSE, L"Local\\Valheaven.VersionDll.Worker");
    if (g_worker_mutex)
    {
        DWORD wait = WaitForSingleObject(g_worker_mutex, 0);
        if (wait == WAIT_TIMEOUT)
        {
            Valheaven_Log("worker mutex already held; skipping duplicate bootstrap");
            return;
        }
        if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED)
            return;
    }

    HANDLE thread = CreateThread(NULL, 0, Worker, self, 0, NULL);
    if (thread) CloseHandle(thread);
}
