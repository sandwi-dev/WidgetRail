#include "../../src/OverlayPlatformInterop/ControllerIsolationReconnect.h"

#include <windows.h>

#include <cstdlib>
#include <atomic>
#include <iostream>
#include <thread>

namespace {
using namespace widgetrail::isolation;
std::atomic_int checks{};
void Check(const bool value, const char* message) {
    checks.fetch_add(1, std::memory_order_relaxed);
    if (!value) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}
}

int main() {
    const RoutingAuthority authority{
        GetCurrentProcessId(), GetTickCount64() + 1, 3, 4};
    const auto pipeName = ControllerIsolationPipeName(authority);
    Check(ControllerIsolationControlPipeName(authority) == pipeName + L".control",
          "one-shot control ingress is authority-bound and distinct from the host pipe");
    ControllerIsolationPipeServer server;
    std::uint32_t serverError{};
    Check(server.Open(pipeName, serverError),
          "current-user remote-rejected pipe opens once");
    HANDLE acceptPosted = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    Check(acceptPosted != nullptr, "accept readiness fence is created");
    ControlFrame observed;
    std::thread serverThread([&] {
        if (!server.Accept(2'000, serverError, acceptPosted)) {
            std::cerr << "server Accept error=" << serverError << '\n';
            Check(false, "server accepts one bounded local client");
        }
        if (!server.Receive(observed, 2'000, serverError)) {
            std::cerr << "server Receive error=" << serverError << '\n';
            Check(false, "server receives one complete fixed frame");
        }
        auto response = observed;
        response.kind = ControlMessageKind::Heartbeat;
        response.observedAtMilliseconds =
            static_cast<std::uint64_t>(ControlProgress::Playing);
        Check(server.Reply(response, serverError),
              "server replies with one complete correlated frame");
    });
    Check(WaitForSingleObject(acceptPosted, 2'000) == WAIT_OBJECT_0,
          "client starts only after ConnectNamedPipe acceptance is posted");
    ControllerIsolationPipeClient client;
    std::uint32_t clientError{};
    if (!client.Connect(pipeName, 2'000, clientError)) {
        std::cerr << "client Connect error=" << clientError << '\n';
        Check(false, "client connects to the current-user local server");
    }
    ControllerIsolationNonce nonce{};
    nonce.fill(7);
    const auto request = MakeControlFrame(
        ControlMessageKind::Heartbeat, nonce, authority, 1);
    ControlFrame response;
    Check(client.Exchange(request, response, 2'000, clientError) &&
              response.sequence == request.sequence &&
              response.authority == authority &&
              SameNonce(response.nonce, nonce) &&
              response.kind == ControlMessageKind::Heartbeat &&
              response.observedAtMilliseconds ==
                  static_cast<std::uint64_t>(ControlProgress::Playing),
          "fixed request and response retain exact correlation authority");
    Check(client.serverProcessId() == GetCurrentProcessId(),
          "client authenticates the exact local server process owner after handshake");
    serverThread.join();

    ControllerIsolationPipeServer controlServer;
    std::uint32_t controlError{};
    Check(controlServer.Open(
              ControllerIsolationControlPipeName(authority), controlError),
          "one-shot control ingress opens while the primary host remains connected");
    std::thread controlThread([&] {
        ControlFrame controlRequest;
        Check(controlServer.Accept(2'000, controlError) &&
                               controlServer.Receive(controlRequest, 2'000, controlError),
              "control ingress accepts one bounded command beside the persistent host");
        auto controlResponse = controlRequest;
        controlResponse.observedAtMilliseconds =
            static_cast<std::uint64_t>(ControlProgress::Contained);
        Check(controlServer.Reply(controlResponse, controlError),
              "control ingress returns its correlated status independently");
    });
    ControllerIsolationPipeClient controlClient;
    Check(controlClient.Connect(
              ControllerIsolationControlPipeName(authority), 2'000,
              clientError),
          "one-shot client does not contend for the occupied primary pipe");
    const auto controlRequest = MakeControlFrame(
        ControlMessageKind::QueryStatus, nonce, authority, 2);
    ControlFrame controlResponse;
    Check(controlClient.Exchange(
              controlRequest, controlResponse, 2'000, clientError) &&
              controlResponse.kind == ControlMessageKind::QueryStatus &&
              controlResponse.sequence == controlRequest.sequence &&
              controlResponse.observedAtMilliseconds ==
                  static_cast<std::uint64_t>(ControlProgress::Contained),
          "one-shot status remains correlated while primary ownership is live");
    controlThread.join();
    controlClient.Close();
    controlServer.Disconnect();
    CloseHandle(acceptPosted);
    client.Close();
    server.Disconnect();
    std::cout << "ControllerIsolationReconnectTests passed (" << checks.load()
              << " checks)\n";
}
