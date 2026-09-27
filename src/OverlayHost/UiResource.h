#pragma once

#include "UiResourceBudget.h"
#include <Windows.h>
#include <wrl/client.h>
#include <new>
#include <type_traits>
#include <utility>

namespace widgetrail::resources {

// COM storage and its accounting lifetime travel together. Aliases (for
// example, a compatible target's bitmap) share the same allocation. This is
// distinct from a raster-content lease, which prevents overwriting live pixels.
template<class T> class UiResource final {
public:
    UiResource() = default;
    UiResource(std::nullptr_t) noexcept {}
    UiResource(const UiResource&) = default;
    UiResource(UiResource&&) noexcept = default;
    UiResource& operator=(UiResource other) noexcept { Swap(other); return *this; }
    template<class U> requires(std::is_convertible_v<U*, T*>)
    UiResource(const UiResource<U>& other) : allocation_(other.Allocation()), resource_(other.Get()) {}

    [[nodiscard]] T* Get() const noexcept { return resource_.Get(); }
    [[nodiscard]] T* operator->() const noexcept { return Get(); }
    [[nodiscard]] explicit operator bool() const noexcept { return Get() != nullptr; }
    [[nodiscard]] const UiResourceBudget::Lease& Allocation() const noexcept { return allocation_; }
    [[nodiscard]] UiResourceBudget::Pin Protect() const { return allocation_ ? allocation_->Protect() : UiResourceBudget::Pin{}; }
    void Reset() noexcept { resource_.Reset(); allocation_.reset(); }
    void Swap(UiResource& other) noexcept { allocation_.swap(other.allocation_); resource_.Swap(other.resource_); }
    friend bool operator==(const UiResource& value, std::nullptr_t) noexcept { return !value; }

    // Only for storage allocated and accounted outside this UI owner (including
    // test fixtures). Never strip a tracked resource's lease through this path.
    [[nodiscard]] static UiResource External(Microsoft::WRL::ComPtr<T> resource) {
        return UiResource({}, std::move(resource));
    }
    template<class Factory>
    static HRESULT Create(const std::shared_ptr<UiResourceBudget>& budget, Kind kind, std::size_t bytes,
        Factory&& create, UiResource& destination, Admission admission = Admission::Required) {
        if (!budget) return E_POINTER;
        try {
            auto allocation = budget->Reserve(kind, bytes, admission);
            if (!allocation) return E_OUTOFMEMORY;
            Microsoft::WRL::ComPtr<T> resource;
            const auto result = std::forward<Factory>(create)(resource.GetAddressOf());
            if (FAILED(result)) return result;
            if (!resource) return E_UNEXPECTED;
            allocation->Commit();
            destination = UiResource(std::move(allocation), std::move(resource));
            return result;
        } catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
    }
    template<class U, class Factory>
    static HRESULT Alias(const UiResource<U>& backing, Factory&& acquire, UiResource& destination) {
        Microsoft::WRL::ComPtr<T> resource;
        const auto result = std::forward<Factory>(acquire)(resource.GetAddressOf());
        if (FAILED(result)) return result;
        if (!resource) return E_UNEXPECTED;
        destination = UiResource(backing.Allocation(), std::move(resource));
        return result;
    }
private:
    UiResource(UiResourceBudget::Lease allocation, Microsoft::WRL::ComPtr<T> resource)
        : allocation_(std::move(allocation)), resource_(std::move(resource)) {}
    // Reverse destruction releases the COM owner before the allocation ticket.
    UiResourceBudget::Lease allocation_;
    Microsoft::WRL::ComPtr<T> resource_;
};
} // namespace widgetrail::resources
