#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdlib>
#include <cerrno>
#define ATS_EXPORTS
extern "C" {
#include "../../src/bridge/atsplugin.h"

// Test-only, deterministic 32-bit ATS. Never distribute as a real vehicle ATS.
void WINAPI Load(void) {}
void WINAPI Dispose(void) {}
int WINAPI GetPluginVersion(void) { return ATS_VERSION; }
void WINAPI SetVehicleSpec(ATS_VEHICLESPEC) {}
void WINAPI Initialize(int) {}
ATS_HANDLES WINAPI Elapse(ATS_VEHICLESTATE state, int* panel, int* sound) {
    if (state.Time == 123 * 16) Sleep(25); // established test-only ATS stall
    // An optional second ATS pause, injected only by the offline cadence
    // regression. It must never be enabled in a real vehicle.
    char frame_value[32]{}, ms_value[32]{};
    DWORD frame_chars = GetEnvironmentVariableA(
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_FRAME", frame_value, sizeof(frame_value));
    DWORD ms_chars = GetEnvironmentVariableA(
        "BVE5COMPAT_TEST_MOCK_ATS_SLOW_MS", ms_value, sizeof(ms_value));
    if (frame_chars > 0 && frame_chars < sizeof(frame_value) &&
        ms_chars > 0 && ms_chars < sizeof(ms_value)) {
        char* end_frame = nullptr;
        char* end_ms = nullptr;
        errno = 0;
        long frame = std::strtol(frame_value, &end_frame, 10);
        long delay = std::strtol(ms_value, &end_ms, 10);
        if (errno == 0 && end_frame != frame_value && *end_frame == '\0' &&
            end_ms != ms_value && *end_ms == '\0' &&
            frame >= 0 && frame <= 100000 && delay >= 0 && delay <= 500 &&
            state.Time == frame * 16) {
            Sleep(static_cast<DWORD>(delay));
        }
    }
    if (panel) { panel[0] = state.Time; panel[1] = 7; panel[255] = 0x12345678; }
    if (sound) {
        sound[0] = 0;
        sound[255] = -20000;
        // Offline test-only varying brake sound state to exercise the
        // host's diagnostic event log without an actual vehicle ATS.
        char cycle_text[32]{};
        DWORD cycle_length = GetEnvironmentVariableA(
            "BVE5COMPAT_TEST_MOCK_SOUND_CYCLE", cycle_text, sizeof(cycle_text));
        int cycle = 0;
        if (cycle_length > 0 && cycle_length < sizeof(cycle_text)) {
            char* end = nullptr;
            errno = 0;
            const long parsed = std::strtol(cycle_text, &end, 10);
            if (errno == 0 && end != cycle_text && *end == '\0' &&
                parsed >= 1 && parsed <= 500) cycle = static_cast<int>(parsed);
        }
        if (cycle > 0) {
            const int tick = state.Time / 16;
            sound[150] = (tick / cycle) % 2;
        }
    }
    ATS_HANDLES result{};
    result.Brake = 1;
    result.Power = 2;
    return result;
}
void WINAPI SetPower(int) {}
void WINAPI SetBrake(int) {}
void WINAPI SetReverser(int) {}
void WINAPI KeyDown(int) {}
void WINAPI KeyUp(int) {}
void WINAPI HornBlow(int) {}
void WINAPI DoorOpen(void) {}
void WINAPI DoorClose(void) {}
void WINAPI SetSignal(int) {}
void WINAPI SetBeaconData(ATS_BEACONDATA) {}
}