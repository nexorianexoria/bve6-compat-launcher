"""Structural regression for b5's per-frame bridge protocol hot path."""
from __future__ import annotations
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
FRONT = (ROOT / "src/bridge/frontend/dllmain.cpp").read_text(encoding="utf-8")
BACK = (ROOT / "src/bridge/backend/exemain.cpp").read_text(encoding="utf-8")


def body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise AssertionError(f"Missing function: {signature}")
    opening = source.find("{", start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:i]
    raise AssertionError(f"Unclosed function: {signature}")


class BridgeHotPathBufferTests(unittest.TestCase):
    def test_frontend_elapse_uses_fixed_stack_buffers(self):
        section = body(FRONT, "ATS_API ATS_HANDLES WINAPI Elapse(")
        self.assertIn("fixed_output_streambuf", section)
        self.assertIn("fixed_input_streambuf", section)
        self.assertIn("std::array<char, 8192> request_bytes", section)
        self.assertIn("std::array<char, 8192> response_bytes", section)
        protocol = section.split("++frontend_elapse_frames;", 1)[0]
        for forbidden in ("std::ostringstream", "std::istringstream",
                          "std::string buffer"):
            self.assertNotIn(forbidden, protocol)

    def test_receive_reuses_session_event_and_has_no_frame_handle_churn(self):
        section = body(FRONT, "bool receive_from_backend(")
        self.assertIn("pipe_read_event", section)
        self.assertIn("ResetEvent(pipe_read_event)", section)
        self.assertNotIn("CreateEventW", section)
        self.assertNotIn("CloseHandle", section)

    def test_event_is_created_once_during_load_and_closed_on_dispose(self):
        load = body(FRONT, "ATS_API void WINAPI Load(")
        dispose = body(FRONT, "ATS_API void WINAPI Dispose(")
        self.assertEqual(load.count("CreateEventW"), 1)
        self.assertIn("CloseHandle(pipe_read_event)", dispose)
        self.assertIn("pipe_read_event = NULL", dispose)

    def test_backend_reuses_input_string_and_parses_without_istringstream(self):
        marker = "std::set<std::string> seen_commands;"
        start = BACK.find(marker)
        self.assertGreaterEqual(start, 0)
        section = BACK[start:BACK.find('using namespace std::literals;', start)]
        self.assertIn("buffer.reserve(8192)", section)
        self.assertIn("buffer.clear()", section)
        self.assertIn("fixed_input_streambuf", section)
        self.assertNotIn("std::istringstream", section)
        self.assertNotIn("std::move(buffer)", section)

    def test_backend_response_is_fixed_buffer_and_protocol_send_accepts_view(self):
        section = body(BACK, "void protocol_send(std::string_view value)")
        self.assertIn("while (offset < value.size())", section)
        response_start = BACK.find("std::array<char, 8192> response_bytes")
        self.assertGreaterEqual(response_start, 0)
        response_end = BACK.find("const auto sent =", response_start)
        response = BACK[response_start:response_end]
        self.assertIn("fixed_output_streambuf", response)
        self.assertIn("protocol_send(response_buffer.view())", response)
        self.assertNotIn("std::ostringstream", response)
        self.assertNotIn("response.str()", response)

    def test_frontend_send_handles_partial_writes(self):
        section = body(FRONT, "bool send_to_backend(std::string_view data)")
        self.assertIn("while (offset < data.size())", section)
        self.assertIn("offset += written", section)


if __name__ == "__main__":
    unittest.main()