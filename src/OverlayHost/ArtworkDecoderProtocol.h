#pragma once

#include <Windows.h>

#include <cstddef>
#include <cstdint>

namespace widgetrail::artworkdecoder {

constexpr std::uint32_t protocolMagic = 0x44524157; // WARD
constexpr std::uint32_t protocolVersion = 2;
constexpr std::size_t maximumEncodedBytes = 8U * 1024U * 1024U;
constexpr std::size_t maximumDecodedBytes = 64U * 1024U * 1024U;

enum class SharedState : std::uint32_t {
    Empty,
    Request,
    Response,
};

enum class ContentType : std::uint32_t {
    Invalid,
    Jpeg,
    Png,
    WebP,
};

enum class TestBehavior : std::uint32_t {
    Normal,
    Hang,
    Exit,
    Succeed,
    MissingCodec,
};

struct SharedHeader {
    std::uint32_t magic{protocolMagic};
    std::uint32_t version{protocolVersion};
    SharedState state{SharedState::Empty};
    ContentType contentType{ContentType::Invalid};
    TestBehavior testBehavior{TestBehavior::Normal};
    std::uint32_t reserved{};
    std::uint64_t correlation{};
    std::uint64_t encodedBytes{};
    std::uint64_t maximumDecodedBytes{};
    std::uint64_t maximumPixels{};
    std::uint32_t maximumDimension{};
    HRESULT result{E_FAIL};
    std::uint32_t width{};
    std::uint32_t height{};
    std::uint32_t stride{};
    std::uint32_t decodedBytes{};
};

constexpr std::size_t encodedOffset = sizeof(SharedHeader);
constexpr std::size_t decodedOffset = encodedOffset + maximumEncodedBytes;
constexpr std::size_t mappingBytes = decodedOffset + maximumDecodedBytes;

} // namespace widgetrail::artworkdecoder
