#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cassert>
#include <iostream>
#include <string>

namespace {

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

} // namespace

int main() {
    std::wstring error;
    const auto valid = gba::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.music",
            "name": "Music controls",
            "instanceId": "music.default",
            "runtimeGeneration": "runtime-1",
            "presentationGeneration": "presentation-1",
            "icon": "music",
            "pinningSupported": true,
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
    assert(valid && error.empty());
    assert(valid->size() == 1);
    assert((*valid)[0].id == L"dev.test.music");
    assert((*valid)[0].name == L"Music controls");
    assert((*valid)[0].instanceId == L"music.default");
    assert((*valid)[0].runtimeGeneration == L"runtime-1");
    assert((*valid)[0].presentationGeneration == L"presentation-1");
    assert((*valid)[0].icon == L"music");
    assert((*valid)[0].pinningSupported);
    assert((*valid)[0].quickActions.size() == 2);
    assert((*valid)[0].quickActions[0].controllerButton == L"x");
    assert(!(*valid)[0].quickActions[1].controllerButton);

    error.clear();
    const auto defaultPinning = gba::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.default",
            "name": "Default",
            "instanceId": "default.instance",
            "runtimeGeneration": "runtime-default",
            "presentationGeneration": "presentation-default",
            "quickActions": []
        }]
    })json", error);
    assert(defaultPinning && !(*defaultPinning)[0].pinningSupported);
    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(R"json({
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
    const auto styledSnapshot = gba::testing::ParseWidgetSnapshotResponse(R"json({
        "snapshot": {
            "sequence": 9,
            "widgetInstanceId": "music.runtime.v1",
            "activeInputScopeId": "root",
            "initialFocusId": "play",
            "root": {
                "id": "root",
                "kind": "stack",
                "children": [
                    {"id":"play","kind":"button","text":"Play","actionId":"play","focusPersistenceId":"transport.play"},
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
    assert(styledSnapshot && error.empty());
    assert(styledSnapshot->root.children.size() == 5);
    const auto& styledButton = styledSnapshot->root.children.front();
    (void)styledButton;
    assert(styledButton.baseStyle.at(L"opacity").number == 0.5);
    assert(styledButton.focusedStyle.at(L"scale").number == 1.05);
    assert(styledButton.pressedStyle.at(L"scale").number == 0.97);
    const auto& loadingIndicator = styledSnapshot->root.children[1];
    assert(styledSnapshot->root.children[0].focusPersistenceId == L"transport.play");
    (void)loadingIndicator;
    assert(loadingIndicator.kind == L"loadingIndicator");
    assert(loadingIndicator.accessibilityLabel == L"Loading music");
    assert(loadingIndicator.indicatorSize == L"compact");
    assert(loadingIndicator.visibleWhen == L"compactOnly");
    const auto& actionSurface = styledSnapshot->root.children[2];
    (void)actionSurface;
    assert(actionSurface.kind == L"actionSurface");
    assert(actionSurface.actionId == L"open-album");
    assert(actionSurface.accessibilityLabel == L"Open album");
    assert(actionSurface.actionSurfaceOrientation == L"horizontal");
    assert(actionSurface.children.size() == 2);
    assert(actionSurface.children[0].kind == L"image");
    assert(actionSurface.children[0].imageFit == L"cover");
    assert(actionSurface.children[1].kind == L"stack");
    assert(actionSurface.children[1].children.size() == 2);
    assert(actionSurface.children[1].children[0].text == L"Album title");
    const auto& cursorList = styledSnapshot->root.children[4];
    (void)cursorList;
    assert(cursorList.collectionAnchorKey == L"game.2");
    assert(cursorList.children[0].collectionItemKey == L"game.2");
    assert(cursorList.children[0].artworkHandle == L"library.art.2");
    assert(cursorList.children[0].imageSource.empty());
    const auto& grid = styledSnapshot->root.children[3];
    (void)grid;
    assert(grid.kind == L"grid");
    assert(grid.gridMinimumColumnWidth == 180.0);
    assert(grid.gridMaximumColumns == 3U);
    assert(grid.children.size() == 2);
    assert(grid.children[1].id == L"grid-two");

    error.clear();
    const auto invalidGrid = gba::testing::ParseWidgetSnapshotResponse(R"json({
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
    assert(!invalidGrid && !error.empty());

    error.clear();
    const auto invalidVisibility = gba::testing::ParseWidgetSnapshotResponse(R"json({
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
    assert(!invalidVisibility && !error.empty());

    error.clear();
    const auto fallbackIcon = gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"fallback","name":"Fallback","instanceId":"fallback","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error);
    assert(fallbackIcon && (*fallbackIcon)[0].icon == L"connection");

    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"One","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","icon":"arbitrary-svg","quickActions":[]}]})json",
        error));
    assert(error.find(L"WidgetGlyph") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"One","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","icon":42,"quickActions":[]}]})json",
        error));
    assert(error.find(L"'icon'") != std::wstring::npos);

    error.clear();
    const auto duplicate = gba::testing::ParseWidgetDescriptors(R"json({"widgets":[
        {"id":"same","name":"One","instanceId":"one","runtimeGeneration":"runtime-one","presentationGeneration":"presentation-one","quickActions":[]},
        {"id":"same","name":"Two","instanceId":"two","runtimeGeneration":"runtime-two","presentationGeneration":"presentation-two","quickActions":[]}
    ]})json", error);
    assert(!duplicate && error.find(L"duplicate") != std::wstring::npos);

    std::string tooMany = "{\"widgets\":[";
    for (int index = 0; index < 257; ++index) {
        if (index != 0) tooMany += ',';
        tooMany += Descriptor(index);
    }
    tooMany += "]}";
    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(tooMany, error));
    assert(error.find(L"256") != std::wstring::npos);

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
    assert(!gba::testing::ParseWidgetDescriptors(tooManyActions, error));
    assert(error.find(L"16") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"bad/id","name":"Bad","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error));
    assert(error.find(L"'id'") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"Bad\nLabel","instanceId":"one","runtimeGeneration":"runtime","presentationGeneration":"presentation","quickActions":[]}]})json",
        error));
    assert(error.find(L"'name'") != std::wstring::npos);

    gba::WidgetInvalidationQueue invalidations;
    assert(invalidations.Push(L"widget-0"));
    assert(invalidations.Push(L"widget-0"));
    assert(invalidations.size() == 1);
    for (int index = 1; index <= 256; ++index) {
        assert(invalidations.Push(L"widget-" + std::to_wstring(index)));
    }
    assert(invalidations.size() == gba::WidgetInvalidationQueue::MaximumWidgetIds);
    assert(!invalidations.Push(L"bad/widget"));
    const auto pending = invalidations.Take();
    assert(pending.size() == gba::WidgetInvalidationQueue::MaximumWidgetIds);
    assert(pending.front() == L"widget-1");
    assert(pending.back() == L"widget-256");
    assert(invalidations.size() == 0);

    error.clear();
    const auto hostEffect = gba::testing::ParseWidgetHostEffectEvent(R"json({
        "protocolVersion":1,
        "type":"widget-host-effect",
        "requestId":0,
        "payload":{"widgetId":"games-apps","runtimeGeneration":"runtime-1","effect":"closeOverlayAfterAppLaunch","sequence":7}
    })json", error);
    assert(hostEffect && error.empty());
    assert(hostEffect->sequence == 7);
    assert(hostEffect->widgetId == L"games-apps");
    assert(hostEffect->runtimeGeneration == L"runtime-1");
    assert(hostEffect->kind == gba::WidgetHostEffectKind::CloseOverlayAfterAppLaunch);

    gba::WidgetHostEffectQueue hostEffects;
    assert(hostEffects.Push(*hostEffect));
    assert(hostEffects.Push(*hostEffect)); // replay is accepted and ignored
    assert(hostEffects.size() == 1);
    assert(hostEffects.lastSequence() == 7);
    assert(!hostEffects.Push({8, L"bad/widget", L"runtime-1",
        gba::WidgetHostEffectKind::CloseOverlayAfterAppLaunch}));
    const auto pendingEffects = hostEffects.Take();
    assert(pendingEffects.size() == 1);
    assert(hostEffects.size() == 0);

    error.clear();
    assert(!gba::testing::ParseWidgetHostEffectEvent(R"json({
        "protocolVersion":1,"type":"widget-host-effect","requestId":0,
        "payload":{"widgetId":"games-apps","runtimeGeneration":"runtime-1","effect":"closeOverlayAfterAppLaunch","sequence":7,"extra":true}
    })json", error));
    assert(!error.empty());

    error.clear();
    const auto actionFailure = gba::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,
        "type":"widget-failed",
        "requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"Provider command failed","canRestart":false}
    })json", error);
    assert(actionFailure && error.empty());
    assert(actionFailure->widgetId == L"music");
    assert(actionFailure->runtimeGeneration == L"runtime-2");
    assert(actionFailure->actionId == L"playback.next");
    assert(actionFailure->sourceElementId == L"player.next");
    assert(actionFailure->code == gba::WidgetActionFailureCode::ControllerActionFailed);
    assert(gba::WidgetActionFailureCodeValue(actionFailure->code) ==
        L"controllerActionFailed");

    error.clear();
    assert(!gba::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"providerFailure","actionId":"playback.next","sourceElementId":"player.next","message":"failed","canRestart":false}
    })json", error));
    assert(!error.empty());

    error.clear();
    const auto artwork = gba::testing::ParseWidgetArtworkResultEvent(R"json({
        "type":"artwork","requestId":0,
        "payload":{"widgetId":"games-apps","artworkHandle":"library.art.0123456789abcdef0123456789abcdef","pngBase64":"AAAA"}
    })json", error);
    assert(artwork && error.empty());
    assert(artwork->widgetId == L"games-apps");
    assert(artwork->artworkHandle ==
           L"library.art.0123456789abcdef0123456789abcdef");
    assert(artwork->pngBase64 == L"AAAA");
    gba::WidgetArtworkResultQueue artworkResults;
    for (std::size_t index = 0;
         index < gba::WidgetArtworkResultQueue::MaximumResults; ++index) {
        auto suffix = std::to_wstring(index);
        suffix.insert(suffix.begin(), 32 - suffix.size(), L'0');
        assert(artworkResults.Push({
            L"games-apps", L"library.art." + suffix, L"AAAA"}));
    }
    assert(artworkResults.Push({
        L"games-apps", L"library.art.00000000000000000000000000000000", L"BBBB"}));
    assert(artworkResults.size() ==
           gba::WidgetArtworkResultQueue::MaximumResults);
    assert(!artworkResults.Push({
        L"games-apps", L"library.art.ffffffffffffffffffffffffffffffff", L"CCCC"}));

    gba::WidgetActionFailureQueue actionFailures;
    for (int index = 0; index <= 16; ++index) {
        assert(actionFailures.Push({
            L"music", L"runtime-2", L"action-" + std::to_wstring(index), L"source"}));
    }
    assert(actionFailures.size() == gba::WidgetActionFailureQueue::MaximumFailures);
    const auto pendingFailures = actionFailures.Take();
    assert(pendingFailures.front().actionId == L"action-1");
    assert(pendingFailures.back().actionId == L"action-16");

    error.clear();
    assert(!gba::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"secret\u000aresponse","canRestart":false}
    })json", error));
    assert(!error.empty());

    error.clear();
    assert(!gba::testing::ParseWidgetActionFailureEvent(R"json({
        "protocolVersion":1,"type":"widget-failed","requestId":0,
        "payload":{"widgetId":"music","runtimeGeneration":"runtime-2","reason":"controllerActionFailed","actionId":"playback.next","sourceElementId":"player.next","message":"failed","canRestart":true}
    })json", error));
    assert(!error.empty());

    error.clear();
    const auto appearance = gba::testing::ParsePlatformAppearance(ValidAppearance, error);
    assert(appearance && error.empty());
    assert(appearance->revision == 7);
    assert(appearance->themeId == L"midnight-blue");
    assert(appearance->themeVersion == L"1.2.0");
    assert(appearance->interfaceScale == 1.1);
    assert(appearance->textScale == 1.25);
    assert(appearance->backdropOpacity == 0.62);
    assert(appearance->motion == gba::PlatformMotionPreference::Reduced);
    assert(appearance->contrast == gba::PlatformContrastPreference::High);
    assert(appearance->boldText);
    assert(appearance->transparency == gba::PlatformTransparencyPreference::Reduced);
    assert(appearance->shellStyles.size() == 3);
    assert(appearance->shellStyles.at(L"panel").at(L"corner-radius").number == 18.0);
    assert(appearance->shellStyles.at(L"title").at(L"font-family").text ==
           L"Segoe UI Variable");

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"cinematic","contrast":"system","boldText":false,"transparency":"full","shellStyles":{}
    })json", error));
    assert(error.find(L"motion") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"extreme","boldText":false,"transparency":"full","shellStyles":{}
    })json", error));
    assert(error.find(L"contrast") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"blurred","shellStyles":{}
    })json", error));
    assert(error.find(L"transparency") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":"yes","transparency":"full","shellStyles":{}
    })json", error));
    assert(error.find(L"types") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":2,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","shellStyles":{}
    })json", error));
    assert(error.find(L"bounds") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","shellStyles":{"unknown":{}}
    })json", error));
    assert(error.find(L"unknown") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","shellStyles":{"canvas":{
            "background":{"kind":"script","text":"unsafe","number":null,"unit":null}
        }}
    })json", error));
    assert(error.find(L"computed value") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(R"json({
        "revision":0,"themeId":"default","themeVersion":"1.0.0",
        "interfaceScale":1,"textScale":1,"backdropOpacity":0.64,
        "motion":"system","contrast":"system","boldText":false,"transparency":"full","shellStyles":{},"unexpected":true
    })json", error));
    assert(error.find(L"unknown properties") != std::wstring::npos);

    std::string tooManyStyleProperties =
        "{\"revision\":0,\"themeId\":\"default\",\"themeVersion\":\"1.0.0\","
        "\"interfaceScale\":1,\"textScale\":1,\"backdropOpacity\":0.64,"
        "\"motion\":\"system\",\"contrast\":\"system\",\"boldText\":false,"
        "\"transparency\":\"full\",\"shellStyles\":{\"canvas\":{";
    for (int index = 0; index < 65; ++index) {
        if (index != 0) tooManyStyleProperties += ',';
        tooManyStyleProperties += "\"property-" + std::to_string(index) +
            "\":{\"kind\":\"number\",\"text\":\"1\",\"number\":1,\"unit\":null}";
    }
    tooManyStyleProperties += "}}}";
    error.clear();
    assert(!gba::testing::ParsePlatformAppearance(tooManyStyleProperties, error));
    assert(error.find(L"property count") != std::wstring::npos);

    gba::PlatformAppearanceRevisionTracker revisions;
    assert(revisions.Notify(3));
    assert(revisions.Notify(2));
    assert(revisions.Notify(5));
    assert(revisions.pending() == 5);
    assert(!revisions.Notify(-1));
    assert(revisions.pending() == 5);
    assert(revisions.Take() == 5);
    assert(!revisions.pending());

    gba::WidgetCatalogRevisionTracker catalogRevisions;
    assert(catalogRevisions.ObserveSnapshot(2));
    assert(catalogRevisions.observed() == 2);
    assert(catalogRevisions.Notify(2));
    assert(!catalogRevisions.pending());
    assert(catalogRevisions.Notify(4));
    assert(catalogRevisions.Notify(3));
    assert(catalogRevisions.pending() == 4);
    assert(catalogRevisions.Take() == 4);
    assert(catalogRevisions.observed() == 2);
    assert(!catalogRevisions.Take());
    assert(catalogRevisions.Notify(4));
    assert(!catalogRevisions.pending());
    catalogRevisions.Retry();
    assert(catalogRevisions.pending() == 4);
    assert(catalogRevisions.Take() == 4);
    assert(!catalogRevisions.ObserveSnapshot(3));
    assert(catalogRevisions.ObserveSnapshot(4));
    assert(catalogRevisions.observed() == 4);
    assert(catalogRevisions.Notify(4));
    assert(!catalogRevisions.pending());
    assert(!catalogRevisions.Notify(-1));
    catalogRevisions.Reset();
    assert(catalogRevisions.observed() == 0);
    assert(catalogRevisions.ObserveSnapshot(0));
    assert(catalogRevisions.Notify(1));
    assert(catalogRevisions.Take() == 1);
    catalogRevisions.Abandon();
    assert(catalogRevisions.Notify(1));
    assert(catalogRevisions.pending() == 1);

    std::vector<gba::WidgetDescriptor> beforeRuntimes{
        {.id = L"evicted", .instanceId = L"instance-1", .runtimeGeneration = L"runtime-1"},
        {.id = L"stable", .instanceId = L"instance-2", .runtimeGeneration = L"runtime-2"},
        {.id = L"removed", .instanceId = L"instance-3", .runtimeGeneration = L"runtime-3"},
    };
    std::vector<gba::WidgetDescriptor> afterRuntimes{
        {.id = L"evicted", .instanceId = L"instance-1", .runtimeGeneration = L"runtime-new"},
        {.id = L"stable", .instanceId = L"instance-2", .runtimeGeneration = L"runtime-2"},
    };
    const auto changedRuntimes = gba::ChangedWidgetRuntimeIds(beforeRuntimes, afterRuntimes);
    assert((changedRuntimes == std::vector<std::wstring>{L"evicted", L"removed"}));

    gba::PlatformAppearanceState appearanceState;
    assert(appearanceState.Publish(*appearance));
    assert(appearanceState.current() && appearanceState.current()->revision == 7);
    gba::PlatformAppearance replay = *appearance;
    replay.themeId = L"same-revision-replay";
    assert(!appearanceState.Publish(std::move(replay)));
    assert(appearanceState.current()->themeId == L"midnight-blue");
    gba::PlatformAppearance stale = *appearance;
    stale.revision = 6;
    stale.themeId = L"stale";
    assert(!appearanceState.Publish(std::move(stale)));
    assert(appearanceState.current()->revision == 7);
    assert(appearanceState.current()->themeId == L"midnight-blue");

    error.clear();
    const auto malformed = gba::testing::ParsePlatformAppearance("{}", error);
    assert(!malformed);
    assert(appearanceState.current()->themeId == L"midnight-blue");

    std::cout << "WidgetBridgeCatalogTests passed\n";
}
