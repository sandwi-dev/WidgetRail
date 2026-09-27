// Real DirectComposition ownership proof. The pixel mode deliberately stops
// painting on this thread while the compositor continues rotating the arc.
void CheckLoadingIndicatorComposition(bool pixels, float scale) {
    using namespace widgetrail;
    if (pixels) {
        BOOL enabled{};
        Check(SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0, &enabled, 0) != 0, "system animation policy readable");
        std::cout << "SYSTEM-CLIENT-ANIMATION enabled=" << enabled << '\n';
    }
    const wchar_t* name = L"WidgetRail.LoadingCompositionTest";
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = name;
    Check(RegisterClassW(&wc) != 0, "loading compositor class");
    const int extent = static_cast<int>(128 * scale);
    const auto window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
        name, L"Loading compositor test", WS_POPUP, 40, 40, extent, extent, nullptr, nullptr, wc.hInstance, nullptr);
    Check(window != nullptr, "loading compositor window");
    ComPtr<ID2D1Factory1> factory;
    Check(SUCCEEDED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf())), "loading compositor factory");
    OverlayCompositionSurface surface; std::wstring error;
    Check(surface.Initialize(window, factory.Get(), error), "loading compositor initialization");
    OverlayCompositionSurface::Frame preparation;
    Check(SUCCEEDED(surface.BeginFrame(extent, extent, preparation)), "loading artwork preparation begins");
    ComPtr<ID2D1BitmapRenderTarget> artwork;
    Check(SUCCEEDED(preparation.target->CreateCompatibleRenderTarget(D2D1::SizeF(64, 64), artwork.GetAddressOf())), "loading artwork target");
    artwork->SetDpi(96, 96); artwork->BeginDraw(); artwork->Clear(D2D1::ColorF(0, 0));
    ComPtr<ID2D1SolidColorBrush> brush;
    Check(SUCCEEDED(artwork->CreateSolidColorBrush(D2D1::ColorF(0, 1, 0), brush.GetAddressOf())), "loading artwork tint");
    Check(icons::DrawLoadingIndicator(artwork.Get(), D2D1::RectF(0, 0, 64, 64), brush.Get(), 0, false, 6), "loading artwork arc");
    Check(SUCCEEDED(artwork->EndDraw()), "loading artwork drawing completes");
    ComPtr<ID2D1Bitmap> bitmap;
    Check(SUCCEEDED(artwork->GetBitmap(bitmap.GetAddressOf())), "loading artwork bitmap");
    Check(SUCCEEDED(surface.EndFrame(preparation)), "loading artwork preparation ends");
    const auto makeScene = [&] {
        auto scene = std::make_shared<WidgetCompositionScene>(); scene->authority = L"loading.proof";
        scene->viewport = {0, 0, 128, 128}; scene->scale = scale;
        WidgetCompositionNode background; background.id = L"background"; background.bounds = background.clip = scene->viewport;
        background.solid = D2D1::ColorF(0, 0, 0); scene->nodes.push_back(background);
        WidgetCompositionNode spinner; spinner.id = L"spinner"; spinner.clock = L"page"; spinner.key = L"loading";
        spinner.kind = WidgetCompositionKind::IndeterminateRotation; spinner.bounds = {32, 32, 64, 64}; spinner.clip = scene->viewport;
        scene->nodes.push_back(spinner);
        WidgetCompositionNode raster; raster.id = L"arc"; raster.parent = spinner.id;
        raster.bounds = spinner.bounds; raster.clip = scene->viewport;
        raster.bitmap = resources::UiResource<ID2D1Bitmap>::External(bitmap); raster.rasterLease = std::make_shared<char>();
        scene->nodes.push_back(raster); return scene;
    };
    const auto commit = [&](const std::shared_ptr<WidgetCompositionScene>& scene) {
        OverlayCompositionSurface::Frame frame;
        Check(SUCCEEDED(surface.BeginFrame(extent, extent, frame)), "loading compositor frame begins");
        // Widget layers sit beneath the host's content ink. Keep that ink
        // transparent, as the production composition path does.
        frame.target->Clear(D2D1::ColorF(0, 0)); frame.widgetScene = scene;
        Check(SUCCEEDED(surface.EndFrame(frame)), "loading compositor frame ends");
        OverlayCompositionSurface::CommitTiming timing;
        Check(SUCCEEDED(surface.CommitFrame(frame, true, timing)), "loading compositor frame commits");
    };
    const auto capturePixels = [&] {
        POINT origin{}; ClientToScreen(window, &origin);
        auto screen = GetDC(nullptr); auto capture = CreateCompatibleDC(screen);
        auto captured = CreateCompatibleBitmap(screen, extent, extent);
        Check(screen && capture && captured, "loading screenshot resources");
        const auto old = SelectObject(capture, captured);
        Check(BitBlt(capture, 0, 0, extent, extent, screen, origin.x, origin.y, SRCCOPY) != 0, "loading screenshot copy");
        SelectObject(capture, old);
        BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info.bmiHeader.biWidth = extent; info.bmiHeader.biHeight = -extent;
        info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
        std::vector<BYTE> bytes(static_cast<std::size_t>(extent) * extent * 4);
        Check(GetDIBits(capture, captured, 0, extent, bytes.data(), &info, DIB_RGB_COLORS) == extent, "loading screenshot readback");
        DeleteObject(captured); DeleteDC(capture); ReleaseDC(nullptr, screen); return bytes;
    };
    const auto signature = [&] {
        // Read one coherent desktop frame; per-point GetPixel calls can sample
        // different rotation phases while waiting for desktop readback.
        const auto bytes = capturePixels(); unsigned bits{};
        for (unsigned i = 0; i < 16; ++i) {
            const double angle = i * 6.283185307179586 / 16;
            constexpr double radius = 64 * .76 * .42;
            const auto x = static_cast<int>(std::round((64 + std::cos(angle) * radius) * scale));
            const auto y = static_cast<int>(std::round((64 + std::sin(angle) * radius) * scale));
            const auto offset = static_cast<std::size_t>(y * extent + x) * 4;
            if (bytes[offset + 1] > 120 && bytes[offset + 2] < 60) bits |= 1U << i;
        }
        return bits;
    };
    if (pixels) { ShowWindow(window, SW_SHOWNOACTIVATE); DwmFlush(); }
    auto scene = makeScene(); commit(scene);
    const auto starts = surface.widgetCompositionCounters().animationStarts;
    const auto initialEdges = surface.widgetCompositionCounters();
    Check(starts == 1, "one compositor loop is started");
    commit(scene);
    Check(surface.widgetCompositionCounters().animationStarts == starts, "same-scene refresh preserves the rotation clock");
    Check(surface.widgetCompositionCounters().visualAdds == initialEdges.visualAdds &&
        surface.widgetCompositionCounters().visualRemoves == initialEdges.visualRemoves &&
        surface.widgetCompositionCounters().visualResets == initialEdges.visualResets,
        "unchanged scene preserves all compositor edges");
    const auto counters = surface.widgetCompositionCounters(); const auto paints = surface.paintCounters().content;
    if (pixels) {
        ShowWindow(window, SW_SHOWNOACTIVATE); DwmFlush(); Sleep(60); DwmFlush();
        // Let window/composition startup messages settle before deliberately
        // blocking this thread. Draining only the initially queued messages
        // can sample before the animation has started on the desktop.
        const auto until = GetTickCount64() + 225;
        while (GetTickCount64() < until) {
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message); DispatchMessageW(&message);
            }
            Sleep(1);
        }
        DwmFlush();
        const auto before = signature();
        Check(before != 0 && before != 0xffff, "visible arc has both painted and empty sectors");
        Sleep(225); DwmFlush();
        const auto after = signature();
        std::cout << "ROTATION-SAMPLES before=" << before << " after=" << after << '\n';
        Check(after != before, "spinner rotates while the host thread performs no painting or commits");
        Sleep(1125); DwmFlush();
        Check(signature() != after, "spinner continues beyond its first loop without host updates");
    }
    Check(surface.paintCounters().content == paints && surface.widgetCompositionCounters().rasterUploads == counters.rasterUploads &&
        surface.widgetCompositionCounters().sceneCommits == counters.sceneCommits, "continuous rotation needs no further frames or uploads");
    const auto mapped = surface.MapWidgetCompositionInput({64, 64});
    Check(mapped.x == 64 && mapped.y == 64, "decorative rotation never remaps input");
    scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes[1].bounds.x += 2; scene->nodes[2].bounds.x += 2;
    commit(scene);
    Check(surface.widgetCompositionCounters().animationStarts == starts, "placement changes preserve rotation continuity");
    Check(surface.widgetCompositionCounters().visualAdds == initialEdges.visualAdds &&
        surface.widgetCompositionCounters().visualRemoves == initialEdges.visualRemoves,
        "placement changes update properties without rebuilding visual edges");
    if (!pixels) {
        auto extra = scene->nodes.back(); extra.id = L"extra"; extra.parent.clear();
        scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes.push_back(extra);
        const auto beforeInsert = surface.widgetCompositionCounters(); commit(scene);
        Check(surface.widgetCompositionCounters().visualAdds == beforeInsert.visualAdds + 1 &&
            surface.widgetCompositionCounters().visualRemoves == beforeInsert.visualRemoves,
            "inserting a raster changes only its parent edge");
        scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes.back().parent = L"spinner";
        const auto beforeReparent = surface.widgetCompositionCounters(); commit(scene);
        Check(surface.widgetCompositionCounters().visualAdds == beforeReparent.visualAdds + 1 &&
            surface.widgetCompositionCounters().visualRemoves == beforeReparent.visualRemoves + 1,
            "reparenting detaches the old edge before adding the new edge");
        scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes.pop_back(); commit(scene);
    }
    scene = std::make_shared<WidgetCompositionScene>(*scene); scene->reducedMotion = true;
    scene->nodes[1].bounds.x -= 2; scene->nodes[2].bounds.x -= 2; commit(scene);
    if (pixels) {
        DwmFlush(); Sleep(60); DwmFlush(); const auto still = signature(); Sleep(150); DwmFlush();
        Check(signature() == still, "reduced motion cancels the loop and keeps a stable arc");
    }
    scene = std::make_shared<WidgetCompositionScene>(*scene); scene->reducedMotion = false; commit(scene);
    Check(surface.widgetCompositionCounters().animationStarts == starts + 1, "leaving reduced motion starts a new loop");
    scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes[1].clock = L"new.page"; commit(scene);
    Check(surface.widgetCompositionCounters().animationStarts == starts + 2, "a new input scope owns a fresh loop");
    scene = std::make_shared<WidgetCompositionScene>(*scene); scene->nodes.resize(1); commit(scene);
    if (pixels) { DwmFlush(); Sleep(60); DwmFlush(); Check(signature() == 0, "completed loading removes the animated pixels"); }
    surface.Reset(); DestroyWindow(window); UnregisterClassW(name, wc.hInstance);
    std::cout << "LOADING-COMPOSITION scale=" << scale << " pixels=" << pixels << " passed\n";
}
