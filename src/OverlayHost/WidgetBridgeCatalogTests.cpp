#include "WidgetBridgeClient.h"

#include <algorithm>
#include <cassert>
#include <iostream>
#include <string>

namespace {

std::string Descriptor(const int index) {
    return "{\"id\":\"widget-" + std::to_string(index) +
        "\",\"name\":\"Widget\",\"instanceId\":\"instance-" +
        std::to_string(index) + "\",\"quickActions\":[]}";
}

} // namespace

int main() {
    std::wstring error;
    const auto valid = gba::testing::ParseWidgetDescriptors(R"json({
        "widgets": [{
            "id": "dev.test.music",
            "name": "Music controls",
            "instanceId": "music.default",
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
    assert((*valid)[0].quickActions.size() == 2);
    assert((*valid)[0].quickActions[0].controllerButton == L"x");
    assert(!(*valid)[0].quickActions[1].controllerButton);

    error.clear();
    const auto duplicate = gba::testing::ParseWidgetDescriptors(R"json({"widgets":[
        {"id":"same","name":"One","instanceId":"one","quickActions":[]},
        {"id":"same","name":"Two","instanceId":"two","quickActions":[]}
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
        "\"instanceId\":\"one\",\"quickActions\":[";
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
        R"json({"widgets":[{"id":"bad/id","name":"Bad","instanceId":"one","quickActions":[]}]})json",
        error));
    assert(error.find(L"'id'") != std::wstring::npos);

    error.clear();
    assert(!gba::testing::ParseWidgetDescriptors(
        R"json({"widgets":[{"id":"one","name":"Bad\nLabel","instanceId":"one","quickActions":[]}]})json",
        error));
    assert(error.find(L"'name'") != std::wstring::npos);

    std::cout << "WidgetBridgeCatalogTests passed\n";
}
