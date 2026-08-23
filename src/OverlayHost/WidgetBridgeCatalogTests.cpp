#include "WidgetBridgeClient.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
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
            "protocolVersion":17,
            "sequence":9,
            "widgetInstanceId":"update.sample",
            "activeInputScopeId":"root",
            "initialFocusId":"a",
            "surface":{"mode":"standard","preferredWidth":560,"preferredHeight":645,"minimumWidth":320,"minimumHeight":240},
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
        "renderStyles":{"a":{"base":{"opacity":{"kind":"number","text":"0.75","number":0.75,"unit":null}}}}
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
    Require(materializedSnapshot.protocolVersion == 18 && materializedSnapshot.sequence == 10 &&
                materializedSnapshot.root.children.size() == 2 &&
                materializedSnapshot.root.children[0].id == L"c" &&
                materializedSnapshot.root.children[1].id == L"a" &&
                materializedSnapshot.root.children[1].text == L"Replaced" &&
                materializedSnapshot.root.children[1].baseStyle.at(L"opacity").number == 0.75,
            "Atomic update did not publish the complete candidate and styles");

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
    VerifyFrameSafeCancellationRecovery();
    VerifyAtomicPresentationUpdateMaterialization();
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
            "advancedPresentation": {
                "schemaVersion": 1,
                "kind": "launcherExperience"
            },
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
    CHECK((*valid)[0].advancedPresentation);
    CHECK((*valid)[0].advancedPresentation->schemaVersion == 1);
    CHECK((*valid)[0].advancedPresentation->kind == L"launcherExperience");
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
    CHECK(defaultPinning && !(*defaultPinning)[0].advancedPresentation);
    CHECK(defaultPinning && !(*defaultPinning)[0].protectedWifiPromptSupported);

    error.clear();
    const auto incompatiblePresentation = widgetrail::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.future",
            "name": "Future",
            "instanceId": "future.instance",
            "runtimeGeneration": "runtime-future",
            "presentationGeneration": "presentation-future",
            "advancedPresentation": {"schemaVersion":99,"kind":"launcherExperience"},
            "quickActions": []
        }]
    })json", error);
    CHECK(incompatiblePresentation &&
           (*incompatiblePresentation)[0].advancedPresentation->schemaVersion == 99);

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
            "sequence": 9,
            "widgetInstanceId": "music.runtime.v1",
            "activeInputScopeId": "root",
            "initialFocusId": "play",
            "advancedPresentation": {
                "kind": "launcherExperience",
                "preset": "heroRail"
            },
            "root": {
                "id": "root",
                "kind": "stack",
                "children": [
                    {"id":"play","kind":"button","text":"Play","actionId":"play","focusPersistenceId":"transport.play"},
                    {"id":"advanced-slot","kind":"stack","advancedPresentationSlot":"detailsPanel","children":[]},
                    {"id":"loading","kind":"loadingIndicator","accessibilityLabel":"Loading music","indicatorSize":"compact","visibleWhen":"compactOnly"},
                    {
                        "id":"album","kind":"actionSurface","actionId":"open-album",
                        "accessibilityLabel":"Open album","actionSurfaceOrientation":"horizontal",
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
    CHECK(styledSnapshot->advancedPresentationKind == L"launcherExperience");
    CHECK(styledSnapshot->advancedPresentationPreset == L"heroRail");
    CHECK(styledSnapshot->root.children[1].advancedPresentationSlot == L"detailsPanel");
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
    CHECK(actionSurface.children.size() == 2);
    CHECK(actionSurface.children[0].kind == L"image");
    CHECK(actionSurface.children[0].imageFit == L"cover");
    CHECK(actionSurface.children[1].kind == L"stack");
    CHECK(actionSurface.children[1].children.size() == 2);
    CHECK(actionSurface.children[1].children[0].text == L"Album title");
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
        "payload":{"widgetId":"games-apps","artworkHandle":"library.art.0123456789abcdef0123456789abcdef","pngBase64":"AAAA"}
    })json", error);
    CHECK(artwork && error.empty());
    CHECK(artwork->widgetId == L"games-apps");
    CHECK(artwork->artworkHandle ==
           L"library.art.0123456789abcdef0123456789abcdef");
    CHECK(artwork->pngBase64 == L"AAAA");

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
        "motion":"cinematic","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{}
    })json", error));
    CHECK(error.find(L"motion") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"extreme","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{}
    })json", error));
    CHECK(error.find(L"contrast") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"blurred","animateWidgetSwitching":false,"shellStyles":{}
    })json", error));
    CHECK(error.find(L"transparency") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":"yes","transparency":"full","animateWidgetSwitching":false,"shellStyles":{}
    })json", error));
    CHECK(error.find(L"types") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":2,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{}
    })json", error));
    CHECK(error.find(L"bounds") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{"unknown":{}}
    })json", error));
    CHECK(error.find(L"unknown") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{"canvas":{
            "background":{"kind":"script","text":"unsafe","number":null,"unit":null}
        }}
    })json", error));
    CHECK(error.find(L"computed value") != std::wstring::npos);

    error.clear();
    CHECK(!widgetrail::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","animateWidgetSwitching":false,"shellStyles":{},"unexpected":true
    })json", error));
    CHECK(error.find(L"unknown properties") != std::wstring::npos);

    std::string tooManyStyleProperties =
        "{\"revision\":0,\"themeId\":\"default\",\"themeVersion\":\"1.0.0\","
        "\"interfaceScale\":1,\"textScale\":1,\"backdropOpacity\":0.64,"
        "\"motion\":\"system\",\"contrast\":\"system\",\"boldText\":false,"
        "\"transparency\":\"full\",\"animateWidgetSwitching\":false,\"shellStyles\":{\"canvas\":{";
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
