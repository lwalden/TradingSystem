"""Tests for config/settings.py."""

import warnings

import pytest
from pydantic import ValidationError

from optimind.config.settings import Settings


def test_default_mode_is_paper() -> None:
    s = Settings()
    assert s.mode == "paper"


def test_paper_port_selected_in_paper_mode() -> None:
    s = Settings(mode="paper")
    assert s.ib_port == s.ib_paper_port


def test_live_port_selected_in_live_mode() -> None:
    s = Settings(mode="live")
    assert s.ib_port == s.ib_live_port


def test_paper_and_live_ports_differ() -> None:
    s = Settings()
    assert s.ib_paper_port != s.ib_live_port


def test_mode_is_case_insensitive(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OPTIMIND_MODE", "PAPER")
    s = Settings()
    assert s.mode == "paper"


def test_invalid_mode_raises() -> None:
    with pytest.raises(ValidationError):
        Settings(mode="sim")  # type: ignore[arg-type]


def test_env_prefix(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OPTIMIND_IB_CLIENT_ID", "42")
    s = Settings()
    assert s.ib_client_id == 42


def test_guided_mode_default_true() -> None:
    s = Settings()
    assert s.guided_mode is True


def test_ai_regime_default_enabled() -> None:
    s = Settings()
    assert s.ai_regime_enabled is True


# ── SecretStr ─────────────────────────────────────────────────────────────────

def test_api_key_is_secret_str() -> None:
    from pydantic import SecretStr
    s = Settings(anthropic_api_key=SecretStr("sk-ant-test"))
    assert isinstance(s.anthropic_api_key, SecretStr)


def test_api_key_masked_in_repr() -> None:
    from pydantic import SecretStr
    s = Settings(anthropic_api_key=SecretStr("sk-ant-supersecret"))
    assert "supersecret" not in repr(s)
    assert "supersecret" not in str(s.anthropic_api_key)


def test_api_key_accessible_via_get_secret_value() -> None:
    from pydantic import SecretStr
    s = Settings(anthropic_api_key=SecretStr("sk-ant-test"))
    assert s.anthropic_api_key.get_secret_value() == "sk-ant-test"


def test_missing_api_key_warns_when_ai_enabled() -> None:
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        Settings(ai_regime_enabled=True)
    assert any("OPTIMIND_ANTHROPIC_API_KEY" in str(w.message) for w in caught)


def test_missing_api_key_no_warning_when_ai_disabled() -> None:
    with warnings.catch_warnings(record=True) as caught:
        warnings.simplefilter("always")
        Settings(ai_regime_enabled=False)
    assert not any("OPTIMIND_ANTHROPIC_API_KEY" in str(w.message) for w in caught)
