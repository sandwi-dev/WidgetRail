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
    assert(gba::testing::ShouldDelegateForegroundActivation(
        42, true, true, true, L"a", L"pressed"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        0, true, true, true, L"a", L"pressed"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        42, false, true, true, L"a", L"pressed"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        42, true, false, true, L"a", L"pressed"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        42, true, true, false, L"a", L"pressed"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        42, true, true, true, L"a", L"repeated"));
    assert(!gba::testing::ShouldDelegateForegroundActivation(
        42, true, true, true, L"a", L"released"));
    assert(gba::testing::ShouldDelegateForegroundActivation(
        42, true, true, true, L"x", L"pressed"));
    std::wstring error;
    const auto valid = gba::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.music",
            "name": "Music controls",
            "instanceId": "music.default",
            "runtimeGeneration": "runtime-1",
            "presentationGeneration": "presentation-1",
            "icon": "music",
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
    assert((*valid)[0].quickActions.size() == 2);
    assert((*valid)[0].quickActions[0].controllerButton == L"x");
    assert(!(*valid)[0].quickActions[1].controllerButton);

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
