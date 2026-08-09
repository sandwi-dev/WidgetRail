#include "WidgetActionFeedback.h"

#include <cstdlib>
#include <iostream>
#include <string>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

} // namespace

int main() {
    gba::WidgetActionFeedbackStore feedback;
    Check(feedback.Publish(L"music", L"music.g1", L"Music action failed", 1000, 4000),
          "first widget failure is accepted");
    Check(feedback.Publish(L"chat", L"chat.g1", L"Chat action failed", 1200, 4000),
          "second widget failure is accepted");
    Check(feedback.size() == 2, "failures for two widgets do not collapse");
    Check(feedback.MessageFor(L"music", L"music.g1", 1200) == L"Music action failed",
          "dashboard can select the first widget failure");
    Check(feedback.MessageFor(L"chat", L"chat.g1", 1200) == L"Chat action failed",
          "open-widget surface can select the second widget failure");
    Check(!feedback.MessageFor(L"music", L"music.g2", 1200),
          "replacement runtime cannot inherit feedback");
    Check(feedback.NextExpiry() == 5000, "earliest expiry is exposed to the host timer");

    Check(feedback.Publish(L"music", L"music.g1", L"New music failure", 1500, 4000),
          "newest failure replaces only the same widget");
    Check(feedback.MessageFor(L"music", L"music.g1", 1500) == L"New music failure",
          "same-widget replacement is visible");
    Check(feedback.MessageFor(L"chat", L"chat.g1", 1500) == L"Chat action failed",
          "offscreen failure cannot erase another widget");
    Check(feedback.NextExpiry() == 5200, "replacement recomputes the earliest expiry");

    Check(!feedback.Expire(5199), "state does not expire before its deadline");
    Check(feedback.Expire(5200), "deadline removes the expired batch");
    Check(!feedback.MessageFor(L"chat", L"chat.g1", 5200),
          "expired feedback leaves the painted surface");
    Check(feedback.MessageFor(L"music", L"music.g1", 5200) == L"New music failure",
          "later feedback survives an earlier widget expiry");
    Check(!feedback.Expire(5200), "the same deadline requests no second invalidation");
    Check(feedback.NextExpiry() == 5500, "host can schedule the remaining deadline");

    Check(feedback.Forget(L"music"), "catalog removal clears generation-owned feedback");
    Check(!feedback.Forget(L"music"), "catalog cleanup is idempotent");
    Check(!feedback.NextExpiry(), "no timer remains after catalog cleanup");

    for (std::size_t index = 0; index < gba::WidgetActionFeedbackStore::MaximumWidgets; ++index) {
        const auto suffix = std::to_wstring(index);
        Check(feedback.Publish(
                  L"widget." + suffix, L"generation." + suffix, L"Action failed", 0, 1),
              "catalog-bounded feedback entry is accepted");
    }
    Check(!feedback.Publish(L"overflow", L"generation", L"Action failed", 0, 1),
          "feedback cannot exceed the catalog widget bound");
    Check(feedback.Clear(), "hide or bridge stop clears retained feedback");
    Check(!feedback.Clear(), "repeated hide cleanup is idempotent");

    std::cout << "WidgetActionFeedbackTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
