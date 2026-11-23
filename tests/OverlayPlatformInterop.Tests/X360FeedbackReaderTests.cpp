#include "../../third_party/ViGEmClient/src/X360FeedbackReader.h"
#include <atomic>
#include <cstdlib>
#include <iostream>
#include <string>

static void Check(bool ok, const char* message) {
    if (!ok) { std::cerr << message << '\n'; std::exit(1); }
}

int main() {
    // Exercise the real overlapped cancellation and join with a local pipe.
    const auto name = L"\\\\.\\pipe\\WidgetRail-feedback-test-" + std::to_wstring(GetCurrentProcessId());
    HANDLE server = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_INBOUND | FILE_FLAG_OVERLAPPED,
        PIPE_TYPE_BYTE | PIPE_WAIT, 1, 256, 256, 0, nullptr);
    Check(server != INVALID_HANDLE_VALUE, "create server");
    HANDLE writer = CreateFileW(name.c_str(), GENERIC_WRITE, 0, nullptr, OPEN_EXISTING, 0, nullptr);
    Check(writer != INVALID_HANDLE_VALUE, "connect writer");
    HANDLE pending = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    HANDLE delivered = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    Check(pending && delivered, "create test events");
    for (unsigned cycle = 0; cycle < 64; ++cycle) {
        std::atomic<unsigned> callbacks{};
        {
            X360FeedbackReader reader;
            Check(reader.Start(server, char{}, [&](char& value, DWORD& bytes, OVERLAPPED& operation) {
                BOOL result = ReadFile(server, &value, 1, &bytes, &operation);
                const DWORD error = GetLastError();
                if (!result && error == ERROR_IO_PENDING) SetEvent(pending);
                SetLastError(error);
                return result;
            }, [&](char value) {
                Check(value == 'x', "feedback payload");
                ++callbacks;
                SetEvent(delivered);
            }), "start reader");
            Check(WaitForSingleObject(pending, 2000) == WAIT_OBJECT_0, "first pending read");
            if (cycle % 2) {
                DWORD written{};
                Check(WriteFile(writer, "x", 1, &written, nullptr) && written == 1, "deliver feedback");
                Check(WaitForSingleObject(delivered, 2000) == WAIT_OBJECT_0, "callback");
                Check(WaitForSingleObject(pending, 2000) == WAIT_OBJECT_0, "next pending read");
            }
        }
        Check(callbacks == cycle % 2, "cancelled read must not deliver feedback");
    }
    CloseHandle(delivered); CloseHandle(pending); CloseHandle(writer); CloseHandle(server);
    std::cout << "64 pending-I/O teardown cycles passed\n";
}
