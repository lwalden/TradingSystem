"""
Shared pytest fixtures and configuration.

Key concern: Settings() reads from a .env file if one exists. Any developer
with OPTIMIND_MODE=live in their .env would break mode-sensitive tests.
The env_isolation fixture clears all OPTIMIND_ vars before each test so tests
are always deterministic regardless of local .env contents.
"""

import pytest


@pytest.fixture(autouse=True)
def env_isolation(monkeypatch: pytest.MonkeyPatch) -> None:
    """
    Clear all OPTIMIND_ environment variables before each test.

    This prevents a local .env file from poisoning test results.
    Tests that need specific env values should set them explicitly via monkeypatch.
    """
    import os
    for key in list(os.environ.keys()):
        if key.startswith("OPTIMIND_"):
            monkeypatch.delenv(key, raising=False)
