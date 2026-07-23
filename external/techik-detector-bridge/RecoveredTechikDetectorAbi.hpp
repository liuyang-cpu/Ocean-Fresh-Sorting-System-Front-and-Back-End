#pragma once

#include <array>
#include <cstddef>
#include <cstdint>

// Recovered statically from tk_driver_dt_demo.exe (2023-06-19, x64).
// This is an experimental ABI description, not a vendor SDK header.
// Do not issue hardware commands until the remaining fields are verified.
namespace oceanfresh::techik::recovered
{
#if defined(_MSC_VER)
#define OCEANFRESH_TECHIK_CALL __fastcall
#else
#define OCEANFRESH_TECHIK_CALL
#endif

    // Start() receives this structure by value. On the Windows x64 ABI the
    // 104-byte value is passed indirectly through the second argument register.
    struct DtInfo
    {
        std::array<std::byte, 0x68> raw{};
    };

    // The demo reads 16-bit pixels from Pixels and treats the buffer as
    // Height rows, each containing Channels blocks of Width pixels.
    struct DtDataFrame
    {
        std::uint64_t unknown00{};              // +0x00
        const std::uint16_t* pixels{};          // +0x08
        const std::byte* channelMetadata{};     // +0x10; 16-byte item per channel
        std::int32_t width{};                   // +0x18
        std::int32_t height{};                  // +0x1C
        std::int32_t channels{};                // +0x20
        std::int32_t unknown24{};               // +0x24
    };

    // The callback thunk in the demo receives Context in RCX and the 40-byte
    // frame value indirectly in RDX, then returns a bool in AL.
    using FrameCallback = bool(OCEANFRESH_TECHIK_CALL*)(void* context, DtDataFrame frame);

    struct DtDataCallback
    {
        FrameCallback function{};               // +0x00
        void* context{};                        // +0x08
    };

    // The demo reserves the range [main-window + 0x208, + 0x2B0) for the
    // TkDriverDt instance. The exact class definition remains private.
    struct TkDriverDtStorage
    {
        std::array<std::byte, 0xA8> raw{};
    };

    using Constructor = void*(OCEANFRESH_TECHIK_CALL*)(void* self);
    using Destructor = void(OCEANFRESH_TECHIK_CALL*)(void* self);
    using Start = bool(OCEANFRESH_TECHIK_CALL*)(void* self, DtInfo info);
    using SetCallback = void(OCEANFRESH_TECHIK_CALL*)(void* self, DtDataCallback callback);
    using GrabEnable = bool(OCEANFRESH_TECHIK_CALL*)(void* self, bool enabled);
    using Exit = void(OCEANFRESH_TECHIK_CALL*)(void* self);

    inline constexpr char ConstructorExport[] = "??0TkDriverDt@device@tk@@QEAA@XZ";
    inline constexpr char DestructorExport[] = "??1TkDriverDt@device@tk@@QEAA@XZ";
    inline constexpr char StartExport[] = "?Start@TkDriverDt@device@tk@@QEAA_NUst_dt_info@23@@Z";
    inline constexpr char SetCallbackExport[] = "?SetCallback@TkDriverDt@device@tk@@QEAAXUst_dt_data_callback@23@@Z";
    inline constexpr char GrabEnableExport[] = "?GrabEnable@TkDriverDt@device@tk@@QEAA_N_N@Z";
    inline constexpr char ExitExport[] = "?Exit@TkDriverDt@device@tk@@QEAAXXZ";

    static_assert(sizeof(DtInfo) == 0x68);
    static_assert(sizeof(DtDataCallback) == 0x10);
    static_assert(sizeof(DtDataFrame) == 0x28);
    static_assert(offsetof(DtDataFrame, pixels) == 0x08);
    static_assert(offsetof(DtDataFrame, channelMetadata) == 0x10);
    static_assert(offsetof(DtDataFrame, width) == 0x18);
    static_assert(offsetof(DtDataFrame, height) == 0x1C);
    static_assert(offsetof(DtDataFrame, channels) == 0x20);
    static_assert(sizeof(TkDriverDtStorage) == 0xA8);
}
