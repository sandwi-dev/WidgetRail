#include "CaptureGeometry.h"
#include "WindowContextCapture.h"
static_assert(ConfiguredCaptureSize(7680,4320,0,0,false).width==7680);
static_assert(ConfiguredCaptureSize(3841,2161,1920,1080,false).width==1919);
static_assert(ConfiguredCaptureSize(1920,1080,1280,720,true).width==1280);
static_assert(ConfiguredCaptureSize(1920,1080,1280,720,true).height==720);
static_assert(ConfiguredCaptureSize(640,360,1280,720,true).width==640);
static_assert(ConfiguredCaptureSize(16384,16384,0,0,false).width==0);
static_assert(ConfiguredCaptureSize(3840,2160,0,720,false).width==0);
static_assert(ConfiguredCaptureSize(1,1,1280,720,true).width==0);
static_assert(CaptureSize(3840, 2160, 3).width == 3840 && CaptureSize(3840, 2160, 3).height == 2160);
static_assert(CaptureSize(1919, 1079, 3).width == 1919 && CaptureSize(1919, 1079, 3).height == 1079);
static_assert(CaptureSize(7680, 4320, 3).width == 7680);
static_assert(CaptureSize(16384, 16384, 3).width == 0);
static_assert(CaptureSize(0, 1080, 3).width == 0);
static_assert(CaptureSize(3840, 2160, 1).width == 1920 && CaptureSize(3840, 2160, 1).height == 1080);
static_assert(CaptureSize(3840, 2160, 2).width == 1280 && CaptureSize(3840, 2160, 2).height == 720);
static_assert(CaptureSize(1919, 1079, 2).width % 2 == 0);
#include "WindowPreviewPolicy.h"
#include <iostream>
#include <stdexcept>
#include <string>
using namespace widgetrail::preview;
int main() {
    int checks{};
    auto require = [&](bool ok) { if (!ok) throw std::runtime_error("preview policy check " + std::to_string(checks)); ++checks; };
    require(!OutputSize(0, 10)); require(!OutputSize(10, 0)); require(!OutputSize(16385, 10));
    require(OutputSize(1920, 1080) == std::pair<uint32_t, uint32_t>{960, 540});
    require(OutputSize(400, 250) == std::pair<uint32_t, uint32_t>{400, 250});
    require(OutputSize(500, 2000) == std::pair<uint32_t, uint32_t>{135, 540});
    require(!SourceFits(0, 10, 0)); require(!SourceFits(-1, 10, 0));
    require(SourceFits(4096, 2160, 0)); require(!SourceFits(4097, 2160, 0));
    require(!SourceFits(INT32_MAX, INT32_MAX, 0));
    require(SourceFits(1, 1, MaximumActiveBytes - 4)); require(!SourceFits(1, 1, MaximumActiveBytes - 3));
    require(!SourceFits(1, 1, UINT64_MAX)); require(MaximumSources == 8);
    std::cout << "WindowPreviewPolicy passed " << checks << " checks\n";
}
