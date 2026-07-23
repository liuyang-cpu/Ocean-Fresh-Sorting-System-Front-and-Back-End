#include "TechikPeripheralRuntime.hpp"

#include <Windows.h>

#include <array>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <stdexcept>
#include <thread>
#include <utility>

namespace
{
    constexpr std::uintptr_t XrayFactoryPointerRva = 0x1EA90;
    constexpr std::uintptr_t XrayType6CreatorRva = 0x1900;
    constexpr std::uintptr_t MotionType4CreatorRva = 0x18A0;
    constexpr std::uintptr_t IoType3CreatorRva = 0x3170;
    constexpr std::uintptr_t IoType7CreatorRva = 0x3070;

    constexpr std::size_t ConnectSlot = 11;
    constexpr std::size_t DisconnectSlot = 12;
    constexpr std::size_t XrayEnableSlot = 13;
    constexpr std::size_t XraySetVoltageSlot = 14;
    constexpr std::size_t XraySetCurrentSlot = 15;
    constexpr std::size_t XrayGetStatusSlot = 16;
    constexpr std::size_t MotionSetRunSlot = 13;
    constexpr std::size_t MotionSetDirectionSlot = 14;
    constexpr std::size_t MotionSetSpeedSlot = 15;
    constexpr std::size_t MotionGetStatusSlot = 16;
    constexpr std::size_t IoSetOutCoilSlot = 13;
    constexpr std::size_t IoGetStatusSlot = 15;

    struct XrayInfo
    {
        std::int32_t id{};
        std::int32_t type{};
        std::int32_t comPort{};
        std::int32_t maxVoltageKv{};
        std::int32_t minVoltageKv{};
        std::int32_t maxCurrentUa{};
        std::int32_t minCurrentUa{};
        std::int32_t maxPowerLimit{};
        std::int32_t minPowerLimit{};
        std::int32_t openWaitStableTimeMilliseconds{};
        std::int32_t closeWaitStableTimeMilliseconds{};
    };

    struct XrayStatus
    {
        bool isOnline{};
        bool enableReference{};
        std::byte padding02[6]{};
        double voltageKvReference{};
        double currentUaReference{};
        double voltageKvMonitor{};
        double currentUaMonitor{};
        double temperatureMonitor{};
        double filamentMonitor{};
        bool enableMonitor{};
        std::byte padding39[3]{};
        std::int32_t faultCode{};
    };

    struct MotionInfo
    {
        std::int32_t id{};
        std::int32_t type{};
        std::int32_t comPort{};
        std::byte padding0C[4]{};
        double speedMax{};
        double speedMin{};
        double speedRatio{};
        bool directionReverse{};
        std::byte padding29[3]{};
        std::int32_t plcId{};
        std::int32_t plcRunPortForward{};
        std::int32_t plcRunPortReverse{};
        std::int32_t plcMonitorPortRun{};
        std::int32_t plcAnalogId{};
    };

    struct MotionStatus
    {
        bool isOnline{};
        bool referenceRun{};
        bool referenceDirection{};
        std::byte padding03[5]{};
        double referenceSpeed{};
        bool monitorRun{};
        bool monitorDirection{};
        std::byte padding12[6]{};
        double monitorSpeed{};
    };

    struct IoInfo
    {
        std::int32_t id{};
        std::int32_t type{};
        std::int32_t comPort{};
        std::int32_t detectorId{};
    };

    struct IoStatus
    {
        bool isOnline{};
        bool coilOutReference[0x100]{};
        std::byte padding101[3]{};
        std::int32_t analogOutReference[2]{};
        bool coilOut[0x100]{};
        bool coilIn[0x100]{};
        std::int32_t analogOut[2]{};
    };

    static_assert(sizeof(XrayInfo) == 0x2C);
    static_assert(sizeof(XrayStatus) == 0x40);
    static_assert(sizeof(MotionInfo) == 0x40);
    static_assert(sizeof(MotionStatus) == 0x20);
    static_assert(sizeof(IoInfo) == 0x10);
    static_assert(sizeof(IoStatus) == 0x314);

    class Module
    {
    public:
        explicit Module(const std::filesystem::path& path)
            : handle_(LoadLibraryW(path.c_str()))
        {
            if (handle_ == nullptr)
            {
                throw std::runtime_error(
                    "LoadLibrary failed for peripheral module: " +
                    std::to_string(GetLastError()));
            }
        }

        ~Module()
        {
            if (handle_ != nullptr)
            {
                FreeLibrary(handle_);
            }
        }

        HMODULE Handle() const
        {
            return handle_;
        }

        template <typename T>
        T Export(const char* name) const
        {
            const auto address = GetProcAddress(handle_, name);
            if (address == nullptr)
            {
                throw std::runtime_error(std::string("Missing export: ") + name);
            }
            return reinterpret_cast<T>(address);
        }

        template <typename T>
        T At(std::uintptr_t rva) const
        {
            return reinterpret_cast<T>(
                reinterpret_cast<std::uintptr_t>(handle_) + rva);
        }

    private:
        HMODULE handle_{};
    };

    template <typename T>
    T Virtual(void* instance, std::size_t slot)
    {
        if (instance == nullptr)
        {
            throw std::runtime_error("Techik peripheral instance is null.");
        }
        const auto table = *reinterpret_cast<void***>(instance);
        return reinterpret_cast<T>(table[slot]);
    }

    using InitFactory = void(__cdecl*)(void*);
    using Creator = void* (__cdecl*)(void*);
    using DeleteObject = void* (__cdecl*)(void*, unsigned int);
    using ConnectXray = bool(__cdecl*)(void*, XrayInfo&);
    using ConnectMotion = bool(__cdecl*)(void*, MotionInfo&);
    using ConnectIo = bool(__cdecl*)(void*, IoInfo&);
    using Disconnect = void(__cdecl*)(void*);
    using SetBoolean = bool(__cdecl*)(void*, bool);
    using SetBooleanVoid = void(__cdecl*)(void*, bool);
    using SetDouble = bool(__cdecl*)(void*, double);
    using SetDoubleVoid = void(__cdecl*)(void*, double);
    using GetXrayStatus = bool(__cdecl*)(void*, XrayStatus&);
    using GetMotionStatus = bool(__cdecl*)(void*, MotionStatus&);
    using GetIoStatus = bool(__cdecl*)(void*, IoStatus&);
    using SetOutCoil = void(__cdecl*)(void*, int, bool);

    class QtCoreApplication
    {
    public:
        explicit QtCoreApplication(const std::filesystem::path& runtimeRoot)
            : module_(runtimeRoot / L"Qt5Core.dll")
        {
            constructor_ = module_.Export<Constructor>(
                "??0QCoreApplication@@QEAA@AEAHPEAPEADH@Z");
            destructor_ = module_.Export<Destructor>(
                "??1QCoreApplication@@UEAA@XZ");
            processEvents_ = module_.Export<ProcessEventsFunction>(
                "?processEvents@QCoreApplication@@SAXV?$QFlags@W4ProcessEventsFlag@QEventLoop@@@@@Z");

            constructor_(storage_.data(), argumentCount_, arguments_.data(), 0);
            constructed_ = true;
        }

        ~QtCoreApplication()
        {
            if (constructed_)
            {
                destructor_(storage_.data());
            }
        }

        void ProcessEvents() const
        {
            processEvents_(0);
        }

    private:
        using Constructor = void* (__cdecl*)(void*, int&, char**, int);
        using Destructor = void(__cdecl*)(void*);
        using ProcessEventsFunction = void(__cdecl*)(int);

        Module module_;
        alignas(16) std::array<std::byte, 0x100> storage_{};
        int argumentCount_{1};
        std::array<char, 26> applicationName_{
            'O','c','e','a','n','F','r','e','s','h','T','e','c','h','i','k',
            'B','r','i','d','g','e','\0'};
        std::array<char*, 2> arguments_{applicationName_.data(), nullptr};
        Constructor constructor_{};
        Destructor destructor_{};
        ProcessEventsFunction processEvents_{};
        bool constructed_{};
    };

    class PeripheralObject
    {
    public:
        PeripheralObject() = default;
        explicit PeripheralObject(void* instance) : instance_(instance) {}

        ~PeripheralObject()
        {
            Reset();
        }

        PeripheralObject(const PeripheralObject&) = delete;
        PeripheralObject& operator=(const PeripheralObject&) = delete;

        PeripheralObject(PeripheralObject&& other) noexcept
            : instance_(std::exchange(other.instance_, nullptr)),
              connected_(std::exchange(other.connected_, false))
        {
        }

        PeripheralObject& operator=(PeripheralObject&& other) noexcept
        {
            if (this != &other)
            {
                Reset();
                instance_ = std::exchange(other.instance_, nullptr);
                connected_ = std::exchange(other.connected_, false);
            }
            return *this;
        }

        void* Get() const
        {
            return instance_;
        }

        void MarkConnected()
        {
            connected_ = true;
        }

        void DisconnectNow()
        {
            if (instance_ != nullptr && connected_)
            {
                Virtual<Disconnect>(instance_, DisconnectSlot)(instance_);
                connected_ = false;
            }
        }

    private:
        void Reset()
        {
            if (instance_ == nullptr)
            {
                return;
            }
            try
            {
                DisconnectNow();
            }
            catch (...)
            {
            }
            Virtual<DeleteObject>(instance_, 3)(instance_, 1);
            instance_ = nullptr;
        }

        void* instance_{};
        bool connected_{};
    };
}

class TechikPeripheralRuntime::Impl
{
public:
    explicit Impl(TechikPeripheralOptions options)
        : options_(std::move(options))
    {
    }

    void Inspect()
    {
        EnsureModules();
    }

    void Connect()
    {
        EnsureModules();
        if (connected_)
        {
            return;
        }

        primaryIo_ = PeripheralObject(ioModule_->At<Creator>(IoType3CreatorRva)(nullptr));
        IoInfo primaryIoInfo{
            options_.ioPrimaryId,
            options_.ioPrimaryType,
            options_.ioPrimaryComPort,
            options_.xrayId};
        if (!Virtual<ConnectIo>(primaryIo_.Get(), ConnectSlot)(primaryIo_.Get(), primaryIoInfo))
        {
            throw std::runtime_error("Techik primary IO Connect returned false.");
        }
        primaryIo_.MarkConnected();
        PumpFor(std::chrono::milliseconds(200));

        secondaryIo_ = PeripheralObject(ioModule_->At<Creator>(IoType7CreatorRva)(nullptr));
        IoInfo secondaryIoInfo{
            options_.ioSecondaryId,
            options_.ioSecondaryType,
            options_.ioSecondaryComPort,
            options_.xrayId};
        if (!Virtual<ConnectIo>(secondaryIo_.Get(), ConnectSlot)(secondaryIo_.Get(), secondaryIoInfo))
        {
            throw std::runtime_error("Techik secondary IO Connect returned false.");
        }
        secondaryIo_.MarkConnected();
        PumpFor(std::chrono::milliseconds(200));

        motion_ = PeripheralObject(
            motionModule_->At<Creator>(MotionType4CreatorRva)(nullptr));
        MotionInfo motionInfo{
            options_.motionId,
            options_.motionType,
            options_.motionComPort,
            {},
            options_.motionSpeedMax,
            options_.motionSpeedMin,
            options_.motionSpeedRatio,
            options_.motionDirectionReverse,
            {},
            options_.motionPlcId,
            options_.motionPlcRunPortForward,
            options_.motionPlcRunPortReverse,
            options_.motionPlcMonitorPortRun,
            options_.motionPlcAnalogId};
        if (!Virtual<ConnectMotion>(motion_.Get(), ConnectSlot)(motion_.Get(), motionInfo))
        {
            throw std::runtime_error("Techik motion Connect returned false.");
        }
        motion_.MarkConnected();
        PumpFor(std::chrono::milliseconds(200));

        xray_ = PeripheralObject(xrayModule_->At<Creator>(XrayType6CreatorRva)(nullptr));
        XrayInfo xrayInfo{
            options_.xrayId,
            options_.xrayType,
            options_.xrayComPort,
            options_.xrayMaxVoltageKv,
            options_.xrayMinVoltageKv,
            options_.xrayMaxCurrentUa,
            options_.xrayMinCurrentUa,
            options_.xrayMaxPowerLimit,
            options_.xrayMinPowerLimit,
            options_.xrayOpenWaitMilliseconds,
            options_.xrayCloseWaitMilliseconds};
        if (!Virtual<ConnectXray>(xray_.Get(), ConnectSlot)(xray_.Get(), xrayInfo))
        {
            throw std::runtime_error("Techik X-ray Connect returned false.");
        }
        xray_.MarkConnected();
        PumpFor(std::chrono::milliseconds(200));
        connected_ = true;
    }

    void ProcessEvents()
    {
        if (application_)
        {
            application_->ProcessEvents();
        }
    }

    TechikPeripheralStatus ReadStatus()
    {
        TechikPeripheralStatus result{};
        if (!connected_)
        {
            return result;
        }

        XrayStatus xrayStatus{};
        MotionStatus motionStatus{};
        IoStatus primaryStatus{};
        IoStatus secondaryStatus{};
        Virtual<GetXrayStatus>(xray_.Get(), XrayGetStatusSlot)(xray_.Get(), xrayStatus);
        Virtual<GetMotionStatus>(motion_.Get(), MotionGetStatusSlot)(motion_.Get(), motionStatus);
        Virtual<GetIoStatus>(primaryIo_.Get(), IoGetStatusSlot)(primaryIo_.Get(), primaryStatus);
        Virtual<GetIoStatus>(secondaryIo_.Get(), IoGetStatusSlot)(secondaryIo_.Get(), secondaryStatus);

        result.xrayOnline = xrayStatus.isOnline;
        result.xrayEnabled = xrayStatus.enableMonitor || xrayStatus.enableReference;
        result.xrayVoltageReference = xrayStatus.voltageKvReference;
        result.xrayCurrentReference = xrayStatus.currentUaReference;
        result.xrayVoltageMonitor = xrayStatus.voltageKvMonitor;
        result.xrayCurrentMonitor = xrayStatus.currentUaMonitor;
        result.xrayFaultCode = xrayStatus.faultCode;
        result.motionOnline = motionStatus.isOnline;
        result.motionRunning = motionStatus.monitorRun || motionStatus.referenceRun;
        result.motionDirection = motionStatus.monitorDirection;
        result.motionSpeed = motionStatus.monitorSpeed;
        result.primaryIoOnline = primaryStatus.isOnline;
        result.secondaryIoOnline = secondaryStatus.isOnline;
        return result;
    }

    void StartMachine(double voltageKv, double currentUa, double speed, bool direction)
    {
        EnsureConnected();
        if (voltageKv < options_.xrayMinVoltageKv ||
            voltageKv > options_.xrayMaxVoltageKv ||
            currentUa < options_.xrayMinCurrentUa ||
            currentUa > options_.xrayMaxCurrentUa ||
            speed < options_.motionSpeedMin ||
            speed > options_.motionSpeedMax)
        {
            throw std::runtime_error("Requested hardware setpoint is outside captured Techik limits.");
        }

        if (!Virtual<SetDouble>(xray_.Get(), XraySetVoltageSlot)(xray_.Get(), voltageKv))
        {
            throw std::runtime_error("Techik X-ray SetVoltage returned false.");
        }
        if (!Virtual<SetDouble>(xray_.Get(), XraySetCurrentSlot)(xray_.Get(), currentUa))
        {
            throw std::runtime_error("Techik X-ray SetCurrent returned false.");
        }
        Virtual<SetDoubleVoid>(motion_.Get(), MotionSetSpeedSlot)(motion_.Get(), speed);
        Virtual<SetBooleanVoid>(motion_.Get(), MotionSetDirectionSlot)(motion_.Get(), direction);
        Virtual<SetBooleanVoid>(motion_.Get(), MotionSetRunSlot)(motion_.Get(), true);
        if (!Virtual<SetBoolean>(xray_.Get(), XrayEnableSlot)(xray_.Get(), true))
        {
            Virtual<SetBooleanVoid>(motion_.Get(), MotionSetRunSlot)(motion_.Get(), false);
            throw std::runtime_error("Techik X-ray Enable(true) returned false.");
        }
    }

    void StopMachine()
    {
        if (!connected_)
        {
            return;
        }
        Virtual<SetBoolean>(xray_.Get(), XrayEnableSlot)(xray_.Get(), false);
        Virtual<SetBooleanVoid>(motion_.Get(), MotionSetRunSlot)(motion_.Get(), false);
    }

    void PulseReject(int port, bool activeHigh, long long pulseMicroseconds)
    {
        EnsureConnected();
        if (port < 0 || pulseMicroseconds <= 0 || pulseMicroseconds > 5'000'000)
        {
            throw std::runtime_error("Invalid Techik reject pulse request.");
        }
        const auto setCoil = Virtual<SetOutCoil>(primaryIo_.Get(), IoSetOutCoilSlot);
        setCoil(primaryIo_.Get(), port, activeHigh);
        const auto deadline = std::chrono::steady_clock::now() +
            std::chrono::microseconds(pulseMicroseconds);
        while (std::chrono::steady_clock::now() < deadline)
        {
            ProcessEvents();
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        setCoil(primaryIo_.Get(), port, !activeHigh);
    }

private:
    void EnsureModules()
    {
        if (application_)
        {
            return;
        }
        if (options_.runtimeRoot.empty())
        {
            throw std::runtime_error("--runtime-root is required for Techik peripherals.");
        }

        AddDllDirectory(options_.runtimeRoot.c_str());
        AddDllDirectory((options_.runtimeRoot / L"plugin").c_str());
        application_ = std::make_unique<QtCoreApplication>(options_.runtimeRoot);
        xrayModule_ = std::make_unique<Module>(
            options_.runtimeRoot / L"plugin" / L"tk_driver_xray.dll");
        ioModule_ = std::make_unique<Module>(
            options_.runtimeRoot / L"plugin" / L"tk_driver_io.dll");
        motionModule_ = std::make_unique<Module>(
            options_.runtimeRoot / L"plugin" / L"tk_driver_motion.dll");

        const auto initializeXray = xrayModule_->Export<InitFactory>("initFactory");
        const auto initializeIo = ioModule_->Export<InitFactory>("initFactory");
        const auto initializeMotion = motionModule_->Export<InitFactory>("initFactory");
        initializeXray(nullptr);
        const auto sharedFactory = *xrayModule_->At<void**>(XrayFactoryPointerRva);
        if (sharedFactory == nullptr)
        {
            throw std::runtime_error("Techik device factory initialization returned null.");
        }
        initializeIo(sharedFactory);
        initializeMotion(sharedFactory);
        ProcessEvents();
    }

    void EnsureConnected() const
    {
        if (!connected_)
        {
            throw std::runtime_error("Techik peripherals are not connected.");
        }
    }

    void PumpFor(std::chrono::milliseconds duration)
    {
        const auto deadline = std::chrono::steady_clock::now() + duration;
        while (std::chrono::steady_clock::now() < deadline)
        {
            ProcessEvents();
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }
    }

    TechikPeripheralOptions options_;
    std::unique_ptr<QtCoreApplication> application_;
    std::unique_ptr<Module> xrayModule_;
    std::unique_ptr<Module> ioModule_;
    std::unique_ptr<Module> motionModule_;
    PeripheralObject primaryIo_;
    PeripheralObject secondaryIo_;
    PeripheralObject motion_;
    PeripheralObject xray_;
    bool connected_{};
};

TechikPeripheralRuntime::TechikPeripheralRuntime(TechikPeripheralOptions options)
    : impl_(std::make_unique<Impl>(std::move(options)))
{
}

TechikPeripheralRuntime::~TechikPeripheralRuntime() = default;

void TechikPeripheralRuntime::Inspect()
{
    impl_->Inspect();
}

void TechikPeripheralRuntime::Connect()
{
    impl_->Connect();
}

void TechikPeripheralRuntime::ProcessEvents()
{
    impl_->ProcessEvents();
}

TechikPeripheralStatus TechikPeripheralRuntime::ReadStatus()
{
    return impl_->ReadStatus();
}

void TechikPeripheralRuntime::StartMachine(
    double voltageKv,
    double currentUa,
    double speed,
    bool direction)
{
    impl_->StartMachine(voltageKv, currentUa, speed, direction);
}

void TechikPeripheralRuntime::StopMachine()
{
    impl_->StopMachine();
}

void TechikPeripheralRuntime::PulseReject(
    int port,
    bool activeHigh,
    long long pulseMicroseconds)
{
    impl_->PulseReject(port, activeHigh, pulseMicroseconds);
}
