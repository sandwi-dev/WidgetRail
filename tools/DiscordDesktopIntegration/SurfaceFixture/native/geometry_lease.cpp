// Generated-fixture helper only. No foreign-process window, focus, visibility,
// controller or media operations. Node-API v8; no V8/node-addon-api dependency.
#include <windows.h>
#include <node_api.h>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>

namespace {
constexpr wchar_t MarkerName[] = L"WidgetRail.GeneratedGeometryLease.v1";
constexpr std::int64_t MaximumDimension = 32768;
constexpr std::int64_t MaximumCoordinate = 1048576;
constexpr UINT PlacementFlags = SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER;
constexpr napi_type_tag LeaseType{0xa91d16aa6ab34af1ULL, 0x89a2d42db5d38de9ULL};

struct Failure { const char* code; const char* message; DWORD nativeError = 0; };
struct PendingException {};

void Check(napi_status status) {
    if (status == napi_ok) return;
    if (status == napi_pending_exception) throw PendingException{};
    throw Failure{"napi_failure", "Node-API could not complete the geometry request."};
}
void Require(bool condition, const char* code, const char* message) {
    if (!condition) throw Failure{code, message};
}
void NativeCheck(BOOL success, const char* code, const char* message) {
    if (!success) throw Failure{code, message, GetLastError()};
}
void CheckPending(napi_env env) {
    bool pending = false; Check(napi_is_exception_pending(env, &pending));
    if (pending) throw PendingException{};
}

napi_value Throw(napi_env env, const Failure& failure) noexcept {
    bool pending = false;
    if (napi_is_exception_pending(env, &pending) != napi_ok || pending) return nullptr;
    napi_value code{}, message{}, error{}, number{};
    if (napi_create_string_utf8(env, failure.code, NAPI_AUTO_LENGTH, &code) != napi_ok ||
        napi_create_string_utf8(env, failure.message, NAPI_AUTO_LENGTH, &message) != napi_ok ||
        napi_create_error(env, code, message, &error) != napi_ok) return nullptr;
    if (failure.nativeError != 0 && napi_create_uint32(env, failure.nativeError, &number) == napi_ok)
        (void)napi_set_named_property(env, error, "win32Error", number);
    (void)napi_throw(env, error);
    return nullptr;
}
template<typename Function> napi_value Boundary(napi_env env, Function&& action) noexcept {
    try { return action(); }
    catch (const PendingException&) { return nullptr; }
    catch (const Failure& failure) { return Throw(env, failure); }
    catch (...) { return Throw(env, {"native_geometry_failure", "Native geometry request failed."}); }
}

class PhysicalDpiScope {
    DPI_AWARENESS_CONTEXT previous_ = nullptr;
public:
    PhysicalDpiScope() {
        previous_ = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        NativeCheck(previous_ != nullptr, "dpi_context_unavailable", "Per-monitor-aware physical geometry is unavailable.");
    }
    ~PhysicalDpiScope() noexcept { if (previous_) (void)SetThreadDpiAwarenessContext(previous_); }
    PhysicalDpiScope(const PhysicalDpiScope&) = delete;
    PhysicalDpiScope& operator=(const PhysicalDpiScope&) = delete;
};

struct Geometry {
    RECT outer{};
    RECT client{}; // Screen coordinates, physical pixels, not client-relative DIPs.
    UINT dpi = 0;
    bool visible = false, minimized = false, maximized = false;
};
struct RequestedRect { std::int64_t x, y, width, height; };
std::int64_t Width(const RECT& rect) noexcept { return std::int64_t(rect.right) - rect.left; }
std::int64_t Height(const RECT& rect) noexcept { return std::int64_t(rect.bottom) - rect.top; }
bool Equal(const RECT& a, const RECT& b) noexcept {
    return a.left == b.left && a.top == b.top && a.right == b.right && a.bottom == b.bottom;
}

void ValidateWindow(HWND window) {
    Require(window && IsWindow(window), "invalid_window", "Window is no longer valid.");
    DWORD process = 0;
    const auto thread = GetWindowThreadProcessId(window, &process);
    Require(thread == GetCurrentThreadId() && process == GetCurrentProcessId(),
        "foreign_window", "Only a window on this process and calling thread is admitted.");
    const auto style = GetWindowLongPtrW(window, GWL_STYLE);
    Require((style & WS_CHILD) == 0 && GetAncestor(window, GA_ROOT) == window,
        "non_top_level_window", "Only an own-thread top-level window is admitted.");
}
void ValidateOrdinary(HWND window) {
    Require(!IsIconic(window) && !IsZoomed(window), "window_show_state_changed",
        "Minimized and maximized windows are outside this generated geometry fixture.");
}
Geometry Observe(HWND window) {
    ValidateWindow(window);
    Geometry result{};
    NativeCheck(GetWindowRect(window, &result.outer), "window_rect_failed", "Unable to read the physical outer rectangle.");
    RECT client{};
    NativeCheck(GetClientRect(window, &client), "client_rect_failed", "Unable to read the client rectangle.");
    POINT first{client.left, client.top}, last{client.right, client.bottom};
    NativeCheck(ClientToScreen(window, &first), "client_origin_failed", "Unable to map the client origin.");
    NativeCheck(ClientToScreen(window, &last), "client_extent_failed", "Unable to map the client extent.");
    result.client = {first.x < last.x ? first.x : last.x, first.y < last.y ? first.y : last.y,
        first.x > last.x ? first.x : last.x, first.y > last.y ? first.y : last.y};
    result.dpi = GetDpiForWindow(window);
    Require(result.dpi != 0, "window_dpi_failed", "Unable to read the current window DPI.");
    result.visible = IsWindowVisible(window) != FALSE;
    result.minimized = IsIconic(window) != FALSE;
    result.maximized = IsZoomed(window) != FALSE;
    return result;
}

struct Lease {
    HWND window = nullptr;
    DWORD process = 0, thread = 0;
    Geometry original{}, restored{};
    bool active = true, busy = false, restorationObserved = false;
    HANDLE Marker() const noexcept { return reinterpret_cast<HANDLE>(const_cast<Lease*>(this)); }
    bool OwnsMarker() const noexcept {
        if (!active || !window || GetCurrentThreadId() != thread || GetCurrentProcessId() != process || !IsWindow(window)) return false;
        DWORD actualProcess = 0;
        if (GetWindowThreadProcessId(window, &actualProcess) != thread || actualProcess != process) return false;
        return GetPropW(window, MarkerName) == Marker();
    }
    void Validate() const {
        Require(active, "lease_released", "The geometry lease has been released.");
        Require(OwnsMarker(), "stale_lease", "The window or its exact lease generation is no longer current.");
        ValidateWindow(window);
    }
    ~Lease() noexcept {
        // GC must not dispatch Electron window messages and re-enter JavaScript.
        // The fixture keeps this lease strongly reachable through explicit
        // restore/disconnect/finish. Finalization retires only its exact marker.
        if (OwnsMarker()) (void)RemovePropW(window, MarkerName);
        active = false;
    }
};
class Operation {
    Lease& lease_;
public:
    explicit Operation(Lease& lease) : lease_(lease) {
        lease_.Validate();
        Require(!lease_.busy, "operation_in_progress", "A geometry operation is already in progress.");
        lease_.busy = true;
    }
    ~Operation() noexcept { lease_.busy = false; }
};

HWND ReadHandle(napi_env env, napi_value value) {
    bool buffer = false; Check(napi_is_buffer(env, value, &buffer));
    Require(buffer, "invalid_handle_buffer", "Use the exact native-window-handle Buffer.");
    void* bytes = nullptr; size_t size = 0;
    Check(napi_get_buffer_info(env, value, &bytes, &size));
    Require(bytes && size == sizeof(HWND), "invalid_handle_buffer", "Native window handle Buffer has the wrong size.");
    HWND window = nullptr; std::memcpy(&window, bytes, sizeof(window));
    return window;
}
napi_value OneArgument(napi_env env, napi_callback_info info) {
    size_t count = 2; napi_value arguments[2]{};
    Check(napi_get_cb_info(env, info, &count, arguments, nullptr, nullptr));
    Require(count == 1, "invalid_arguments", "Exactly one argument is required.");
    return arguments[0];
}
Lease& ReadLease(napi_env env, napi_callback_info info, napi_value* argument = nullptr) {
    size_t count = argument ? 2 : 1; napi_value arguments[2]{}, self{};
    Check(napi_get_cb_info(env, info, &count, arguments, &self, nullptr));
    Require(count == (argument ? 1u : 0u), "invalid_arguments", "Unexpected geometry lease arguments.");
    bool tagged = false; Check(napi_check_object_type_tag(env, self, &LeaseType, &tagged));
    Require(tagged, "invalid_lease", "Use the original geometry lease as the receiver.");
    void* pointer = nullptr; Check(napi_unwrap(env, self, &pointer));
    Require(pointer != nullptr, "invalid_lease", "The geometry lease is unavailable.");
    if (argument) *argument = arguments[0];
    return *static_cast<Lease*>(pointer);
}
std::int64_t ReadInteger(napi_env env, napi_value object, const char* key) {
    napi_value value{}; Check(napi_get_named_property(env, object, key, &value));
    napi_valuetype type{}; Check(napi_typeof(env, value, &type));
    Require(type == napi_number, "invalid_bounds", "Geometry values must be finite integer numbers.");
    double number = 0; Check(napi_get_value_double(env, value, &number));
    Require(std::isfinite(number) && number == std::trunc(number) &&
        number >= static_cast<double>(std::numeric_limits<std::int32_t>::min()) &&
        number <= static_cast<double>(std::numeric_limits<std::int32_t>::max()),
        "invalid_bounds", "Geometry values must be finite signed 32-bit integers.");
    return static_cast<std::int64_t>(number);
}
RequestedRect ReadRect(napi_env env, napi_value object) {
    napi_valuetype type{}; Check(napi_typeof(env, object, &type));
    bool array = false; Check(napi_is_array(env, object, &array));
    Require(type == napi_object && !array, "invalid_bounds", "Use an object containing x, y, width and height.");
    napi_value keys{}; uint32_t count = 0;
    Check(napi_get_all_property_names(env, object, napi_key_own_only, napi_key_all_properties, napi_key_numbers_to_strings, &keys));
    Check(napi_get_array_length(env, keys, &count));
    Require(count == 4, "invalid_bounds", "Bounds must contain exactly x, y, width and height.");
    unsigned present = 0;
    for (uint32_t index = 0; index < count; ++index) {
        napi_value key{}; Check(napi_get_element(env, keys, index, &key));
        Check(napi_typeof(env, key, &type));
        Require(type == napi_string, "invalid_bounds", "Bounds keys must be ordinary field names.");
        char name[8]{}; size_t length = 0;
        Check(napi_get_value_string_utf8(env, key, name, sizeof(name), &length));
        const unsigned bit = length == 1 && name[0] == 'x' ? 1u : length == 1 && name[0] == 'y' ? 2u :
            length == 5 && std::memcmp(name, "width", 5) == 0 ? 4u :
            length == 6 && std::memcmp(name, "height", 6) == 0 ? 8u : 0u;
        Require(bit != 0 && (present & bit) == 0, "invalid_bounds", "Unexpected bounds field.");
        present |= bit;
    }
    Require(present == 15u, "invalid_bounds", "Bounds fields are incomplete.");
    // Read each value once. Getters/proxies can execute JavaScript; authority is
    // checked again after this snapshot and before any native window mutation.
    RequestedRect rect{ReadInteger(env, object, "x"), ReadInteger(env, object, "y"),
        ReadInteger(env, object, "width"), ReadInteger(env, object, "height")};
    Require(rect.x >= -MaximumCoordinate && rect.x <= MaximumCoordinate &&
        rect.y >= -MaximumCoordinate && rect.y <= MaximumCoordinate &&
        rect.width > 0 && rect.width <= MaximumDimension && rect.height > 0 && rect.height <= MaximumDimension,
        "invalid_bounds", "Bounds exceed the generated fixture's coordinate or dimension limits.");
    return rect;
}

void Set(napi_env env, napi_value object, const char* key, napi_value value) {
    Check(napi_set_named_property(env, object, key, value));
}
napi_value Number(napi_env env, std::int64_t number) {
    napi_value value{}; Check(napi_create_int64(env, number, &value)); return value;
}
napi_value Boolean(napi_env env, bool flag) {
    napi_value value{}; Check(napi_get_boolean(env, flag, &value)); return value;
}
napi_value RectValue(napi_env env, const RECT& rect) {
    napi_value value{}; Check(napi_create_object(env, &value));
    Set(env, value, "x", Number(env, rect.left)); Set(env, value, "y", Number(env, rect.top));
    Set(env, value, "width", Number(env, Width(rect))); Set(env, value, "height", Number(env, Height(rect)));
    return value;
}
napi_value Snapshot(napi_env env, const Geometry& geometry) {
    napi_value value{}; Check(napi_create_object(env, &value));
    Set(env, value, "outer", RectValue(env, geometry.outer));
    Set(env, value, "client", RectValue(env, geometry.client));
    Set(env, value, "windowDpi", Number(env, geometry.dpi));
    Set(env, value, "visible", Boolean(env, geometry.visible));
    Set(env, value, "minimized", Boolean(env, geometry.minimized));
    Set(env, value, "maximized", Boolean(env, geometry.maximized));
    return value;
}
napi_value RestoreResult(napi_env env, const Lease& lease, bool observed) {
    napi_value value{}; Check(napi_create_object(env, &value));
    Set(env, value, "restored", Boolean(env, true));
    Set(env, value, "observed", Boolean(env, observed));
    Set(env, value, "snapshot", Snapshot(env, lease.restored));
    return value;
}

napi_value ReadWindow(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        const auto window = ReadHandle(env, OneArgument(env, info));
        PhysicalDpiScope dpi;
        return Snapshot(env, Observe(window));
    });
}
napi_value GrantForegroundReturn(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        size_t count = 3; napi_value args[3]{};
        Check(napi_get_cb_info(env, info, &count, args, nullptr, nullptr));
        Require(count == 2, "invalid_arguments", "Exact own window and receiving process are required.");
        const auto window = ReadHandle(env, args[0]);
        ValidateWindow(window);
        napi_valuetype type{}; Check(napi_typeof(env, args[1], &type));
        Require(type == napi_number, "invalid_process", "Use a specific receiving process ID.");
        double number = 0; Check(napi_get_value_double(env, args[1], &number));
        Require(std::isfinite(number) && number == std::trunc(number) && number > 0 && number <= 0x7fffffff,
            "invalid_process", "Use a specific receiving process ID, never ASFW_ANY.");
        const auto process = static_cast<DWORD>(number);
        Require(process != GetCurrentProcessId(), "invalid_process", "The receiver must be the generated host.");
        // The trusted fixture pipe caller supplies its own PID. Production must
        // bind this value to the authenticated host process before this boundary.
        // Grant while the exact client window is foreground, before hiding it.
        Require(GetForegroundWindow() == window, "foreground_owner_changed", "The generated client no longer owns foreground.");
        const auto granted = AllowSetForegroundWindow(process) != FALSE;
        return Boolean(env, granted);
    });
}
napi_value ReadCurrent(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        auto& lease = ReadLease(env, info); Operation operation(lease);
        PhysicalDpiScope dpi;
        const auto geometry = Observe(lease.window); lease.Validate();
        return Snapshot(env, geometry);
    });
}
napi_value PlaceClient(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        napi_value value{}; auto& lease = ReadLease(env, info, &value);
        lease.Validate();
        const auto requested = ReadRect(env, value);
        Operation operation(lease); // Revalidates after any JavaScript getters.
        PhysicalDpiScope dpi; ValidateOrdinary(lease.window);
        const auto before = Observe(lease.window);
        const auto left = std::int64_t(before.client.left) - before.outer.left;
        const auto top = std::int64_t(before.client.top) - before.outer.top;
        const auto right = std::int64_t(before.outer.right) - before.client.right;
        const auto bottom = std::int64_t(before.outer.bottom) - before.client.bottom;
        const auto x = requested.x - left, y = requested.y - top;
        const auto width = requested.width + left + right, height = requested.height + top + bottom;
        Require(x >= std::numeric_limits<int>::min() && x <= std::numeric_limits<int>::max() &&
            y >= std::numeric_limits<int>::min() && y <= std::numeric_limits<int>::max() &&
            width > 0 && width <= MaximumDimension && height > 0 && height <= MaximumDimension,
            "invalid_outer_bounds", "Measured nonclient insets produce unsupported outer bounds.");
        lease.Validate(); ValidateOrdinary(lease.window);
        // Even a rejected SetWindowPos can have sent window messages. An
        // unconfirmed result keeps this lease active for explicit restoration.
        NativeCheck(SetWindowPos(lease.window, nullptr, static_cast<int>(x), static_cast<int>(y),
            static_cast<int>(width), static_cast<int>(height), PlacementFlags),
            "placement_failed", "SetWindowPos rejected the client placement.");
        lease.Validate(); ValidateOrdinary(lease.window); CheckPending(env);
        const auto actual = Observe(lease.window);
        Require(actual.client.left == requested.x && actual.client.top == requested.y &&
            Width(actual.client) == requested.width && Height(actual.client) == requested.height,
            "placement_unconfirmed", "Actual physical client bounds did not match the request; no retry was attempted.");
        return Snapshot(env, actual);
    });
}
napi_value Restore(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        auto& lease = ReadLease(env, info);
        if (!lease.active) {
            Require(lease.restorationObserved, "lease_released", "The lease has no verified restoration snapshot.");
            return RestoreResult(env, lease, false); // Cached result; no HWND reuse risk.
        }
        Operation operation(lease); PhysicalDpiScope dpi; ValidateOrdinary(lease.window);
        NativeCheck(SetWindowPos(lease.window, nullptr, lease.original.outer.left, lease.original.outer.top,
            static_cast<int>(Width(lease.original.outer)), static_cast<int>(Height(lease.original.outer)), PlacementFlags),
            "restore_failed", "SetWindowPos rejected restoration of the original physical outer bounds.");
        lease.Validate(); ValidateOrdinary(lease.window); CheckPending(env);
        const auto actual = Observe(lease.window);
        Require(Equal(actual.outer, lease.original.outer), "restore_unconfirmed",
            "Actual physical outer bounds did not match the original rectangle; no retry was attempted.");
        Require(RemovePropW(lease.window, MarkerName) == lease.Marker(), "lease_marker_lost", "Lease generation marker could not be retired.");
        lease.restored = actual; lease.restorationObserved = true; lease.active = false;
        return RestoreResult(env, lease, true);
    });
}
void Finalize(napi_env, void* data, void*) noexcept { delete static_cast<Lease*>(data); }
napi_value Acquire(napi_env env, napi_callback_info info) noexcept {
    return Boundary(env, [&] {
        const auto window = ReadHandle(env, OneArgument(env, info));
        PhysicalDpiScope dpi; ValidateWindow(window); ValidateOrdinary(window);
        Require(GetPropW(window, MarkerName) == nullptr, "duplicate_lease", "This window already has a generated geometry lease.");
        auto lease = std::make_unique<Lease>();
        lease->window = window; lease->process = GetCurrentProcessId(); lease->thread = GetCurrentThreadId();
        lease->original = Observe(window);
        Require(Width(lease->original.outer) > 0 && Width(lease->original.outer) <= MaximumDimension &&
            Height(lease->original.outer) > 0 && Height(lease->original.outer) <= MaximumDimension,
            "unsupported_original_bounds", "The original window dimensions exceed fixture limits.");
        napi_value object{}; Check(napi_create_object(env, &object));
        const napi_property_descriptor properties[] = {
            {"read", nullptr, ReadCurrent, nullptr, nullptr, nullptr, napi_default_method, nullptr},
            {"placeClient", nullptr, PlaceClient, nullptr, nullptr, nullptr, napi_default_method, nullptr},
            {"restore", nullptr, Restore, nullptr, nullptr, nullptr, napi_default_method, nullptr},
        };
        Check(napi_define_properties(env, object, sizeof(properties) / sizeof(properties[0]), properties));
        Check(napi_type_tag_object(env, object, &LeaseType));
        // No JavaScript is called between this final admission and marker install.
        ValidateWindow(window); ValidateOrdinary(window);
        Require(GetPropW(window, MarkerName) == nullptr, "duplicate_lease", "This window already has a generated geometry lease.");
        NativeCheck(SetPropW(window, MarkerName, lease->Marker()), "lease_marker_failed", "Could not install the window generation marker.");
        Check(napi_wrap(env, object, lease.get(), Finalize, nullptr, nullptr));
        (void)lease.release();
        return object;
    });
}
} // namespace

NAPI_MODULE_INIT() {
    return Boundary(env, [&] {
        const napi_property_descriptor properties[] = {
            {"acquire", nullptr, Acquire, nullptr, nullptr, nullptr, napi_default_method, nullptr},
            {"read", nullptr, ReadWindow, nullptr, nullptr, nullptr, napi_default_method, nullptr},
            {"grantForegroundReturn", nullptr, GrantForegroundReturn, nullptr, nullptr, nullptr, napi_default_method, nullptr},
        };
        Check(napi_define_properties(env, exports, sizeof(properties) / sizeof(properties[0]), properties));
        return exports;
    });
}
