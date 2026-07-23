#pragma once

#include <filesystem>
#include <memory>
#include <string>

struct TechikPeripheralOptions
{
    std::filesystem::path runtimeRoot;

    int xrayId{0};
    int xrayType{6};
    int xrayComPort{5};
    int xrayMaxVoltageKv{60};
    int xrayMinVoltageKv{30};
    int xrayMaxCurrentUa{8000};
    int xrayMinCurrentUa{500};
    int xrayMaxPowerLimit{350000};
    int xrayMinPowerLimit{6000};
    int xrayOpenWaitMilliseconds{8000};
    int xrayCloseWaitMilliseconds{5000};

    int motionId{0};
    int motionType{4};
    int motionComPort{1};
    double motionSpeedMax{120};
    double motionSpeedMin{5};
    double motionSpeedRatio{23};
    bool motionDirectionReverse{false};
    int motionPlcId{0};
    int motionPlcRunPortForward{9};
    int motionPlcRunPortReverse{8};
    int motionPlcMonitorPortRun{7};
    int motionPlcAnalogId{0};

    int ioPrimaryId{0};
    int ioPrimaryType{3};
    int ioPrimaryComPort{3};
    int ioSecondaryId{1};
    int ioSecondaryType{7};
    int ioSecondaryComPort{1};
};

struct TechikPeripheralStatus
{
    bool xrayOnline{};
    bool xrayEnabled{};
    double xrayVoltageReference{};
    double xrayCurrentReference{};
    double xrayVoltageMonitor{};
    double xrayCurrentMonitor{};
    int xrayFaultCode{};

    bool motionOnline{};
    bool motionRunning{};
    bool motionDirection{};
    double motionSpeed{};

    bool primaryIoOnline{};
    bool secondaryIoOnline{};
};

class TechikPeripheralRuntime
{
public:
    explicit TechikPeripheralRuntime(TechikPeripheralOptions options);
    ~TechikPeripheralRuntime();

    TechikPeripheralRuntime(const TechikPeripheralRuntime&) = delete;
    TechikPeripheralRuntime& operator=(const TechikPeripheralRuntime&) = delete;

    void Inspect();
    void Connect();
    void ProcessEvents();
    TechikPeripheralStatus ReadStatus();
    void StartMachine(double voltageKv, double currentUa, double speed, bool direction);
    void StopMachine();
    void PulseReject(int port, bool activeHigh, long long pulseMicroseconds);

private:
    class Impl;
    std::unique_ptr<Impl> impl_;
};
