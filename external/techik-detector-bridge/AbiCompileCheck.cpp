#include "RecoveredTechikDetectorAbi.hpp"

int main()
{
    using namespace oceanfresh::techik::recovered;
    DtInfo info{};
    DtDataFrame frame{};
    DtDataCallback callback{};
    TkDriverDtStorage storage{};
    return static_cast<int>(info.raw.size() + sizeof(frame) + sizeof(callback) + storage.raw.size() == 0);
}
