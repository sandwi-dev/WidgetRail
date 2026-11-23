// WidgetRail adaptation for the vendored ViGEm client. See ../LICENSE.
#pragma once
#include <Windows.h>
#include <thread>

// The serialized target owner starts and destroys this reader. Only its
// worker submits/cancels I/O; the report and OVERLAPPED stay alive until the
// kernel completes the request, including when stop races with submission.
class X360FeedbackReader final {
public:
    X360FeedbackReader() = default;
    X360FeedbackReader(const X360FeedbackReader&) = delete;
    X360FeedbackReader& operator=(const X360FeedbackReader&) = delete;

    template<class Report, class Submit, class Deliver>
    bool Start(HANDLE device, Report initialReport, Submit submit, Deliver deliver) noexcept {
        if (!stop_ || !completed_ || thread_.joinable()) return false;
        try {
            thread_ = std::thread([this, device, report = initialReport, submit, deliver]() mutable {
                const HANDLE events[]{stop_, completed_};
                while (WaitForSingleObject(stop_, 0) == WAIT_TIMEOUT) {
                    OVERLAPPED operation{};
                    operation.hEvent = completed_;
                    ResetEvent(completed_);
                    DWORD bytes{};
                    if (!submit(report, bytes, operation)) {
                        if (GetLastError() != ERROR_IO_PENDING) return;
                        if (WaitForMultipleObjects(2, events, FALSE, INFINITE) != WAIT_OBJECT_0 + 1) {
                            CancelIoEx(device, &operation);
                            GetOverlappedResult(device, &operation, &bytes, TRUE);
                            return;
                        }
                        if (!GetOverlappedResult(device, &operation, &bytes, FALSE)) return;
                    }
                    if (WaitForSingleObject(stop_, 0) != WAIT_TIMEOUT) return;
                    deliver(report);
                }
            });
            return true;
        } catch (...) {
            return false;
        }
    }

    ~X360FeedbackReader() {
        if (thread_.joinable()) {
            SetEvent(stop_);
            thread_.join();
        }
        if (completed_) CloseHandle(completed_);
        if (stop_) CloseHandle(stop_);
    }
private:
    HANDLE stop_{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    HANDLE completed_{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    std::thread thread_;
};
