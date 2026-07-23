namespace OceanFresh.SortingSystem.Domain;

public enum UserRole
{
    Operator = 1,
    Administrator = 2
}

public enum OperationAuditCategory
{
    Login = 1,
    Detection = 2,
    Review = 3,
    Management = 4
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
    Normal = 0,
    Deleted = 9
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

public enum RuntimeDataSourceMode
{
    XrayCamera = 1,
    LocalImageDirectory = 2
}

public enum DetectionSessionStatus
{
    Created = 1,
    Running = 2,
    Stopped = 3
}

public enum ManualReviewJudgement
{
    ConfirmedAbnormal = 1,
    FalsePositive = 2,
    RelabeledAbnormal = 3
}

public enum DeviceType
{
    Conveyor = 1,
    XrayDetector = 2,
    Ejector = 3,
    Controller = 4,
    XraySource = 5
}

public enum HardwareSignalSeverity
{
    Normal = 0,
    Warning = 1,
    Critical = 2
}
