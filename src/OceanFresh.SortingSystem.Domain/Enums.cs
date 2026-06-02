namespace OceanFresh.SortingSystem.Domain;

public enum UserRole
{
    Operator = 1,
    Administrator = 2
}

public enum DefectHandlingAction
{
    Pass = 0,
    Sink = 1,
    AirJet = 2,
    Pusher = 3,
    StopLine = 4
}

public enum ModelStatus
{
    Draft = 1,
    Active = 2,
    Disabled = 3,
    RolledBack = 4
}

public enum AlarmSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum DeviceState
{
    Offline = 0,
    Idle = 1,
    Running = 2,
    Warning = 3,
    Faulted = 4
}

public enum RuntimeMode
{
    Stopped = 0,
    Starting = 1,
    Running = 2,
    SafeStop = 3,
    Faulted = 4
}
