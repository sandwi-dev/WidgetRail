// Compiled only into the isolated console test target. Exercise OverlayApp's
// actual DrawWidget/publication/scheduler methods using a WIC target and a fake
// session transport. No platform initialization, controller hooks, bridge process
// or visible overlay is started.
#include <iostream>
#include <mutex>
#include <wincodec.h>

int OverlayApp::RunPublicationTests() {
    std::size_t checks{};
    const auto check = [&](const bool value, const char* message) {
        ++checks;
        if (!value) throw std::runtime_error(message);
    };
    struct Environment final {
        std::wstring previous;
        bool existed{};
        Environment() {
            const auto length = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
            existed = length != 0;
            if (existed) {
                previous.resize(length);
                previous.resize(GetEnvironmentVariableW(L"LOCALAPPDATA", previous.data(), length));
            }
            // Retain isolated diagnostic evidence under the invocation's workspace.
            const auto root = std::filesystem::absolute(std::filesystem::path(L"artifacts/native-collection/host-publication") /
                (std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64())));
            std::filesystem::create_directories(root);
            if (!SetEnvironmentVariableW(L"LOCALAPPDATA", root.c_str())) throw std::runtime_error("Test state isolation failed");
        }
        ~Environment() { SetEnvironmentVariableW(L"LOCALAPPDATA", existed ? previous.c_str() : nullptr); }
    } environment;
    const auto apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        check(SUCCEEDED(apartment), "test COM apartment");
        {
            using namespace widgetrail;
            struct Source { std::mutex mutex; WidgetSnapshot snapshot; };
            auto source = std::make_shared<Source>();
            auto& document = source->snapshot;
            document.protocolVersion = 59; document.instanceId = L"frame.instance"; document.sequence = 1;
            document.activeInputScopeId = L"page"; document.initialFocusId = L"expanded";
            document.surface.emplace(); document.surface->preferredWidth = 640; document.surface->preferredHeight = 480;
            document.root.id = L"page"; document.root.kind = L"stack"; document.root.inputScopeId = L"page";
            const auto length = [](double value) { WidgetStyleValue style; style.kind = L"length"; style.number = value; style.unit = L"px"; return style; };
            const auto button = [&](std::wstring id) {
                WidgetNode node; node.id = std::move(id); node.kind = L"button"; node.text = node.id; node.actionId = L"choose";
                node.baseStyle[L"height"] = length(44); return node;
            };
            auto expanded = button(L"expanded"), compact = button(L"compact");
            expanded.visibleWhen = L"expandedOnly"; compact.visibleWhen = L"compactOnly";
            expanded.focusPersistenceId = compact.focusPersistenceId = L"shared-control";
            WidgetNode items; items.id = L"items"; items.kind = L"scroll"; items.scrollAxis = L"vertical";
            items.collectionLayout = WidgetNode::CollectionLayout{false, 44}; items.collectionAnchorKey = L"key.0";
            items.initialChildFocusId = L"item.40";
            for (int i = 0; i < 64; ++i) {
                auto item = button(L"item." + std::to_wstring(i)); item.collectionItemKey = L"key." + std::to_wstring(i);
                items.children.push_back(std::move(item));
            }
            document.root.children = {expanded, compact, items};
            WidgetDescriptor descriptor;
            descriptor.id = L"fixture"; descriptor.name = L"Fixture"; descriptor.instanceId = document.instanceId;
            descriptor.runtimeGeneration = L"runtime"; descriptor.presentationGeneration = L"presentation";
            const auto publication = [source](long long base, WidgetPresentationTransactionKind kind, long long recovery) {
                std::scoped_lock lock(source->mutex);
                WidgetPresentationPublication value;
                value.checkpoint = source->snapshot; value.requestBaseSequence = base;
                value.transactionKind = kind; value.recoveryOriginSequence = recovery;
                return WidgetSessionOperationResult<WidgetPresentationPublication>::Success(std::move(value));
            };
            WidgetSessionOperations operations;
            operations.ensureStarted = [](std::stop_token) { return WidgetSessionOperationResult<bool>::Success(true); };
            operations.listWidgets = [descriptor](std::stop_token) { return WidgetSessionOperationResult<WidgetCatalogSnapshot>::Success({{descriptor}, true}); };
            operations.establish = [publication](std::stop_token, std::wstring_view, WidgetLifecycleState, long long base,
                WidgetPresentationTransactionKind kind, long long recovery) { return publication(base, kind, recovery); };
            operations.getSnapshot = [publication](std::stop_token, std::wstring_view, long long base,
                WidgetPresentationTransactionKind kind, long long recovery) { return publication(base, kind, recovery); };
            operations.setLifecycle = [](std::stop_token, std::wstring_view, WidgetLifecycleState) { return WidgetSessionOperationResult<bool>::Success(true); };
            operations.bridgeSessionGeneration = [] { return 1LL; };
            operations.deferPresentationAdmission = true;
            OverlayApp app(std::move(operations));
            app.window_ = CreateWindowExW(0, L"STATIC", L"WidgetRail publication test", WS_POPUP, 0, 0, 800, 700,
                nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
            check(app.window_ != nullptr && !IsWindowVisible(app.window_), "test owner stays hidden");
            check(SUCCEEDED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, app.d2dFactory_.GetAddressOf())), "D2D factory");
            check(SUCCEEDED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                reinterpret_cast<IUnknown**>(app.writeFactory_.GetAddressOf()))), "text factory");
            ComPtr<IWICImagingFactory> wic; ComPtr<IWICBitmap> bitmap;
            check(SUCCEEDED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(wic.GetAddressOf()))), "WIC factory");
            check(SUCCEEDED(wic->CreateBitmap(1200, 1000, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, bitmap.GetAddressOf())), "WIC canvas");
            check(SUCCEEDED(app.d2dFactory_->CreateWicBitmapRenderTarget(bitmap.Get(), D2D1::RenderTargetProperties(), app.renderTarget_.GetAddressOf())), "offscreen target");
            check(app.EnsureGraphicsResources(800, 700, 96), "main host graphics setup");
            app.imageCache_ = std::make_unique<RemoteImageCache>(RemoteImageLimits{}, RemoteImageCache::CompletionCallback{},
                [](std::wstring_view, std::stop_token, const RemoteImageLimits&) { return RemoteImageFetchResult{E_ABORT, {}, {}}; });
            app.declarativeRenderer_ = std::make_unique<DeclarativeRenderer>(app.d2dFactory_.Get(), app.writeFactory_.Get(), app.imageCache_.get());
            app.overlayTransitionSample_.contentOpacity = 1;
            app.state_ = OverlayState({{L"fixture"}, L"fixture", true}, {L"fixture"});
            check(app.state_.Dispatch(Command::ToggleOverlay) && app.WidgetOwnsInputFocus(L"fixture"), "test widget input owner");
            check(app.sessions_.EstablishCatalog().has_value(), "fake transport catalog");
            app.sessions_.SetLifecycleTargets({{L"fixture", WidgetLifecycleState::Interactive}});
            const auto admit = [&](long long sequence) {
                const auto deadline = GetTickCount64() + 3000;
                while (!app.sessions_.Snapshot(L"fixture") || app.sessions_.Snapshot(L"fixture")->sequence != sequence) {
                    (void)app.sessions_.TakeEvents();
                    app.PumpCollectionPreparation();
                    check(GetTickCount64() < deadline, "main host preparation/admission converges");
                    Sleep(1);
                }
            };
            admit(1);
            app.interactionSession_.SetFocus(L"fixture", *app.SnapshotFor(L"fixture"), L"expanded");
            const auto draw = [&] {
                const auto surface = app.DesiredWidgetSurfaceTarget();
                app.renderTarget_->BeginDraw(); app.renderTarget_->Clear(D2D1::ColorF(0, 0));
                app.DrawWidget(surface.windowWidthDip, surface.windowHeightDip, 1, CompositionPaintLayer::Content);
                check(SUCCEEDED(app.renderTarget_->EndDraw()) && app.lastWidgetRenderResult_.succeeded, "production DrawWidget succeeds");
                check(app.pendingRendererPublicationId_ != 0, "DrawWidget stages renderer publication");
                return app.pendingRendererPublicationId_;
            };
            const auto rejected = draw();
            check(app.interactionSession_.focusedElementId() == L"expanded", "drawing cannot reconcile responsive focus before submission");
            app.BlockWidgetFramePublication(); // inject failed EndDraw/composition submission
            check(!app.InteractionSnapshotFor(L"fixture") && !app.declarativeRenderer_->CommitFramePublication(rejected), "failed frame blocks input and rejects renderer token");
            check(app.interactionSession_.focusedElementId() == L"expanded", "failed frame preserves desired focus");
            (void)draw(); check(app.CompleteWidgetFramePublication(), "replacement frame publishes");
            check(app.interactionSession_.focusedElementId() == L"compact" && app.InteractionSnapshotFor(L"fixture"), "responsive recovery occurs only after successful submission");
            {
                std::scoped_lock lock(source->mutex); ++document.sequence;
                document.focusGroupEntryRequest = FocusGroupEntryRequest{1, L"items"};
            }
            check(app.sessions_.RequestSnapshot(L"fixture"), "request group-entry snapshot"); admit(2);
            auto authority = *app.InteractionAuthority(L"fixture", *app.SnapshotFor(L"fixture"));
            (void)app.interactionSession_.ObserveFocusGroupEntryRequest(authority, input::FocusGroupEntryAdmission::Active);
            (void)draw();
            check(app.interactionSession_.FocusGroupEntryRequestPending(authority), "drawn group request stays pending before submission");
            app.BlockWidgetFramePublication();
            check(app.interactionSession_.FocusGroupEntryRequestPending(authority), "failed submission preserves one-shot group request");
            (void)draw(); check(app.CompleteWidgetFramePublication(), "group replacement publishes");
            check(!app.interactionSession_.FocusGroupEntryRequestPending(authority) &&
                app.interactionSession_.focusedElementId() == L"item.40" && app.lastWidgetRenderResult_.focusRects.contains(L"item.40"),
                "group target is revealed and consumed together on successful publication");
            accessibility::ActionRequest reveal;
            reveal.kind = accessibility::ActionKind::Realize; reveal.widgetId = L"fixture"; reveal.runtimeGeneration = L"runtime";
            reveal.snapshotSequence = 2; reveal.activeInputScopeId = L"page"; reveal.nodeId = L"item.60";
            app.pendingAccessibilityRealization_ = reveal;
            app.accessibilityRealizationReady_ = false;
            for (unsigned i = 0; app.PumpAccessibilityRealization(); ++i) check(i < 128, "UIA preparation bounded");
            check(app.accessibilityRealizationReady_, "UIA target is prepared");
            (void)draw(); app.BlockWidgetFramePublication();
            check(app.pendingAccessibilityRealization_.has_value(), "unseen UIA reveal survives failed submission");
            (void)draw(); check(app.CompleteWidgetFramePublication(), "UIA replacement publishes");
            check(!app.pendingAccessibilityRealization_ && app.interactionSession_.focusedElementId() == L"item.40" &&
                app.lastWidgetRenderResult_.focusRects.contains(L"item.60"), "UIA reveal acknowledges visibility without stealing controller focus");
            (void)draw();
            {
                std::scoped_lock lock(source->mutex); ++document.sequence; document.focusGroupEntryRequest.reset();
                document.root.children.back().children.front().text = L"New source";
            }
            check(app.sessions_.RequestSnapshot(L"fixture"), "request superseding source"); admit(3);
            check(!app.CompleteWidgetFramePublication() && !app.InteractionSnapshotFor(L"fixture"),
                "a frame from a superseded source cannot acknowledge current input authority");
            (void)draw(); check(app.CompleteWidgetFramePublication() && app.InteractionSnapshotFor(L"fixture"), "current source recovers publication");
            check(!IsWindowVisible(app.window_), "host test never showed an overlay");
            DestroyWindow(app.window_); app.window_ = nullptr;
        }
        CoUninitialize();
        std::cout << "WidgetFramePublicationTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& error) {
        if (SUCCEEDED(apartment)) CoUninitialize();
        std::cerr << "WidgetFramePublicationTests failed after " << checks << " checks: " << error.what() << '\n';
        return 1;
    }
}
