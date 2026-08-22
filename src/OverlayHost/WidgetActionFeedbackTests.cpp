#include "WidgetActionFeedback.h"
#include "WidgetBridgeClient.h"

#include <cstdlib>
#include <iostream>
#include <optional>
#include <string>
#include <vector>

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
    widgetrail::WidgetActionFeedbackController feedback;
    Check(!feedback.Publish(
              L"music", L"music.g1", L"Hidden failure", 900, 4000).shouldInvalidate,
          "hidden bridge events cannot publish feedback");
    feedback.Show();
    Check(feedback.Publish(
              L"music", L"music.g1", L"Music action failed", 1000, 4000).shouldInvalidate,
          "first widget failure is accepted");
    Check(feedback.Publish(
              L"chat", L"chat.g1", L"Chat action failed", 1200, 4000).shouldInvalidate,
          "second widget failure is accepted");
    Check(feedback.size() == 2, "failures for two widgets do not collapse");
    Check(feedback.MessageFor(L"music", L"music.g1", 1200) == L"Music action failed",
          "dashboard can select the first widget failure");
    Check(feedback.MessageFor(L"chat", L"chat.g1", 1200) == L"Chat action failed",
          "open-widget surface can select the second widget failure");
    Check(!feedback.MessageFor(L"music", L"music.g2", 1200),
          "replacement runtime cannot inherit feedback");
    Check(feedback.NextExpiry() == 5000, "earliest expiry is exposed to the host timer");

    Check(feedback.Publish(
              L"music", L"music.g1", L"New music failure", 1500, 4000).shouldInvalidate,
          "newest failure replaces only the same widget");
    Check(feedback.MessageFor(L"music", L"music.g1", 1500) == L"New music failure",
          "same-widget replacement is visible");
    Check(feedback.MessageFor(L"chat", L"chat.g1", 1500) == L"Chat action failed",
          "offscreen failure cannot erase another widget");
    Check(feedback.NextExpiry() == 5200, "replacement recomputes the earliest expiry");

    Check(!feedback.Expire(5199).shouldInvalidate,
          "state does not expire before its deadline");
    Check(feedback.Expire(5200).shouldInvalidate,
          "deadline removes the expired batch after timer-registration failure");
    Check(!feedback.MessageFor(L"chat", L"chat.g1", 5200),
          "expired feedback leaves the painted surface");
    Check(feedback.MessageFor(L"music", L"music.g1", 5200) == L"New music failure",
          "later feedback survives an earlier widget expiry");
    Check(!feedback.Expire(5200).shouldInvalidate,
          "the same deadline requests no second invalidation");
    Check(feedback.NextExpiry() == 5500, "host can schedule the remaining deadline");

    Check(feedback.Forget(L"music").shouldInvalidate,
          "catalog removal clears generation-owned feedback");
    Check(!feedback.Forget(L"music").shouldInvalidate,
          "catalog cleanup is idempotent");
    Check(!feedback.NextExpiry(), "no timer remains after catalog cleanup");

    for (std::size_t index = 0; index < widgetrail::WidgetActionFeedbackStore::MaximumWidgets; ++index) {
        const auto suffix = std::to_wstring(index);
        Check(feedback.Publish(
                  L"widget." + suffix, L"generation." + suffix, L"Action failed", 0, 1)
                  .shouldInvalidate,
              "catalog-bounded feedback entry is accepted");
    }
    Check(!feedback.Publish(
              L"overflow", L"generation", L"Action failed", 0, 1).shouldInvalidate,
          "feedback cannot exceed the catalog widget bound");
    Check(!feedback.Hide().shouldInvalidate,
          "hide clears state without requesting a hidden repaint");
    Check(feedback.size() == 0, "hide or bridge stop clears retained feedback");
    Check(!feedback.MessageFor(L"widget.0", L"generation.0", 0),
          "hidden surfaces expose no retained feedback");
    Check(!feedback.Hide().shouldInvalidate, "repeated hide cleanup is idempotent");
    feedback.Show();
    Check(!feedback.MessageFor(L"widget.0", L"generation.0", 0),
          "show does not resurrect prior runtime feedback");

    std::uint64_t replacementNow = 30'000;
    std::optional<std::uint64_t> replacementDeadline;
    int replacementInvalidations = 0;
    widgetrail::WidgetActionFeedbackHost replacementHost({
        [&] { return replacementNow; },
        [&](const std::optional<std::uint64_t> deadline) {
            replacementDeadline = deadline;
        },
        [&] { ++replacementInvalidations; },
    });
    Check(replacementHost.ReconcileCatalog({
              {L"music", L"Music", L"music.instance", L"music.g1", L"music.p1"},
          }),
          "replacement host accepts its exact catalog authority");
    replacementHost.Show();
    Check(replacementHost.PublishBridgeFailures({
              {L"music", L"music.g1", L"music.play", L"music.play.button"},
          }).OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Published,
          "replacement host publishes the first same-widget failure");
    const auto firstDeadline = replacementNow +
        widgetrail::WidgetActionFeedbackHost::DisplayDurationMilliseconds;
    Check(replacementDeadline == firstDeadline,
          "first same-widget failure owns one bounded deadline");

    replacementNow += 2'200;
    Check(replacementHost.PublishBridgeFailures({
              {L"music", L"music.g1", L"music.play", L"music.play.button"},
          }).OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Published,
          "same-widget replacement is admitted under the same runtime authority");
    const auto secondDeadline = replacementNow +
        widgetrail::WidgetActionFeedbackHost::DisplayDurationMilliseconds;
    Check(replacementDeadline == secondDeadline && secondDeadline > firstDeadline,
          "same-widget replacement computes a fresh bounded deadline");

    const int invalidationsBeforeOldDeadline = replacementInvalidations;
    replacementNow = firstDeadline;
    replacementHost.OnDeadlineTimer();
    Check(replacementHost.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::OpenWidget,
              L"music", L"music") == L"Music action failed; try again",
          "old timer delivery cannot expire the same-widget replacement");
    Check(replacementDeadline == secondDeadline &&
              replacementInvalidations == invalidationsBeforeOldDeadline,
          "old timer delivery preserves the replacement deadline without repaint");

    replacementNow = secondDeadline;
    replacementHost.OnDeadlineTimer();
    Check(!replacementHost.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::OpenWidget,
              L"music", L"music"),
          "same-widget replacement expires exactly at its own bounded deadline");
    Check(!replacementDeadline &&
              replacementInvalidations == invalidationsBeforeOldDeadline + 1,
          "replacement expiry clears its timer and repaints exactly once");

    std::uint64_t now = 10'000;
    std::optional<std::uint64_t> scheduledDeadline;
    bool timerAvailable = true;
    int scheduleRequests = 0;
    int invalidations = 0;
    widgetrail::WidgetActionFeedbackHost host({
        [&] { return now; },
        [&](const std::optional<std::uint64_t> deadline) {
            ++scheduleRequests;
            scheduledDeadline = timerAvailable ? deadline : std::nullopt;
        },
        [&] { ++invalidations; },
    });
    const std::vector<widgetrail::WidgetDescriptor> initialCatalog{
        {L"music", L"Music", L"music.instance", L"music.g1", L"music.p1"},
        {L"chat", L"Chat", L"chat.instance", L"chat.g1", L"chat.p1"},
    };
    Check(host.ReconcileCatalog(initialCatalog),
          "host accepts the bounded bridge catalog projection");
    host.Show();
    const int schedulesBeforeBatch = scheduleRequests;
    const auto firstBatch = host.PublishBridgeFailures({
        {L"music", L"music.g1", L"music.play", L"music.play.button"},
        {L"chat", L"chat.g1", L"chat.send", L"chat.send.button"},
    });
    Check(firstBatch.count == 2 &&
              firstBatch.OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Published &&
              firstBatch.OutcomeAt(1) == widgetrail::WidgetActionFeedbackOutcome::Published,
          "one drained bridge batch publishes both generation-owned failures");
    Check(invalidations == 1,
          "one bridge batch emits exactly one host invalidation");
    Check(scheduleRequests == schedulesBeforeBatch + 1 && scheduledDeadline == 14'000,
          "one bridge batch schedules exactly one earliest deadline");
    Check(host.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::Dashboard, L"music", L"chat") ==
              L"Music action failed; try again",
          "dashboard selects feedback for its exact catalog widget");
    Check(host.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::OpenWidget, L"music", L"chat") ==
              L"Chat action failed; try again",
          "open-widget surface selects feedback independently");

    now = 10'500;
    const auto offscreenBatch = host.PublishBridgeFailures({
        {L"chat", L"chat.g1", L"chat.retry", L"chat.retry.button"},
    });
    Check(offscreenBatch.OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Published,
          "later offscreen failure remains independently presentable");
    Check(host.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::Dashboard, L"music", L"chat") ==
              L"Music action failed; try again",
          "offscreen failure cannot erase active dashboard feedback");

    now = 14'000;
    const int invalidationsBeforeExpiry = invalidations;
    host.OnDeadlineTimer();
    Check(invalidations == invalidationsBeforeExpiry + 1,
          "dedicated deadline removes painted copy with one invalidation");
    Check(!host.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::Dashboard, L"music", L"chat"),
          "expired dashboard copy is no longer selectable");
    host.OnDeadlineTimer();
    Check(invalidations == invalidationsBeforeExpiry + 1,
          "repeated deadline delivery does not repaint unchanged state");

    timerAvailable = false;
    now = 15'000;
    Check(host.PublishBridgeFailures({
              {L"music", L"music.g1", L"music.pause", L"music.pause.button"},
          }).OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Published,
          "feedback remains admitted when Win32 timer creation is unavailable");
    Check(!scheduledDeadline,
          "failed dedicated timer registration leaves no false scheduled deadline");
    const int invalidationsBeforeFallback = invalidations;
    now = 19'000;
    host.OnControllerTimer();
    Check(invalidations == invalidationsBeforeFallback + 1,
          "controller timer fallback expires feedback with one repaint");
    host.OnControllerTimer();
    Check(invalidations == invalidationsBeforeFallback + 1,
          "controller timer fallback is idempotent after cleanup");

    timerAvailable = true;
    now = 20'000;
    (void)host.PublishBridgeFailures({
        {L"music", L"music.g1", L"music.next", L"music.next.button"},
    });
    const int invalidationsBeforeReplacement = invalidations;
    const std::vector<widgetrail::WidgetDescriptor> replacedCatalog{
        {L"music", L"Music", L"music.instance.2", L"music.g2", L"music.p2"},
        {L"chat", L"Chat", L"chat.instance", L"chat.g1", L"chat.p1"},
    };
    Check(host.ReconcileCatalog(replacedCatalog),
          "runtime replacement atomically reconciles the catalog projection");
    Check(invalidations == invalidationsBeforeReplacement + 1 &&
              !host.MessageForSurface(
                  widgetrail::WidgetActionFeedbackSurface::Dashboard, L"music", L"chat"),
          "runtime replacement removes old-generation painted feedback once");
    Check(host.PublishBridgeFailures({
              {L"music", L"music.g1", L"music.next", L"music.next.button"},
          }).OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Stale,
          "late bridge failure cannot cross a reconciled runtime generation");

    now = 21'000;
    (void)host.PublishBridgeFailures({
        {L"chat", L"chat.g1", L"chat.send", L"chat.send.button"},
    });
    const std::vector<widgetrail::WidgetDescriptor> removedCatalog{
        {L"music", L"Music", L"music.instance.2", L"music.g2", L"music.p2"},
    };
    Check(host.ReconcileCatalog(removedCatalog) && host.size() == 0,
          "catalog removal retires feedback for the removed widget");

    now = 22'000;
    (void)host.PublishBridgeFailures({
        {L"music", L"music.g2", L"music.play", L"music.play.button"},
    });
    host.Hide();
    Check(host.size() == 0 && !scheduledDeadline,
          "hide clears presentation state and cancels its deadline");
    host.Show();
    Check(!host.MessageForSurface(
              widgetrail::WidgetActionFeedbackSurface::Dashboard, L"music", L""),
          "show never resurrects hidden feedback");
    host.Stop();
    Check(host.PublishBridgeFailures({
              {L"music", L"music.g2", L"music.play", L"music.play.button"},
          }).OutcomeAt(0) == widgetrail::WidgetActionFeedbackOutcome::Stale,
          "bridge stop retires the catalog authority used by late failures");

    std::cout << "WidgetActionFeedbackTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
