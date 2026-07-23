#include <Windows.h>

#include "TechikPeripheralRuntime.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <deque>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace
{
    constexpr std::array<std::byte, 4> RawMagic{
        std::byte{'O'}, std::byte{'F'}, std::byte{'R'}, std::byte{'1'}};

    struct DtInfo
    {
        std::int32_t id{};
        std::int32_t type{};
        std::int32_t networkId{};
        std::int32_t subCardPixels{};
        std::int32_t linePixels{};
        std::int32_t channels{};
        double pitchSize{};
        std::int32_t subFrameHeight{};
        std::int32_t boundXrayId{};
        bool scanDirection{};
        bool enableDarkDynamicTracking{};
        std::byte padding2A[2]{};
        std::int32_t darkDynamicPixelStart{};
        std::int32_t darkDynamicPixelLast{};
        std::int32_t ccdBinningMode{};
        std::int32_t ccdDualShiftPixels{};
        std::int32_t iasTdiLevel{};
        std::int32_t iasTdiLevelOffset{};
        std::int32_t iasKvThresholdLow{};
        std::int32_t iasKvThresholdHigh{};
        std::int32_t iasSoftBinningMode{};
        std::int32_t calibrationType{};
        std::int32_t calibrationDarkLines{};
        std::int32_t calibrationFullLines{};
        std::int32_t calibrationDarkTarget{};
        std::int32_t calibrationFullTarget{};
    };

    struct DtDataFrame
    {
        std::int32_t detectorId{};
        std::byte padding04[4]{};
        const std::uint16_t* pixels{};
        const std::byte* lineLabels{};
        std::int32_t width{};
        std::int32_t height{};
        std::int32_t channels{};
        std::int32_t reserved{};
    };

    using FrameCallback = bool(__cdecl*)(void*, DtDataFrame);

    struct DtDataCallback
    {
        FrameCallback function{};
        void* context{};
    };

    static_assert(sizeof(DtInfo) == 0x68);
    static_assert(sizeof(DtDataFrame) == 0x28);
    static_assert(sizeof(DtDataCallback) == 0x10);

    using Constructor = void* (__cdecl*)(void*);
    using Destructor = void(__cdecl*)(void*);
    using Start = bool(__cdecl*)(void*, DtInfo);
    using SetCallback = void(__cdecl*)(void*, DtDataCallback);
    using GrabEnable = bool(__cdecl*)(void*, bool);
    using Exit = void(__cdecl*)(void*);

    struct Options
    {
        std::filesystem::path sdkRoot;
        std::filesystem::path outputDirectory;
        std::filesystem::path runtimeRoot;
        std::string framePipe;
        bool inspectOnly{true};
        bool frameStreamSelfTest{};
        bool runPeripherals{};
        int aggregateHeight{300};
        int frameQueueCapacity{16};
        DtInfo detector{
            0, 0, 0, 128, 1536, 1, 0.4, 50, 0, true, false, {}, 0, 0,
            0, 0, 56, 4, 5, 25, 0, 0, 250, 1000, 0, 52428};
        TechikPeripheralOptions peripherals;
    };

    class CommandQueue
    {
    public:
        void Start()
        {
            reader_ = std::thread([this]
            {
                std::string command;
                while (std::getline(std::cin, command))
                {
                    std::lock_guard lock(gate_);
                    commands_.push_back(std::move(command));
                }
                closed_.store(true);
            });
        }

        ~CommandQueue()
        {
            if (reader_.joinable())
            {
                CancelSynchronousIo(
                    reinterpret_cast<HANDLE>(reader_.native_handle()));
                reader_.join();
            }
        }

        bool TryPop(std::string& command)
        {
            std::lock_guard lock(gate_);
            if (commands_.empty())
            {
                return false;
            }
            command = std::move(commands_.front());
            commands_.pop_front();
            return true;
        }

        bool IsClosed() const
        {
            return closed_.load();
        }

    private:
        std::mutex gate_;
        std::deque<std::string> commands_;
        std::atomic_bool closed_{};
        std::thread reader_;
    };

    class Module
    {
    public:
        explicit Module(const std::filesystem::path& path)
            : handle_(LoadLibraryW(path.c_str()))
        {
            if (handle_ == nullptr)
            {
                throw std::runtime_error("LoadLibrary failed: " + std::to_string(GetLastError()));
            }
        }

        ~Module()
        {
            if (handle_ != nullptr)
            {
                FreeLibrary(handle_);
            }
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

    private:
        HMODULE handle_{};
    };

    class FrameWriter
    {
    public:
        FrameWriter(
            std::filesystem::path outputDirectory,
            std::string framePipe,
            int aggregateHeight,
            std::size_t frameQueueCapacity)
            : outputDirectory_(std::move(outputDirectory)),
              framePipe_(std::move(framePipe)),
              aggregateHeight_(aggregateHeight),
              maxQueuedFrames_(frameQueueCapacity)
        {
            if (!framePipe_.empty())
            {
                const auto pipePath = std::string{"\\\\.\\pipe\\"} + framePipe_;
                if (!WaitNamedPipeA(pipePath.c_str(), 10'000))
                {
                    throw std::runtime_error(
                        "Timed out waiting for detector frame pipe: " +
                        std::to_string(GetLastError()));
                }

                pipeHandle_ = CreateFileA(
                    pipePath.c_str(),
                    GENERIC_WRITE,
                    0,
                    nullptr,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    nullptr);
                if (pipeHandle_ == INVALID_HANDLE_VALUE)
                {
                    throw std::runtime_error(
                        "Failed to connect detector frame pipe: " +
                        std::to_string(GetLastError()));
                }

                pipeWriter_ = std::thread([this] { WritePipeFrames(); });
            }
            else
            {
                std::filesystem::create_directories(outputDirectory_);
            }
        }

        ~FrameWriter()
        {
            {
                std::lock_guard lock(pipeGate_);
                pipeStopping_ = true;
            }
            pipeReady_.notify_all();
            if (pipeWriter_.joinable())
            {
                pipeWriter_.join();
            }
            if (pipeHandle_ != INVALID_HANDLE_VALUE)
            {
                CloseHandle(pipeHandle_);
            }
        }

        bool OnFrame(const DtDataFrame& frame)
        {
            if (frame.pixels == nullptr || frame.width <= 0 || frame.height <= 0 ||
                frame.channels <= 0 || frame.width > 16384 || frame.height > 4096 ||
                frame.channels > 8)
            {
                return false;
            }

            const auto rowWidth = static_cast<std::size_t>(frame.width) *
                                  static_cast<std::size_t>(frame.channels);
            const auto sampleCount = rowWidth * static_cast<std::size_t>(frame.height);

            std::lock_guard lock(gate_);
            if (rowWidth_ != rowWidth)
            {
                pixels_.clear();
                rowWidth_ = rowWidth;
            }

            pixels_.insert(pixels_.end(), frame.pixels, frame.pixels + sampleCount);
            const auto requiredSamples = rowWidth_ *
                static_cast<std::size_t>(std::max(1, aggregateHeight_));
            if (pixels_.size() < requiredSamples)
            {
                return true;
            }

            auto serialized = SerializeFrame(
                frame.detectorId,
                static_cast<std::int32_t>(rowWidth_),
                aggregateHeight_,
                pixels_.data(),
                requiredSamples);
            pixels_.erase(pixels_.begin(), pixels_.begin() + requiredSamples);
            if (pipeHandle_ != INVALID_HANDLE_VALUE)
            {
                EnqueuePipeFrame(std::move(serialized));
            }
            else
            {
                WriteFrameFile(serialized);
            }
            return true;
        }

    private:
        struct RawHeader
        {
            std::array<std::byte, 4> magic{};
            std::uint32_t headerSize{};
            std::int32_t detectorId{};
            std::int32_t width{};
            std::int32_t height{};
            std::int32_t channels{};
            std::int64_t sequence{};
            std::int64_t capturedUnixMicroseconds{};
        };

        std::vector<std::byte> SerializeFrame(
            std::int32_t detectorId,
            std::int32_t width,
            std::int32_t height,
            const std::uint16_t* pixels,
            std::size_t sampleCount)
        {
            const auto sequence = ++sequence_;
            const auto now = std::chrono::system_clock::now();
            const auto captured = std::chrono::duration_cast<std::chrono::microseconds>(
                now.time_since_epoch()).count();
            RawHeader header{
                RawMagic,
                sizeof(RawHeader),
                detectorId,
                width,
                height,
                1,
                sequence,
                captured};

            std::vector<std::byte> serialized(
                sizeof(header) + sampleCount * sizeof(std::uint16_t));
            std::memcpy(serialized.data(), &header, sizeof(header));
            std::memcpy(
                serialized.data() + sizeof(header),
                pixels,
                sampleCount * sizeof(std::uint16_t));
            return serialized;
        }

        void WriteFrameFile(const std::vector<std::byte>& serialized)
        {
            RawHeader header{};
            std::memcpy(&header, serialized.data(), sizeof(header));
            const auto stem = std::to_string(header.capturedUnixMicroseconds) +
                              "-" + std::to_string(header.sequence);
            const auto temporary = outputDirectory_ / (stem + ".tmp");
            const auto final = outputDirectory_ / (stem + ".ofxraw");

            {
                std::ofstream stream(temporary, std::ios::binary | std::ios::trunc);
                stream.write(
                    reinterpret_cast<const char*>(serialized.data()),
                    static_cast<std::streamsize>(serialized.size()));
                stream.flush();
                if (!stream)
                {
                    throw std::runtime_error("Failed to write detector frame.");
                }
            }

            std::filesystem::rename(temporary, final);
            std::cout << "{\"event\":\"frame\",\"file\":\""
                      << final.filename().string()
                      << "\",\"width\":" << header.width
                      << ",\"height\":" << header.height
                      << ",\"sequence\":" << header.sequence << "}" << std::endl;
        }

        void EnqueuePipeFrame(std::vector<std::byte> serialized)
        {
            std::lock_guard lock(pipeGate_);
            if (pipeFailed_)
            {
                return;
            }
            if (pipeFrames_.size() >= maxQueuedFrames_)
            {
                pipeFrames_.pop_front();
                ++droppedFrames_;
            }
            pipeFrames_.push_back(std::move(serialized));
            pipeReady_.notify_one();
        }

        void WritePipeFrames()
        {
            while (true)
            {
                std::vector<std::byte> serialized;
                {
                    std::unique_lock lock(pipeGate_);
                    pipeReady_.wait(lock, [this]
                    {
                        return pipeStopping_ || !pipeFrames_.empty();
                    });
                    if (pipeStopping_ && pipeFrames_.empty())
                    {
                        return;
                    }
                    serialized = std::move(pipeFrames_.front());
                    pipeFrames_.pop_front();
                }

                std::size_t writtenTotal = 0;
                while (writtenTotal < serialized.size())
                {
                    DWORD written{};
                    const auto remaining = serialized.size() - writtenTotal;
                    const auto chunk = static_cast<DWORD>(
                        std::min<std::size_t>(remaining, 1024 * 1024));
                    if (!WriteFile(
                            pipeHandle_,
                            serialized.data() + writtenTotal,
                            chunk,
                            &written,
                            nullptr) ||
                        written == 0)
                    {
                        std::lock_guard lock(pipeGate_);
                        pipeFailed_ = true;
                        pipeFrames_.clear();
                        std::cerr << "{\"event\":\"frame_pipe_error\",\"code\":"
                                  << GetLastError() << "}" << std::endl;
                        return;
                    }
                    writtenTotal += written;
                }

                RawHeader header{};
                std::memcpy(&header, serialized.data(), sizeof(header));
                std::cout << "{\"event\":\"frame_streamed\",\"width\":"
                          << header.width << ",\"height\":" << header.height
                          << ",\"sequence\":" << header.sequence
                          << ",\"dropped\":" << droppedFrames_ << "}" << std::endl;
            }
        }

        std::filesystem::path outputDirectory_;
        std::string framePipe_;
        int aggregateHeight_{};
        std::size_t maxQueuedFrames_{16};
        std::mutex gate_;
        std::vector<std::uint16_t> pixels_;
        std::size_t rowWidth_{};
        std::int64_t sequence_{};
        HANDLE pipeHandle_{INVALID_HANDLE_VALUE};
        std::mutex pipeGate_;
        std::condition_variable pipeReady_;
        std::deque<std::vector<std::byte>> pipeFrames_;
        std::thread pipeWriter_;
        bool pipeStopping_{};
        bool pipeFailed_{};
        std::uint64_t droppedFrames_{};
    };

    bool __cdecl HandleFrame(void* context, DtDataFrame frame)
    {
        try
        {
            return static_cast<FrameWriter*>(context)->OnFrame(frame);
        }
        catch (const std::exception& exception)
        {
            std::cerr << "{\"event\":\"frame_error\",\"message\":\""
                      << exception.what() << "\"}" << std::endl;
            return false;
        }
    }

    std::string ReadArgument(int argc, char** argv, std::string_view name, std::string fallback = {})
    {
        for (int index = 1; index + 1 < argc; ++index)
        {
            if (argv[index] == name)
            {
                return argv[index + 1];
            }
        }
        return fallback;
    }

    int ReadInt(int argc, char** argv, std::string_view name, int fallback)
    {
        const auto value = ReadArgument(argc, argv, name);
        return value.empty() ? fallback : std::stoi(value);
    }

    double ReadDouble(int argc, char** argv, std::string_view name, double fallback)
    {
        const auto value = ReadArgument(argc, argv, name);
        return value.empty() ? fallback : std::stod(value);
    }

    bool HasFlag(int argc, char** argv, std::string_view name)
    {
        for (int index = 1; index < argc; ++index)
        {
            if (argv[index] == name)
            {
                return true;
            }
        }
        return false;
    }

    Options ParseOptions(int argc, char** argv)
    {
        Options options;
        options.sdkRoot = std::filesystem::path(ReadArgument(argc, argv, "--sdk-root"));
        options.outputDirectory = std::filesystem::path(
            ReadArgument(argc, argv, "--output-directory"));
        options.runtimeRoot = std::filesystem::path(
            ReadArgument(argc, argv, "--runtime-root"));
        options.framePipe = ReadArgument(argc, argv, "--frame-pipe");
        options.inspectOnly = !HasFlag(argc, argv, "--run-detector");
        options.frameStreamSelfTest = HasFlag(argc, argv, "--frame-stream-self-test");
        options.runPeripherals = HasFlag(argc, argv, "--run-peripherals");
        options.aggregateHeight = ReadInt(argc, argv, "--aggregate-height", 300);
        options.frameQueueCapacity = std::clamp(
            ReadInt(argc, argv, "--frame-queue-capacity", 16),
            4,
            128);
        options.detector.id = ReadInt(argc, argv, "--id", 0);
        options.detector.type = ReadInt(argc, argv, "--type", 0);
        options.detector.networkId = ReadInt(argc, argv, "--network-id", 0);
        options.detector.subCardPixels = ReadInt(argc, argv, "--sub-card-pixels", 128);
        options.detector.linePixels = ReadInt(argc, argv, "--line-pixels", 1536);
        options.detector.channels = ReadInt(argc, argv, "--channels", 1);
        options.detector.pitchSize = ReadDouble(argc, argv, "--pitch-size", 0.4);
        options.detector.subFrameHeight = ReadInt(argc, argv, "--sub-frame-height", 50);
        options.detector.boundXrayId = ReadInt(argc, argv, "--bound-xray-id", 0);
        options.detector.scanDirection = ReadInt(argc, argv, "--scan-direction", 1) != 0;
        options.detector.enableDarkDynamicTracking =
            ReadInt(argc, argv, "--dark-tracking", 0) != 0;
        options.detector.darkDynamicPixelStart =
            ReadInt(argc, argv, "--dark-pixel-start", 0);
        options.detector.darkDynamicPixelLast =
            ReadInt(argc, argv, "--dark-pixel-last", 0);
        options.detector.ccdBinningMode = ReadInt(argc, argv, "--ccd-binning-mode", 0);
        options.detector.ccdDualShiftPixels =
            ReadInt(argc, argv, "--ccd-dual-shift-pixels", 0);
        options.detector.iasTdiLevel = ReadInt(argc, argv, "--ias-tdi-level", 56);
        options.detector.iasTdiLevelOffset =
            ReadInt(argc, argv, "--ias-tdi-level-offset", 4);
        options.detector.iasKvThresholdLow =
            ReadInt(argc, argv, "--ias-kv-threshold-low", 5);
        options.detector.iasKvThresholdHigh =
            ReadInt(argc, argv, "--ias-kv-threshold-high", 25);
        options.detector.iasSoftBinningMode =
            ReadInt(argc, argv, "--ias-soft-binning-mode", 0);
        options.detector.calibrationType =
            ReadInt(argc, argv, "--calibration-type", 0);
        options.detector.calibrationDarkLines =
            ReadInt(argc, argv, "--calibration-dark-lines", 250);
        options.detector.calibrationFullLines =
            ReadInt(argc, argv, "--calibration-full-lines", 1000);
        options.detector.calibrationDarkTarget =
            ReadInt(argc, argv, "--calibration-dark-target", 0);
        options.detector.calibrationFullTarget =
            ReadInt(argc, argv, "--calibration-full-target", 52428);

        options.peripherals.runtimeRoot = options.runtimeRoot;
        options.peripherals.xrayId = ReadInt(argc, argv, "--xray-id", 0);
        options.peripherals.xrayType = ReadInt(argc, argv, "--xray-type", 6);
        options.peripherals.xrayComPort = ReadInt(argc, argv, "--xray-com-port", 5);
        options.peripherals.xrayMaxVoltageKv =
            ReadInt(argc, argv, "--xray-max-voltage-kv", 60);
        options.peripherals.xrayMinVoltageKv =
            ReadInt(argc, argv, "--xray-min-voltage-kv", 30);
        options.peripherals.xrayMaxCurrentUa =
            ReadInt(argc, argv, "--xray-max-current-ua", 8000);
        options.peripherals.xrayMinCurrentUa =
            ReadInt(argc, argv, "--xray-min-current-ua", 500);
        options.peripherals.xrayMaxPowerLimit =
            ReadInt(argc, argv, "--xray-max-power-limit", 350000);
        options.peripherals.xrayMinPowerLimit =
            ReadInt(argc, argv, "--xray-min-power-limit", 6000);
        options.peripherals.xrayOpenWaitMilliseconds =
            ReadInt(argc, argv, "--xray-open-wait-ms", 8000);
        options.peripherals.xrayCloseWaitMilliseconds =
            ReadInt(argc, argv, "--xray-close-wait-ms", 5000);

        options.peripherals.motionId = ReadInt(argc, argv, "--motion-id", 0);
        options.peripherals.motionType = ReadInt(argc, argv, "--motion-type", 4);
        options.peripherals.motionComPort =
            ReadInt(argc, argv, "--motion-com-port", 1);
        options.peripherals.motionSpeedMax =
            ReadDouble(argc, argv, "--motion-speed-max", 120);
        options.peripherals.motionSpeedMin =
            ReadDouble(argc, argv, "--motion-speed-min", 5);
        options.peripherals.motionSpeedRatio =
            ReadDouble(argc, argv, "--motion-speed-ratio", 23);
        options.peripherals.motionDirectionReverse =
            ReadInt(argc, argv, "--motion-direction-reverse", 0) != 0;
        options.peripherals.motionPlcId =
            ReadInt(argc, argv, "--motion-plc-id", 0);
        options.peripherals.motionPlcRunPortForward =
            ReadInt(argc, argv, "--motion-plc-run-port-forward", 9);
        options.peripherals.motionPlcRunPortReverse =
            ReadInt(argc, argv, "--motion-plc-run-port-reverse", 8);
        options.peripherals.motionPlcMonitorPortRun =
            ReadInt(argc, argv, "--motion-plc-monitor-port-run", 7);
        options.peripherals.motionPlcAnalogId =
            ReadInt(argc, argv, "--motion-plc-analog-id", 0);

        options.peripherals.ioPrimaryId =
            ReadInt(argc, argv, "--io-primary-id", 0);
        options.peripherals.ioPrimaryType =
            ReadInt(argc, argv, "--io-primary-type", 3);
        options.peripherals.ioPrimaryComPort =
            ReadInt(argc, argv, "--io-primary-com-port", 3);
        options.peripherals.ioSecondaryId =
            ReadInt(argc, argv, "--io-secondary-id", 1);
        options.peripherals.ioSecondaryType =
            ReadInt(argc, argv, "--io-secondary-type", 7);
        options.peripherals.ioSecondaryComPort =
            ReadInt(argc, argv, "--io-secondary-com-port", 1);
        return options;
    }

    void WritePeripheralStatus(const TechikPeripheralStatus& status)
    {
        std::cout << "{\"event\":\"peripheral_status\","
                  << "\"xray_online\":" << (status.xrayOnline ? "true" : "false")
                  << ",\"xray_enabled\":" << (status.xrayEnabled ? "true" : "false")
                  << ",\"xray_voltage_ref\":" << status.xrayVoltageReference
                  << ",\"xray_current_ref\":" << status.xrayCurrentReference
                  << ",\"xray_voltage_mon\":" << status.xrayVoltageMonitor
                  << ",\"xray_current_mon\":" << status.xrayCurrentMonitor
                  << ",\"xray_fault\":" << status.xrayFaultCode
                  << ",\"motion_online\":" << (status.motionOnline ? "true" : "false")
                  << ",\"motion_running\":" << (status.motionRunning ? "true" : "false")
                  << ",\"motion_direction\":" << (status.motionDirection ? "true" : "false")
                  << ",\"motion_speed\":" << status.motionSpeed
                  << ",\"io_primary_online\":" << (status.primaryIoOnline ? "true" : "false")
                  << ",\"io_secondary_online\":" << (status.secondaryIoOnline ? "true" : "false")
                  << "}" << std::endl;
    }
}

int main(int argc, char** argv)
{
    try
    {
        const auto options = ParseOptions(argc, argv);
        if (options.sdkRoot.empty())
        {
            throw std::runtime_error("--sdk-root is required.");
        }

        SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
        AddDllDirectory(options.sdkRoot.c_str());
        SetCurrentDirectoryW(options.sdkRoot.c_str());

        std::unique_ptr<TechikPeripheralRuntime> peripherals;
        if (!options.runtimeRoot.empty())
        {
            peripherals = std::make_unique<TechikPeripheralRuntime>(options.peripherals);
            peripherals->Inspect();
            std::cout << "{\"event\":\"peripheral_inspection\","
                         "\"abi\":\"techik-2024-captured-x64\","
                         "\"xray_type\":6,\"motion_type\":4,"
                         "\"io_types\":[3,7],\"plugins_ready\":true}"
                      << std::endl;
        }

        Module module(options.sdkRoot / L"tk_driver_dt.dll");
        const auto constructor = module.Export<Constructor>(
            "??0TkDriverDt@device@tk@@QEAA@XZ");
        const auto destructor = module.Export<Destructor>(
            "??1TkDriverDt@device@tk@@QEAA@XZ");
        const auto start = module.Export<Start>(
            "?Start@TkDriverDt@device@tk@@QEAA_NUst_dt_info@23@@Z");
        const auto setCallback = module.Export<SetCallback>(
            "?SetCallback@TkDriverDt@device@tk@@QEAAXUst_dt_data_callback@23@@Z");
        const auto grabEnable = module.Export<GrabEnable>(
            "?GrabEnable@TkDriverDt@device@tk@@QEAA_N_N@Z");
        const auto exitDetector = module.Export<Exit>(
            "?Exit@TkDriverDt@device@tk@@QEAAXXZ");

        std::cout << "{\"event\":\"inspection\",\"abi\":\"techik-demo-2023-x64\","
                     "\"dt_info_size\":104,\"frame_size\":40,\"exports_ready\":true}"
                  << std::endl;
        if (options.frameStreamSelfTest)
        {
            const std::array<std::uint16_t, 4> pixels{1, 1024, 32768, 65535};
            FrameWriter writer(
                options.outputDirectory,
                options.framePipe,
                2,
                static_cast<std::size_t>(options.frameQueueCapacity));
            if (!writer.OnFrame(DtDataFrame{
                    7,
                    {},
                    pixels.data(),
                    nullptr,
                    2,
                    2,
                    1,
                    0}))
            {
                throw std::runtime_error("Detector frame stream self-test failed.");
            }
            std::cout << "{\"event\":\"frame_stream_self_test\",\"ok\":true}"
                      << std::endl;
            return 0;
        }
        if (options.inspectOnly)
        {
            return 0;
        }

        if (options.outputDirectory.empty() && options.framePipe.empty())
        {
            throw std::runtime_error(
                "--frame-pipe or --output-directory is required in detector mode.");
        }

        alignas(16) std::array<std::byte, 0x200> detectorStorage{};
        constructor(detectorStorage.data());
        bool started = false;
        bool grabbing = false;
        try
        {
            FrameWriter writer(
                options.outputDirectory,
                options.framePipe,
                options.aggregateHeight,
                static_cast<std::size_t>(options.frameQueueCapacity));
            setCallback(detectorStorage.data(), DtDataCallback{HandleFrame, &writer});
            started = start(detectorStorage.data(), options.detector);
            if (!started)
            {
                throw std::runtime_error("TkDriverDt::Start returned false.");
            }

            grabbing = grabEnable(detectorStorage.data(), true);
            if (!grabbing)
            {
                throw std::runtime_error("TkDriverDt::GrabEnable(true) returned false.");
            }

            std::cout << "{\"event\":\"detector_started\",\"line_pixels\":"
                      << options.detector.linePixels
                      << ",\"sub_frame_height\":" << options.detector.subFrameHeight
                      << ",\"aggregate_height\":" << options.aggregateHeight << "}"
                      << std::endl;

            if (options.runPeripherals)
            {
                if (!peripherals)
                {
                    throw std::runtime_error(
                        "--runtime-root is required with --run-peripherals.");
                }
                peripherals->Connect();
                std::cout << "{\"event\":\"peripherals_connected\"}" << std::endl;
                WritePeripheralStatus(peripherals->ReadStatus());
            }

            CommandQueue commands;
            commands.Start();
            bool stopping = false;
            while (!stopping && !commands.IsClosed())
            {
                if (peripherals)
                {
                    peripherals->ProcessEvents();
                }

                std::string command;
                while (commands.TryPop(command))
                {
                    try
                    {
                        std::istringstream input(command);
                        std::string operation;
                        input >> operation;
                        if (operation == "stop" || operation == "exit")
                        {
                            stopping = true;
                            break;
                        }
                        if (operation == "ping")
                        {
                            std::cout << "{\"event\":\"pong\"}" << std::endl;
                        }
                        else if (operation == "status" && peripherals)
                        {
                            WritePeripheralStatus(peripherals->ReadStatus());
                        }
                        else if (operation == "machine_start" && peripherals)
                        {
                            double voltage{};
                            double current{};
                            double speed{};
                            int direction{};
                            if (!(input >> voltage >> current >> speed >> direction))
                            {
                                throw std::runtime_error("Invalid machine_start command.");
                            }
                            peripherals->StartMachine(
                                voltage, current, speed, direction != 0);
                            std::cout << "{\"event\":\"machine_started\"}" << std::endl;
                        }
                        else if (operation == "machine_stop" && peripherals)
                        {
                            peripherals->StopMachine();
                            std::cout << "{\"event\":\"machine_stopped\"}" << std::endl;
                        }
                        else if (operation == "eject" && peripherals)
                        {
                            int port{};
                            int activeHigh{};
                            long long pulseMicroseconds{};
                            if (!(input >> port >> activeHigh >> pulseMicroseconds))
                            {
                                throw std::runtime_error("Invalid eject command.");
                            }
                            peripherals->PulseReject(
                                port, activeHigh != 0, pulseMicroseconds);
                            std::cout << "{\"event\":\"eject_completed\",\"port\":"
                                      << port << "}" << std::endl;
                        }
                    }
                    catch (const std::exception& exception)
                    {
                        std::cerr << "{\"event\":\"command_error\",\"message\":\""
                                  << exception.what() << "\"}" << std::endl;
                    }
                }
                std::this_thread::sleep_for(std::chrono::milliseconds(5));
            }

            if (peripherals)
            {
                peripherals->StopMachine();
            }
        }
        catch (...)
        {
            if (grabbing)
            {
                grabEnable(detectorStorage.data(), false);
            }
            if (started)
            {
                exitDetector(detectorStorage.data());
            }
            destructor(detectorStorage.data());
            throw;
        }

        if (grabbing)
        {
            grabEnable(detectorStorage.data(), false);
        }
        if (started)
        {
            exitDetector(detectorStorage.data());
        }
        destructor(detectorStorage.data());
        std::cout << "{\"event\":\"detector_stopped\"}" << std::endl;
        return 0;
    }
    catch (const std::exception& exception)
    {
        std::cerr << "{\"event\":\"fatal\",\"message\":\""
                  << exception.what() << "\"}" << std::endl;
        return 1;
    }
}
