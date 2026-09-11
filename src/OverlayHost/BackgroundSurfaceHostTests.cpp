#include "DeclarativeRenderer.h"
#include "BackgroundSurfaceTransitionPolicy.h"
#include "CompositorBackgroundSurfaceCoordinator.h"
#include "RemoteImageCache.h"
#include "WidgetBridgeClient.h"

#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <iostream>
#include <chrono>
#include <condition_variable>
#include <cmath>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace widgetrail {
struct CompositorBackgroundSurfaceCoordinatorTestAccess final {
    using Image = CompositorBackgroundSurfaceCoordinator::Image;
    static void ConfigureTarget(
        ID2D1RenderTarget* target, POINT offset, float scale) {
        CompositorBackgroundSurfaceCoordinator::ConfigureTarget(
            target, RECT{0, 0, 256, 256}, offset, scale);
    }
    static Image Rebase(
        const Image& committed, const Image& incoming,
        DeclarativeRenderer& renderer, ID2D1RenderTarget* target, float scale) {
        CompositorBackgroundSurfaceCoordinator coordinator;
        coordinator.state_.committed = committed;
        coordinator.state_.incoming = incoming;
        coordinator.state_.transitionStartedAt = 1000;
        Image result;
        std::wstring diagnostic;
        if (!coordinator.RebaseOutgoing(
                coordinator.state_, renderer, target, scale, 1200,
                result, diagnostic))
            throw std::runtime_error("compositor pixel rebase failed");
        return result;
    }
    static bool SameDestination(
        const ComputedCompositorBackground& left,
        const ComputedCompositorBackground& right) {
        return CompositorBackgroundSurfaceCoordinator::SameDestination(left, right);
    }
    static std::optional<std::uint64_t> Deadline(
        const ComputedCompositorBackground& descriptor,
        const std::uint64_t observedAt) {
        CompositorBackgroundSurfaceCoordinator coordinator;
        coordinator.state_.proposal =
            CompositorBackgroundSurfaceCoordinator::Proposal{
                {descriptor, {}}, observedAt, 1, false};
        return coordinator.deadline();
    }
    static bool CommitAndCancelAreTransactional() {
        CompositorBackgroundSurfaceCoordinator coordinator;
        coordinator.state_.generation = 3;
        auto changed = coordinator.state_;
        changed.generation = 4;
        coordinator.pendingObservation_ =
            CompositorBackgroundSurfaceCoordinator::PendingObservation{7, changed};
        coordinator.CancelObservation(7);
        if (coordinator.state_.generation != 3 ||
            coordinator.pendingObservation_) return false;
        coordinator.pendingObservation_ =
            CompositorBackgroundSurfaceCoordinator::PendingObservation{8, changed};
        return coordinator.CommitObservation(8) &&
            coordinator.state_.generation == 4 &&
            !coordinator.pendingObservation_;
    }
    static std::uint64_t LiveGeneration(
        const CompositorBackgroundSurfaceCoordinator& coordinator) {
        return coordinator.state_.generation;
    }
};
} // namespace widgetrail

namespace {

void Require(const bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}

void WaitForState(
    const widgetrail::RemoteImageCache& cache,
    const std::wstring_view key,
    const widgetrail::RemoteImageState expected,
    const char* message) {
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
    while (std::chrono::steady_clock::now() < deadline) {
        if (cache.GetState(key) == expected) return;
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    Require(false, message);
}

class ArtworkRequestLog final {
public:
    void Push(const std::wstring_view key) {
        {
            std::scoped_lock lock(mutex_);
            requests_.emplace_back(key);
        }
        changed_.notify_all();
    }

    void WaitForSize(const std::size_t count, const char* message) const {
        std::unique_lock lock(mutex_);
        Require(changed_.wait_for(
                    lock, std::chrono::seconds(2),
                    [&] { return requests_.size() >= count; }),
                message);
    }

    void WaitForBack(const std::wstring_view key, const char* message) const {
        std::unique_lock lock(mutex_);
        Require(changed_.wait_for(
                    lock, std::chrono::seconds(2),
                    [&] { return !requests_.empty() && requests_.back() == key; }),
                message);
    }

    [[nodiscard]] std::size_t size() const {
        std::scoped_lock lock(mutex_);
        return requests_.size();
    }

    [[nodiscard]] std::wstring front() const {
        std::scoped_lock lock(mutex_);
        return requests_.front();
    }

    [[nodiscard]] std::wstring back() const {
        std::scoped_lock lock(mutex_);
        return requests_.back();
    }

    [[nodiscard]] std::wstring at(const std::size_t index) const {
        std::scoped_lock lock(mutex_);
        return requests_.at(index);
    }

    [[nodiscard]] bool Contains(const std::wstring_view key) const {
        std::scoped_lock lock(mutex_);
        return std::ranges::find(requests_, key) != requests_.end();
    }

private:
    mutable std::mutex mutex_;
    mutable std::condition_variable changed_;
    std::vector<std::wstring> requests_;
};

const std::wstring& RequireBackgroundHandle(
    const widgetrail::RenderResult& result,
    const std::wstring_view surfaceId,
    const char* const message) {
    const auto found = result.backgroundArtworkHandles.find(
        std::wstring{surfaceId});
    Require(found != result.backgroundArtworkHandles.end(), message);
    return found->second;
}

const widgetrail::declarative::Rect& RequireElementRect(
    const widgetrail::RenderResult& result,
    const std::wstring_view elementId,
    const char* const message) {
    const auto found = result.elementRects.find(std::wstring{elementId});
    Require(found != result.elementRects.end(), message);
    return found->second;
}

std::array<BYTE, 4> ReadPixel(
    IWICBitmap* const bitmap,
    const UINT x,
    const UINT y) {
    Require(bitmap != nullptr, "pixel fixture omitted its WIC bitmap");
    WICRect region{
        static_cast<INT>(x), static_cast<INT>(y), 1, 1};
    Microsoft::WRL::ComPtr<IWICBitmapLock> lock;
    Require(SUCCEEDED(bitmap->Lock(
                &region, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())) &&
            lock,
        "pixel fixture could not lock the rendered background");
    UINT byteCount{};
    BYTE* bytes{};
    Require(SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)) &&
            bytes && byteCount >= 4U,
        "pixel fixture could not read the rendered background");
    return {bytes[0], bytes[1], bytes[2], bytes[3]};
}

bool HasDiagnostic(
    const widgetrail::RenderResult& result,
    const std::wstring_view code) {
    return std::ranges::any_of(
        result.diagnostics,
        [&](const auto& diagnostic) { return diagnostic.code == code; });
}

void VerifyCompositorRebasePixels(
    ID2D1Factory* d2d, IDWriteFactory* write, IWICImagingFactory* wic) {
    using Microsoft::WRL::ComPtr;
    using Access = widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess;
    ComPtr<IWICBitmap> canvas;
    Require(SUCCEEDED(wic->CreateBitmap(
        256, 256, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad,
        canvas.ReleaseAndGetAddressOf())), "compositor pixel canvas failed");
    ComPtr<ID2D1RenderTarget> target;
    Require(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())), "compositor pixel target failed");
    widgetrail::DeclarativeRenderer renderer{d2d, write, nullptr};
    const auto bitmap = [&](bool green) {
        // An asymmetric image makes a second contain/cover operation observable.
        std::array<UINT32, 8> pixels = green
            ? std::array<UINT32, 8>{0xff00ff00, 0xff00ff00, 0xff0000ff, 0xff0000ff,
                                    0xff00ff00, 0xff00ff00, 0xff0000ff, 0xff0000ff}
            : std::array<UINT32, 8>{0xffff0000, 0xffff0000, 0xffffffff, 0xffffffff,
                                    0xffff0000, 0xffff0000, 0xffffffff, 0xffffffff};
        ComPtr<ID2D1Bitmap> result;
        Require(SUCCEEDED(target->CreateBitmap(
            D2D1::SizeU(4, 2), pixels.data(), 4 * sizeof(UINT32),
            D2D1::BitmapProperties(D2D1::PixelFormat(
                DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96),
            result.ReleaseAndGetAddressOf())), "compositor source bitmap failed");
        return result;
    };
    const auto red = bitmap(false);
    const auto green = bitmap(true);
    const auto paint = [&](const Access::Image& image, float opacity) {
        Require(renderer.PaintCompositorBackground(
            target.Get(), image.descriptor, image.bitmap.Get(), false, opacity,
            image.surfaceComposite), "compositor pixel paint failed");
    };
    const auto begin = [&](float scale) {
        Access::ConfigureTarget(target.Get(), POINT{11, 7}, scale);
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0, 0, 0, 0));
    };
    const auto end = [&] {
        Require(SUCCEEDED(target->EndDraw()), "compositor pixel EndDraw failed");
    };
    const auto read = [&] {
        std::vector<std::array<BYTE, 4>> result;
        for (UINT y = 0; y < 140; ++y)
            for (UINT x = 0; x < 220; ++x)
                result.push_back(ReadPixel(canvas.Get(), x, y));
        return result;
    };
    unsigned int retargetCount{};
    for (const bool clipped : {false, true}) {
        for (const auto bounds : {widgetrail::declarative::Rect{16, 12, 80, 48},
                                 widgetrail::declarative::Rect{16.25F, 12.5F, 79.5F, 47.25F}}) {
            for (const float scale : {1.0F, 1.05F, 1.25F, 1.5F, 2.0F}) {
                for (const float opacity : {1.0F, 0.45F}) {
                    for (const auto fit : {L"cover", L"contain"}) {
                        widgetrail::ComputedCompositorBackground descriptor;
                        descriptor.bounds = bounds;
                        descriptor.imageFit = fit;
                    descriptor.opacity = opacity;
                    if (clipped)
                        descriptor.clipBounds = widgetrail::declarative::Rect{
                            bounds.x + 0.2F, bounds.y + 0.3F,
                            bounds.width - 0.8F, bounds.height - 0.6F};
                        Access::Image committed{descriptor, red};
                        Access::Image incoming{descriptor, green};
                        for (int retarget = 0; retarget < 3; ++retarget) {
                            begin(scale);
                            paint(committed, 1.0F);
                            paint(incoming, 0.875F); // cubic ease-out at 200/400 ms
                            end();
                            const auto before = read();
                            const auto composite = Access::Rebase(
                                committed, incoming, renderer, target.Get(), scale);
                            const auto size = composite.bitmap->GetPixelSize();
                            Require(size.width == static_cast<UINT>(
                                        std::ceil((bounds.x + bounds.width) * scale) -
                                        std::floor(bounds.x * scale)) &&
                                    size.height == static_cast<UINT>(
                                        std::ceil((bounds.y + bounds.height) * scale) -
                                        std::floor(bounds.y * scale)),
                                "rebase did not preserve physical resolution");
                            begin(scale);
                            paint(composite, 1.0F);
                            end();
                            const auto after = read();
                            for (std::size_t p = 0; p < before.size(); ++p)
                                for (std::size_t c = 0; c < 4; ++c)
                                    Require(std::abs(int(before[p][c]) - int(after[p][c])) <= 2,
                                        "retarget changed presented pixels");
                            committed = composite;
                            incoming.bitmap = retarget % 2 == 0 ? red : green;
                            ++retargetCount;
                        }
                    }
                }
            }
        }
    }
    // Check the production drawing transform against actual pixel placement,
    // independently of the before/after comparison above.
    widgetrail::ComputedCompositorBackground descriptor;
    descriptor.bounds = {0, 0, 8, 4};
    descriptor.imageFit = L"cover";
    descriptor.opacity = 1.0F;
    begin(2.0F);
    paint(Access::Image{descriptor, red}, 1.0F);
    end();
    Require(ReadPixel(canvas.Get(), 12, 8)[3] == 255 &&
            ReadPixel(canvas.Get(), 10, 8)[3] == 0 &&
            ReadPixel(canvas.Get(), 12, 6)[3] == 0 &&
            ReadPixel(canvas.Get(), 27, 8)[3] == 0,
        "background draw ignored or scaled the backing-surface offset");
    descriptor.clipBounds = widgetrail::declarative::Rect{2, 1, 4, 2};
    begin(2.0F);
    paint(Access::Image{descriptor, red}, 1.0F);
    end();
    Require(ReadPixel(canvas.Get(), 12, 8)[3] == 0 &&
            ReadPixel(canvas.Get(), 16, 10)[3] == 255 &&
            ReadPixel(canvas.Get(), 24, 10)[3] == 0,
        "compositor artwork escaped the retained ancestor clip");
    std::cout << "Compositor background pixel continuity: "
              << retargetCount << " retargets passed\n";
}

} // namespace

int wmain() {
    using Microsoft::WRL::ComPtr;
    try {
        const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        Require(SUCCEEDED(initialized) || initialized == RPC_E_CHANGED_MODE,
            "COM initialization failed");

        {
            using widgetrail::background_surface_policy::FadeMilliseconds;
            using widgetrail::background_surface_policy::SettleMilliseconds;
            static_assert(SettleMilliseconds == 150);
            static_assert(FadeMilliseconds == 400);
            widgetrail::ComputedCompositorBackground first;
            first.authorityId = L"widget\x1fruntime\x1fpresentation";
            first.widgetInstanceId = L"instance";
            first.nodeId = L"surface";
            first.focusedElementId = L"first";
            first.artworkHandle = L"artwork";
            first.imageFit = L"cover";
            first.bounds = {0, 0, 1280, 720};
            first.snapshotSequence = 1;
            first.resourceGeneration = 9;
            auto compatible = first;
            compatible.focusedElementId = L"same-artwork-second-focus";
            compatible.snapshotSequence = 2;
            Require(widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                        SameDestination(first, compatible),
                "same destination did not dedupe across current-tree refresh");
            auto clipped = compatible;
            clipped.clipBounds = widgetrail::declarative::Rect{1, 1, 1278, 718};
            Require(!widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                         SameDestination(first, clipped),
                "changed viewport clip retained stale compositor pixels");
            auto changed = compatible;
            changed.artworkHandle = L"replacement";
            Require(!widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                         SameDestination(first, changed),
                "changed artwork retained same-destination authority");
            changed = compatible;
            changed.authorityId += L"-stale";
            Require(!widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                         SameDestination(first, changed),
                "changed runtime/presentation authority was not retired");
            const auto deadline =
                widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                    Deadline(first, 1000);
            Require(deadline && *deadline == 1150,
                "coordinator did not own the exact 150 ms settle deadline");
            Require(widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                        CommitAndCancelAreTransactional(),
                "coordinator commit/cancel changed live state out of order");

        }

        {
        std::wstring error;
        auto parsed = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot": {
                "protocolVersion":39,
                "sequence":1,
                "widgetInstanceId":"background-surface-test.instance",
                "activeInputScopeId":"background-surface-test.root",
                "initialFocusId":"background-surface-test.first",
                "root": {
                    "id":"background-surface-test.root",
                    "kind":"backgroundSurface",
                    "artworkHandle":"background-surface-test.artwork",
                    "imageFit":"cover",
                    "usesFocusedDescendantArtwork":true,
                    "children":[{
                        "id":"background-surface-test.foreground",
                        "kind":"stack",
                        "children":[{
                            "id":"background-surface-test.first",
                            "kind":"button",
                            "text":"First background",
                            "actionId":"background-surface-test.first",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.first",
                            "children":[]
                        },{
                            "id":"background-surface-test.second",
                            "kind":"button",
                            "text":"Second background",
                            "actionId":"background-surface-test.second",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.second",
                            "children":[]
                        },{
                            "id":"background-surface-test.ordinary",
                            "kind":"button",
                            "text":"Ordinary action",
                            "actionId":"background-surface-test.ordinary",
                            "children":[]
                        },{
                            "id":"background-surface-test.supersession",
                            "kind":"button",
                            "text":"Superseding background",
                            "actionId":"background-surface-test.supersession",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.supersession",
                            "children":[]
                        },{
                            "id":"background-surface-test.replacement",
                            "kind":"button",
                            "text":"Replacement background",
                            "actionId":"background-surface-test.replacement",
                            "focusBackgroundArtworkHandle":"background-surface-test.focus.replacement",
                            "children":[]
                        }]
                    }]
                }
            }
        })json", error);
        Require(parsed.has_value() && error.empty(),
            "production native snapshot parser rejected the package projection");

        ComPtr<ID2D1Factory> d2d;
        Require(SUCCEEDED(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
            "D2D factory creation failed");
        ComPtr<IDWriteFactory> write;
        Require(SUCCEEDED(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
            "DirectWrite factory creation failed");
        ComPtr<IWICImagingFactory> wic;
        Require(SUCCEEDED(CoCreateInstance(
            CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
            "WIC factory creation failed");
        VerifyCompositorRebasePixels(d2d.Get(), write.Get(), wic.Get());
        ComPtr<IWICBitmap> canvas;
        Require(SUCCEEDED(wic->CreateBitmap(
            640, 420, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
            "WIC canvas creation failed");
        ComPtr<ID2D1RenderTarget> target;
        Require(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), D2D1::RenderTargetProperties(),
            target.ReleaseAndGetAddressOf())),
            "render-target creation failed");

        ArtworkRequestLog requests;
        widgetrail::RemoteImageCache cache(
            {}, {}, {},
            [&](const std::wstring_view key,
                const widgetrail::TrustedArtworkDemandAuthority&,
                std::stop_token) {
                requests.Push(key);
                return widgetrail::TrustedArtworkRequestDisposition::Accepted;
            });
        widgetrail::DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
        widgetrail::DeclarativeRenderOptions options;
        options.artworkWidgetId = L"widgetrail.tests.background-surface";
        options.artworkRuntimeGeneration = L"runtime";
        options.artworkPresentationGeneration = L"presentation";
        options.artworkAuthorityId = L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation";
        options.surfaceBackground = {0.0F, 0.0F, 0.0F, 0.0F};
        std::uint64_t frameTime = 1000;
        options.animationTimestampMilliseconds = frameTime;
        const widgetrail::declarative::Rect viewport{0.0F, 0.0F, 640.0F, 420.0F};
        const auto render = [&](const std::wstring_view focusedId) {
            target->BeginDraw();
            auto result = renderer.Render(target.Get(), *parsed, focusedId, viewport, options);
            Require(SUCCEEDED(target->EndDraw()), "focused background render failed");
            Require(result.succeeded, "focused background render did not succeed");
            return result;
        };
        const auto defaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.artwork");
        const auto firstKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.first");
        const auto secondKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.second");
        const auto replacementKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.replacement");
        const auto supersessionKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.focus.supersession");
        const auto failedReplacementKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface",
                L"background-surface-test.root",
                L"background-surface-test.focus.failure");
        constexpr std::wstring_view png =
            L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
            L"AAAADUlEQVR42mNk+M/wHwAF/gL+Xh8ftQAAAABJRU5ErkJggg==";

        // Exercise the final transition policy independently from the legacy
        // focused-artwork lifecycle matrix below. Distinct one-pixel sources
        // make a mid-transition flatten observable without exposing renderer
        // state through a test-only production API.
        {
            std::wstring retargetError;
            auto retargetSnapshot = widgetrail::testing::ParseWidgetSnapshotResponse(
                R"json({"snapshot":{"protocolVersion":39,"sequence":1,
                "widgetInstanceId":"background.retarget.instance",
                "activeInputScopeId":"background.retarget.root",
                "initialFocusId":"background.retarget.red",
                "root":{"id":"background.retarget.root","kind":"backgroundSurface",
                "artworkHandle":"background.retarget.red","imageFit":"cover",
                "usesFocusedDescendantArtwork":true,"children":[{
                "id":"background.retarget.content","kind":"stack","children":[{
                "id":"background.retarget.red","kind":"button","text":"Red",
                "actionId":"background.retarget.red",
                "focusBackgroundArtworkHandle":"background.retarget.red","children":[]},{
                "id":"background.retarget.green","kind":"button","text":"Green",
                "actionId":"background.retarget.green",
                "focusBackgroundArtworkHandle":"background.retarget.green","children":[]},{
                "id":"background.retarget.blue","kind":"button","text":"Blue",
                "actionId":"background.retarget.blue",
                "focusBackgroundArtworkHandle":"background.retarget.blue","children":[]},{
                "id":"background.retarget.ordinary","kind":"button","text":"Ordinary",
                "actionId":"background.retarget.ordinary","children":[]}]}]}}})json",
                retargetError);
            Require(retargetSnapshot.has_value() && retargetError.empty(),
                "native parser rejected the retarget fixture");

            constexpr std::wstring_view redPng =
                L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARn"
                L"QU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/jPwPAfAAUA"
                L"Af+mXJtdAAAAAElFTkSuQmCC";
            constexpr std::wstring_view greenPng =
                L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARn"
                L"QU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY2D4z/AfAAQB"
                L"Af9eLuGlAAAAAElFTkSuQmCC";
            constexpr std::wstring_view bluePng =
                L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARn"
                L"QU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY2Bg+P8fAAMC"
                L"Af/Jsq3uAAAAAElFTkSuQmCC";
            constexpr std::wstring_view retargetWidget =
                L"widgetrail.tests.background-retarget";
            constexpr std::wstring_view retargetSurface =
                L"background.retarget.root";

            widgetrail::RemoteImageCache retargetCache(
                {}, {}, {}, [](const std::wstring_view,
                               const widgetrail::TrustedArtworkDemandAuthority&,
                               std::stop_token) {
                    return widgetrail::TrustedArtworkRequestDisposition::Accepted;
                });
            const auto admitArtwork = [&](const std::wstring_view handle,
                                          const std::wstring_view content) {
                const auto key = widgetrail::RemoteImageCache::TrustedArtworkKey(
                    retargetWidget, retargetSurface, handle);
                Require(retargetCache.RequestTrustedArtwork(key) ==
                            widgetrail::RemoteImageRequestResult::Queued,
                    "retarget fixture did not queue exact artwork authority");
                Require(retargetCache.SupplyTrustedArtwork(
                            retargetWidget, handle, L"image/png",
                            std::wstring{content}),
                    "retarget fixture rejected exact artwork completion");
                WaitForState(
                    retargetCache, key, widgetrail::RemoteImageState::Ready,
                    "retarget fixture artwork did not become ready");
            };
            admitArtwork(L"background.retarget.red", redPng);
            admitArtwork(L"background.retarget.green", greenPng);
            admitArtwork(L"background.retarget.blue", bluePng);

            widgetrail::DeclarativeRenderer retargetRenderer{
                d2d.Get(), write.Get(), &retargetCache};
            widgetrail::DeclarativeRenderOptions retargetOptions;
            retargetOptions.artworkWidgetId = std::wstring{retargetWidget};
            retargetOptions.artworkRuntimeGeneration = L"retarget-runtime";
            retargetOptions.artworkPresentationGeneration = L"retarget-presentation";
            retargetOptions.artworkAuthorityId =
                L"widgetrail.tests.background-retarget\x1fruntime\x1fpresentation";
            retargetOptions.surfaceBackground = {0.0F, 0.0F, 0.0F, 0.0F};
            std::uint64_t retargetTime = 1000;
            const auto renderRetarget = [&](const std::wstring_view focusedId) {
                retargetOptions.animationTimestampMilliseconds = retargetTime;
                target->BeginDraw();
                auto result = retargetRenderer.Render(
                    target.Get(), *retargetSnapshot, focusedId, viewport,
                    retargetOptions);
                Require(SUCCEEDED(target->EndDraw()),
                    "retarget background draw failed");
                Require(result.succeeded, "retarget background render failed");
                return result;
            };

            const auto initial = renderRetarget(L"background.retarget.ordinary");
            Require(RequireBackgroundHandle(
                        initial, retargetSurface,
                        "retarget fixture omitted initial committed artwork") ==
                    L"background.retarget.red" &&
                    !initial.animationActive,
                "first ready background did not commit without animation");

            const auto greenStart = renderRetarget(L"background.retarget.green");
            const auto greenStartPixel = ReadPixel(canvas.Get(), 620, 400);
            Require(greenStart.animationActive &&
                    greenStart.backgroundSurfaceAnimationDamage &&
                    HasDiagnostic(greenStart, L"background_crossfade_start") &&
                    greenStartPixel[2] > 240U && greenStartPixel[1] < 15U,
                "first idle replacement did not start from the committed red frame");

            retargetTime = 1100;
            const auto greenProgress = renderRetarget(L"background.retarget.green");
            const auto greenProgressPixel = ReadPixel(canvas.Get(), 620, 400);
            Require(greenProgress.animationActive &&
                    greenProgressPixel[1] > greenProgressPixel[2],
                "400 ms crossfade did not advance toward the green target");

            const auto blueCandidate = renderRetarget(L"background.retarget.blue");
            Require(blueCandidate.animationActive &&
                    HasDiagnostic(
                        blueCandidate, L"background_crossfade_candidate") &&
                    !HasDiagnostic(
                        blueCandidate, L"background_crossfade_retarget"),
                "active transition did not retain blue as metadata-only latest candidate");

            // Returning to the committed red source is itself a valid latest
            // target. Replacing blue with red resets the 150 ms stability clock.
            retargetTime = 1149;
            const auto redCandidate = renderRetarget(L"background.retarget.red");
            Require(HasDiagnostic(
                        redCandidate, L"background_crossfade_candidate") &&
                    !HasDiagnostic(
                        redCandidate, L"background_crossfade_retarget"),
                "return-to-committed did not replace the prior latest candidate");
            retargetTime = 1298;
            const auto beforeStable = renderRetarget(L"background.retarget.red");
            Require(beforeStable.animationActive &&
                    !HasDiagnostic(
                        beforeStable, L"background_crossfade_retarget"),
                "latest candidate retargeted before remaining unchanged for 150 ms");

            // A half-pixel target cannot preserve exact surface-space geometry.
            // The failed rebase must retain the latest candidate, not snap or
            // cancel the active transition. Restoring exact geometry at the
            // same timestamp then permits the one bounded flatten-and-retarget.
            retargetTime = 1299;
            target->SetTransform(D2D1::Matrix3x2F::Translation(0.5F, 0.0F));
            const auto rejectedRebase = renderRetarget(L"background.retarget.red");
            target->SetTransform(D2D1::Matrix3x2F::Identity());
            Require(rejectedRebase.animationActive &&
                    HasDiagnostic(
                        rejectedRebase, L"background_crossfade_rebase-failed") &&
                    !HasDiagnostic(
                        rejectedRebase, L"background_crossfade_retarget"),
                "inexact geometry did not fail closed while retaining transition work");
            const auto redRetarget = renderRetarget(L"background.retarget.red");
            const auto redRetargetPixel = ReadPixel(canvas.Get(), 620, 400);
            Require(redRetarget.animationActive &&
                    HasDiagnostic(
                        redRetarget, L"background_crossfade_retarget") &&
                    redRetargetPixel[1] > redRetargetPixel[2],
                "stable return target did not begin from the flattened current blend");

            retargetTime = 1400;
            const auto noProposal =
                renderRetarget(L"background.retarget.ordinary");
            Require(noProposal.animationActive &&
                    RequireBackgroundHandle(
                        noProposal, retargetSurface,
                        "no-proposal frame omitted active artwork") ==
                        L"background.retarget.red",
                "no-proposal focus cancelled or blanked the active transition");
            retargetTime = 1499;
            const auto redMidpoint = renderRetarget(L"background.retarget.red");
            const auto redMidpointPixel = ReadPixel(canvas.Get(), 620, 400);
            Require(redMidpoint.animationActive &&
                    redMidpointPixel[2] > redMidpointPixel[1] &&
                    redMidpointPixel[1] > 0U,
                "retarget snapped instead of blending over the full duration");
            retargetTime = 1698;
            Require(renderRetarget(L"background.retarget.red").animationActive,
                "400 ms crossfade settled one millisecond early");
            retargetTime = 1699;
            const auto redSettled = renderRetarget(L"background.retarget.red");
            Require(!redSettled.animationActive &&
                    RequireBackgroundHandle(
                        redSettled, retargetSurface,
                        "settled retarget omitted committed red artwork") ==
                        L"background.retarget.red",
                "400 ms crossfade did not settle at its exact boundary");

            // Queue blue immediately before green settles. The active timer
            // ends at 2400, leaving one static absolute wake for the remaining
            // candidate stability interval instead of pretending it is motion.
            retargetTime = 2000;
            Require(renderRetarget(L"background.retarget.green").animationActive,
                "settle fixture did not start its first transition");
            retargetTime = 2390;
            Require(HasDiagnostic(
                        renderRetarget(L"background.retarget.blue"),
                        L"background_crossfade_candidate"),
                "settle fixture did not queue its latest candidate");
            retargetTime = 2400;
            const auto settleWake = renderRetarget(L"background.retarget.blue");
            Require(!settleWake.animationActive &&
                    !settleWake.backgroundSurfaceAnimationDamage &&
                    settleWake.backgroundSurfaceSettleWake &&
                    settleWake.backgroundSurfaceSettleWake->deadlineMilliseconds == 2540,
                "settled transition did not publish one non-animating 150 ms wake");
            retargetTime = 2539;
            const auto beforeWake = renderRetarget(L"background.retarget.blue");
            Require(!beforeWake.animationActive &&
                    beforeWake.backgroundSurfaceSettleWake &&
                    beforeWake.backgroundSurfaceSettleWake->deadlineMilliseconds == 2540,
                "static candidate was consumed before its exact settle deadline");
            retargetTime = 2540;
            const auto afterWake = renderRetarget(L"background.retarget.blue");
            Require(afterWake.animationActive &&
                    !afterWake.backgroundSurfaceSettleWake &&
                    HasDiagnostic(afterWake, L"background_crossfade_start"),
                "settle wake did not start the stable latest candidate exactly once");

            const auto bitmapStats = retargetRenderer.GetImageBitmapCacheStats();
            Require(bitmapStats.entries <= bitmapStats.maximumEntries &&
                    bitmapStats.bytes <= bitmapStats.maximumBytes,
                "retarget fixture exceeded bounded renderer bitmap ownership");

            widgetrail::DeclarativeRenderer compositorRenderer{
                d2d.Get(), write.Get(), &retargetCache};
            auto compositorOptions = retargetOptions;
            compositorOptions.compositorBackgroundAvailable = true;
            compositorOptions.animationTimestampMilliseconds = 3000;
            target->BeginDraw();
            const auto eligible = compositorRenderer.Render(
                target.Get(), *retargetSnapshot, L"background.retarget.red",
                viewport, compositorOptions);
            Require(SUCCEEDED(target->EndDraw()) && eligible.succeeded &&
                    eligible.compositorBackground &&
                    eligible.compositorBackground->nodeId == retargetSurface &&
                    eligible.compositorBackground->focusedElementId ==
                        L"background.retarget.red" &&
                    eligible.compositorBackground->artworkHandle ==
                        L"background.retarget.red",
                "transparent current-tree surface did not emit exact compositor authority");
            {
                widgetrail::DeclarativeRenderer scaledRenderer{
                    d2d.Get(), write.Get(), &retargetCache};
                auto scaledOptions = compositorOptions;
                scaledOptions.pixelScale = 1.05F;
                const widgetrail::declarative::Rect insetViewport{1, 1, 598, 358};
                target->BeginDraw();
                const auto scaled = scaledRenderer.Render(
                    target.Get(), *retargetSnapshot, L"background.retarget.red",
                    insetViewport, scaledOptions);
                Require(SUCCEEDED(target->EndDraw()) && scaled.succeeded &&
                        scaled.compositorBackground &&
                        scaled.compositorBackground->clipBounds,
                    "105 percent layout rounding incorrectly selected raster fallback");
                const auto& background = *scaled.compositorBackground;
                Require(std::abs(background.bounds.x -
                            background.clipBounds->x) > 0.01F &&
                        background.clipBounds->x >= insetViewport.x &&
                        background.clipBounds->y >= insetViewport.y,
                    "scaled background did not retain separate image-fit and clip bounds");
            }

            auto opaqueSnapshot = *retargetSnapshot;
            widgetrail::WidgetNode opaqueRoot;
            opaqueRoot.id = L"background.retarget.opaque-root";
            opaqueRoot.kind = L"stack";
            opaqueRoot.imageSource = L"data:image/png;base64," +
                std::wstring{redPng};
            opaqueRoot.children.push_back(std::move(opaqueSnapshot.root));
            opaqueSnapshot.root = std::move(opaqueRoot);
            target->BeginDraw();
            const auto fallback = compositorRenderer.Render(
                target.Get(), opaqueSnapshot, L"background.retarget.red",
                viewport, compositorOptions);
            Require(SUCCEEDED(target->EndDraw()) && fallback.succeeded &&
                    !fallback.compositorBackground &&
                    HasDiagnostic(
                        fallback, L"background_crossfade_compositor-fallback"),
                "painted ancestor did not retain the raster fallback boundary");

            ComPtr<ID2D1Factory1> compositionFactory;
            Require(SUCCEEDED(D2D1CreateFactory(
                        D2D1_FACTORY_TYPE_SINGLE_THREADED,
                        IID_PPV_ARGS(compositionFactory.ReleaseAndGetAddressOf()))),
                "compositor fixture could not create its D2D factory");
            HWND compositionWindow = CreateWindowExW(
                WS_EX_TOOLWINDOW, L"STATIC", L"WIDGE-184 compositor fixture",
                WS_POPUP, 0, 0, 640, 360, nullptr, nullptr,
                GetModuleHandleW(nullptr), nullptr);
            Require(compositionWindow != nullptr,
                "compositor fixture could not create its hidden HWND");
            widgetrail::OverlayCompositionSurface composition;
            std::wstring compositionError;
            Require(composition.Initialize(
                        compositionWindow, compositionFactory.Get(), compositionError),
                "compositor fixture could not initialize the real DComp owner");
            widgetrail::CompositorBackgroundSurfaceCoordinator coordinator;
            widgetrail::DeclarativeRenderer integratedRenderer{
                compositionFactory.Get(), write.Get(), &retargetCache};
            auto integratedOptions = compositorOptions;
            const widgetrail::declarative::Rect integratedViewport{
                0, 0, 640, 360};
            const auto renderIntegrated = [&](const std::wstring_view focus,
                                              const std::uint64_t now) {
                widgetrail::OverlayCompositionSurface::Frame content;
                Require(SUCCEEDED(composition.BeginFrame(
                            widgetrail::OverlayCompositionSurface::Layer::Content,
                            640, 360, 0, 0, nullptr, content)),
                    "integrated fixture could not begin Content");
                content.target->SetDpi(96, 96);
                content.target->Clear(D2D1::ColorF(0, 0, 0, 0));
                integratedOptions.animationTimestampMilliseconds = now;
                auto result = integratedRenderer.Render(
                    content.target.Get(), *retargetSnapshot, focus,
                    integratedViewport, integratedOptions);
                Require(result.succeeded && result.compositorBackground &&
                        SUCCEEDED(composition.EndFrame(content)),
                    "integrated fixture did not produce a compositor descriptor");
                return std::pair{std::move(content), std::move(result)};
            };
            const auto commitObservation = [&](
                widgetrail::OverlayCompositionSurface::Frame content,
                widgetrail::CompositorBackgroundSurfaceCoordinator::Observation observation) {
                std::vector<widgetrail::OverlayCompositionSurface::Frame*> frames{&content};
                for (auto& background : observation.frames)
                    frames.push_back(&background);
                widgetrail::OverlayCompositionSurface::CommitTiming timing;
                Require(SUCCEEDED(composition.CommitFrames(
                            frames, false, timing, nullptr,
                            observation.presentation
                                ? &*observation.presentation : nullptr)) &&
                        coordinator.CommitObservation(observation.transactionId),
                    "integrated host transaction did not publish atomically");
            };

            auto [redContent, redResult] = renderIntegrated(
                L"background.retarget.red", 3000);
            auto redObservation = coordinator.Observe(
                *redResult.compositorBackground, integratedRenderer, composition,
                640, 360, 1.0F, 3000);
            Require(redObservation.has_value(),
                "initial integrated observation was rejected");
            commitObservation(std::move(redContent), std::move(*redObservation));

            auto [greenContent, greenResult] = renderIntegrated(
                L"background.retarget.green", 3100);
            auto greenObservation = coordinator.Observe(
                *greenResult.compositorBackground, integratedRenderer, composition,
                640, 360, 1.0F, 3100);
            Require(greenObservation && greenObservation->presentation &&
                    greenObservation->presentation->prepared,
                "ordinary host frame did not prepare the settled candidate");
            commitObservation(std::move(greenContent), std::move(*greenObservation));
            const auto contentBeforeTransition =
                composition.paintCounters().content;
            std::wstring advanceDiagnostic;
            Require(coordinator.Advance(
                        greenResult.compositorBackground, integratedRenderer,
                        composition, 640, 360, 1.0F, 3250,
                        advanceDiagnostic) == widgetrail::
                            CompositorBackgroundSurfaceCoordinator::
                                AdvanceDisposition::Advanced &&
                    composition.paintCounters().content == contentBeforeTransition,
                "committed background-only transition repainted Content");

            auto [blueContent, blueResult] = renderIntegrated(
                L"background.retarget.blue", 3300);
            auto blueObservation = coordinator.Observe(
                *blueResult.compositorBackground, integratedRenderer, composition,
                640, 360, 1.0F, 3300);
            Require(blueObservation.has_value(),
                "active-transition retarget observation was rejected");
            commitObservation(std::move(blueContent), std::move(*blueObservation));
            const auto contentBeforeRebase = composition.paintCounters().content;
            Require(coordinator.Advance(
                        blueResult.compositorBackground, integratedRenderer,
                        composition, 640, 360, 1.0F, 3450,
                        advanceDiagnostic) == widgetrail::
                            CompositorBackgroundSurfaceCoordinator::
                                AdvanceDisposition::Advanced &&
                    composition.paintCounters().content == contentBeforeRebase,
                "rapid background-only rebase repainted Content");

            auto [cancelledContent, cancelledResult] = renderIntegrated(
                L"background.retarget.red", 3500);
            auto cancelledObservation = coordinator.Observe(
                *cancelledResult.compositorBackground, integratedRenderer,
                composition, 640, 360, 1.0F, 3500);
            Require(cancelledObservation.has_value(),
                "cancelled transaction fixture was not created");
            const auto generationBeforeCancel =
                widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                    LiveGeneration(coordinator);
            coordinator.CancelObservation(cancelledObservation->transactionId);
            Require(widgetrail::CompositorBackgroundSurfaceCoordinatorTestAccess::
                        LiveGeneration(coordinator) == generationBeforeCancel &&
                    !coordinator.CommitObservation(
                        cancelledObservation->transactionId),
                "failed/cancelled host transaction published live state");
            composition.AbandonFrame(cancelledContent);
            coordinator.Retire(composition);
            composition.Reset();
            DestroyWindow(compositionWindow);
            retargetCache.Shutdown();
        }

        // First establish the authored default as the last committed image.
        parsed->root.children[0].children[0].focusBackgroundArtworkHandle.clear();
        (void)render(L"background-surface-test.first");
        requests.WaitForSize(1U,
            "ordinary background request did not reach its demand owner");
        Require(requests.size() == 1U && requests.front() == defaultKey,
            "ordinary background render changed exact default artwork authority");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.artwork", L"image/png", std::wstring(png)),
            "default artwork completion was rejected");
        WaitForState(cache, defaultKey, widgetrail::RemoteImageState::Ready,
            "default artwork did not become ready");
        const auto defaultFrame = render(L"background-surface-test.first");
        Require(defaultFrame.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "default background did not become the committed image");

        // A replacement target owns a different Direct2D resource domain.
        // The renderer must retire the committed target-local bitmap before
        // the render pass copies transition state, then recreate it from the
        // bounded decoded cache for the exact current proposal.
        ComPtr<IWICBitmap> replacementCanvas;
        Require(SUCCEEDED(wic->CreateBitmap(
            640, 420, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad,
            replacementCanvas.ReleaseAndGetAddressOf())),
            "replacement WIC canvas creation failed");
        ComPtr<ID2D1RenderTarget> replacementTarget;
        Require(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            replacementCanvas.Get(), D2D1::RenderTargetProperties(),
            replacementTarget.ReleaseAndGetAddressOf())),
            "replacement render-target creation failed");
        replacementTarget->BeginDraw();
        const auto replacementDomainFrame = renderer.Render(
            replacementTarget.Get(), *parsed,
            L"background-surface-test.first", viewport, options);
        Require(SUCCEEDED(replacementTarget->EndDraw()),
            "resource-domain replacement reused a prior-target background bitmap");
        Require(replacementDomainFrame.succeeded &&
                replacementDomainFrame.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.artwork",
            "resource-domain replacement did not recreate the exact committed background");

        parsed->root.children[0].children[0].focusBackgroundArtworkHandle =
            L"background-surface-test.focus.first";
        const auto firstPending = render(L"background-surface-test.first");
        requests.WaitForSize(2U,
            "focused background request did not reach its demand owner");
        Require(requests.size() == 2U && requests.back() == firstKey,
            "focused background did not request the exact focused handle");
        Require(firstPending.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "pending focused artwork blanked the last committed image");
        Require(cache.FailTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.first"),
            "focused artwork failure was not admitted");
        const auto firstFailed = render(L"background-surface-test.first");
        Require(firstFailed.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "failed focused artwork blanked the last committed image");

        const auto plan = renderer.PlanFocusUpdate(
            *parsed, L"background-surface-test.first",
            L"background-surface-test.second", viewport);
        Require(plan && plan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
            "focus-background transition did not remain paint-only");
        const auto secondPending = render(L"background-surface-test.second");
        requests.WaitForSize(3U,
            "second focus request did not reach its demand owner");
        Require(requests.size() == 3U && requests.back() == secondKey,
            "second focus did not request its exact artwork handle once");
        Require(secondPending.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "second pending focus did not retain the committed image");

        // An older focus completion may enter the bounded cache, but cannot
        // overwrite the exact current focus selection.
        Require(!cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.first", L"image/png", std::wstring(png)),
            "failed older focus unexpectedly accepted a replacement completion");
        const auto afterLateFirst = render(L"background-surface-test.second");
        Require(afterLateFirst.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.artwork",
            "older focus completion overwrote the current pending selection");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.second", L"image/png", std::wstring(png)),
            "current focused artwork completion was rejected");
        WaitForState(cache, secondKey, widgetrail::RemoteImageState::Ready,
            "current focused artwork did not become ready");
        const auto secondReady = render(L"background-surface-test.second");
        Require(secondReady.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.focus.second",
            "current focused artwork did not begin the bounded replacement");

        const auto supersessionPending =
            render(L"background-surface-test.supersession");
        Require(requests.size() == 3U &&
                !requests.Contains(supersessionKey),
            "unstable latest focus started decode work before its settle deadline");
        Require(supersessionPending.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.second" &&
                std::any_of(
                    supersessionPending.diagnostics.begin(),
                    supersessionPending.diagnostics.end(),
                    [](const auto& diagnostic) {
                        return diagnostic.code ==
                            L"background_crossfade_candidate";
                    }),
            "A-to-B-to-unstable-C did not retain one metadata-only latest candidate and active B transition");

        // Re-enter B from the decoded cache, then deterministically cross the
        // complete transition interval before exercising the older
        // B-to-pending-C retention oracle below.
        (void)render(L"background-surface-test.second");
        frameTime += 400;
        options.animationTimestampMilliseconds = frameTime;
        const auto secondSettled = render(L"background-surface-test.second");
        Require(secondSettled.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.focus.second" &&
                !secondSettled.backgroundSurfaceAnimationDamage,
            "focused B did not become the exact committed image after 400 ms");

        const auto requestsBeforeOrdinary = requests.size();
        const auto ordinaryFrame = render(L"background-surface-test.ordinary");
        Require(requests.size() == requestsBeforeOrdinary,
            "ordinary focus requested a replacement background");
        Require(ordinaryFrame.backgroundArtworkHandles.at(L"background-surface-test.root") ==
            L"background-surface-test.focus.second",
            "ordinary focus did not retain the last ready focused artwork");

        const auto replacementPending = render(L"background-surface-test.replacement");
        requests.WaitForSize(requestsBeforeOrdinary + 1U,
            "replacement request did not reach its demand owner");
        Require(requests.size() == requestsBeforeOrdinary + 1U &&
                requests.back() == replacementKey,
            "subsequent focused background did not request its exact handle once");
        Require(replacementPending.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.second",
            "pending subsequent artwork did not retain the prior focused image");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.replacement", L"image/png", std::wstring(png)),
            "subsequent focused artwork completion was rejected");
        WaitForState(cache, replacementKey, widgetrail::RemoteImageState::Ready,
            "subsequent focused artwork did not become ready");
        const auto replacementReady = render(L"background-surface-test.replacement");
        Require(replacementReady.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.replacement",
            "subsequent ready artwork did not replace the retained image");

        // A target-backed frame may paint a ready replacement and still fail
        // on a later node. That rejected frame cannot publish its staged
        // replacement into the renderer's committed presentation state.
        const auto acceptedCandidate =
            render(L"background-surface-test.second");
        Require(HasDiagnostic(
                    acceptedCandidate, L"background_crossfade_candidate"),
            "transaction fixture did not publish its accepted B candidate");
        options.failAfterNodeDrawForTesting = true;
        target->BeginDraw();
        const auto rejectedReplacement = renderer.Render(
            target.Get(), *parsed, L"background-surface-test.replacement",
            viewport, options);
        Require(SUCCEEDED(target->EndDraw()),
            "post-draw replacement rejection did not complete target drawing");
        Require(!rejectedReplacement.succeeded,
            "post-draw replacement fixture did not reject the frame");
        options.failAfterNodeDrawForTesting = false;

        frameTime += 150;
        options.animationTimestampMilliseconds = frameTime;
        const auto afterRejectedReplacement =
            render(L"background-surface-test.second");
        Require(afterRejectedReplacement.animationActive &&
                afterRejectedReplacement.backgroundSurfaceAnimationDamage &&
                RequireBackgroundHandle(
                    afterRejectedReplacement,
                    L"background-surface-test.root",
                    "preserved transaction candidate omitted B") ==
                    L"background-surface-test.focus.second" &&
                HasDiagnostic(
                    afterRejectedReplacement,
                    L"background_crossfade_retarget"),
            "rejected frame cleared the previously accepted B candidate");

        frameTime += 400;
        options.animationTimestampMilliseconds = frameTime;

        // Explicit clearing is transaction-owned too: a later render failure
        // must leave the exact prior committed focused background available.
        (void)render(L"background-surface-test.replacement");
        parsed->root.usesFocusedDescendantArtwork = false;
        options.failAfterNodeDrawForTesting = true;
        target->BeginDraw();
        const auto rejectedClear = renderer.Render(
            target.Get(), *parsed, L"background-surface-test.ordinary",
            viewport, options);
        Require(SUCCEEDED(target->EndDraw()),
            "post-draw clear rejection did not complete target drawing");
        Require(!rejectedClear.succeeded,
            "post-draw clear fixture did not reject the frame");
        options.failAfterNodeDrawForTesting = false;
        parsed->root.usesFocusedDescendantArtwork = true;
        const auto afterRejectedClear = render(L"background-surface-test.ordinary");
        Require(afterRejectedClear.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                L"background-surface-test.focus.replacement",
            "rejected explicit clear mutated committed background state");

        frameTime += 400;
        options.animationTimestampMilliseconds = frameTime;
        const auto replacementSettled =
            render(L"background-surface-test.replacement");
        Require(replacementSettled.backgroundArtworkHandles.at(
                    L"background-surface-test.root") ==
                    L"background-surface-test.focus.replacement" &&
                !replacementSettled.backgroundSurfaceAnimationDamage,
            "replacement C did not become committed after the full transition");

        parsed->root.children[0].children[0].focusBackgroundArtworkHandle =
            L"background-surface-test.focus.failure";
        const auto failedReplacementPending =
            render(L"background-surface-test.first");
        requests.WaitForBack(failedReplacementKey,
            "failed replacement request did not reach its demand owner");
        Require(requests.back() == failedReplacementKey,
            "failed replacement did not request its exact handle");
        Require(RequireBackgroundHandle(
                    failedReplacementPending,
                    L"background-surface-test.root",
                    "pending later override omitted the committed focused image") ==
                L"background-surface-test.focus.replacement",
            "pending later override did not retain the prior focused image");
        Require(cache.FailTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.focus.failure"),
            "later focused artwork failure was not admitted");
        const auto failedReplacement = render(L"background-surface-test.first");
        Require(RequireBackgroundHandle(
                    failedReplacement,
                    L"background-surface-test.root",
                    "failed later override omitted the committed focused image") ==
                L"background-surface-test.focus.replacement",
            "failed later override replaced the prior focused image with default");
        (void)render(L"background-surface-test.replacement");

        const auto replacementPlan = renderer.PlanFocusUpdate(
            *parsed, L"background-surface-test.replacement",
            L"background-surface-test.ordinary", viewport);
        Require(replacementPlan &&
                replacementPlan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
            "sparse focused-background transition did not remain paint-only");
        const auto replacementSurfaceBounds = RequireElementRect(
            replacementReady, L"background-surface-test.root",
            "ready replacement omitted BackgroundSurface geometry");
        Require(replacementPlan->damage.x <= replacementSurfaceBounds.x + 0.01F &&
                replacementPlan->damage.y <= replacementSurfaceBounds.y + 0.01F &&
                replacementPlan->damage.x + replacementPlan->damage.width >=
                    replacementSurfaceBounds.x + replacementSurfaceBounds.width - 0.01F &&
                replacementPlan->damage.y + replacementPlan->damage.height >=
                    replacementSurfaceBounds.y + replacementSurfaceBounds.height - 0.01F &&
                replacementPlan->damage.x >= viewport.x - 0.01F &&
                replacementPlan->damage.y >= viewport.y - 0.01F &&
                replacementPlan->damage.x + replacementPlan->damage.width <=
                    viewport.x + viewport.width + 0.01F &&
                replacementPlan->damage.y + replacementPlan->damage.height <=
                    viewport.y + viewport.height + 0.01F,
            "focused-background change did not contain exact full-surface damage");

        parsed->root.artworkHandle = L"background-surface-test.artwork.changed";
        const auto changedDefaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.root",
            L"background-surface-test.artwork.changed");
        const auto changedDefaultPending = render(L"background-surface-test.ordinary");
        requests.WaitForBack(changedDefaultKey,
            "changed default request did not reach its demand owner");
        Require(requests.back() == changedDefaultKey &&
                !changedDefaultPending.backgroundArtworkHandles.contains(
                    L"background-surface-test.root"),
            "changed default did not reset the retained focused artwork");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface",
            L"background-surface-test.artwork.changed", L"image/png", std::wstring(png)),
            "changed default artwork completion was rejected");
        WaitForState(cache, changedDefaultKey, widgetrail::RemoteImageState::Ready,
            "changed default artwork did not become ready");
        const auto changedDefaultReady = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    changedDefaultReady,
                    L"background-surface-test.root",
                    "changed default omitted the reset surface artwork") ==
                L"background-surface-test.artwork.changed",
            "changed default did not initialize the reset surface");

        (void)render(L"background-surface-test.replacement");
        parsed->root.usesFocusedDescendantArtwork = false;
        (void)render(L"background-surface-test.ordinary");
        parsed->root.usesFocusedDescendantArtwork = true;
        const auto afterExplicitReset = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterExplicitReset,
                    L"background-surface-test.root",
                    "explicit reset omitted the authored default artwork") ==
                L"background-surface-test.artwork.changed",
            "explicit reset retained the prior focused override");

        (void)render(L"background-surface-test.replacement");
        const auto afterFocusAuthorityLoss = render(L"");
        Require(RequireBackgroundHandle(
                    afterFocusAuthorityLoss,
                    L"background-surface-test.root",
                    "focus loss omitted the committed focused background") ==
                L"background-surface-test.focus.replacement",
            "host-owned focus loss discarded the exact committed focused background");

        (void)render(L"background-surface-test.replacement");
        options.artworkAuthorityId =
            L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation.changed";
        const auto changedAuthority = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    changedAuthority,
                    L"background-surface-test.root",
                    "new presentation authority omitted its default artwork") ==
                L"background-surface-test.artwork.changed",
            "new presentation authority inherited a prior focused override");
        options.artworkAuthorityId =
            L"widgetrail.tests.background-surface\x1fruntime\x1fpresentation";
        const auto priorAuthorityReentered =
            render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    priorAuthorityReentered,
                    L"background-surface-test.root",
                    "retired presentation re-entry omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "returning to a retired presentation authority resurrected its old override");

        (void)render(L"background-surface-test.replacement");
        renderer.ForgetWidgetState(parsed->instanceId);
        const auto afterRetirement = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterRetirement,
                    L"background-surface-test.root",
                    "widget retirement omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "widget retirement retained a focused override");

        (void)render(L"background-surface-test.replacement");
        const auto retainedSurface = parsed->root;
        parsed->root.kind = L"stack";
        parsed->root.imageSource.clear();
        parsed->root.artworkHandle.clear();
        parsed->root.usesFocusedDescendantArtwork = false;
        (void)render(L"background-surface-test.ordinary");
        parsed->root = retainedSurface;
        const auto afterSurfaceReadded = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    afterSurfaceReadded,
                    L"background-surface-test.root",
                    "re-added surface omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "removed and re-added surface resurrected its old focused override");

        auto responsiveSurface = parsed->root;
        responsiveSurface.visibleWhen = L"expandedOnly";
        auto compactAction = responsiveSurface.children[0].children[2];
        compactAction.id = L"background-surface-test.compact";
        compactAction.actionId = L"background-surface-test.compact";
        compactAction.visibleWhen = L"compactOnly";
        auto responsiveRoot = responsiveSurface;
        responsiveRoot.id = L"background-surface-test.responsive-root";
        responsiveRoot.kind = L"stack";
        responsiveRoot.imageSource.clear();
        responsiveRoot.artworkHandle.clear();
        responsiveRoot.imageFit.clear();
        responsiveRoot.usesFocusedDescendantArtwork = false;
        responsiveRoot.visibleWhen.clear();
        responsiveRoot.children = {responsiveSurface, compactAction};
        parsed->root = responsiveRoot;
        options.responsiveViewport = widgetrail::declarative::Size{960.0F, 540.0F};
        const auto responsiveOverride =
            render(L"background-surface-test.replacement");
        Require(RequireBackgroundHandle(
                    responsiveOverride,
                    L"background-surface-test.root",
                    "expanded surface omitted focused artwork") ==
                L"background-surface-test.focus.replacement",
            "expanded surface did not establish its ready focused override");
        options.responsiveViewport = widgetrail::declarative::Size{959.0F, 540.0F};
        (void)render(L"background-surface-test.compact");
        options.responsiveViewport = widgetrail::declarative::Size{960.0F, 540.0F};
        const auto responsiveReadded = render(L"background-surface-test.ordinary");
        Require(RequireBackgroundHandle(
                    responsiveReadded,
                    L"background-surface-test.root",
                    "responsive re-entry omitted default artwork") ==
                L"background-surface-test.artwork.changed",
            "responsive mode away and back resurrected an absent surface override");
        parsed->root = responsiveSurface;
        parsed->root.visibleWhen.clear();
        options.responsiveViewport.reset();

        std::wstring nestedError;
        auto nested = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":2,
            "widgetInstanceId":"background.nested","activeInputScopeId":"nested.outer",
            "initialFocusId":"nested.action","root":{"id":"nested.outer","kind":"backgroundSurface",
            "artworkHandle":"nested.outer.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.content","kind":"stack","children":[
            {"id":"nested.inner","kind":"backgroundSurface",
            "artworkHandle":"nested.inner.default","imageFit":"cover",
            "usesFocusedDescendantArtwork":true,
            "children":[{"id":"nested.inner.content","kind":"stack","children":[
            {"id":"nested.action","kind":"button","text":"Nested action",
            "actionId":"nested.action","focusBackgroundArtworkHandle":"nested.focus",
            "children":[]},{"id":"nested.ordinary","kind":"button",
            "text":"Nested ordinary","actionId":"nested.ordinary","children":[]}]}]},
            {"id":"nested.outer.action","kind":"button","text":"Outer action",
            "actionId":"nested.outer.action",
            "focusBackgroundArtworkHandle":"nested.outer.focus","children":[]}]}]}}
        })json", nestedError);
        Require(nested.has_value() && nestedError.empty(),
            "native parser rejected nested focused-background ownership");
        const auto renderNested = [&](const std::wstring_view focusedId) {
            target->BeginDraw();
            auto result = renderer.Render(
                target.Get(), *nested, focusedId, viewport, options);
            Require(SUCCEEDED(target->EndDraw()), "nested focused-background draw failed");
            Require(result.succeeded, "nested focused-background render failed");
            return result;
        };
        const auto requestCountBeforeNested = requests.size();
        const auto nestedFrame = renderNested(L"nested.action");
        const auto expectedOuterKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface", L"nested.outer",
                L"nested.outer.default");
        const auto expectedInnerKey =
            widgetrail::RemoteImageCache::TrustedArtworkKey(
                L"widgetrail.tests.background-surface", L"nested.inner",
                L"nested.focus");
        requests.WaitForSize(requestCountBeforeNested + 2U,
            "nested focus requests did not reach their demand owner");
        Require(requests.size() == requestCountBeforeNested + 2U,
            "nested focus did not preserve two independent surface owners");
        Require(requests.at(requestCountBeforeNested) == expectedOuterKey &&
                requests.at(requestCountBeforeNested + 1U) == expectedInnerKey,
            "nested BackgroundSurface did not form a hard focus-artwork boundary");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.outer.default",
            L"image/png", std::wstring(png)),
            "nested outer artwork completion was rejected");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.focus",
            L"image/png", std::wstring(png)),
            "nested focused artwork completion was rejected");
        WaitForState(cache, expectedOuterKey, widgetrail::RemoteImageState::Ready,
            "nested outer artwork did not become ready");
        WaitForState(cache, expectedInnerKey, widgetrail::RemoteImageState::Ready,
            "nested focused artwork did not become ready");
        const auto nestedReady = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    nestedReady, L"nested.outer",
                    "nested ready frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    nestedReady, L"nested.inner",
                    "nested ready frame omitted inner artwork") ==
                    L"nested.focus",
            "nested surfaces did not retain independent exact ready handles");

        const auto outerFocusKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface", L"nested.outer",
            L"nested.outer.focus");
        const auto innerDefaultKey = widgetrail::RemoteImageCache::TrustedArtworkKey(
            L"widgetrail.tests.background-surface", L"nested.inner",
            L"nested.inner.default");
        const auto requestsBeforeOuterFocus = requests.size();
        const auto outerFocusPending = renderNested(L"nested.outer.action");
        requests.WaitForSize(requestsBeforeOuterFocus + 2U,
            "outer focus requests did not reach their demand owner");
        Require(requests.size() == requestsBeforeOuterFocus + 2U &&
                requests.at(requestsBeforeOuterFocus) == outerFocusKey &&
                requests.at(requestsBeforeOuterFocus + 1U) == innerDefaultKey &&
                RequireBackgroundHandle(
                    outerFocusPending, L"nested.outer",
                    "outer-focus pending frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                !outerFocusPending.backgroundArtworkHandles.contains(L"nested.inner"),
            "selecting a different surface did not retire the prior surface override");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.outer.focus",
            L"image/png", std::wstring(png)),
            "outer focused artwork completion was rejected");
        WaitForState(cache, outerFocusKey, widgetrail::RemoteImageState::Ready,
            "outer focused artwork did not become ready");
        const auto outerFocusReady = renderNested(L"nested.outer.action");
        Require(RequireBackgroundHandle(
                    outerFocusReady, L"nested.outer",
                    "outer-focus ready frame omitted outer artwork") ==
                    L"nested.outer.focus" &&
                !outerFocusReady.backgroundArtworkHandles.contains(L"nested.inner"),
            "different selected surface did not establish independent authority");
        const auto innerFocusRestored = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    innerFocusRestored, L"nested.outer",
                    "inner-focus frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerFocusRestored, L"nested.inner",
                    "inner-focus frame omitted inner artwork") ==
                    L"nested.focus",
            "selecting the inner surface retained the different outer override");

        auto& outerContent = nested->root.children.front();
        const auto retainedInner = outerContent.children.front();
        outerContent.children.erase(outerContent.children.begin());
        const auto innerRemoved = renderNested(L"");
        Require(RequireBackgroundHandle(
                    innerRemoved, L"nested.outer",
                    "inner-removal frame omitted outer artwork") ==
                L"nested.outer.default",
            "removing the inner surface altered the outer surface handle");
        outerContent.children.insert(outerContent.children.begin(), retainedInner);
        const auto innerReaddedPending = renderNested(L"nested.ordinary");
        requests.WaitForBack(innerDefaultKey,
            "re-added inner request did not reach its demand owner");
        Require(requests.back() == innerDefaultKey &&
                !innerReaddedPending.backgroundArtworkHandles.contains(L"nested.inner") &&
                RequireBackgroundHandle(
                    innerReaddedPending, L"nested.outer",
                    "inner-readded pending frame omitted outer artwork") ==
                    L"nested.outer.default",
            "re-added inner surface resurrected its old focused override");
        Require(cache.SupplyTrustedArtwork(
            L"widgetrail.tests.background-surface", L"nested.inner.default",
            L"image/png", std::wstring(png)),
            "nested inner default completion was rejected");
        WaitForState(cache, innerDefaultKey, widgetrail::RemoteImageState::Ready,
            "nested inner default did not become ready");
        const auto innerReaddedReady = renderNested(L"nested.ordinary");
        Require(RequireBackgroundHandle(
                    innerReaddedReady, L"nested.outer",
                    "inner-readded ready frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerReaddedReady, L"nested.inner",
                    "inner-readded ready frame omitted inner artwork") ==
                    L"nested.inner.default",
            "re-added inner surface did not initialize independently from default");

        const auto outerOverrideBeforeNonOpt =
            renderNested(L"nested.outer.action");
        Require(RequireBackgroundHandle(
                    outerOverrideBeforeNonOpt, L"nested.outer",
                    "non-opt-in fixture omitted outer artwork") ==
                    L"nested.outer.focus" &&
                RequireBackgroundHandle(
                    outerOverrideBeforeNonOpt, L"nested.inner",
                    "non-opt-in fixture omitted inner artwork") ==
                    L"nested.inner.default",
            "non-opt-in boundary fixture did not establish the outer override");
        outerContent.children.front().usesFocusedDescendantArtwork = false;
        const auto nestedNonOpt = renderNested(L"nested.action");
        Require(RequireBackgroundHandle(
                    nestedNonOpt, L"nested.outer",
                    "nested non-opt-in frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    nestedNonOpt, L"nested.inner",
                    "nested non-opt-in frame omitted inner artwork") ==
                    L"nested.inner.default",
            "focus inside a nested non-opt-in surface retained the outer override");
        outerContent.children.front().usesFocusedDescendantArtwork = true;
        const auto innerReset = renderNested(L"nested.ordinary");
        Require(RequireBackgroundHandle(
                    innerReset, L"nested.outer",
                    "inner-reset frame omitted outer artwork") ==
                    L"nested.outer.default" &&
                RequireBackgroundHandle(
                    innerReset, L"nested.inner",
                    "inner-reset frame omitted inner artwork") ==
                    L"nested.inner.default",
            "resetting the inner surface altered the independent outer owner");

        std::wstring v38Error;
        const auto rejectedV38 = widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":38,"sequence":1,
            "widgetInstanceId":"background.v38","activeInputScopeId":"surface",
            "initialFocusId":"action","root":{"id":"surface","kind":"backgroundSurface",
            "usesFocusedDescendantArtwork":true,"children":[{"id":"action","kind":"button",
            "text":"Action","actionId":"action","focusBackgroundArtworkHandle":"artwork.focus",
            "children":[]}]}}
        })json", v38Error);
        Require(!rejectedV38.has_value(),
            "native parser admitted focused backgrounds below protocol v39");
        std::wstring emptyHandleError;
        Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":1,
            "widgetInstanceId":"background.empty","activeInputScopeId":"root",
            "initialFocusId":"action","root":{"id":"root","kind":"stack",
            "children":[{"id":"action","kind":"button","text":"Action",
            "actionId":"action","focusBackgroundArtworkHandle":"","children":[]}]}}
        })json", emptyHandleError),
            "native parser admitted an empty focused-background handle");
        std::wstring wrongOwnerError;
        Require(!widgetrail::testing::ParseWidgetSnapshotResponse(R"json({
            "snapshot":{"protocolVersion":39,"sequence":1,
            "widgetInstanceId":"background.owner","activeInputScopeId":"root",
            "initialFocusId":"action","root":{"id":"root","kind":"stack",
            "usesFocusedDescendantArtwork":false,"children":[{"id":"action",
            "kind":"button","text":"Action","actionId":"action","children":[]}]}}
        })json", wrongOwnerError),
            "native parser admitted focused-background ownership on a non-surface");
        cache.Shutdown();
        std::cout << "BackgroundSurface host tests: 12/12 passed.\n";
        }
        if (SUCCEEDED(initialized)) CoUninitialize();
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "FAIL: " << exception.what() << '\n';
        return 1;
    }
}
