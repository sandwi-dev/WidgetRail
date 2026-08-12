#pragma once

#include <string>

namespace gba::launcher {

/// Executes the bounded host-owned Launcher Experience semantic route inside
/// the production OverlayHost binary. The fixture supplies only sealed static
/// content and never projects Game Launcher domain state.
[[nodiscard]] bool RunProductionHostSemanticProof(std::wstring& diagnostic);

} // namespace gba::launcher
