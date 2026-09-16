#pragma once
#include "ControllerIsolationReader.h"

namespace widgetrail::isolation {

// A present Bluetooth HID interface can outlive its physical connection.
// Prove that the exact candidate delivers a fresh report before hiding it.
// The reader is stopped on every outcome, before isolation opens its own handle.
template<class Reader>
bool ProbeDualSenseConnection(Reader& reader, const SelectedControllerEnrollment& enrollment,
    std::uint32_t waitMilliseconds) noexcept {
    SelectedControllerCurrent current;
    const bool live = reader.Start(&enrollment) &&
        reader.WaitForReading(waitMilliseconds) && reader.Sample(current) && current.connected;
    reader.Stop();
    return live;
}

} // namespace widgetrail::isolation
