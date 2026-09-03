#include "WidgetBridgeClient.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <thread>
#include <string>
#include <vector>

namespace {

void Require(const bool condition, const char* message) {
    if (condition) return;
    std::cerr << "WidgetBridgeCatalogTests failed: " << message << '\n';
    std::exit(EXIT_FAILURE);
}

#define CHECK(condition) Require(static_cast<bool>(condition), #condition)

void VerifyWidgetBridgePipeReadinessContract() {
    ULONGLONG tick{};
    std::size_t attempts{};
    const auto delayedReady = widgetrail::WaitForWidgetBridgePipeReadiness(
        [&]() {
            ++attempts;
            return widgetrail::WidgetBridgePipeConnectAttempt{
                .pipe = tick > 5'000 ? reinterpret_cast<HANDLE>(1) : INVALID_HANDLE_VALUE,
                .error = tick > 5'000 ? ERROR_SUCCESS :
                    static_cast<DWORD>(ERROR_FILE_NOT_FOUND),
            };
        },
        []() { return false; },
        [&]() { return tick; },
        [&](const DWORD milliseconds) { tick += milliseconds; });
    CHECK(delayedReady.status == widgetrail::WidgetBridgePipeReadinessStatus::Connected);
    CHECK(delayedReady.pipe == reinterpret_cast<HANDLE>(1));
    CHECK(tick > 5'000);
    CHECK(tick < widgetrail::WidgetBridgeReadinessContract::AcceptTimeoutMilliseconds);
    CHECK(attempts > 250);

    tick = 0;
    attempts = 0;
    const auto exited = widgetrail::WaitForWidgetBridgePipeReadiness(
        [&]() {
            ++attempts;
            return widgetrail::WidgetBridgePipeConnectAttempt{
                .error = static_cast<DWORD>(ERROR_FILE_NOT_FOUND),
            };
        },
        []() { return true; },
        [&]() { return tick; },
        [&](const DWORD milliseconds) { tick += milliseconds; });
    CHECK(exited.status == widgetrail::WidgetBridgePipeReadinessStatus::ChildExited);
    CHECK(exited.error == ERROR_FILE_NOT_FOUND);
    CHECK(attempts == 1);
    CHECK(tick == 0);

    tick = 0;
    attempts = 0;
    const auto neverReady = widgetrail::WaitForWidgetBridgePipeReadiness(
        [&]() {
            ++attempts;
            return widgetrail::WidgetBridgePipeConnectAttempt{
                .error = static_cast<DWORD>(ERROR_FILE_NOT_FOUND),
            };
        },
        []() { return false; },
        [&]() { return tick; },
        [&](const DWORD milliseconds) { tick += milliseconds; });
    CHECK(neverReady.status == widgetrail::WidgetBridgePipeReadinessStatus::TimedOut);
    CHECK(neverReady.error == ERROR_FILE_NOT_FOUND);
    CHECK(attempts == 500);
    CHECK(tick == widgetrail::WidgetBridgeReadinessContract::AcceptTimeoutMilliseconds);

    CHECK(widgetrail::ProjectStartupSettingsFailure({}) ==
          L"Settings is unavailable in the admitted widget catalog.");
    CHECK(widgetrail::ProjectStartupSettingsFailure(
              L"WidgetBridge pipe readiness timed out (Win32 error 2).") ==
          L"WidgetBridge startup failed: WidgetBridge pipe readiness timed out "
          L"(Win32 error 2).");

    const auto hostPath = std::filesystem::path{__FILE__}.parent_path() / "main.cpp";
    std::ifstream hostStream(hostPath, std::ios::binary);
    CHECK(hostStream.good());
    std::ostringstream hostPayload;
    hostPayload << hostStream.rdbuf();
    const auto host = hostPayload.str();
    const auto establish = host.find("if (auto change = sessions_.EstablishCatalog())");
    const auto capture = host.find(
        "bridgeStartupFailure = bridge_.lastError();", establish);
    const auto projection = host.find(
        "ProjectStartupSettingsFailure(\n                        bridgeStartupFailure)", capture);
    CHECK(establish != std::string::npos);
    CHECK(capture != std::string::npos);
    CHECK(projection != std::string::npos);
}

std::string Descriptor(const int index) {
    return "{\"id\":\"widget-" + std::to_string(index) +
        "\",\"name\":\"Widget\",\"instanceId\":\"instance-" +
        std::to_string(index) + "\",\"runtimeGeneration\":\"runtime-" +
        std::to_string(index) + "\",\"presentationGeneration\":\"presentation-" +
        std::to_string(index) + "\",\"quickActions\":[]}";
}

constexpr std::string_view ValidAppearance = R"json({
    "revision": 7,
    "themeId": "midnight-blue",
    "themeVersion": "1.2.0",
    "interfaceScale": 1.1,
    "textScale": 1.25,
    "backdropOpacity": 0.62,
    "motion": "reduced",
    "contrast": "high",
    "boldText": true,
    "transparency": "reduced",
    "animateWidgetSwitching": false,
    "widgetSurfaceAppearance": "theme",
    "widgetSurfaceAppearanceOverrides": {},
    "shellStyles": {
        "canvas": {
            "background": {"kind":"color","text":"#101820","number":null,"unit":null}
        },
        "panel": {
            "corner-radius": {"kind":"length","text":"18px","number":18,"unit":"px"}
        },
        "title": {
            "font-size": {"kind":"length","text":"24px","number":24,"unit":"px"},
            "font-family": {"kind":"fontFamily","text":"Segoe UI Variable","number":null,"unit":null}
        }
    }
})json";

void WriteBytes(const HANDLE pipe, const void* bytes, const DWORD length) {
    DWORD written{};
    Require(WriteFile(pipe, bytes, length, &written, nullptr) != FALSE &&
                written == length,
            "Could not write deterministic bridge frame bytes");
}

void VerifyCancelledFrameRead(const std::size_t publishedBytes) {
    constexpr std::string_view body =
        R"json({"protocolVersion":1,"type":"snapshot","requestId":1,"payload":{}})json";
    const std::int32_t length = static_cast<std::int32_t>(body.size());
    std::vector<std::byte> frame(sizeof(length) + body.size());
    std::memcpy(frame.data(), &length, sizeof(length));
    std::memcpy(frame.data() + sizeof(length), body.data(), body.size());
    Require(publishedBytes < frame.size(), "Cancelled frame case must remain incomplete");

    HANDLE reader{};
    HANDLE writer{};
    Require(CreatePipe(&reader, &writer, nullptr, 0) != FALSE,
            "Could not create deterministic bridge frame pipe");
    if (publishedBytes > 0)
        WriteBytes(writer, frame.data(), static_cast<DWORD>(publishedBytes));

    std::atomic_bool started{};
    std::optional<widgetrail::testing::BridgeFrameReadResult> result;
    std::jthread readThread([&] {
        started = true;
        result = widgetrail::testing::ReadBridgeFrame(reader);
    });
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    bool readPending{};
    do {
        BOOL pending{};
        if (started &&
            GetThreadIOPendingFlag(readThread.native_handle(), &pending) && pending) {
            readPending = true;
            break;
        }
        Sleep(1);
    } while (std::chrono::steady_clock::now() < deadline);
    Sleep(20);
    const bool cancelled = CancelSynchronousIo(readThread.native_handle()) != FALSE;
    CloseHandle(writer);
    readThread.join();
    CloseHandle(reader);
    Require(readPending,
            "Bridge frame reader did not enter its next blocking read after the prefix");
    Require(cancelled, "Could not cancel deterministic bridge frame read");
    Require(result && !result->frame && result->transportTainted &&
                result->error == ERROR_OPERATION_ABORTED,
            "Cancelled incomplete frame did not taint the bridge transport");
}

void VerifyFrameSafeCancellationRecovery() {
    VerifyCancelledFrameRead(0);
    VerifyCancelledFrameRead(2);
    VerifyCancelledFrameRead(sizeof(std::int32_t) + 7);

    constexpr std::string_view body =
        R"json({"protocolVersion":1,"type":"snapshot","requestId":2,"payload":{}})json";
    const std::int32_t length = static_cast<std::int32_t>(body.size());
    HANDLE reader{};
    HANDLE writer{};
    Require(CreatePipe(&reader, &writer, nullptr, 0) != FALSE,
            "Could not create replacement bridge frame pipe");
    WriteBytes(writer, &length, sizeof(length));
    WriteBytes(writer, body.data(), static_cast<DWORD>(body.size()));
    const auto recovered = widgetrail::testing::ReadBridgeFrame(reader);
    CloseHandle(writer);
    CloseHandle(reader);
    Require(recovered.frame && *recovered.frame == body &&
                !recovered.transportTainted && recovered.error == ERROR_SUCCESS,
            "Next ordinary request did not succeed on a correctly framed replacement transport");
}

void VerifyAtomicPresentationUpdateMaterialization() {
    std::wstring error;
    const auto checkpoint = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":21,
            "sequence":9,
            "widgetInstanceId":"update.sample",
            "activeInputScopeId":"root",
            "initialFocusId":"a",
            "surface":{"mode":"standard","preferredWidth":560,"preferredHeight":645,"minimumWidth":320,"minimumHeight":240},
            "pinnedLayouts":[
                {"id":"compact","name":"Compact",
                 "surface":{"mode":"compact","preferredWidth":360,"preferredHeight":240,
                            "minimumWidth":240,"minimumHeight":180},
                 "activeInputScopeId":"compact.root","initialFocusId":"compact.play",
                 "root":{"id":"compact.root","kind":"stack","children":[
                    {"id":"compact.play","kind":"button","text":"Play",
                     "actionId":"play","children":[]}
                 ]}}
            ],
            "root":{"id":"root","kind":"stack","children":[
                {"id":"a","kind":"button","text":"Before","actionId":"activate.a","children":[]},
                {"id":"b","kind":"text","text":"Remove me","children":[]}
            ]}
        },
        "renderStyles":{}
    })json", error);
    Require(checkpoint && error.empty() && !checkpoint->documentJson.empty(),
            "Could not parse the retained update checkpoint");

    const auto update = widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "widgetId":"sample",
        "update":{
            "protocolVersion":18,
            "widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,
            "sequence":10,
            "operations":[
                {"kind":"setProperties","targetId":"a","properties":[
                    {"property":"text","value":"After"},
                    {"property":"isDisabled","value":true}
                ]},
                {"kind":"insertChild","parentId":"root","index":2,
                 "subtree":{"id":"c","kind":"text","text":"Inserted","children":[]}},
                {"kind":"moveChild","parentId":"root","childId":"c","index":0},
                {"kind":"removeChild","parentId":"root","childId":"b"},
                {"kind":"replaceSubtree","targetId":"a",
                 "subtree":{"id":"a","kind":"button","text":"Replaced","actionId":"activate.a","children":[]}},
                {"kind":"setProperties","properties":[
                    {"property":"initialFocusId","value":"a"}
                ]}
            ]
        },
        "renderStyles":{
            "a":{"base":{"opacity":{"kind":"number","text":"0.75","number":0.75,"unit":null}}},
            "compact/compact.play":{"base":{"opacity":{"kind":"number","text":"0.5","number":0.5,"unit":null}}}
        }
    })json", error);
    Require(update && error.empty(), "Could not parse the bounded update batch");

    error.clear();
    const auto typedUpdate =
        widgetrail::testing::ParseTypedWidgetPresentationUpdateResponse(R"json({
        "widgetId":"sample",
        "transactionKind":"incrementalUpdate",
        "baseSequence":9,
        "recoveryOriginSequence":0,
        "update":{
            "protocolVersion":18,
            "widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,
            "sequence":10,
            "operations":[]
        },
        "renderStyles":{}
    })json", error);
    Require(typedUpdate && error.empty() && typedUpdate->baseSequence == 9 &&
                typedUpdate->sequence == 10,
            "Typed incremental publication fields were rejected by the native parser");

    error.clear();
    Require(!widgetrail::testing::ParseTypedWidgetPresentationUpdateResponse(R"json({
        "widgetId":"sample",
        "transactionKind":"incrementalUpdate",
        "baseSequence":9,
        "recoveryOriginSequence":0,
        "unexpected":true,
        "update":{
            "protocolVersion":18,
            "widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,
            "sequence":10,
            "operations":[]
        },
        "renderStyles":{}
    })json", error),
            "Unknown typed publication fields did not fail closed");
    auto materialized = widgetrail::MaterializeWidgetPresentationUpdate(
        *checkpoint, *update, L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", error);
    Require(materialized && error.empty(), "Could not materialize the atomic update");
    const auto& materializedSnapshot = materialized->snapshot;
    Require(materializedSnapshot.protocolVersion == 21 && materializedSnapshot.sequence == 10 &&
                materializedSnapshot.pinnedLayouts.size() == 1 &&
                materializedSnapshot.pinnedLayouts[0].id == L"compact" &&
                materializedSnapshot.pinnedLayouts[0].root &&
                materializedSnapshot.pinnedLayouts[0].root->children[0]
                        .baseStyle.at(L"opacity").number == 0.5 &&
                materializedSnapshot.root.children.size() == 2 &&
                materializedSnapshot.root.children[0].id == L"c" &&
                materializedSnapshot.root.children[1].id == L"a" &&
                materializedSnapshot.root.children[1].text == L"Replaced" &&
                materializedSnapshot.root.children[1].baseStyle.at(L"opacity").number == 0.75,
            "Atomic update did not publish the complete candidate and styles");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,"sequence":1,
            "widgetInstanceId":"layouts.old","activeInputScopeId":"root",
            "pinnedLayouts":[{"id":"compact","name":"Compact",
                "surface":{"mode":"compact","preferredWidth":360,"preferredHeight":240}}],
            "root":{"id":"root","kind":"stack","children":[]}
        },"renderStyles":{}
    })json", error),
            "Protocol-v19 snapshot admitted a pinned layout catalog");

    error.clear();
    const auto projected = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":21,"sequence":2,
            "widgetInstanceId":"layouts.projected","activeInputScopeId":"root",
            "pinnedLayouts":[{"id":"compact","name":"Compact",
                "surface":{"mode":"compact","preferredWidth":360,"preferredHeight":240},
                "activeInputScopeId":"compact.root","initialFocusId":"compact.play",
                "root":{"id":"compact.root","kind":"stack","children":[
                    {"id":"compact.play","kind":"button","text":"Play",
                     "actionId":"play","children":[]}
                ]}}],
            "root":{"id":"root","kind":"stack","children":[]}
        },"renderStyles":{
            "compact/compact.play":{"base":{"opacity":{"kind":"number","text":"0.625","number":0.625,"unit":null}}}
        }
    })json", error);
    Require(projected && error.empty() && projected->protocolVersion == 21 &&
                projected->pinnedLayouts.size() == 1 &&
                projected->pinnedLayouts[0].root &&
                projected->pinnedLayouts[0].root->id == L"compact.root" &&
                projected->pinnedLayouts[0].root->children[0]
                        .baseStyle.at(L"opacity").number == 0.625 &&
                projected->pinnedLayouts[0].activeInputScopeId == L"compact.root" &&
                projected->pinnedLayouts[0].initialFocusId == L"compact.play",
            "Native admission did not retain the bounded declarative projection");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":20,"sequence":2,
            "widgetInstanceId":"layouts.projected.old","activeInputScopeId":"root",
            "pinnedLayouts":[{"id":"compact","name":"Compact",
                "surface":{"mode":"compact"},"activeInputScopeId":"compact.root",
                "root":{"id":"compact.root","kind":"stack","children":[]}}],
            "root":{"id":"root","kind":"stack","children":[]}
        },"renderStyles":{}
    })json", error),
            "Protocol-v20 snapshot admitted a declarative pinned projection");

    std::string tooManyLayouts = R"json({"snapshot":{"protocolVersion":20,"sequence":1,
        "widgetInstanceId":"layouts.many","activeInputScopeId":"root","pinnedLayouts":[)json";
    for (int index = 0; index < 9; ++index) {
        if (index != 0) tooManyLayouts += ',';
        tooManyLayouts += "{\"id\":\"layout." + std::to_string(index) +
            "\",\"name\":\"Layout\",\"surface\":{\"mode\":\"compact\","
            "\"preferredWidth\":360,\"preferredHeight\":240}}";
    }
    tooManyLayouts += R"json(],"root":{"id":"root","kind":"stack","children":[]}},"renderStyles":{}})json";
    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(tooManyLayouts, error),
            "Native snapshot admission exceeded the pinned layout catalog bound");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":20,"sequence":1,
            "widgetInstanceId":"layouts.duplicate","activeInputScopeId":"root",
            "pinnedLayouts":[
                {"id":"same","name":"First","surface":{"mode":"compact"}},
                {"id":"same","name":"Second","surface":{"mode":"wide"}}
            ],
            "root":{"id":"root","kind":"stack","children":[]}
        },"renderStyles":{}
    })json", error),
            "Native snapshot admission accepted duplicate pinned layout identity");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":20,"sequence":1,
            "widgetInstanceId":"layouts.surface","activeInputScopeId":"root",
            "pinnedLayouts":[{"id":"compact","name":"Compact",
                "surface":{"mode":"compact","preferredWidth":360},"unknown":true}],
            "root":{"id":"root","kind":"stack","children":[]}
        },"renderStyles":{}
    })json", error),
            "Native snapshot admission accepted malformed or unknown pinned layout fields");

    auto stale = *update;
    stale.baseSequence = 8;
    error.clear();
    Require(!widgetrail::MaterializeWidgetPresentationUpdate(
                *checkpoint, stale,
                L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", error) &&
                checkpoint->sequence == 9 && checkpoint->root.children[0].text == L"Before",
            "Stale update mutated the retained checkpoint");

    const auto partial = widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "update":{
            "protocolVersion":18,
            "widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,
            "sequence":11,
            "operations":[
                {"kind":"setProperties","targetId":"a","properties":[
                    {"property":"text","value":"Must not publish"}
                ]},
                {"kind":"removeChild","parentId":"root","childId":"absent"}
            ]
        },
        "renderStyles":{}
    })json", error);
    Require(partial.has_value(),
            "Could not parse the deterministic partial-failure batch");
    error.clear();
    Require(!widgetrail::MaterializeWidgetPresentationUpdate(
                *checkpoint, *partial,
                L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", error) &&
                checkpoint->root.children[0].text == L"Before",
            "A later operation failure partially mutated the checkpoint");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "update":{
            "protocolVersion":18,"widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,"sequence":10,"operations":[],"unknown":true
        }
    })json", error),
            "Unknown update fields did not fail closed");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "update":{
            "protocolVersion":18,"widgetInstanceId":"update.sample",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":9,"sequence":10,"operations":[]
        },
        "unknown":true
    })json", error),
            "Unknown presentation payload fields did not fail closed");
}

void VerifySelectControllerInputSerializationAndPopupRaster() {
    const auto withoutSelect =
        widgetrail::testing::SerializeControllerInputRequest({});
    const auto withSelect = widgetrail::testing::SerializeControllerInputRequest(
        L"density.comfortable");
    Require(withoutSelect.find("expectedSelectOptionActionId") ==
                std::string::npos &&
            withSelect.find(
                R"json("expectedSelectOptionActionId":"density.comfortable")json") !=
                std::string::npos &&
            withSelect.find(R"json("focusedElementId":"select.control")json") !=
                std::string::npos,
            "Native controller-input serialization did not preserve exact Select option authority");

    widgetrail::WidgetPresentationImpact unchanged;
    Require(!widgetrail::RequiresCompleteSelectPopupRaster(
                unchanged, true, true),
            "An unchanged Select option collection forced a complete raster");
    widgetrail::WidgetPresentationImpact changed;
    changed.selectOptionsChanged = true;
    Require(!widgetrail::RequiresCompleteSelectPopupRaster(
                changed, false, false) &&
            widgetrail::RequiresCompleteSelectPopupRaster(
                changed, true, false) &&
            widgetrail::RequiresCompleteSelectPopupRaster(
                changed, false, true),
            "Open or previously-open Select option changes did not force one complete raster");

    std::wstring error;
    const auto checkpoint = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.update","activeInputScopeId":"density",
        "initialFocusId":"density","root":{"id":"density","kind":"select",
        "text":"Density","accessibilityValue":"Compact","selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true}],"children":[]}},"renderStyles":{}})json", error);
    Require(checkpoint && error.empty(),
            "Could not parse the Select partial-update checkpoint");
    const auto update = widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "widgetId":"select","update":{"protocolVersion":18,
        "widgetInstanceId":"select.update",
        "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        "baseSequence":1,"sequence":2,"operations":[
            {"kind":"setProperties","targetId":"density","properties":[
                {"property":"selectOptions","value":[
                    {"id":"spacious","label":"Spacious",
                     "actionId":"density.spacious","isSelected":true}]},
                {"property":"accessibilityValue","value":"Spacious"}]}]},
        "renderStyles":{}})json", error);
    Require(update && error.empty(),
            "Could not parse the Select option partial update");
    const auto materialized = widgetrail::MaterializeWidgetPresentationUpdate(
        *checkpoint, *update, L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", error);
    Require(materialized && error.empty() &&
                materialized->impact.selectOptionsChanged &&
                materialized->snapshot.root.selectOptions.size() == 1 &&
                materialized->snapshot.root.selectOptions[0].actionId ==
                    L"density.spacious",
            "Select option partial update did not retain full-raster impact authority");
}

void VerifyEmbeddedMediaSnapshotContract() {
    std::wstring error;
    constexpr std::string_view productionShapedMediaViewportNode = R"json({
        "snapshot": {
            "protocolVersion":41,"sequence":7,
            "widgetInstanceId":"production.media-viewport","activeInputScopeId":"root",
            "quickActions":[],"pinnedLayouts":[],
            "embeddedMedia":{"id":"production-media","accessibleName":"Production media",
                "entryAsset":"media/index.html","aspectRatio":1.7777777778,
                "surface":{"mode":"standard","preferredWidth":640,"preferredHeight":360,
                           "minimumWidth":240,"minimumHeight":180},
                "resources":[{"path":"media/index.html","contentType":"text/html"}],
                "commands":["activate"]},
            "root":{"id":"root","kind":"stack","contextActions":[],"selectOptions":[],
                "styleClasses":[],"shortcuts":[],"children":[
                {"id":"production.title","kind":"text","text":"Before",
                 "contextActions":[],"selectOptions":[],"styleClasses":[],"shortcuts":[],
                 "children":[]},
                {"id":"production.viewport","kind":"mediaViewport",
                 "accessibilityLabel":"Production media","mediaSurfaceId":"production-media",
                 "contextActions":[],"selectOptions":[],"styleClasses":[],"shortcuts":[],
                 "children":[]}
            ]}
        }
    })json";
    const auto productionCheckpoint =
        widgetrail::testing::ParseWidgetSnapshotResponse(
            productionShapedMediaViewportNode, error);
    Require(productionCheckpoint && error.empty() &&
                productionCheckpoint->root.children.size() == 2 &&
                productionCheckpoint->root.children[1].kind == L"mediaViewport" &&
                productionCheckpoint->root.children[1].selectOptions.empty(),
            "ordinary/recovery checkpoint admission rejected the affected production-shaped MediaViewport node defaults");

    error.clear();
    const auto productionUpdate =
        widgetrail::testing::ParseWidgetPresentationUpdateResponse(R"json({
        "widgetId":"production-media","update":{
            "protocolVersion":18,
            "widgetInstanceId":"production.media-viewport",
            "presentationGeneration":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "baseSequence":7,"sequence":8,"operations":[
                {"kind":"setProperties","targetId":"production.title","properties":[
                    {"property":"text","value":"After"}
                ]}
            ]
        },"renderStyles":{}
    })json", error);
    Require(productionUpdate && error.empty(),
            "incremental MediaViewport presentation update could not be parsed");
    const auto productionMaterialization =
        widgetrail::MaterializeWidgetPresentationUpdate(
            *productionCheckpoint, *productionUpdate,
            L"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", error);
    Require(productionMaterialization && error.empty() &&
                productionMaterialization->snapshot.sequence == 8 &&
                productionMaterialization->snapshot.root.children[0].text == L"After" &&
                productionMaterialization->snapshot.root.children[1].kind == L"mediaViewport" &&
                productionMaterialization->snapshot.root.children[1].selectOptions.empty(),
            "incremental presentation-update materialization rejected the affected production-shaped MediaViewport node defaults");

    const auto mutate = [](std::string source, const std::string_view from,
                           const std::string_view to) {
        const auto offset = source.find(from);
        Require(offset != std::string::npos,
            "MediaViewport mutation source was absent");
        source.replace(offset, from.size(), to);
        return source;
    };
    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(
                mutate(std::string{productionShapedMediaViewportNode},
                    R"json("mediaSurfaceId":"production-media",
                 "contextActions":[],"selectOptions":[])json",
                    R"json("mediaSurfaceId":"production-media",
                 "contextActions":[],"selectOptions":[{"id":"invalid","label":"Invalid","actionId":"invalid","isSelected":true}])json"),
                error),
            "MediaViewport admitted a non-empty Select option collection");

    const auto valid = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":27,"sequence":7,
            "widgetInstanceId":"neutral.adapter","activeInputScopeId":"root",
            "surface":{"mode":"standard","preferredWidth":760,"preferredHeight":425,
                       "minimumWidth":320,"minimumHeight":180},
            "embeddedMedia":{"id":"media","accessibleName":"Neutral media",
                "entryAsset":"media/index.html","aspectRatio":1.7777777778,
                "surface":{"mode":"standard","preferredWidth":760,"preferredHeight":425,
                           "minimumWidth":320,"minimumHeight":180},
                "resources":[
                    {"path":"media/index.html","contentType":"text/html"},
                    {"path":"media/tone.wav","contentType":"audio/wav"}],
                "commands":["activate","togglePlayback","back"],
                "allowedFrameOrigins":["https://frames.neutral.invalid"],
                "allowedFrameDomainFamilies":["example.com"],
                "pendingCommand":{"sequence":8,"kind":"setPlaybackRate",
                    "mediaKey":"aurora-video","playbackRate":1.5}},
            "root":{"id":"root","kind":"stack","contextActions":[],"children":[
                {"id":"title","kind":"text","text":"Aurora fixture","contextActions":[],"children":[]},
                {"id":"viewport","kind":"mediaViewport","mediaSurfaceId":"media",
                 "accessibilityLabel":"Neutral media","styleClasses":["media-shell-viewport"],
                 "shortcuts":[],"contextActions":[],"children":[]},
                {"id":"controls","kind":"button","text":"Play",
                 "accessibilityLabel":"Play","actionId":"play","contextActions":[],"children":[]}
            ]}
        }
    })json", error);
    Require(valid && error.empty() && valid->embeddedMedia &&
                valid->embeddedMedia->id == L"media" &&
                valid->embeddedMedia->resources.size() == 2 &&
                valid->embeddedMedia->commands.size() == 3 &&
                valid->embeddedMedia->allowedFrameDomainFamilies ==
                    std::vector<std::wstring>{L"example.com"} &&
                valid->embeddedMedia->pendingCommand &&
                valid->embeddedMedia->pendingCommand->kind == L"setPlaybackRate" &&
                valid->embeddedMedia->pendingCommand->playbackRate == 1.5 &&
                valid->root.children.size() == 3 &&
                valid->root.children[1].kind == L"mediaViewport" &&
                valid->root.children[1].mediaSurfaceId == L"media",
            "valid protocol-v24 MediaViewport snapshot was rejected");

    constexpr std::string_view validJson = R"json({
        "snapshot": {
            "protocolVersion":24,"sequence":7,
            "widgetInstanceId":"cedar.adapter","activeInputScopeId":"root",
            "embeddedMedia":{"id":"cedar-media","accessibleName":"Cedar media",
                "entryAsset":"media/index.html","aspectRatio":1.7777777778,
                "surface":{"mode":"standard","preferredWidth":640,"preferredHeight":360,
                           "minimumWidth":240,"minimumHeight":180},
                "resources":[{"path":"media/index.html","contentType":"text/html"}],
                "commands":["activate"],
                "allowedFrameOrigins":["https://frames.cedar.invalid"]},
            "root":{"id":"root","kind":"stack","children":[
                {"id":"cedar.viewport","kind":"mediaViewport",
                 "mediaSurfaceId":"cedar-media","accessibilityLabel":"Cedar media",
                 "styleClasses":["media-shell-viewport"],"shortcuts":[],"children":[]}
            ]}
        }
    })json";
    for (const auto& [malformed, expected] : {
             std::pair{mutate(
                 mutate(std::string{validJson},
                     R"json("kind":"mediaViewport")json",
                     R"json("kind":"text")json"),
                 R"json("mediaSurfaceId":"cedar-media",)json",
                 R"json("text":"No viewport",)json"),
                 std::wstring_view{L"requires one MediaViewport"}},
             std::pair{mutate(std::string{validJson},
                 R"json("mediaSurfaceId":"cedar-media")json",
                 R"json("mediaSurfaceId":"wrong-media")json"),
                 std::wstring_view{L"does not match"}},
             std::pair{mutate(std::string{validJson},
                 R"json("mediaSurfaceId":"cedar-media",)json",
                 R"json("mediaSurfaceId":"cedar-media","actionId":"escape",)json"),
                 std::wstring_view{L"unsupported properties"}},
             std::pair{mutate(std::string{validJson},
                 R"json("shortcuts":[])json",
                 R"json("shortcuts":[{"button":"a","actionId":"escape","phase":"pressed"}])json"),
                 std::wstring_view{L"shortcuts must be empty"}},
             std::pair{mutate(std::string{validJson},
                 "https://frames.cedar.invalid", "https://frames.cedar.invalid/"),
                 std::wstring_view{L"frame origin declaration is invalid"}},
             std::pair{mutate(std::string{validJson},
                 "https://frames.cedar.invalid", "HTTPS://frames.cedar.invalid"),
                 std::wstring_view{L"frame origin declaration is invalid"}},
             std::pair{mutate(std::string{validJson},
                 "https://frames.cedar.invalid", "https://user@frames.cedar.invalid"),
                 std::wstring_view{L"frame origin declaration is invalid"}},
             std::pair{mutate(std::string{validJson},
                 "https://frames.cedar.invalid", "https://*.cedar.invalid"),
                 std::wstring_view{L"frame origin declaration is invalid"}}}) {
        error.clear();
        Require(!widgetrail::testing::ParseWidgetSnapshotResponse(malformed, error) &&
                    error.find(expected) != std::wstring::npos,
            "invalid MediaViewport binding did not fail closed precisely");
    }

    error.clear();
    const auto unknown = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":22,"sequence":7,
            "widgetInstanceId":"neutral.adapter","activeInputScopeId":"root",
            "embeddedMedia":{"id":"media","accessibleName":"Neutral media",
                "entryAsset":"media/index.html","aspectRatio":1.0,
                "surface":{"mode":"standard","preferredWidth":320,"preferredHeight":180,
                           "minimumWidth":320,"minimumHeight":180},
                "resources":[{"path":"media/index.html","contentType":"text/html"}],
                "commands":[],"navigate":"https://example.invalid"},
            "root":{"id":"root","kind":"stack","children":[]}
        }
    })json", error);
    Require(!unknown && error.find(L"unknown") != std::wstring::npos,
            "unknown embedded media browsing field was admitted");

    constexpr std::string_view retainedHiddenJson = R"json({
        "snapshot": {
            "protocolVersion":29,"sequence":8,
            "widgetInstanceId":"cedar.adapter","activeInputScopeId":"root",
            "embeddedMedia":{"id":"cedar-media","accessibleName":"Cedar media",
                "entryAsset":"media/index.html","retainSessionWhenHidden":true,
                "aspectRatio":1.7777777778,
                "surface":{"mode":"standard","preferredWidth":640,"preferredHeight":360,
                           "minimumWidth":240,"minimumHeight":180},
                "resources":[{"path":"media/index.html","contentType":"text/html"}],
                "commands":["activate"]},
            "root":{"id":"root","kind":"stack","children":[]}
        }
    })json";
    error.clear();
    const auto retainedHidden =
        widgetrail::testing::ParseWidgetSnapshotResponse(retainedHiddenJson, error);
    Require(retainedHidden && retainedHidden->embeddedMedia &&
                retainedHidden->embeddedMedia->retainSessionWhenHidden && error.empty(),
            "valid retained-hidden embedded media snapshot was rejected");
    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(
                mutate(std::string{retainedHiddenJson},
                    R"json("children":[])json",
                    R"json("children":[{"id":"viewport","kind":"mediaViewport","mediaSurfaceId":"cedar-media","accessibilityLabel":"Cedar media","shortcuts":[],"children":[]}])json"),
                error) && error.find(L"cannot publish a MediaViewport") != std::wstring::npos,
            "retained-hidden media with a viewport did not fail closed");
    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(
                mutate(std::string{retainedHiddenJson},
                    R"json("protocolVersion":29)json",
                    R"json("protocolVersion":28)json"), error) &&
                error.find(L"protocol version 29") != std::wstring::npos,
            "retained-hidden media was admitted before protocol v29");

    constexpr std::string_view overlayFullscreenJson = R"json({
        "snapshot": {
            "protocolVersion":30,"sequence":9,
            "widgetInstanceId":"cedar.adapter","activeInputScopeId":"root",
            "embeddedMedia":{"id":"cedar-media","accessibleName":"Cedar media",
                "entryAsset":"media/index.html","overlayFullscreenCapable":true,
                "mediaSeekStepSeconds":5,
                "aspectRatio":1.7777777778,
                "surface":{"mode":"standard","preferredWidth":640,"preferredHeight":360,
                           "minimumWidth":240,"minimumHeight":180},
                "resources":[{"path":"media/index.html","contentType":"text/html"}],
                "commands":["togglePlayback","seekBackward","seekForward","back"]},
            "root":{"id":"root","kind":"stack","children":[
                {"id":"cedar.viewport","kind":"mediaViewport",
                 "mediaSurfaceId":"cedar-media","accessibilityLabel":"Cedar media",
                 "shortcuts":[],"children":[]}
            ]}
        }
    })json";
    error.clear();
    const auto overlayFullscreen =
        widgetrail::testing::ParseWidgetSnapshotResponse(overlayFullscreenJson, error);
    Require(overlayFullscreen && overlayFullscreen->embeddedMedia &&
                overlayFullscreen->embeddedMedia->overlayFullscreenCapable &&
                overlayFullscreen->embeddedMedia->mediaSeekStepSeconds == 5.0 &&
                error.empty(),
            "valid protocol-v30 overlay fullscreen media snapshot was rejected");
    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(
                mutate(std::string{overlayFullscreenJson},
                    R"json("protocolVersion":30)json",
                    R"json("protocolVersion":29)json"), error) &&
                error.find(L"protocol version 30") != std::wstring::npos,
            "overlay fullscreen media was admitted before protocol v30");
}

void VerifyEmbeddedMediaBundleBoundary() {
    constexpr std::string_view valid = R"json({
        "widgetId":"aurora-widget","instanceId":"aurora-instance",
        "runtimeGeneration":"runtime-1","presentationGeneration":"presentation-1",
        "sequence":7,"surfaceId":"media","entryAsset":"media/index.html",
        "retainSessionWhenHidden":true,
        "overlayFullscreenCapable":true,"mediaSeekStepSeconds":5,
        "surface":{"mode":"standard","preferredWidth":760,"preferredHeight":425,
                   "minimumWidth":320,"minimumHeight":180},
        "aspectRatio":1.7777777778,"accessibleName":"Aurora media",
        "commands":["activate","togglePlayback"],
        "allowedFrameOrigins":["https://frames.aurora.invalid"],
        "allowedFrameDomainFamilies":["example.com"],
        "pendingCommand":{"sequence":8,"kind":"setMuted",
            "mediaKey":"aurora-video","muted":true},
        "resources":[{"path":"media/index.html","contentType":"text/html",
            "sha256":"0000000000000000000000000000000000000000000000000000000000000000",
            "contentBase64":"QQ=="}]
    })json";
    const auto parse = [](const std::string_view json) {
        std::wstring error;
        return widgetrail::testing::ParseEmbeddedMediaBundleResponse(
            json, L"aurora-widget", L"aurora-instance", L"runtime-1",
            L"presentation-1", 7, L"media", error);
    };
    const auto replace = [](std::string source, const std::string_view from,
                            const std::string_view to) {
        const auto offset = source.find(from);
        Require(offset != std::string::npos, "embedded media mutation source was absent");
        source.replace(offset, from.size(), to);
        return source;
    };
    const auto parsed = parse(valid);
    Require(parsed && parsed->surface.retainSessionWhenHidden &&
                parsed->surface.overlayFullscreenCapable &&
                parsed->surface.mediaSeekStepSeconds == 5.0 &&
                parsed->surface.allowedFrameDomainFamilies ==
                std::vector<std::wstring>{L"example.com"} &&
                parsed->surface.pendingCommand &&
                parsed->surface.pendingCommand->kind == L"setMuted" &&
                parsed->surface.pendingCommand->muted == true,
            "valid native embedded media domain family bundle was rejected");
    const auto rateCommand = parse(replace(
        replace(std::string{valid}, "\"kind\":\"setMuted\"",
                "\"kind\":\"setPlaybackRate\""),
        "\"muted\":true", "\"playbackRate\":1.5"));
    Require(rateCommand && rateCommand->surface.pendingCommand &&
                rateCommand->surface.pendingCommand->playbackRate == 1.5,
            "valid sparse SetPlaybackRate bundle command was rejected");
    const auto loopCommand = parse(replace(
        replace(std::string{valid}, "\"kind\":\"setMuted\"",
                "\"kind\":\"setLoop\""),
        "\"muted\":true", "\"loop\":false"));
    Require(loopCommand && loopCommand->surface.pendingCommand &&
                loopCommand->surface.pendingCommand->loop == false,
            "valid sparse SetLoop bundle command was rejected");
    for (const auto& malformed : {
             replace(std::string{valid}, "\"mode\":\"standard\"",
                     "\"mode\":\"browser\""),
             replace(std::string{valid}, "\"example.com\"", "\"com\""),
             replace(std::string{valid}, "\"example.com\"", "\"deep.example.com\""),
             replace(std::string{valid}, "\"example.com\"", "\"Example.com\""),
             replace(std::string{valid}, "\"aspectRatio\":1.7777777778",
                     "\"aspectRatio\":20"),
             replace(std::string{valid}, "\"accessibleName\":\"Aurora media\"",
                     "\"accessibleName\":\"\""),
             replace(std::string{valid}, "\"commands\":[\"activate\",\"togglePlayback\"]",
                     "\"commands\":[\"activate\",\"activate\"]"),
             replace(std::string{valid}, "\"commands\":[\"activate\",\"togglePlayback\"]",
                     "\"commands\":[\"browse\"]"),
             replace(std::string{valid}, "\"muted\":true", "\"loop\":true"),
             replace(std::string{valid}, "\"muted\":true",
                     "\"muted\":true,\"script\":\"bad\""),
             replace(std::string{valid}, "https://frames.aurora.invalid",
                     "https://frames.aurora.invalid/"),
             replace(std::string{valid}, "https://frames.aurora.invalid",
                     "HTTPS://frames.aurora.invalid"),
             replace(std::string{valid}, "https://frames.aurora.invalid",
                     "https://user@frames.aurora.invalid"),
             replace(std::string{valid}, "https://frames.aurora.invalid",
                     "https://*.aurora.invalid"),
             replace(std::string{valid}, "media/index.html", "../secret.html"),
             replace(std::string{valid}, "\"contentType\":\"text/html\"",
                     "\"contentType\":\"text/html\\r\\nX-Test: injected\""),
             replace(std::string{valid},
                     "0000000000000000000000000000000000000000000000000000000000000000",
                     "abcd"),
             replace(std::string{valid}, "\"minimumWidth\":320",
                     "\"minimumWidth\":800"),
             replace(std::string{valid}, "\"minimumHeight\":180}",
                     "\"minimumHeight\":180,\"unknown\":true}")}) {
        Require(!parse(malformed),
                "malformed native embedded media bundle crossed the trust boundary");
    }

    std::string oversized((262'145 / 3) * 4, 'A');
    oversized += "AAA=";
    Require(!parse(replace(std::string{valid}, "QQ==", oversized)),
            "oversized decoded embedded media resource was admitted");

    const auto encodedZeros = [](const std::size_t bytes) {
        std::string encoded((bytes / 3) * 4, 'A');
        if (bytes % 3 == 1) encoded += "AA==";
        if (bytes % 3 == 2) encoded += "AAA=";
        return encoded;
    };
    const auto resource = [](const std::string_view path,
                             const std::string_view type,
                             const std::string_view content) {
        return "{\"path\":\"" + std::string{path} +
            "\",\"contentType\":\"" + std::string{type} +
            "\",\"sha256\":\"" + std::string(64, '0') +
            "\",\"contentBase64\":\"" + std::string{content} + "\"}";
    };
    std::string aggregate{valid};
    const auto resourcesStart = aggregate.find("\"resources\":[");
    const auto resourcesEnd = aggregate.rfind(']');
    Require(resourcesStart != std::string::npos && resourcesEnd != std::string::npos,
            "embedded media aggregate fixture boundaries were absent");
    const auto exactLimit = encodedZeros(262'144);
    const auto excessiveResources =
        "\"resources\":[" + resource("media/index.html", "text/html", exactLimit) +
        "," + resource("media/second.wav", "audio/wav", exactLimit) +
        "," + resource("media/third.wav", "audio/wav", "QQ==") + "]";
    aggregate.replace(
        resourcesStart, resourcesEnd + 1 - resourcesStart, excessiveResources);
    Require(!parse(aggregate),
            "embedded media aggregate decoded-size overflow was admitted");

    std::cout << "WidgetBridge embedded media native boundary cases passed=13\n";
}

void VerifyVirtualCollectionProtocol() {
    std::wstring error;
    const auto snapshot = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,
            "sequence":7,
            "widgetInstanceId":"virtual.sample",
            "activeInputScopeId":"virtual.list",
            "initialFocusId":"virtual.item.5000",
            "root":{"id":"virtual.list","kind":"scroll","scrollAxis":"vertical",
                "scrollNearStartActionId":"virtual.before",
                "scrollNearEndActionId":"virtual.after",
                "scrollPaginationThreshold":2,
                "collectionAnchorKey":"key.5000",
                "virtualCollectionWindow":{
                    "requestGeneration":4,"change":"append",
                    "firstItemIndex":5000,"totalItemCount":10000,
                    "hasBefore":true,"hasAfter":true,
                    "estimatedItemExtent":56
                },
                "children":[
                    {"id":"virtual.item.5000","kind":"button","text":"A",
                     "actionId":"select","collectionItemKey":"key.5000","children":[]},
                    {"id":"virtual.item.5001","kind":"button","text":"B",
                     "actionId":"select","collectionItemKey":"key.5001","children":[]}
                ]}
        },
        "renderStyles":{}
    })json", error);
    Require(snapshot && error.empty() && snapshot->protocolVersion == 19 &&
                snapshot->root.virtualCollectionWindow &&
                snapshot->root.virtualCollectionWindow->requestGeneration == 4 &&
                snapshot->root.virtualCollectionWindow->firstItemIndex == 5000 &&
                snapshot->root.virtualCollectionWindow->totalItemCount == 10'000,
            "Protocol-v19 virtual collection window did not cross the native parser");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,"sequence":8,
            "widgetInstanceId":"virtual.sample","activeInputScopeId":"virtual.list",
            "root":{"id":"virtual.list","kind":"scroll","scrollAxis":"vertical",
                "scrollNearEndActionId":"virtual.after","scrollPaginationThreshold":2,
                "collectionAnchorKey":"key.9999",
                "virtualCollectionWindow":{
                    "requestGeneration":5,"change":"replace",
                    "firstItemIndex":9999,"totalItemCount":10000,
                    "hasBefore":false,"hasAfter":true,"estimatedItemExtent":56
                },
                "children":[
                    {"id":"virtual.item.9999","kind":"button","text":"A",
                     "actionId":"select","collectionItemKey":"key.9999","children":[]},
                    {"id":"virtual.item.10000","kind":"button","text":"B",
                     "actionId":"select","collectionItemKey":"key.10000","children":[]}
                ]}
        },"renderStyles":{}
    })json", error),
            "Out-of-range virtual window was admitted by the native parser");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,"sequence":9,
            "widgetInstanceId":"virtual.sample","activeInputScopeId":"virtual.list",
            "root":{"id":"virtual.list","kind":"scroll","scrollAxis":"vertical",
                "scrollNearStartActionId":"virtual.before","scrollPaginationThreshold":2,
                "collectionAnchorKey":"key.1000000",
                "virtualCollectionWindow":{
                    "requestGeneration":6,"change":"replace",
                    "firstItemIndex":1000000,"totalItemCount":null,
                    "hasBefore":true,"hasAfter":false,"estimatedItemExtent":1
                },
                "children":[
                    {"id":"virtual.item.1000000","kind":"button","text":"A",
                     "actionId":"select","collectionItemKey":"key.1000000","children":[]}
                ]}
        },"renderStyles":{}
    })json", error),
            "Unknown-total virtual window escaped the bounded logical item domain");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,"sequence":10,
            "widgetInstanceId":"virtual.sample","activeInputScopeId":"virtual.list",
            "root":{"id":"virtual.list","kind":"scroll","scrollAxis":"vertical",
                "scrollNearEndActionId":"virtual.after","scrollPaginationThreshold":2,
                "collectionAnchorKey":"key.0",
                "virtualCollectionWindow":{
                    "requestGeneration":9007199254740992,"change":"replace",
                    "firstItemIndex":0,"totalItemCount":100,
                    "hasBefore":false,"hasAfter":true,"estimatedItemExtent":56
                },
                "children":[
                    {"id":"virtual.item.0","kind":"button","text":"A",
                     "actionId":"select","collectionItemKey":"key.0","children":[]}
                ]}
        },"renderStyles":{}
    })json", error),
            "Native admission exceeded the managed JSON-safe generation bound");

    error.clear();
    Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":19,"sequence":11,
            "widgetInstanceId":"virtual.sample","activeInputScopeId":"virtual.list",
            "root":{"id":"virtual.list","kind":"scroll","scrollAxis":"vertical",
                "scrollNearStartActionId":"virtual.before","scrollPaginationThreshold":2,
                "collectionAnchorKey":"key.unknown",
                "virtualCollectionWindow":{
                    "requestGeneration":7,"change":"prepend",
                    "firstItemIndex":null,"totalItemCount":null,
                    "hasBefore":true,"hasAfter":false,"estimatedItemExtent":56
                },
                "children":[
                    {"id":"virtual.item.unknown","kind":"button","text":"A",
                     "actionId":"select","collectionItemKey":"key.unknown","children":[]}
                ]}
        },"renderStyles":{}
    })json", error),
            "Native admission accepted an unverifiable unknown-position direction");
}

} // namespace

int main() {
    VerifyWidgetBridgePipeReadinessContract();
    const auto nowPlayingManifest = std::filesystem::path{__FILE__}.parent_path()
        .parent_path() / L"FirstPartyWidgets" / L"MediaSessionsWidget" /
        L"manifest.json";
    std::ifstream manifestStream(nowPlayingManifest, std::ios::binary);
    CHECK(manifestStream.good());
    std::ostringstream manifestPayload;
    manifestPayload << manifestStream.rdbuf();
    const auto manifestText = manifestPayload.str();
    CHECK(manifestText.find(
              "\"id\": \"widgetrail.firstparty.media-sessions\"") !=
          std::string::npos);
    CHECK(manifestText.find("\"name\": \"Now Playing\"") != std::string::npos);
    CHECK(manifestText.find("\"pinningSupported\": true") != std::string::npos);

    VerifyFrameSafeCancellationRecovery();
    VerifyAtomicPresentationUpdateMaterialization();
    VerifySelectControllerInputSerializationAndPopupRaster();
    VerifyEmbeddedMediaSnapshotContract();
    VerifyEmbeddedMediaBundleBoundary();
    VerifyVirtualCollectionProtocol();
    std::vector<wchar_t> secret(14);
    for (std::size_t index = 0; index < secret.size(); ++index)
        secret[index] = static_cast<wchar_t>(L'!' + index);
    auto protectedFrame = widgetrail::ProtectedWifiSecretFrame::Create(secret);
    CHECK(protectedFrame && protectedFrame->bytes().size() == secret.size());
    CHECK(std::equal(
        protectedFrame->bytes().begin(),
        protectedFrame->bytes().end(),
        secret.begin(),
        [](const unsigned char encoded, const wchar_t source) {
            return encoded == static_cast<unsigned char>(source);
        }));
    protectedFrame->clear();
    CHECK(protectedFrame->bytes().empty());
    secret[0] = L'\n';
    CHECK(!widgetrail::ProtectedWifiSecretFrame::Create(secret));
    std::wstring error;
    const auto valid = widgetrail::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.music",
            "name": "Music controls",
            "instanceId": "music.default",
            "runtimeGeneration": "runtime-1",
            "presentationGeneration": "presentation-1",
            "icon": "music",
            "pinningSupported": true,
            "protectedWifiPromptSupported": true,
            "quickActions": [{
                "id": "refresh",
                "label": "Refresh",
                "actionId": "refresh-now",
                "sourceElementId": "toolbar.refresh",
                "controllerButton": "x"
            }, {
                "id": "open",
                "label": "Open",
                "actionId": "open",
                "sourceElementId": "toolbar.open",
                "controllerButton": null
            }]
        }]
    })json", error);
    CHECK(valid && error.empty());
    CHECK(valid->size() == 1);
    CHECK((*valid)[0].id == L"dev.test.music");
    CHECK((*valid)[0].name == L"Music controls");
    CHECK((*valid)[0].instanceId == L"music.default");
    CHECK((*valid)[0].runtimeGeneration == L"runtime-1");
    CHECK((*valid)[0].presentationGeneration == L"presentation-1");
    CHECK((*valid)[0].icon == L"music");
    CHECK((*valid)[0].pinningSupported);
    CHECK((*valid)[0].protectedWifiPromptSupported);
    CHECK((*valid)[0].quickActions.size() == 2);
    CHECK((*valid)[0].quickActions[0].controllerButton == L"x");
    CHECK(!(*valid)[0].quickActions[1].controllerButton);
    CHECK(widgetrail::FindDescriptorQuickAction((*valid)[0], L"refresh") ==
           &(*valid)[0].quickActions[0]);
    CHECK(widgetrail::FindDescriptorQuickAction((*valid)[0], L"missing") == nullptr);

    error.clear();
    const auto defaultPinning = widgetrail::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.default",
            "name": "Default",
            "instanceId": "default.instance",
            "runtimeGeneration": "runtime-default",
            "presentationGeneration": "presentation-default",
            "quickActions": []
        }]
    })json", error);
    CHECK(defaultPinning && !(*defaultPinning)[0].pinningSupported);
    CHECK(defaultPinning && !(*defaultPinning)[0].protectedWifiPromptSupported);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.invalid-protected",
            "name": "Invalid protected",
            "instanceId": "invalid-protected.instance",
            "runtimeGeneration": "runtime-invalid-protected",
            "presentationGeneration": "presentation-invalid-protected",
            "protectedWifiPromptSupported": "yes",
            "quickActions": []
        }]
    })json", error));
    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.bad",
            "name": "Bad",
            "instanceId": "bad.instance",
            "runtimeGeneration": "runtime-bad",
            "presentationGeneration": "presentation-bad",
            "pinningSupported": "yes",
            "quickActions": []
        }]
    })json", error));

    error.clear();
    const auto styledSnapshot = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion": 34,
            "sequence": 9,
            "widgetInstanceId": "music.runtime.v1",
            "activeInputScopeId": "root",
            "initialFocusId": "play",
            "root": {
                "id": "root",
                "kind": "stack",
                "contextActions": [],
                "children": [
                    {"id":"play","kind":"button","text":"Play","actionId":"play","focusPersistenceId":"transport.play","contextActions":[]},
                    {"id":"details","kind":"stack","children":[]},
                    {"id":"loading","kind":"loadingIndicator","accessibilityLabel":"Loading music","indicatorSize":"compact","visibleWhen":"compactOnly"},
                    {
                        "id":"album","kind":"actionSurface","actionId":"open-album",
                        "accessibilityLabel":"Open album","actionSurfaceOrientation":"horizontal",
                        "contextActions":[
                            {"actionId":"album.queue","label":"Add to queue","style":"default","isDisabled":false,"isBusy":false},
                            {"actionId":"album.remove","label":"Remove","style":"danger","isDisabled":true,"isBusy":false}
                        ],
                        "children":[
                            {"id":"album.art","kind":"image","imageSource":"https://example.test/album.png","imageFit":"cover"},
                            {"id":"album.copy","kind":"stack","children":[
                                {"id":"album.title","kind":"text","text":"Album title"},
                                {"id":"album.artist","kind":"text","text":"Artist"}
                            ]}
                        ]
                    },
                    {
                        "id":"library-grid","kind":"grid",
                        "gridMinimumColumnWidth":180,"gridMaximumColumns":3,
                        "children":[
                            {"id":"grid-one","kind":"button","text":"One","actionId":"one"},
                            {"id":"grid-two","kind":"button","text":"Two","actionId":"two"}
                        ]
                    },
                    {
                        "id":"cursor-list","kind":"scroll","scrollAxis":"vertical",
                        "collectionAnchorKey":"game.2",
                        "children":[
                            {"id":"game-two","kind":"button","text":"Game","actionId":"launch",
                             "collectionItemKey":"game.2","artworkHandle":"library.art.2","imageFit":"contain"}
                        ]
                    },
                    {
                        "id":"search","kind":"textEntry","text":"Halo",
                        "actionId":"search.commit","accessibilityLabel":"Search installed games",
                        "accessibilityValue":"Halo","textEntryValue":"Halo",
                        "textEntryPlaceholder":"Search installed games",
                        "textEntryMaximumLength":96
                    }
                ]
            }
        },
        "renderStyles": {
            "play": {
                "base": {"opacity":{"kind":"number","text":"0.5","number":0.5,"unit":null}},
                "focused": {"scale":{"kind":"number","text":"1.05","number":1.05,"unit":null}},
                "pressed": {"scale":{"kind":"number","text":"0.97","number":0.97,"unit":null}}
            }
        }
    })json", error);
    CHECK(styledSnapshot && error.empty());
    CHECK(styledSnapshot->root.children.size() == 7);
    const auto& styledButton = styledSnapshot->root.children.front();
    (void)styledButton;
    CHECK(styledButton.baseStyle.at(L"opacity").number == 0.5);
    CHECK(styledButton.focusedStyle.at(L"scale").number == 1.05);
    CHECK(styledButton.pressedStyle.at(L"scale").number == 0.97);
    const auto& loadingIndicator = styledSnapshot->root.children[2];
    CHECK(styledSnapshot->root.children[0].focusPersistenceId == L"transport.play");
    (void)loadingIndicator;
    CHECK(loadingIndicator.kind == L"loadingIndicator");
    CHECK(loadingIndicator.accessibilityLabel == L"Loading music");
    CHECK(loadingIndicator.indicatorSize == L"compact");
    CHECK(loadingIndicator.visibleWhen == L"compactOnly");
    const auto& actionSurface = styledSnapshot->root.children[3];
    (void)actionSurface;
    CHECK(actionSurface.kind == L"actionSurface");
    CHECK(actionSurface.actionId == L"open-album");
    CHECK(actionSurface.accessibilityLabel == L"Open album");
    CHECK(actionSurface.actionSurfaceOrientation == L"horizontal");
    CHECK(actionSurface.contextActions.size() == 2);
    CHECK(actionSurface.contextActions[0].actionId == L"album.queue" &&
          actionSurface.contextActions[0].label == L"Add to queue" &&
          actionSurface.contextActions[0].style == L"default" &&
          !actionSurface.contextActions[0].isDisabled);
    CHECK(actionSurface.contextActions[1].actionId == L"album.remove" &&
          actionSurface.contextActions[1].style == L"danger" &&
          actionSurface.contextActions[1].isDisabled);
    CHECK(actionSurface.children.size() == 2);
    CHECK(actionSurface.children[0].kind == L"image");
    CHECK(actionSurface.children[0].imageFit == L"cover");
    CHECK(actionSurface.children[1].kind == L"stack");
    CHECK(actionSurface.children[1].children.size() == 2);
    CHECK(actionSurface.children[1].children[0].text == L"Album title");
    error.clear();
    const auto posterSnapshot = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":37,"sequence":1,
            "widgetInstanceId":"poster.instance",
            "activeInputScopeId":"poster","initialFocusId":"poster",
            "root":{"id":"poster","kind":"actionSurface","actionId":"poster.open",
                "accessibilityLabel":"Open complete poster",
                "actionSurfaceOrientation":"vertical",
                "actionSurfacePresentation":"poster",
                "children":[
                    {"id":"poster.artwork","kind":"image",
                     "imageSource":"https://example.test/poster.jpg","imageFit":"cover",
                     "accessibilityLabel":"Poster artwork","children":[]},
                    {"id":"poster.scrim","kind":"stack","children":[
                        {"id":"poster.title","kind":"text","text":"Poster title","children":[]}
                    ]}
                ]}
        }
    })json", error);
    CHECK(posterSnapshot && error.empty());
    CHECK(posterSnapshot->root.actionSurfacePresentation == L"poster");
    CHECK(posterSnapshot->root.children.size() == 2U &&
          posterSnapshot->root.children.front().imageFit == L"cover");

    error.clear();
    const auto backgroundSnapshot = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":38,"sequence":1,
            "widgetInstanceId":"background.instance",
            "activeInputScopeId":"background.content","initialFocusId":"background.open",
            "root":{"id":"background","kind":"backgroundSurface",
                "artworkHandle":"gallery.background","imageFit":"cover",
                "styleClasses":["wrail-background-surface"],
                "children":[{"id":"background.content","kind":"stack","children":[
                    {"id":"background.open","kind":"button","text":"Open",
                     "actionId":"open","children":[]}
                ]}]}
        }
    })json", error);
    CHECK(backgroundSnapshot && error.empty());
    CHECK(backgroundSnapshot->root.kind == L"backgroundSurface" &&
          backgroundSnapshot->root.artworkHandle == L"gallery.background" &&
          backgroundSnapshot->root.imageFit == L"cover" &&
          backgroundSnapshot->root.children.size() == 1U);

    error.clear();
    const auto focusPresentationSnapshot =
        widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":40,"sequence":1,
            "widgetInstanceId":"focus-presentation.instance",
            "activeInputScopeId":"focus-presentation.content",
            "initialFocusId":"focus-presentation.first",
            "root":{"id":"focus-presentation.surface","kind":"focusPresentationSurface",
                "defaultFocusPresentation":{"id":"focus-presentation.default","kind":"text",
                    "text":"Choose an item","children":[]},
                "children":[{"id":"focus-presentation.content","kind":"row",
                    "inputScopeId":"focus-presentation.content","children":[
                        {"id":"focus-presentation.first","kind":"button","text":"First",
                         "actionId":"first","focusPresentation":{
                            "id":"focus-presentation.first.fragment","kind":"stack","children":[
                                {"id":"focus-presentation.first.text","kind":"text",
                                 "text":"First details","children":[]}
                            ]},"children":[]},
                        {"id":"focus-presentation.second","kind":"button","text":"Second",
                         "actionId":"second","children":[]}
                    ]}]
            }
        }
    })json", error);
    CHECK(focusPresentationSnapshot && error.empty());
    CHECK(focusPresentationSnapshot->root.kind == L"focusPresentationSurface" &&
          focusPresentationSnapshot->root.defaultFocusPresentation.size() == 1U &&
          focusPresentationSnapshot->root.children.size() == 1U &&
          focusPresentationSnapshot->root.children.front().children.front()
              .focusPresentation.size() == 1U);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":39,"sequence":1,
            "widgetInstanceId":"focus-presentation.legacy",
            "activeInputScopeId":"focus-presentation.content",
            "initialFocusId":"focus-presentation.first",
            "root":{"id":"focus-presentation.surface","kind":"focusPresentationSurface",
                "defaultFocusPresentation":{"id":"focus-presentation.default","kind":"text",
                    "text":"Choose an item","children":[]},
                "children":[{"id":"focus-presentation.content","kind":"stack","children":[
                    {"id":"focus-presentation.first","kind":"button","text":"First",
                     "actionId":"first","children":[]}
                ]}]
            }
        }
    })json", error) && !error.empty());

    error.clear();
    const auto selectSnapshot = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":41,"sequence":13,
            "widgetInstanceId":"select.valid","activeInputScopeId":"density",
            "initialFocusId":"density",
            "root":{"id":"density","kind":"select","text":"Density: Compact",
                "accessibilityValue":"Compact","selectOptions":[
                    {"id":"compact","label":"Compact","actionId":"density.compact",
                     "isSelected":true,"glyph":"check"},
                    {"id":"wide","label":"Wide","actionId":"density.wide",
                     "accessibilityLabel":"Wide layout"}
                ],"children":[]}
        }
    })json", error);
    CHECK(selectSnapshot && error.empty() && selectSnapshot->root.isSelect &&
          selectSnapshot->root.accessibilityLabel.empty() &&
          selectSnapshot->root.selectOptions.size() == 2);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":40,"sequence":1,
        "widgetInstanceId":"select.old","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","text":"Density: Compact",
        "accessibilityValue":"Compact","selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true}],"children":[]}}
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.whitespace","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","text":"   ",
        "accessibilityLabel":"Density","accessibilityValue":"Compact","selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true}],"children":[]}}
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.whitespace-option","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","text":"Density: Compact",
        "accessibilityValue":"  ","selectOptions":[
            {"id":"compact","label":"  ","actionId":"density.compact",
             "isSelected":true,"accessibilityLabel":"  "}],"children":[]}}
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.unnamed","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","accessibilityLabel":"Density",
        "accessibilityValue":"Compact","selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true}],"children":[]}}
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.bad-option","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","text":"Density: Compact",
        "accessibilityValue":"Compact","selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true,"glyph":"not-a-glyph",
             "accessibilityLabel":"Bad\nlabel"}],"children":[]}}
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"select.root-selected","activeInputScopeId":"density",
        "root":{"id":"density","kind":"select","text":"Density: Compact",
        "accessibilityValue":"Compact","isSelected":false,"selectOptions":[
            {"id":"compact","label":"Compact","actionId":"density.compact",
             "isSelected":true}],"children":[]}}
    })json", error));

    error.clear();
    const auto ordinaryEmptyOptions =
        widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot":{"protocolVersion":41,"sequence":1,
        "widgetInstanceId":"ordinary.empty-options","activeInputScopeId":"root",
        "root":{"id":"root","kind":"stack","selectOptions":[],"children":[]}}
    })json", error);
    CHECK(ordinaryEmptyOptions && error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":40,"sequence":1,
            "widgetInstanceId":"focus-presentation.interactive",
            "activeInputScopeId":"focus-presentation.content",
            "initialFocusId":"focus-presentation.first",
            "root":{"id":"focus-presentation.surface","kind":"focusPresentationSurface",
                "defaultFocusPresentation":{"id":"focus-presentation.unsafe","kind":"button",
                    "text":"Unsafe","actionId":"unsafe","children":[]},
                "children":[{"id":"focus-presentation.content","kind":"stack","children":[
                    {"id":"focus-presentation.first","kind":"button","text":"First",
                     "actionId":"first","children":[]}
                ]}]
            }
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":37,"sequence":1,
            "widgetInstanceId":"background.legacy",
            "activeInputScopeId":"background.content","initialFocusId":"background.open",
            "root":{"id":"background","kind":"backgroundSurface","children":[
                {"id":"background.content","kind":"stack","children":[
                    {"id":"background.open","kind":"button","text":"Open",
                     "actionId":"open","children":[]}
                ]}
            ]}
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":36,"sequence":1,
            "widgetInstanceId":"poster.legacy",
            "activeInputScopeId":"poster","initialFocusId":"poster",
            "root":{"id":"poster","kind":"actionSurface","actionId":"poster.open",
                "accessibilityLabel":"Poster","actionSurfaceOrientation":"vertical",
                "actionSurfacePresentation":"poster",
                "children":[{"id":"poster.scrim","kind":"stack","children":[]}]}
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":37,"sequence":1,
            "widgetInstanceId":"poster.contain",
            "activeInputScopeId":"poster","initialFocusId":"poster",
            "root":{"id":"poster","kind":"actionSurface","actionId":"poster.open",
                "accessibilityLabel":"Poster","actionSurfaceOrientation":"vertical",
                "actionSurfacePresentation":"poster",
                "children":[
                    {"id":"poster.artwork","kind":"image",
                     "imageSource":"https://example.test/poster.jpg","imageFit":"contain","children":[]},
                    {"id":"poster.scrim","kind":"stack","children":[]}
                ]}
        }
    })json", error) && !error.empty());
    const auto& cursorList = styledSnapshot->root.children[5];
    (void)cursorList;
    CHECK(cursorList.collectionAnchorKey == L"game.2");
    CHECK(cursorList.children[0].collectionItemKey == L"game.2");
    CHECK(cursorList.children[0].artworkHandle == L"library.art.2");
    CHECK(cursorList.children[0].imageSource.empty());
    const auto& grid = styledSnapshot->root.children[4];
    (void)grid;
    CHECK(grid.kind == L"grid");
    CHECK(grid.gridMinimumColumnWidth == 180.0);
    CHECK(grid.gridMaximumColumns == 3U);
    CHECK(grid.children.size() == 2);
    CHECK(grid.children[1].id == L"grid-two");
    const auto& textEntry = styledSnapshot->root.children[6];
    (void)textEntry;
    CHECK(textEntry.kind == L"button");
    CHECK(textEntry.isTextEntry);
    CHECK(textEntry.textEntryValue == L"Halo");
    CHECK(textEntry.textEntryPlaceholder == L"Search installed games");
    CHECK(textEntry.textEntryMaximumLength == 96U);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":34,"sequence":10,
            "widgetInstanceId":"context.invalid-kind",
            "activeInputScopeId":"root",
            "root":{"id":"root","kind":"stack","contextActions":[
                {"actionId":"root.open","label":"Open"}
            ],"children":[]}
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":34,"sequence":11,
            "widgetInstanceId":"context.malformed",
            "activeInputScopeId":"surface",
            "initialFocusId":"surface",
            "root":{"id":"surface","kind":"actionSurface","actionId":"open",
                "accessibilityLabel":"Open","contextActions":[
                    {"actionId":"surface.more","label":"More","style":"unknown"}
                ],"children":[]}
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":34,"sequence":12,
            "widgetInstanceId":"context.wrong-type",
            "activeInputScopeId":"root",
            "root":{"id":"root","kind":"stack","contextActions":{},"children":[]}
        }
    })json", error) && !error.empty());

    error.clear();
    const auto rememberedGroup = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":33,"sequence":1,
            "widgetInstanceId":"focus-groups.instance",
            "activeInputScopeId":"root","initialFocusId":"entry",
            "root":{"id":"root","kind":"stack","inputScopeId":"root","children":[
                {"id":"entry","kind":"button","text":"Entry","actionId":"entry",
                 "focus":{"down":"controls"}},
                {"id":"controls","kind":"row","initialChildFocusId":"controls.play","children":[
                    {"id":"controls.play","kind":"button","text":"Play","actionId":"play"},
                    {"id":"controls.seek","kind":"slider","accessibilityLabel":"Seek",
                     "value":10,"minimum":0,"maximum":100,"step":5,
                     "valueChangedActionId":"seek.changed"}
                ]}
            ]}
        }
    })json", error);
    CHECK(rememberedGroup && error.empty());
    CHECK(rememberedGroup->protocolVersion == 33 &&
          rememberedGroup->root.children[0].focusDown == L"controls" &&
          rememberedGroup->root.children[1].initialChildFocusId == L"controls.play");

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":33,"sequence":1,
            "widgetInstanceId":"focus-groups.invalid-kind",
            "activeInputScopeId":"root","initialFocusId":"entry",
            "root":{"id":"root","kind":"stack","children":[
                {"id":"entry","kind":"button","text":"Entry","actionId":"entry",
                 "initialChildFocusId":"child","children":[
                    {"id":"child","kind":"button","text":"Child","actionId":"child"}
                 ]}
            ]}
        }
    })json", error));

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":33,"sequence":1,
            "widgetInstanceId":"focus-groups.cross-scope",
            "activeInputScopeId":"root","initialFocusId":"entry",
            "root":{"id":"root","kind":"stack","children":[
                {"id":"entry","kind":"button","text":"Entry","actionId":"entry"},
                {"id":"controls","kind":"row","initialChildFocusId":"dialog.play","children":[
                    {"id":"dialog","kind":"stack","inputScopeId":"dialog","children":[
                        {"id":"dialog.play","kind":"button","text":"Play","actionId":"play"}
                    ]}
                ]}
            ]}
        }
    })json", error));

    error.clear();
    const auto invalidGrid = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "sequence": 1,
            "widgetInstanceId": "grid.invalid",
            "activeInputScopeId": "grid",
            "root": {
                "id": "grid", "kind": "grid",
                "gridMinimumColumnWidth": "wide",
                "children": []
            }
        }
    })json", error);
    CHECK(!invalidGrid && !error.empty());

    error.clear();
    const auto invalidTextEntry = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "sequence": 1,
            "widgetInstanceId": "text.invalid",
            "activeInputScopeId": "search",
            "root": {
                "id":"search","kind":"textEntry","text":"Search",
                "actionId":"search.commit","textEntryValue":"bad\nvalue",
                "textEntryPlaceholder":"Search","textEntryMaximumLength":96
            }
        }
    })json", error);
    CHECK(!invalidTextEntry && !error.empty());

    error.clear();
    const auto sensitiveTextEntry = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion": 28,
            "sequence": 2,
            "widgetInstanceId": "provider-neutral.secret",
            "activeInputScopeId": "secret.root",
            "root": {
                "id":"secret.root","kind":"stack","children":[{
                    "id":"secret.entry","kind":"textEntry","text":"Enter access key",
                    "actionId":"secret.commit","accessibilityLabel":"Enter access key",
                    "textEntryValue":"","textEntryPlaceholder":"Enter access key",
                    "textEntryMaximumLength":64,"textEntryInputKind":"sensitive"
                }]
            }
        }
    })json", error);
    CHECK(sensitiveTextEntry && error.empty());
    if (sensitiveTextEntry) {
        const auto& sensitive = sensitiveTextEntry->root.children.front();
        CHECK(sensitive.isTextEntry);
        CHECK(sensitive.textEntryInputKind == L"sensitive");
        CHECK(sensitive.textEntryValue.empty());
        CHECK(sensitive.accessibilityValue.empty());
    }

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion": 28,"sequence":3,
            "widgetInstanceId":"provider-neutral.secret",
            "activeInputScopeId":"secret.entry",
            "root":{"id":"secret.entry","kind":"textEntry",
                "actionId":"secret.commit","textEntryValue":"must-not-cross",
                "textEntryPlaceholder":"Enter access key","textEntryMaximumLength":64,
                "textEntryInputKind":"sensitive"}
        }
    })json", error) && !error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "protocolVersion":28,"sequence":4,
            "widgetInstanceId":"provider-neutral.secret",
            "activeInputScopeId":"secret.entry",
            "root":{"id":"secret.entry","kind":"textEntry",
                "actionId":"secret.commit","textEntryValue":"",
                "textEntryPlaceholder":"Enter access key","textEntryMaximumLength":64,
                "textEntryInputKind":"opaque"}
        }
    })json", error) && !error.empty());

    error.clear();
    const auto invalidVisibility = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "sequence": 1,
            "widgetInstanceId": "visibility.invalid",
            "activeInputScopeId": "root",
            "root": {
                "id": "root", "kind": "stack",
                "children": [
                    {"id":"bad","kind":"text","text":"Bad","visibleWhen":"sometimes"}
                ]
            }
        }
    })json", error);
    CHECK(!invalidVisibility && !error.empty());

    error.clear();
    const auto fallbackIcon = widgetrail::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"fallback","name":"Fallback","instanceId":"fallback","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error);
    CHECK(fallbackIcon && (*fallbackIcon)[0].icon == L"connection");

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"One","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","icon":"arbitrary-svg","quickActions":[]}]})json",
        error));
    CHECK(error.find(L"WidgetGlyph") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"One","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","icon":42,"quickActions":[]}]})json",
        error));
    CHECK(error.find(L"'icon'") != std::wstring::npos);

    error.clear();
    const auto duplicate = widgetrail::testing::ParseWidgetDescriptors(R"json({"widgets":[
        {"id":"same","name":"One","instanceId":"one","runtimeGeneration":"runtime-one","presentationGeneration":"presentation-one","quickActions":[]},
        {"id":"same","name":"Two","instanceId":"two","runtimeGeneration":"runtime-two","presentationGeneration":"presentation-two","quickActions":[]}
    ]})json", error);
    CHECK(!duplicate && error.find(L"duplicate") != std::wstring::npos);

    std::string tooMany = "{\"widgets\":[";
    for (int index = 0; index < 257; ++index) {
        if (index != 0) tooMany += ',';
        tooMany += Descriptor(index);
    }
    tooMany += "]}";
    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(tooMany, error));
    CHECK(error.find(L"256") != std::wstring::npos);

    std::string tooManyActions = "{\"widgets\":[{\"id\":\"one\",\"name\":\"One\","
        "\"instanceId\":\"one\",\"runtimeGeneration\":\"runtime\","
        "\"presentationGeneration\":\"presentation\",\"quickActions\":[";
    for (int index = 0; index < 17; ++index) {
        if (index != 0) tooManyActions += ',';
        tooManyActions += "{\"id\":\"action-" + std::to_string(index) +
            "\",\"label\":\"Action\",\"actionId\":\"run\","
            "\"sourceElementId\":\"source\"}";
    }
    tooManyActions += "]}]}";
    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(tooManyActions, error));
    CHECK(error.find(L"16") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"bad/id","name":"Bad","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error));
    CHECK(error.find(L"'id'") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"Bad\nLabel","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error));
    CHECK(error.find(L"'name'") != std::wstring::npos);

    widgetrail::WidgetInvalidationQueue invalidations;
    CHECK(invalidations.Push(L"widget-0"));
    CHECK(invalidations.Push(L"widget-0"));
    CHECK(invalidations.size() == 1);
    for (int index = 1; index <= 256; ++index) {
        CHECK(invalidations.Push(L"widget-" + std::to_wstring(index)));
    }
    CHECK(invalidations.size() == widgetrail::WidgetInvalidationQueue::MaximumWidgetIds);
    CHECK(!invalidations.Push(L"bad/widget"));
    const auto pending = invalidations.Take();
    CHECK(pending.size() == widgetrail::WidgetInvalidationQueue::MaximumWidgetIds);
    CHECK(pending.front() == L"widget-1");
    CHECK(pending.back() == L"widget-256");
    CHECK(invalidations.size() == 0);

    error.clear();
    const auto hostEffect = widgetrail::testing::ParseWidgetHostEffectEvent(R"json({
        "protocolVersion":1,
        "type":"widget-host-effect",
        "requestId":0,
        "payload":{"widgetId":"games-apps","runtimeGeneration":"runtime-1","effect":"closeOverlayAfterAppLaunch","sequence":7}
    })json", error);
    CHECK(hostEffect && error.empty());
    CHECK(hostEffect->sequence == 7);
    CHECK(hostEffect->widgetId == L"games-apps");
    CHECK(hostEffect->runtimeGeneration == L"runtime-1");
    CHECK(hostEffect->kind == widgetrail::WidgetHostEffectKind::CloseOverlayAfterAppLaunch);

    widgetrail::WidgetHostEffectQueue hostEffects;
    CHECK(hostEffects.Push(*hostEffect));
    CHECK(hostEffects.Push(*hostEffect)); // replay is accepted and ignored
    CHECK(hostEffects.size() == 1);
    CHECK(hostEffects.lastSequence() == 7);
    CHECK(!hostEffects.Push({8, L"bad/widget", L"runtime-1",
        widgetrail::WidgetHostEffectKind::CloseOverlayAfterAppLaunch}));
    const auto pendingEffects = hostEffects.Take();
    CHECK(pendingEffects.size() == 1);
    CHECK(hostEffects.size() == 0);

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetHostEffectEvent(R"json({
        "protocolVersion":1,"type":"widget-host-effect","requestId":0,
        "payload":{"widgetId":"games-apps","runtimeGeneration":"runtime-1","effect":"closeOverlayAfterAppLaunch","sequence":7,"extra":true}
    })json", error));
    CHECK(!error.empty());

    error.clear();
    const auto actionFailure = widgetrail::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,
        "type":"widget-failed",
        "requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"Provider command failed","canRestart":false}
    })json", error);
    CHECK(actionFailure && error.empty());
    CHECK(actionFailure->widgetId == L"music");
    CHECK(actionFailure->runtimeGeneration == L"runtime-2");
    CHECK(actionFailure->actionId == L"playback.next");
    CHECK(actionFailure->sourceElementId == L"player.next");
    CHECK(actionFailure->code == widgetrail::WidgetActionFailureCode::ControllerActionFailed);
    CHECK(widgetrail::WidgetActionFailureCodeValue(actionFailure->code) ==
        L"controllerActionFailed");

    error.clear();
    const auto workerStartFailure =
        widgetrail::testing::ParseWidgetRuntimeFailureEvent(R"json({
        "protocolVersion":1,
        "type":"widget-failed",
        "requestId":0,
        "payload":{"widgetId":"music","reason":"connectionFailed","exitCode":65,"diagnosticCode":"exit_65","restartsUsed":0,"canRestart":true}
    })json", error);
    CHECK(workerStartFailure && error.empty());
    CHECK(workerStartFailure->widgetId == L"music");
    CHECK(workerStartFailure->category ==
        widgetrail::WidgetBridgeRuntimeFailureCategory::WorkerStart);
    CHECK(workerStartFailure->safeMessage ==
        L"Widget worker failed to start (exit_65).");

    error.clear();
    const auto runtimeFailure =
        widgetrail::testing::ParseWidgetRuntimeFailureEvent(R"json({
        "protocolVersion":1,
        "type":"widget-failed",
        "requestId":0,
        "payload":{"widgetId":"music","reason":"processExited","exitCode":1,"restartsUsed":2,"canRestart":false}
    })json", error);
    CHECK(runtimeFailure && error.empty());
    CHECK(runtimeFailure->category ==
        widgetrail::WidgetBridgeRuntimeFailureCategory::WorkerExited);
    CHECK(runtimeFailure->safeMessage == L"Widget worker exited unexpectedly.");

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"providerFailure","actionId":"playback.next","sourceElementId":"player.next","message":"failed","canRestart":false}
    })json", error));
    CHECK(!error.empty());

    error.clear();
    const auto artwork = widgetrail::testing::ParseWidgetArtworkResultEvent(R"json({
        "type":"artwork","requestId":0,
        "payload":{"widgetId":"games-apps","artworkHandle":"gallery.artwork.cover","contentType":"image/jpeg","contentBase64":"/9j/2Q=="}
    })json", error);
    CHECK(artwork && error.empty());
    CHECK(artwork->widgetId == L"games-apps");
    CHECK(artwork->artworkHandle == L"gallery.artwork.cover");
    CHECK(artwork->contentType == L"image/jpeg");
    CHECK(artwork->contentBase64 == L"/9j/2Q==");

    error.clear();
    const auto webpArtwork = widgetrail::testing::ParseWidgetArtworkResultEvent(R"json({
        "type":"artwork","requestId":0,
        "payload":{"widgetId":"gallery","artworkHandle":"gallery.artwork.webp","contentType":"image/webp","contentBase64":"UklGRh4AAABXRUJQVlA4TBEAAAAvAQAAAAdQmWZ0qf+BiOh/AAA="}
    })json", error);
    CHECK(webpArtwork && error.empty());
    CHECK(webpArtwork->contentType == L"image/webp");
    CHECK(webpArtwork->artworkHandle == L"gallery.artwork.webp");

    error.clear();
    const auto localPackage =
        widgetrail::testing::ParseLocalWidgetPackageInstallResultEvent(R"json({
        "type":"local-widget-package-install-completed","requestId":0,
        "payload":{"operationId":"11111111-2222-3333-4444-555555555555","status":"installed-disabled","widgetId":"fixture","version":"1.2.3","message":"Local widget package installed disabled. Review it before enabling."}
    })json", error);
    Require(localPackage && error.empty(),
            "valid local package completion framing was rejected");
    Require(localPackage->status ==
                widgetrail::LocalWidgetPackageInstallStatus::InstalledDisabled,
            "installed-disabled completion status changed");
    Require(localPackage->widgetId == L"fixture" &&
                localPackage->version == L"1.2.3",
            "installed completion identity changed");
    Require(localPackage->safeMessage.find(L"\\") == std::wstring::npos,
            "local completion exposed a path");

    error.clear();
    const auto cancelledPackage =
        widgetrail::testing::ParseLocalWidgetPackageInstallResultEvent(R"json({
        "type":"local-widget-package-install-completed","requestId":0,
        "payload":{"operationId":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","status":"cancelled","widgetId":"","version":"","message":"Local widget package install was cancelled."}
    })json", error);
    Require(cancelledPackage && error.empty(),
            "valid local package cancellation framing was rejected");
    Require(cancelledPackage->status ==
                widgetrail::LocalWidgetPackageInstallStatus::Cancelled,
            "cancelled completion status changed");
    Require(cancelledPackage->widgetId.empty() &&
                cancelledPackage->version.empty(),
            "cancelled completion carried package identity");

    error.clear();
    Require(!widgetrail::testing::ParseLocalWidgetPackageInstallResultEvent(R"json({
        "type":"local-widget-package-install-completed","requestId":0,
        "payload":{"operationId":"11111111-2222-3333-4444-555555555555","status":"failed","widgetId":null,"version":null,"message":"C:\\\\private\\\\package.wrwidget","path":"C:\\\\private\\\\package.wrwidget"}
    })json", error),
            "path-bearing local package completion was admitted");
    Require(!error.empty(),
            "path-bearing completion rejection omitted its diagnostic");

    error.clear();
    Require(!widgetrail::testing::ParseLocalWidgetPackageInstallResultEvent(R"json({
        "type":"widget-invalidated","requestId":0,
        "payload":{"operationId":"11111111-2222-3333-4444-555555555555","status":"failed","widgetId":"","version":"","message":"Install failed"}
    })json", error),
            "wrong-operation local package fixture was admitted");
    Require(!error.empty(),
            "wrong-operation rejection omitted its diagnostic");

    error.clear();
    Require(!widgetrail::testing::ParseLocalWidgetPackageInstallResultEvent(R"json({
        "type":"local-widget-package-install-completed","requestId":0,
        "payload":{"operationId":"11111111-2222-3333-4444-555555555555","status":"failed","widgetId":"","version":"","message":"Install failed","extra":"malformed"}
    })json", error),
            "malformed local package completion was admitted");
    Require(!error.empty(),
            "malformed completion rejection omitted its diagnostic");
    widgetrail::WidgetArtworkResultQueue artworkResults;
    for (std::size_t index = 0;
         index < widgetrail::WidgetArtworkResultQueue::MaximumResults; ++index) {
        auto suffix = std::to_wstring(index);
        suffix.insert(suffix.begin(), 32 - suffix.size(), L'0');
        CHECK(artworkResults.Push({
            L"games-apps", L"library.art." + suffix, L"AAAA"}));
    }
    CHECK(artworkResults.Push({
        L"games-apps", L"library.art.00000000000000000000000000000000", L"BBBB"}));
    CHECK(artworkResults.size() ==
           widgetrail::WidgetArtworkResultQueue::MaximumResults);
    CHECK(!artworkResults.Push({
        L"games-apps", L"library.art.ffffffffffffffffffffffffffffffff", L"CCCC"}));

    widgetrail::WidgetActionFailureQueue actionFailures;
    for (int index = 0; index <= 16; ++index) {
        CHECK(actionFailures.Push({
            L"music", L"runtime-2", L"action-" + std::to_wstring(index), L"source"}));
    }
    CHECK(actionFailures.size() == widgetrail::WidgetActionFailureQueue::MaximumFailures);
    const auto pendingFailures = actionFailures.Take();
    CHECK(pendingFailures.front().actionId == L"action-1");
    CHECK(pendingFailures.back().actionId == L"action-16");

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"secret\u000aresponse","canRestart":false}
    })json", error));
    CHECK(!error.empty());

    error.clear();
    CHECK(!widgetrail::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"failed","canRestart":true}
    })json", error));
    CHECK(!error.empty());

    error.clear();
    const auto appearance = widgetrail::testing::ParsePlatformAppearance(ValidAppearance, error);
    CHECK(appearance && error.empty());
    CHECK(appearance->revision == 7);
    CHECK(appearance->themeId == L"midnight-blue");
    CHECK(appearance->themeVersion == L"1.2.0");
    CHECK(appearance->interfaceScale == 1.1);
    CHECK(appearance->textScale == 1.25);
    CHECK(appearance->backdropOpacity == 0.62);
    CHECK(appearance->motion == widgetrail::PlatformMotionPreference::Reduced);
    CHECK(appearance->contrast == widgetrail::PlatformContrastPreference::High);
    CHECK(appearance->boldText);
    CHECK(appearance->transparency == widgetrail::PlatformTransparencyPreference::Reduced);
    CHECK(!appearance->animateWidgetSwitching);
    CHECK(appearance->shellStyles.size() == 3);
    CHECK(appearance->shellStyles.at(L"panel").at(L"corner-radius").number == 18.0);
    CHECK(appearance->shellStyles.at(L"title").at(L"font-family").text ==
           L"Segoe UI Variable");

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"cinematic","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{}
    })json", error));
    CHECK(error.find(L"motion") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"extreme","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{}
    })json", error));
    CHECK(error.find(L"contrast") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"blurred","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{}
    })json", error));
    CHECK(error.find(L"transparency") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":"yes","transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{}
    })json", error));
    CHECK(error.find(L"types") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":2,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{}
    })json", error));
    CHECK(error.find(L"bounds") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{"unknown":{}}
    })json", error));
    CHECK(error.find(L"unknown") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{"canvas":{
            "background":{"kind":"script","text":"unsafe","number":null,"unit":null}
        }}
    })json", error));
    CHECK(error.find(L"computed value") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,
        "widgetSurfaceAppearance":"theme","widgetSurfaceAppearanceOverrides":{},"shellStyles":{},"unexpected":true
    })json", error));
    CHECK(error.find(L"unknown properties") != std::wstring::npos);

    std::string tooManyStyleProperties =
        "{\"revision\":0,\"themeId\":\"default\",\"themeVersion\":\"1.0.0\","
        "\"interfaceScale\":1,\"textScale\":1,\"backdropOpacity\":0.64,"
        "\"motion\":\"system\",\"contrast\":\"system\",\"boldText\":false,"
        "\"transparency\":\"full\",\"animateWidgetSwitching\":false,"
        "\"widgetSurfaceAppearance\":\"theme\","
        "\"widgetSurfaceAppearanceOverrides\":{},\"shellStyles\":{\"canvas\":{";
    for (int index = 0; index < 65; ++index) {
        if (index != 0) tooManyStyleProperties += ',';
        tooManyStyleProperties += "\"property-" + std::to_string(index) +
            "\":{\"kind\":\"number\",\"text\":\"1\",\"number\":1,\"unit\":null}";
    }
    tooManyStyleProperties += "}}}";
    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(tooManyStyleProperties, error));
    CHECK(error.find(L"property count") != std::wstring::npos);

    widgetrail::PlatformAppearanceRevisionTracker revisions;
    CHECK(revisions.Notify(3));
    CHECK(revisions.Notify(2));
    CHECK(revisions.Notify(5));
    CHECK(revisions.pending() == 5);
    CHECK(!revisions.Notify(-1));
    CHECK(revisions.pending() == 5);
    CHECK(revisions.Take() == 5);
    CHECK(!revisions.pending());

    widgetrail::WidgetCatalogRevisionTracker catalogRevisions;
    CHECK(catalogRevisions.ObserveSnapshot(2));
    CHECK(catalogRevisions.observed() == 2);
    CHECK(catalogRevisions.Notify(2));
    CHECK(!catalogRevisions.pending());
    CHECK(catalogRevisions.Notify(4));
    CHECK(catalogRevisions.Notify(3));
    CHECK(catalogRevisions.pending() == 4);
    CHECK(catalogRevisions.Take() == 4);
    CHECK(catalogRevisions.observed() == 2);
    CHECK(!catalogRevisions.Take());
    CHECK(catalogRevisions.Notify(4));
    CHECK(!catalogRevisions.pending());
    catalogRevisions.Retry();
    CHECK(catalogRevisions.pending() == 4);
    CHECK(catalogRevisions.Take() == 4);
    CHECK(!catalogRevisions.ObserveSnapshot(3));
    CHECK(catalogRevisions.ObserveSnapshot(4));
    CHECK(catalogRevisions.observed() == 4);
    CHECK(catalogRevisions.Notify(4));
    CHECK(!catalogRevisions.pending());
    CHECK(!catalogRevisions.Notify(-1));
    catalogRevisions.Reset();
    CHECK(catalogRevisions.observed() == 0);
    CHECK(catalogRevisions.ObserveSnapshot(0));
    CHECK(catalogRevisions.Notify(1));
    CHECK(catalogRevisions.Take() == 1);
    catalogRevisions.Abandon();
    CHECK(catalogRevisions.Notify(1));
    CHECK(catalogRevisions.pending() == 1);

    std::vector<widgetrail::WidgetDescriptor> beforeRuntimes{
        {.id = L"evicted", .instanceId = L"instance-1", .runtimeGeneration = L"runtime-1"},
        {.id = L"stable", .instanceId = L"instance-2", .runtimeGeneration = L"runtime-2"},
        {.id = L"removed", .instanceId = L"instance-3", .runtimeGeneration = L"runtime-3"},
    };
    std::vector<widgetrail::WidgetDescriptor> afterRuntimes{
        {.id = L"evicted", .instanceId = L"instance-1", .runtimeGeneration = L"runtime-new"},
        {.id = L"stable", .instanceId = L"instance-2", .runtimeGeneration = L"runtime-2"},
    };
    const auto changedRuntimes = widgetrail::ChangedWidgetRuntimeIds(beforeRuntimes, afterRuntimes);
    CHECK((changedRuntimes == std::vector<std::wstring>{L"evicted", L"removed"}));

    widgetrail::PlatformAppearanceState appearanceState;
    CHECK(appearanceState.Publish(*appearance));
    CHECK(appearanceState.current() && appearanceState.current()->revision == 7);
    widgetrail::PlatformAppearance replay = *appearance;
    replay.themeId = L"same-revision-replay";
    CHECK(!appearanceState.Publish(std::move(replay)));
    CHECK(appearanceState.current()->themeId == L"midnight-blue");
    widgetrail::PlatformAppearance stale = *appearance;
    stale.revision = 6;
    stale.themeId = L"stale";
    CHECK(!appearanceState.Publish(std::move(stale)));
    CHECK(appearanceState.current()->revision == 7);
    CHECK(appearanceState.current()->themeId == L"midnight-blue");

    error.clear();
    const auto malformed = widgetrail::testing::ParsePlatformAppearance("{}", error);
    CHECK(!malformed);
    CHECK(appearanceState.current()->themeId == L"midnight-blue");

    std::cout << "WidgetBridgeCatalogTests passed\n";
}
