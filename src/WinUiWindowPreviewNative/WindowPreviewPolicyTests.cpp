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
