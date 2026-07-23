#include "DiaNoRegistry.hpp"

#include <dia2.h>
#include <diacreate.h>

#ifndef OCEANFRESH_MSDIA_PATH
#define OCEANFRESH_MSDIA_PATH \
    L"D:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\DIA SDK\\bin\\amd64\\msdia140.dll"
#endif

HRESULT WINAPI OceanFreshNoRegistryCoCreateInstance(
    REFCLSID classId,
    LPUNKNOWN outer,
    DWORD context,
    REFIID interfaceId,
    LPVOID* result)
{
    if (outer != nullptr || result == nullptr)
    {
        return E_INVALIDARG;
    }

    (void)context;
    return NoRegCoCreate(OCEANFRESH_MSDIA_PATH, classId, interfaceId, result);
}
