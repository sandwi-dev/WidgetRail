#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <algorithm>
#include <string>
#include <vector>

namespace widgetrail {
struct DisplayScaleContext final {
    std::wstring id;
    std::wstring name{L"Display unavailable"};
    std::vector<std::wstring> devicePaths;
    bool operator==(const DisplayScaleContext&) const = default;
};

// Build a deterministic connection token for host reporting. The shared
// managed provider resolves the physical identity used for saved sizing.
inline DisplayScaleContext MakeDisplayScaleContext(
    std::vector<std::pair<std::wstring, std::wstring>> targets) {
    if (targets.empty()) return {};
    for (auto& target : targets) {
        if (target.first.empty() || target.first.size() > 256) return {};
        CharUpperBuffW(target.first.data(), static_cast<DWORD>(target.first.size()));
    }
    std::sort(targets.begin(), targets.end());
    targets.erase(std::unique(targets.begin(), targets.end(), [](const auto& a, const auto& b) {
        return a.first == b.first;
    }), targets.end());
    std::wstring identity;
    for (const auto& target : targets) { identity += target.first; identity += L'\n'; }
    UCHAR hash[32]{};
    if (BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0,
            reinterpret_cast<PUCHAR>(identity.data()),
            static_cast<ULONG>(identity.size() * sizeof(wchar_t)), hash, sizeof(hash)) < 0) return {};
    DisplayScaleContext result;
    for (const auto& target : targets) result.devicePaths.push_back(target.first);
    constexpr wchar_t hex[] = L"0123456789abcdef";
    for (const auto byte : hash) { result.id += hex[byte >> 4]; result.id += hex[byte & 15]; }
    result.name = targets.size() > 1 ? L"Duplicated displays: " : L"";
    for (const auto& target : targets) {
        if (result.name.size() > (targets.size() > 1 ? 21U : 0U)) result.name += L", ";
        result.name += target.second.empty() ? L"Monitor" : target.second;
    }
    if (result.name.size() > 128) result.name.resize(128);
    for (auto& c : result.name) if (c < L' ') c = L' ';
    return result;
}

// Read only, called when selecting a placement anchor, never on the paint path.
inline DisplayScaleContext ResolveDisplayScaleContext(const HMONITOR monitor) {
    MONITORINFOEXW info{};
    info.cbSize = sizeof(info);
    if (!monitor || !GetMonitorInfoW(monitor, &info)) return {};
    for (int attempt = 0; attempt < 3; ++attempt) {
        UINT32 pathCount{}, modeCount{};
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != ERROR_SUCCESS ||
            pathCount == 0 || pathCount > 64 || modeCount > 256) return {};
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
        const auto status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(),
            &modeCount, modes.data(), nullptr);
        if (status == ERROR_INSUFFICIENT_BUFFER) continue;
        if (status != ERROR_SUCCESS) return {};
        std::vector<std::pair<std::wstring, std::wstring>> targets;
        for (UINT32 i = 0; i < pathCount; ++i) {
            const auto& path = paths[i];
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
            source.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(source),
                path.sourceInfo.adapterId, path.sourceInfo.id};
            if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS) return {};
            if (_wcsicmp(source.viewGdiDeviceName, info.szDevice) != 0) continue;
            DISPLAYCONFIG_TARGET_DEVICE_NAME target{};
            target.header = {DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, sizeof(target),
                path.targetInfo.adapterId, path.targetInfo.id};
            if (DisplayConfigGetDeviceInfo(&target.header) != ERROR_SUCCESS) return {};
            targets.emplace_back(target.monitorDevicePath, target.monitorFriendlyDeviceName);
        }
        return MakeDisplayScaleContext(std::move(targets));
    }
    return {};
}
}
