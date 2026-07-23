#pragma once

#include <Windows.h>

HRESULT WINAPI OceanFreshNoRegistryCoCreateInstance(
    REFCLSID classId,
    LPUNKNOWN outer,
    DWORD context,
    REFIID interfaceId,
    LPVOID* result);

